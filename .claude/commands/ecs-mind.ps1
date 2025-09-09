#Requires -Version 5.1
param(
  [int]$Iterations = 10,
  [string]$Scope = "src/PurlieuEcs",
  [int]$SleepSeconds = 6
)

$ErrorActionPreference = "Stop"
$Root = "C:\Purlieu.Ecs"
Set-Location $Root
$LogDir = Join-Path $Root ".ecsmind_logs"
$Cmd = "claude"
$TemplatePath = Join-Path $Root ".claude\commands\ecsmind2_headless.min.md"

# --- ensure dirs ---
if (!(Test-Path $LogDir)) { New-Item -ItemType Directory -Force -Path $LogDir | Out-Null }
if (!(Test-Path (Split-Path $TemplatePath))) { New-Item -ItemType Directory -Force -Path (Split-Path $TemplatePath) | Out-Null }

# --- create a minimal headless template if missing ---
if (!(Test-Path $TemplatePath)) {
@"
# ECSMind2 (Headless, Single Turn)
You are running non-interactively. Hard rules:
- One response only. Simulate all “rounds” internally; no follow-up questions.
- No tools (bash/web/edit/git/fs/exec). If a tool would help, print commands/diffs inline as text.
- Keep it concise and actionable. End with a literal line `END`.

## Baseline
- Stateless systems; pure logic; no DI.
- Components are `struct`s; no engine refs; no heap-only collections.
- Storage is archetype+chunk SoA; queries are zero-alloc once built.
- No reflection in hot paths (init/codegen only).
- Events/Intents are one-frame and cleared post-processing.
- Engine/visual bridges live outside the ECS assembly.

## Roles (internal debate)
Core Architect, API Designer, Data & Perf, Query Eng, Test Lead, Tooling & DX, Release Manager, Integration Eng, Red Team.

## Protocol (simulate in one response)
Round 0: Local scan (≤5 bullets) scoped to `{scope}` from repo root `C:\Purlieu.Ecs`.
Round 1: Exactly 2 candidate answers to `{question}` (≤3 sentences each).
Rounds 2..N: Each role adds one short note per round; Red Team attacks both options every round.
Finalization: pick a single winner (or BLOCKED with exact files:lines). Compute Local Fit Score 0–10 using `{weights}` if given.
Deliverables (always):
- Decision (1 paragraph)
- Why (3 bullets)
- Checklist (6 concrete steps for this week)
- Tests (5 test names)
- Patches (1–3 small unified diffs if obvious)
- Risks table (High | Medium | Low + mitigation)
End with `END`.
"@ | Set-Content -Encoding UTF8 $TemplatePath
    Write-Host "Created minimal template at $TemplatePath"
}

# --- quick CLI ping (fail fast) ---
try {
  $pong = & $Cmd -p "ping" 2>&1
  if ($pong -notmatch "pong") { throw "Claude CLI didn’t return 'pong'." }
  Write-Host "CLI OK" -ForegroundColor Green
} catch {
  Write-Error "Claude CLI not available. Ensure 'claude -p ""ping""' works in $Root. $_"
  exit 1
}

function Write-PromptFile([string]$Label, [string]$Body) {
  $p = Join-Path $LogDir ("prompt_{0}_{1}.txt" -f $Label,(New-Guid).Guid.Replace("-",""))
  $Body | Set-Content -Encoding UTF8 $p
  return $p
}

function Extract-Decision([string]$Text) {
  $re = New-Object System.Text.RegularExpressions.Regex('Decision:\s*(.+?)(\r?\n\r?\n|Why:|Checklist:|Tests:|Patches:|Risks|END)', 'Singleline, IgnoreCase')
  $m = $re.Match($Text)
  if ($m.Success) {
    $d = $m.Groups[1].Value.Trim()
    if ($d.Length -gt 200) { $d.Substring(0,200) } else { $d }
  } else {
    $flat = ($Text -replace '\s+',' ')
    if ($flat.Length -gt 200) { $flat.Substring(0,200) } else { $flat }
  }
}

function Invoke-ClaudeOnce([string]$Prompt, [switch]$Continue, [int]$TimeoutSec = 180) {
  # PS 5.1: no ternary — do it explicitly
  if ($Continue) { $label = "impl" } else { $label = "steps" }
  $file = Write-PromptFile $label $Prompt

  if ($Continue) { $args = "--continue -p --output-format text" } else { $args = "-p --output-format text" }

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = "cmd.exe"
  $psi.Arguments = "/c type `"$file`" | $Cmd $args"
  $psi.WorkingDirectory = $Root
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true
  $psi.RedirectStandardInput  = $true
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true

  $p = New-Object System.Diagnostics.Process
  $p.StartInfo = $psi
  $null = $p.Start()
  try { $p.StandardInput.Close() } catch {}

  $outTask = $p.StandardOutput.ReadToEndAsync()
  $errTask = $p.StandardError.ReadToEndAsync()

  if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    try { $p.Kill() } catch {}
    return @{ Code = -1; Out = ""; Err = "TIMEOUT after $TimeoutSec s" }
  }

  $out = $outTask.Result
  $err = $errTask.Result
  return @{ Code = $p.ExitCode; Out = $out; Err = $err }
}

# --------- LOOP ---------
$prevDecision = $null

for ($i=1; $i -le $Iterations; $i++) {
  Write-Host "`n--- Iteration $i ---" -ForegroundColor Cyan

  $spec = Get-Content $TemplatePath -Raw

  # Build the previous_decision line for PS 5.1 (no ternary)
  if ([string]::IsNullOrWhiteSpace($prevDecision)) {
    $prevLine = ""
  } else {
    $san = $prevDecision.Replace('"','''').Replace("`r"," ").Replace("`n"," ")
    $prevLine = "previous_decision=""$san"""
  }

  # Steps: debate+decision+deliverables in ONE shot
  $stepsPrompt = @"
$spec

Params:
question="What are the next logical steps for the Purlieu ECS combat/damage foundation?"
rounds=3
scope=$Scope
$prevLine
"@

  $steps = Invoke-ClaudeOnce -Prompt $stepsPrompt -TimeoutSec 180
  $stepsOutPath = Join-Path $LogDir ("iter{0:00}_steps_stdout.txt" -f $i)
  $stepsErrPath = Join-Path $LogDir ("iter{0:00}_steps_stderr.txt" -f $i)
  $steps.Out | Set-Content -Encoding UTF8 $stepsOutPath
  $steps.Err | Set-Content -Encoding UTF8 $stepsErrPath

  if ($steps.Code -ne 0) {
    Write-Host "[steps] exit $($steps.Code). See logs:`n$stepsOutPath`n$stepsErrPath" -ForegroundColor Red
    break
  }

  # Extract decision to carry forward
  $prevDecision = Extract-Decision $steps.Out
  Write-Host "[decision] $prevDecision" -ForegroundColor Yellow

  # Implementation pass (still one shot; continue context)
  $implPrompt = "Let's implement those steps. Provide exact file paths under C:\Purlieu.Ecs and small unified diffs where obvious. End with END."
  $impl = Invoke-ClaudeOnce -Prompt $implPrompt -Continue -TimeoutSec 300
  $implOutPath = Join-Path $LogDir ("iter{0:00}_impl_stdout.txt" -f $i)
  $implErrPath = Join-Path $LogDir ("iter{0:00}_impl_stderr.txt" -f $i)
  $impl.Out | Set-Content -Encoding UTF8 $implOutPath
  $impl.Err | Set-Content -Encoding UTF8 $implErrPath

  if ($impl.Code -ne 0) {
    Write-Host "[impl] exit $($impl.Code). See logs:`n$implOutPath`n$implErrPath" -ForegroundColor Red
    break
  }

  Start-Sleep -Seconds $SleepSeconds
}

Write-Host "`nDone. Logs in $LogDir" -ForegroundColor Green

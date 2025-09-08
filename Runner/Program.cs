using System.Diagnostics;
using System.Text;

class Program
{
    private const string WorkDir = @"C:\Purlieu.Ecs";
    private static readonly string CommandPath = Path.Combine(WorkDir, ".claude", "commands", "ecsmind.md");
    private static readonly string ClaudeCmd =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd");

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Runner: " + typeof(Program).Assembly.Location);
        Console.WriteLine("Build UTC: " + DateTime.UtcNow.ToString("O"));
        Console.WriteLine("WD: " + WorkDir);

        if (!File.Exists(CommandPath))
        {
            Console.Error.WriteLine($"Missing agent file: {CommandPath}");
            return 2;
        }
        string agentSpec = await File.ReadAllTextAsync(CommandPath, Encoding.UTF8);

        int iterations = (args.Length > 0 && int.TryParse(args[0], out var n)) ? n : 25;

        var invoke = await DetectClaudeInvokerOrDie();

        // Prove CLI is callable
        await MustSucceed("preflight-ping", "cmd.exe", $"/c {invoke} -p " + Quote("ping"), 20);

        Directory.CreateDirectory(Path.Combine(WorkDir, ".ecsmind_logs"));

        for (int i = 1; i <= iterations; i++)
        {
            Console.WriteLine($"\n--- Iteration {i} ---");

            // 1) Ask for next steps (fresh conversation; NO slash)
            string stepsPrompt =
                agentSpec + "\n\n" +
                "You are ECSMind. Task: Given the current Purlieu ECS repo at C:\\Purlieu.Ecs, what are the next logical steps?\n" +
                "Return ONLY a concise numbered list of steps.";
            string stepsArgs = $"/c {invoke} -p --output-format text --max-turns 40 " + Quote(stepsPrompt);
            var stepsExit = await ExecStreaming("steps", "cmd.exe", stepsArgs, i, 180);
            if (stepsExit != 0) return stepsExit;

            // 2) Implement those steps (continue same conversation)
            string implPrompt =
                "Implement those steps now. For each step, provide exact file paths under C:\\Purlieu.Ecs " +
                "and either a unified diff patch or a full code block to paste. Add a one-sentence rationale per step.";
            string implArgs = $"/c {invoke} --continue -p --output-format text --max-turns 80 " + Quote(implPrompt);
            var implExit = await ExecStreaming("impl", "cmd.exe", implArgs, i, 300);
            if (implExit != 0) return implExit;
        }

        return 0;
    }

    private static async Task<string> DetectClaudeInvokerOrDie()
    {
        if (await TryExitCode("/c claude -v") == 0) return "claude";
        if (File.Exists(ClaudeCmd) && await TryExitCode("/c " + Quote(ClaudeCmd) + " -v") == 0) return Quote(ClaudeCmd);
        throw new Exception("Cannot run Claude CLI. Ensure `claude -p \"ping\"` works in C:\\Purlieu.Ecs.");
    }

    private static async Task<int> TryExitCode(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = args,
            WorkingDirectory = WorkDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    private static async Task MustSucceed(string label, string file, string args, int secondsTimeout)
    {
        var code = await ExecStreaming(label, file, args, iteration: 0, secondsTimeout: secondsTimeout);
        if (code != 0) throw new Exception($"{label} failed (exit {code}).");
    }

    private static async Task<int> ExecStreaming(string label, string file, string args, int iteration, int secondsTimeout)
    {
        string logDir = Path.Combine(WorkDir, ".ecsmind_logs");
        Directory.CreateDirectory(logDir);
        string outPath = Path.Combine(logDir, $"iter{iteration:00}_{label}_stdout.txt");
        string errPath = Path.Combine(logDir, $"iter{iteration:00}_{label}_stderr.txt");

        using var outWriter = new StreamWriter(outPath, append: false, Encoding.UTF8);
        using var errWriter = new StreamWriter(errPath, append: false, Encoding.UTF8);

        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = WorkDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // close so CLI can’t wait for input
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Console.WriteLine($"[{label}] {file} {args}");
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutTcs = new TaskCompletionSource();
        var stderrTcs = new TaskCompletionSource();

        p.OutputDataReceived += async (_, e) =>
        {
            if (e.Data is null) { stdoutTcs.TrySetResult(); return; }
            Console.WriteLine(e.Data);
            await outWriter.WriteLineAsync(e.Data);
            await outWriter.FlushAsync();
        };
        p.ErrorDataReceived += async (_, e) =>
        {
            if (e.Data is null) { stderrTcs.TrySetResult(); return; }
            Console.Error.WriteLine(e.Data);
            await errWriter.WriteLineAsync(e.Data);
            await errWriter.FlushAsync();
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { p.StandardInput.Close(); } catch { }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(secondsTimeout));
        var waitExit = WaitForExitAsync(p, cts.Token);

        try
        {
            await Task.WhenAll(waitExit, stdoutTcs.Task, stderrTcs.Task);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            Console.Error.WriteLine($"[{label}] TIMEOUT after {secondsTimeout}s");
            return -1;
        }

        Console.WriteLine($"[{label}] exit {p.ExitCode}. Logs: {outPath} | {errPath}");
        return p.ExitCode;
    }

    private static async Task WaitForExitAsync(Process p, CancellationToken token)
    {
        while (!p.HasExited) await Task.Delay(50, token);
    }

    private static string Quote(string s) => $"\"{s.Replace("\"", "\\\"")}\"";
}

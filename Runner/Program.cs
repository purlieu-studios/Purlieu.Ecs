using System.Diagnostics;

class Program
{
    static async Task Main(string[] args)
    {
        int iterations = (args.Length > 0 && int.TryParse(args[0], out var n)) ? n : 25;

        // Try to resolve claude path up-front so we fail early with a helpful message
        string? claudePath = await ResolveClaudePath();
        if (claudePath is null)
        {
            Console.Error.WriteLine(
                "Could not find the 'claude' CLI. Fix one of these:\n" +
                "  • Ensure 'claude' runs in a fresh CMD window (PATH)\n" +
                "  • Or set env var CLAUDE_PATH to the full path (e.g., C:\\Users\\You\\AppData\\Local\\Programs\\claude\\claude.exe)\n" +
                "  • Or install via your package manager so it’s on PATH\n");
            Environment.Exit(1);
        }

        for (int i = 1; i <= iterations; i++)
        {
            Console.WriteLine($"\n--- Iteration {i} ---");

            // 1) Ask for next steps
            string response1 = await RunClaude(claudePath, "/ecsmind what do you think the next logical steps are?");
            Console.WriteLine("Claude (steps):\n" + response1);

            // 2) Ask to implement those steps
            string response2 = await RunClaude(claudePath, "Let's implement those steps");
            Console.WriteLine("Claude (implementation):\n" + response2);
        }
    }

    // Prefer direct path if CLAUDE_PATH is set; else try `where claude`; else null
    static async Task<string?> ResolveClaudePath()
    {
        var env = Environment.GetEnvironmentVariable("CLAUDE_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        // Use `where claude` via cmd to respect PATHEXT and PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c where claude",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string stdout = await p.StandardOutput.ReadToEndAsync();
            string _ = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            // Take the first non-empty existing path
            foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Trim();
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { /* ignore and fall through */ }

        // If `where` didn’t find it, return null — caller will print guidance
        return null;
    }

    static async Task<string> RunClaude(string claudePathOrName, string prompt)
    {
        var psi = new ProcessStartInfo
        {
            FileName = claudePathOrName,
            Arguments = QuoteArg(prompt),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // ✅ Force the working directory to the repo root
            WorkingDirectory = @"C:\Purlieu.Ecs"
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception($"claude exited with code {process.ExitCode}.\nSTDERR:\n{error}");

        return output.Trim();
    }
    static string QuoteArg(string s)
    {
        // wrap in quotes and escape internal quotes for Windows cmd
        return $"\"{s.Replace("\"", "\\\"")}\"";
    }
}

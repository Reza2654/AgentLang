using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using AgentLang.VM;

namespace AgentLang.Cli;

public static class WatchCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent watch <file.agt> [--run]");
            Console.ResetColor();
            return 1;
        }

        string filePath = Path.GetFullPath(args[1]);
        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File '{filePath}' not found.");
            Console.ResetColor();
            return 1;
        }

        bool shouldRun = args.Any(a => a.Equals("--run", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"⚡ AgentLang Hot-Reload Watcher active on: {Path.GetFileName(filePath)}");
        Console.WriteLine($"   Directory: {Path.GetDirectoryName(filePath)}");
        Console.WriteLine($"   Mode: {(shouldRun ? "Check & Execute (--run)" : "Check Only")}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("   Press Ctrl+C to stop watching...\n");
        Console.ResetColor();

        // Initial check
        await EvaluateFileAsync(filePath, shouldRun);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        string dir = Path.GetDirectoryName(filePath)!;
        string fileName = Path.GetFileName(filePath);

        using var watcher = new FileSystemWatcher(dir, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        var debounceLock = new object();
        DateTime lastTrigger = DateTime.MinValue;

        watcher.Changed += async (s, e) =>
        {
            lock (debounceLock)
            {
                if (DateTime.UtcNow - lastTrigger < TimeSpan.FromMilliseconds(250))
                {
                    return;
                }
                lastTrigger = DateTime.UtcNow;
            }

            // Small delay to let file handle release
            await Task.Delay(50);
            await EvaluateFileAsync(filePath, shouldRun);
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        Console.WriteLine("\nStopped watching.");
        return 0;
    }

    public static async Task<bool> EvaluateFileAsync(string filePath, bool shouldRun)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{timestamp}] Checking {Path.GetFileName(filePath)}...");
        Console.ResetColor();

        string source;
        try
        {
            source = await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [Warn] Failed to read file: {ex.Message}");
            Console.ResetColor();
            return false;
        }

        var sourceText = new SourceText(source, filePath);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        if (parser.Diagnostics.HasErrors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Syntax Errors ({parser.Diagnostics.Diagnostics.Count}):");
            foreach (var diag in parser.Diagnostics.Diagnostics)
            {
                Console.WriteLine($"  - {diag.Span.Start.Line}:{diag.Span.Start.Column} [{diag.Id}]: {diag.Message}");
            }
            Console.ResetColor();
            return false;
        }

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        if (analyzer.Diagnostics.HasErrors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Semantic Errors ({analyzer.Diagnostics.Diagnostics.Count}):");
            foreach (var diag in analyzer.Diagnostics.Diagnostics)
            {
                Console.WriteLine($"  - {diag.Span.Start.Line}:{diag.Span.Start.Column} [{diag.Id}]: {diag.Message}");
            }
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Syntax & semantic checks passed with 0 errors.");
        Console.ResetColor();

        if (shouldRun)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("--- Running Program Output ---");
            Console.ResetColor();

            try
            {
                var outputWriter = new StringWriter();
                var runtime = new AgentLangRuntime(output: outputWriter);
                await runtime.ExecuteProgramAsync(program);

                string outText = outputWriter.ToString().TrimEnd();
                if (!string.IsNullOrEmpty(outText))
                {
                    Console.WriteLine(outText);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("(No console output generated)");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Runtime Error: {ex.Message}");
                Console.ResetColor();
                return false;
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("------------------------------\n");
            Console.ResetColor();
        }

        return true;
    }
}

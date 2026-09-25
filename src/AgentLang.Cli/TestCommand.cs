using System.Diagnostics;
using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Security;

namespace AgentLang.Cli;

public static class TestCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string target = args.Length > 1 ? args[1] : ".";
        var files = new List<string>();

        if (File.Exists(target))
        {
            files.Add(target);
        }
        else if (Directory.Exists(target))
        {
            files.AddRange(Directory.GetFiles(target, "*test*.agent", SearchOption.AllDirectories));
            if (files.Count == 0)
            {
                string testsDir = Path.Combine(target, "tests");
                if (Directory.Exists(testsDir))
                {
                    files.AddRange(Directory.GetFiles(testsDir, "*.agent", SearchOption.AllDirectories));
                }
            }
        }

        if (files.Count == 0)
        {
            Console.WriteLine("No test files found matching '*test*.agent' or in 'tests/' directory.");
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Running {files.Count} AgentLang test suite(s)...\n");
        Console.ResetColor();

        int passed = 0;
        int failed = 0;
        var totalSw = Stopwatch.StartNew();

        foreach (var file in files)
        {
            string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);
            var sw = Stopwatch.StartNew();

            try
            {
                string code = await File.ReadAllTextAsync(file);
                var src = new SourceText(code, file);
                var parser = new Parser.Parser(src);
                var prog = parser.ParseProgram();

                if (parser.Diagnostics.HasErrors)
                {
                    sw.Stop();
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"✗ FAIL: {relPath} ({sw.ElapsedMilliseconds} ms)");
                    Console.ResetColor();
                    foreach (var diag in parser.Diagnostics.Errors)
                    {
                        Console.WriteLine($"    Parser error [{diag.Code}]: {diag.Message}");
                    }
                    continue;
                }

                var sec = new SecurityEngine(new AutoApprovalProvider(true));
                var runtime = new AgentLangRuntime(securityEngine: sec, output: TextWriter.Null);
                await runtime.ExecuteProgramAsync(prog);

                sw.Stop();
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ PASS: {relPath} ({sw.ElapsedMilliseconds} ms)");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                sw.Stop();
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ FAIL: {relPath} ({sw.ElapsedMilliseconds} ms)");
                Console.ResetColor();
                Console.WriteLine($"    {ex.Message}");
            }
        }

        totalSw.Stop();
        Console.WriteLine();
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Test run succeeded! Passed: {passed}, Failed: {failed}, Total: {files.Count} ({totalSw.ElapsedMilliseconds} ms)");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Test run failed! Passed: {passed}, Failed: {failed}, Total: {files.Count} ({totalSw.ElapsedMilliseconds} ms)");
            Console.ResetColor();
            return 1;
        }
    }
}

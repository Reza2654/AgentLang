using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Semantic;

namespace AgentLang.Cli;

public static class CheckCommand
{
    public static int Run(string[] args)
    {
        string target = args.Length > 1 ? args[1] : ".";
        var files = new List<string>();

        if (File.Exists(target))
        {
            files.Add(target);
        }
        else if (Directory.Exists(target))
        {
            files.AddRange(Directory.GetFiles(target, "*.agt", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(target, "*.agent", SearchOption.AllDirectories));
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Target '{target}' does not exist.");
            Console.ResetColor();
            return 1;
        }

        if (files.Count == 0)
        {
            Console.WriteLine($"No .agt or .agent files found in '{target}'.");
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Checking {files.Count} AgentLang file(s)...\n");
        Console.ResetColor();

        int totalErrors = 0;
        int totalWarnings = 0;

        foreach (var file in files)
        {
            string code = File.ReadAllText(file);
            var source = new SourceText(code, file);
            var parser = new Parser.Parser(source);
            var program = parser.ParseProgram();

            var sem = new SemanticAnalyzer();
            sem.Analyze(program);

            int errors = parser.Diagnostics.Errors.Count() + sem.Diagnostics.Errors.Count();
            int warnings = parser.Diagnostics.Warnings.Count() + sem.Diagnostics.Warnings.Count();

            totalErrors += errors;
            totalWarnings += warnings;

            string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);

            if (errors == 0 && warnings == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("✓ ");
                Console.ResetColor();
                Console.WriteLine(relPath);
            }
            else
            {
                Console.ForegroundColor = errors > 0 ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.Write(errors > 0 ? "✗ " : "⚠ ");
                Console.ResetColor();
                Console.WriteLine($"{relPath} ({errors} errors, {warnings} warnings)");

                foreach (var diag in parser.Diagnostics.Concat(sem.Diagnostics))
                {
                    Console.ForegroundColor = diag.Severity == DiagnosticSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
                    Console.WriteLine($"  [{diag.Code}] Line {diag.Span.Start.Line}, Col {diag.Span.Start.Column}: {diag.Message}");
                    if (diag.Suggestion != null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"    Hint: {diag.Suggestion}");
                    }
                    Console.ResetColor();
                }
            }
        }

        Console.WriteLine();
        if (totalErrors == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"All files passed check! ({files.Count} files, {totalWarnings} warnings)");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Check failed: {totalErrors} error(s), {totalWarnings} warning(s)");
            Console.ResetColor();
            return 1;
        }
    }
}

using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Semantic;

namespace AgentLang.Cli;

public static class CheckCommand
{
    public static int Run(string[] args)
    {
        bool jsonFormat = args.Any(a => a is "--json" or "-j") ||
                          args.Zip(args.Skip(1), (a, b) => a == "--format" && b.Equals("json", StringComparison.OrdinalIgnoreCase)).Any(x => x);

        string target = ".";
        foreach (var arg in args.Skip(1))
        {
            if (arg is "--json" or "-j" or "--format" || arg.Equals("json", StringComparison.OrdinalIgnoreCase))
                continue;
            target = arg;
            break;
        }

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
            if (jsonFormat)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Target '{target}' does not exist.",
                    files = Array.Empty<object>()
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Target '{target}' does not exist.");
                Console.ResetColor();
            }
            return 1;
        }

        if (files.Count == 0)
        {
            if (jsonFormat)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"No .agt or .agent files found in '{target}'.",
                    files = Array.Empty<object>()
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"No .agt or .agent files found in '{target}'.");
            }
            return 0;
        }

        if (!jsonFormat)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Checking {files.Count} AgentLang file(s)...\n");
            Console.ResetColor();
        }

        int totalErrors = 0;
        int totalWarnings = 0;
        var jsonFileReports = new List<object>();

        foreach (var file in files)
        {
            string code = File.ReadAllText(file);
            var source = new SourceText(code, file);
            var parser = new Parser.Parser(source);
            var program = parser.ParseProgram();

            var sem = new SemanticAnalyzer();
            sem.Analyze(program);

            var allDiagnostics = parser.Diagnostics.Concat(sem.Diagnostics).ToList();
            int errors = allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            int warnings = allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

            totalErrors += errors;
            totalWarnings += warnings;

            string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);

            if (jsonFormat)
            {
                jsonFileReports.Add(new
                {
                    file = relPath,
                    errors,
                    warnings,
                    diagnostics = allDiagnostics.Select(d => new
                    {
                        code = d.Id,
                        severity = d.Severity.ToString().ToLowerInvariant(),
                        message = d.Message,
                        line = d.Span.Start.Line,
                        column = d.Span.Start.Column,
                        length = d.Span.Length,
                        snippet = d.Span.Start.Line > 0 ? source.GetLineText(d.Span.Start.Line) : string.Empty,
                        suggestion = d.Suggestion
                    })
                });
            }
            else
            {
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

                    foreach (var diag in allDiagnostics)
                    {
                        Console.ForegroundColor = diag.Severity == DiagnosticSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
                        Console.WriteLine(source.FormatDiagnostic(diag));
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                }
            }
        }

        if (jsonFormat)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                success = totalErrors == 0,
                total_files = files.Count,
                total_errors = totalErrors,
                total_warnings = totalWarnings,
                files = jsonFileReports
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return totalErrors == 0 ? 0 : 1;
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

using System.Text;
using System.Text.Json;
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
                WriteSimpleJsonError($"Target '{target}' does not exist.");
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
                using var emptyMs = new MemoryStream();
                using (var writer = new Utf8JsonWriter(emptyMs, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteBoolean("success", true);
                    writer.WriteString("message", $"No .agt or .agent files found in '{target}'.");
                    writer.WriteStartArray("files");
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                Console.WriteLine(Encoding.UTF8.GetString(emptyMs.ToArray()));
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
        var jsonFileReports = new List<(string File, int Errors, int Warnings, SourceText Source, List<Diagnostic> Diagnostics)>();

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
                jsonFileReports.Add((relPath, errors, warnings, source, allDiagnostics));
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
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("success", totalErrors == 0);
                writer.WriteNumber("total_files", files.Count);
                writer.WriteNumber("total_errors", totalErrors);
                writer.WriteNumber("total_warnings", totalWarnings);
                writer.WriteStartArray("files");
                foreach (var rep in jsonFileReports)
                {
                    writer.WriteStartObject();
                    writer.WriteString("file", rep.File);
                    writer.WriteNumber("errors", rep.Errors);
                    writer.WriteNumber("warnings", rep.Warnings);
                    writer.WriteStartArray("diagnostics");
                    foreach (var d in rep.Diagnostics)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("code", d.Id);
                        writer.WriteString("severity", d.Severity.ToString().ToLowerInvariant());
                        writer.WriteString("message", d.Message);
                        writer.WriteNumber("line", d.Span.Start.Line);
                        writer.WriteNumber("column", d.Span.Start.Column);
                        writer.WriteNumber("length", d.Span.Length);
                        writer.WriteString("snippet", d.Span.Start.Line > 0 ? rep.Source.GetLineText(d.Span.Start.Line) : string.Empty);
                        if (d.Suggestion is not null)
                            writer.WriteString("suggestion", d.Suggestion);
                        else
                            writer.WriteNull("suggestion");
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            Console.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
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

    private static void WriteSimpleJsonError(string error)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("success", false);
            writer.WriteString("error", error);
            writer.WriteStartArray("files");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }
}

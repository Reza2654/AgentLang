using System.Collections.Concurrent;
using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Semantic;

namespace AgentLang.VM.Lsp;

public sealed class DocumentAnalysisResult
{
    public required string Uri { get; init; }
    public required string Text { get; init; }
    public required ProgramNode? Ast { get; init; }
    public required List<LspDiagnostic> Diagnostics { get; init; }
}

public sealed class LspDocumentManager
{
    private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DocumentAnalysisResult> _cachedResults = new(StringComparer.OrdinalIgnoreCase);

    public void UpdateDocument(string uri, string text)
    {
        _documents[uri] = text;
        _cachedResults[uri] = Analyze(uri, text);
    }

    public void RemoveDocument(string uri)
    {
        _documents.TryRemove(uri, out _);
        _cachedResults.TryRemove(uri, out _);
    }

    public string? GetDocumentText(string uri) =>
        _documents.TryGetValue(uri, out var text) ? text : null;

    public DocumentAnalysisResult? GetAnalysis(string uri)
    {
        if (_cachedResults.TryGetValue(uri, out var res)) return res;
        if (_documents.TryGetValue(uri, out var text))
        {
            var analyzed = Analyze(uri, text);
            _cachedResults[uri] = analyzed;
            return analyzed;
        }
        return null;
    }

    public DocumentAnalysisResult Analyze(string uri, string text)
    {
        var diagnostics = new List<LspDiagnostic>();
        var sourceText = new SourceText(text, uri);
        var parser = new Parser.Parser(sourceText);

        ProgramNode? program = null;
        try
        {
            program = parser.ParseProgram();
        }
        catch (Exception ex)
        {
            diagnostics.Add(new LspDiagnostic
            {
                Code = "AL0001",
                Message = $"Parser crash: {ex.Message}",
                Severity = LspDiagnosticSeverity.Error,
                Range = new LspRange(0, 0, 0, 1)
            });
        }

        // Add Parser Diagnostics
        foreach (var diag in parser.Diagnostics.Diagnostics)
        {
            diagnostics.Add(ToLspDiagnostic(diag));
        }

        // Run Semantic Analyzer if AST was generated
        if (program != null)
        {
            try
            {
                var analyzer = new SemanticAnalyzer();
                analyzer.Analyze(program);

                foreach (var diag in analyzer.Diagnostics.Diagnostics)
                {
                    diagnostics.Add(ToLspDiagnostic(diag));
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new LspDiagnostic
                {
                    Code = "AL0002",
                    Message = $"Semantic analysis error: {ex.Message}",
                    Severity = LspDiagnosticSeverity.Warning,
                    Range = new LspRange(0, 0, 0, 1)
                });
            }
        }

        return new DocumentAnalysisResult
        {
            Uri = uri,
            Text = text,
            Ast = program,
            Diagnostics = diagnostics
        };
    }

    private static LspDiagnostic ToLspDiagnostic(Diagnostic diag)
    {
        int startLine = Math.Max(0, diag.Span.Start.Line - 1);
        int startChar = Math.Max(0, diag.Span.Start.Column - 1);
        int endLine = Math.Max(0, diag.Span.End.Line - 1);
        int endChar = Math.Max(0, diag.Span.End.Column - 1);

        if (endLine < startLine || (endLine == startLine && endChar <= startChar))
        {
            endLine = startLine;
            endChar = startChar + Math.Max(1, diag.Span.Length);
        }

        var severity = diag.Severity switch
        {
            DiagnosticSeverity.Error => LspDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => LspDiagnosticSeverity.Warning,
            DiagnosticSeverity.Info => LspDiagnosticSeverity.Information,
            _ => LspDiagnosticSeverity.Error
        };

        string msg = diag.Message;
        if (!string.IsNullOrEmpty(diag.Suggestion))
        {
            msg += $" (Suggestion: {diag.Suggestion})";
        }

        return new LspDiagnostic
        {
            Code = diag.Id,
            Severity = severity,
            Message = msg,
            Source = "agentlang",
            Range = new LspRange(startLine, startChar, endLine, endChar)
        };
    }
}

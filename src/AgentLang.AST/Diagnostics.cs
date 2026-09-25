namespace AgentLang.AST;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record Diagnostic(
    string Id,
    string Message,
    DiagnosticSeverity Severity,
    SourceSpan Span,
    string? Suggestion = null)
{
    public override string ToString() =>
        $"{Span}: {Severity.ToString().ToLowerInvariant()} {Id}: {Message}" +
        (Suggestion != null ? $" (Suggestion: {Suggestion})" : "");
}

public sealed class SourceText
{
    public string Content { get; }
    public string FilePath { get; }
    private readonly List<int> _lineStarts;

    public SourceText(string content, string filePath = "<source>")
    {
        Content = content ?? string.Empty;
        FilePath = filePath;
        _lineStarts = ComputeLineStarts(Content);
    }

    private static List<int> ComputeLineStarts(string text)
    {
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                lineStarts.Add(i + 1);
            }
            else if (text[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }
        return lineStarts;
    }

    public string GetLineText(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > _lineStarts.Count)
            return string.Empty;

        int startIndex = _lineStarts[lineNumber - 1];
        int endIndex = lineNumber < _lineStarts.Count ? _lineStarts[lineNumber] : Content.Length;

        string line = Content[startIndex..endIndex];
        return line.TrimEnd('\r', '\n');
    }

    public string FormatDiagnostic(Diagnostic diagnostic)
    {
        var sb = new System.Text.StringBuilder();
        string severityColor = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            _ => "Info"
        };

        sb.AppendLine($"{severityColor}: {diagnostic.Message}");
        sb.AppendLine($"  --> {FilePath}:{diagnostic.Span.Start.Line}:{diagnostic.Span.Start.Column}");

        int lineNum = diagnostic.Span.Start.Line;
        if (lineNum > 0)
        {
            string lineContent = GetLineText(lineNum);
            string lineNumStr = lineNum.ToString();
            string padding = new(' ', lineNumStr.Length);

            sb.AppendLine($"{padding} |");
            sb.AppendLine($"{lineNumStr} | {lineContent}");

            int col = Math.Max(1, diagnostic.Span.Start.Column);
            int length = Math.Max(1, diagnostic.Span.Length);
            // Don't span past the line length
            int underlineLength = Math.Min(length, Math.Max(1, lineContent.Length - col + 1));
            string caretSpaces = new(' ', Math.Max(0, col - 1));
            string carets = new('^', underlineLength);

            sb.AppendLine($"{padding} | {caretSpaces}{carets}");

            if (!string.IsNullOrWhiteSpace(diagnostic.Suggestion))
            {
                sb.AppendLine($"{padding} |");
                sb.AppendLine($"{padding} = suggestion: {diagnostic.Suggestion}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}

public sealed class DiagnosticBag : System.Collections.IEnumerable
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public bool HasWarnings => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);

    public void Report(string id, string message, DiagnosticSeverity severity, SourceSpan span, string? suggestion = null)
    {
        _diagnostics.Add(new Diagnostic(id, message, severity, span, suggestion));
    }

    public void ReportError(string id, string message, SourceSpan span, string? suggestion = null) =>
        Report(id, message, DiagnosticSeverity.Error, span, suggestion);

    public void ReportWarning(string id, string message, SourceSpan span, string? suggestion = null) =>
        Report(id, message, DiagnosticSeverity.Warning, span, suggestion);

    public void AddRange(IEnumerable<Diagnostic> diagnostics) =>
        _diagnostics.AddRange(diagnostics);

    public System.Collections.IEnumerator GetEnumerator() => _diagnostics.GetEnumerator();
}

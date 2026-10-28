using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLang.VM.Lsp;

public sealed class LspRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public sealed class LspResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LspError? Error { get; set; }

    public static LspResponse Success(object? id, object? result) =>
        new() { Id = id, Result = result };

    public static LspResponse Failed(object? id, int code, string message) =>
        new() { Id = id, Error = new LspError { Code = code, Message = message } };
}

public sealed class LspNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

public sealed class LspError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class LspPosition
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("character")]
    public int Character { get; set; }

    public LspPosition() { }
    public LspPosition(int line, int character)
    {
        Line = line;
        Character = character;
    }
}

public sealed class LspRange
{
    [JsonPropertyName("start")]
    public LspPosition Start { get; set; } = new();

    [JsonPropertyName("end")]
    public LspPosition End { get; set; } = new();

    public LspRange() { }
    public LspRange(int startLine, int startChar, int endLine, int endChar)
    {
        Start = new LspPosition(startLine, startChar);
        End = new LspPosition(endLine, endChar);
    }
}

public enum LspDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public sealed class LspDiagnostic
{
    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    [JsonPropertyName("severity")]
    public LspDiagnosticSeverity Severity { get; set; } = LspDiagnosticSeverity.Error;

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "agentlang";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public enum LspCompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25
}

public sealed class LspCompletionItem
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public LspCompletionItemKind Kind { get; set; } = LspCompletionItemKind.Keyword;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("documentation")]
    public object? Documentation { get; set; }

    [JsonPropertyName("insertText")]
    public string? InsertText { get; set; }
}

public sealed class LspHover
{
    [JsonPropertyName("contents")]
    public MarkupContent Contents { get; set; } = new();

    [JsonPropertyName("range")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LspRange? Range { get; set; }
}

public sealed class MarkupContent
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "markdown";

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public enum LspSymbolKind
{
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26
}

public sealed class LspDocumentSymbol
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("kind")]
    public LspSymbolKind Kind { get; set; } = LspSymbolKind.Class;

    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    [JsonPropertyName("selectionRange")]
    public LspRange SelectionRange { get; set; } = new();

    [JsonPropertyName("children")]
    public List<LspDocumentSymbol>? Children { get; set; }
}

public sealed class LspLocation
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    public LspLocation() { }
    public LspLocation(string uri, LspRange range)
    {
        Uri = uri;
        Range = range;
    }
}

public sealed class LspTextEdit
{
    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    [JsonPropertyName("newText")]
    public string NewText { get; set; } = string.Empty;

    public LspTextEdit() { }
    public LspTextEdit(LspRange range, string newText)
    {
        Range = range;
        NewText = newText;
    }
}

public sealed class LspWorkspaceEdit
{
    [JsonPropertyName("changes")]
    public Dictionary<string, List<LspTextEdit>> Changes { get; set; } = new();
}

public sealed class LspCodeAction
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "quickfix";

    [JsonPropertyName("diagnostics")]
    public List<LspDiagnostic>? Diagnostics { get; set; }

    [JsonPropertyName("edit")]
    public LspWorkspaceEdit? Edit { get; set; }
}

using System.Text.Json;
using AgentLang.AST;

namespace AgentLang.VM.Lsp;

public sealed class AgentLspServer
{
    private readonly LspTransport _transport;
    private readonly AgentVirtualMachine _vm;
    public LspDocumentManager Documents { get; } = new();
    public bool IsRunning { get; private set; }

    public AgentLspServer(LspTransport transport, AgentVirtualMachine? vm = null)
    {
        _transport = transport;
        _vm = vm ?? new AgentVirtualMachine();
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        IsRunning = true;
        try
        {
            while (!ct.IsCancellationRequested && IsRunning)
            {
                string? rawMessage = await _transport.ReadMessageAsync(ct);
                if (rawMessage == null) break;

                await HandleMessageAsync(rawMessage, ct);
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    public async Task HandleMessageAsync(string rawMessage, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(rawMessage);
        var root = doc.RootElement;

        if (!root.TryGetProperty("method", out var methodProp))
        {
            return;
        }

        string method = methodProp.GetString() ?? "";
        object? id = null;
        if (root.TryGetProperty("id", out var idProp))
        {
            id = idProp.ValueKind switch
            {
                JsonValueKind.Number => idProp.GetInt64(),
                JsonValueKind.String => idProp.GetString(),
                _ => null
            };
        }

        switch (method)
        {
            case "initialize":
                await HandleInitializeAsync(id, ct);
                break;

            case "initialized":
                // No-op client confirmation
                break;

            case "textDocument/didOpen":
                await HandleDidOpenAsync(root, ct);
                break;

            case "textDocument/didChange":
                await HandleDidChangeAsync(root, ct);
                break;

            case "textDocument/didClose":
                await HandleDidCloseAsync(root, ct);
                break;

            case "textDocument/completion":
                await HandleCompletionAsync(id, root, ct);
                break;

            case "textDocument/hover":
                await HandleHoverAsync(id, root, ct);
                break;

            case "textDocument/documentSymbol":
                await HandleDocumentSymbolAsync(id, root, ct);
                break;

            case "textDocument/definition":
                await HandleDefinitionAsync(id, root, ct);
                break;

            case "textDocument/formatting":
                await HandleFormattingAsync(id, root, ct);
                break;

            case "textDocument/codeAction":
                await HandleCodeActionAsync(id, root, ct);
                break;

            case "shutdown":
                await _transport.SendMessageAsync(JsonSerializer.Serialize(LspResponse.Success(id, null)), ct);
                break;

            case "exit":
                IsRunning = false;
                break;

            default:
                if (id != null)
                {
                    // Return empty success for unhandled optional methods
                    await _transport.SendMessageAsync(JsonSerializer.Serialize(LspResponse.Success(id, null)), ct);
                }
                break;
        }
    }

    private async Task HandleInitializeAsync(object? id, CancellationToken ct)
    {
        var capabilities = new
        {
            capabilities = new
            {
                textDocumentSync = 1, // Full document sync
                completionProvider = new
                {
                    resolveProvider = false,
                    triggerCharacters = new[] { ".", ":", "\"", " " }
                },
                hoverProvider = true,
                documentSymbolProvider = true,
                definitionProvider = true,
                documentFormattingProvider = true,
                codeActionProvider = true
            },
            serverInfo = new
            {
                name = "agentlang-lsp",
                version = "0.8.1"
            }
        };

        var response = LspResponse.Success(id, capabilities);
        await _transport.SendMessageAsync(JsonSerializer.Serialize(response), ct);
    }

    private async Task HandleDidOpenAsync(JsonElement root, CancellationToken ct)
    {
        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp) &&
            td.TryGetProperty("text", out var textProp))
        {
            string uri = uriProp.GetString() ?? "";
            string text = textProp.GetString() ?? "";

            Documents.UpdateDocument(uri, text);
            await PublishDiagnosticsAsync(uri, ct);
        }
    }

    private async Task HandleDidChangeAsync(JsonElement root, CancellationToken ct)
    {
        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp) &&
            p.TryGetProperty("contentChanges", out var cc) &&
            cc.GetArrayLength() > 0)
        {
            string uri = uriProp.GetString() ?? "";
            string text = cc[0].GetProperty("text").GetString() ?? "";

            Documents.UpdateDocument(uri, text);
            await PublishDiagnosticsAsync(uri, ct);
        }
    }

    private async Task HandleDidCloseAsync(JsonElement root, CancellationToken ct)
    {
        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp))
        {
            string uri = uriProp.GetString() ?? "";
            Documents.RemoveDocument(uri);

            // Clear diagnostics on close
            var notification = new LspNotification
            {
                Method = "textDocument/publishDiagnostics",
                Params = new { uri, diagnostics = Array.Empty<LspDiagnostic>() }
            };
            await _transport.SendMessageAsync(JsonSerializer.Serialize(notification), ct);
        }
    }

    private async Task PublishDiagnosticsAsync(string uri, CancellationToken ct)
    {
        var analysis = Documents.GetAnalysis(uri);
        var diagnostics = analysis?.Diagnostics ?? [];

        var notification = new LspNotification
        {
            Method = "textDocument/publishDiagnostics",
            Params = new
            {
                uri,
                diagnostics
            }
        };

        await _transport.SendMessageAsync(JsonSerializer.Serialize(notification), ct);
    }

    private async Task HandleCompletionAsync(object? id, JsonElement root, CancellationToken ct)
    {
        var items = new List<LspCompletionItem>();

        // 1. Language Keywords & Directives
        string[] keywords =
        [
            "agent", "schema", "workflow", "pipeline", "state", "goal", "achieve",
            "decide", "permission", "on event", "budget", "tools", "model",
            "memory", "strategy", "loop", "until", "print", "return", "if", "else"
        ];

        foreach (var kw in keywords)
        {
            items.Add(new LspCompletionItem
            {
                Label = kw,
                Kind = LspCompletionItemKind.Keyword,
                Detail = $"AgentLang keyword: {kw}"
            });
        }

        // 2. Built-in LLM Model Names (Official Production & Frontier Models)
        string[] models =
        [
            // Google Gemini (Official)
            "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite",
            "gemini-3.8-flash", "gemini-3.1-pro-preview", "gemini-4-argon",
            "gemini-2.0-flash",

            // OpenAI (Official)
            "gpt-4o", "gpt-4o-mini", "gpt-4.5-preview",
            "o3-mini", "o3", "o1", "o1-mini",

            // Anthropic Claude (Official)
            "claude-3-7-sonnet", "claude-3-5-sonnet", "claude-3-5-haiku", "claude-3-opus",

            // Meta Llama (Official)
            "llama-3.3-70b-instruct", "llama-3.2-3b-instruct", "llama3.2", "llama-3.1-8b-instruct",

            // DeepSeek (Official API)
            "deepseek-chat", "deepseek-reasoner",

            // Mistral & Qwen (Official)
            "mistral-large-latest", "codestral-latest", "mistral:7b",
            "qwen-2.5-72b-instruct", "qwen-2.5-coder-32b-instruct"
        ];

        foreach (var model in models)
        {
            items.Add(new LspCompletionItem
            {
                Label = $"\"{model}\"",
                Kind = LspCompletionItemKind.Value,
                Detail = $"LLM Model: {model}"
            });
        }

        // 3. Built-in Tools
        string[] tools =
        [
            "web_search", "document_summary", "code_generator", "file_reader", "calculator"
        ];

        foreach (var tool in tools)
        {
            items.Add(new LspCompletionItem
            {
                Label = tool,
                Kind = LspCompletionItemKind.Function,
                Detail = $"Built-in Agent Tool: {tool}"
            });
        }

        // 4. In-document symbols (Declared agents & schemas)
        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp))
        {
            string uri = uriProp.GetString() ?? "";
            var analysis = Documents.GetAnalysis(uri);
            if (analysis?.Ast != null)
            {
                foreach (var decl in analysis.Ast.Declarations)
                {
                    if (decl is AgentDeclarationNode a)
                    {
                        items.Add(new LspCompletionItem
                        {
                            Label = a.Name,
                            Kind = LspCompletionItemKind.Class,
                            Detail = $"Agent '{a.Name}'"
                        });
                    }
                    else if (decl is SchemaDeclarationNode s)
                    {
                        items.Add(new LspCompletionItem
                        {
                            Label = s.Name,
                            Kind = LspCompletionItemKind.Struct,
                            Detail = $"Schema '{s.Name}'"
                        });
                    }
                    else if (decl is WorkflowDeclarationNode w)
                    {
                        items.Add(new LspCompletionItem
                        {
                            Label = w.Name,
                            Kind = LspCompletionItemKind.Function,
                            Detail = $"Workflow '{w.Name}'"
                        });
                    }
                }
            }
        }

        var response = LspResponse.Success(id, items);
        await _transport.SendMessageAsync(JsonSerializer.Serialize(response), ct);
    }

    private async Task HandleHoverAsync(object? id, JsonElement root, CancellationToken ct)
    {
        string hoverMarkdown = "";

        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp) &&
            p.TryGetProperty("position", out var posProp))
        {
            string uri = uriProp.GetString() ?? "";
            int line = posProp.GetProperty("line").GetInt32();
            int character = posProp.GetProperty("character").GetInt32();

            string? text = Documents.GetDocumentText(uri);
            if (!string.IsNullOrEmpty(text))
            {
                string word = GetWordAtPosition(text, line, character);
                hoverMarkdown = GetDocumentationForWord(word, uri);
            }
        }

        if (string.IsNullOrEmpty(hoverMarkdown))
        {
            await _transport.SendMessageAsync(JsonSerializer.Serialize(LspResponse.Success(id, null)), ct);
            return;
        }

        var hoverResult = new LspHover
        {
            Contents = new MarkupContent
            {
                Kind = "markdown",
                Value = hoverMarkdown
            }
        };

        var response = LspResponse.Success(id, hoverResult);
        await _transport.SendMessageAsync(JsonSerializer.Serialize(response), ct);
    }

    private async Task HandleDocumentSymbolAsync(object? id, JsonElement root, CancellationToken ct)
    {
        var symbols = new List<LspDocumentSymbol>();

        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp))
        {
            string uri = uriProp.GetString() ?? "";
            var analysis = Documents.GetAnalysis(uri);

            if (analysis?.Ast != null)
            {
                foreach (var decl in analysis.Ast.Declarations)
                {
                    if (decl is AgentDeclarationNode a)
                    {
                        symbols.Add(new LspDocumentSymbol
                        {
                            Name = a.Name,
                            Kind = LspSymbolKind.Class,
                            Detail = "agent",
                            Range = ToLspRange(a.Span),
                            SelectionRange = ToLspRange(a.Span)
                        });
                    }
                    else if (decl is SchemaDeclarationNode s)
                    {
                        var fieldChildren = s.Fields.Select(f => new LspDocumentSymbol
                        {
                            Name = f.Name,
                            Kind = LspSymbolKind.Field,
                            Detail = f.TypeName + (f.IsArray ? "[]" : ""),
                            Range = ToLspRange(f.Span),
                            SelectionRange = ToLspRange(f.Span)
                        }).ToList();

                        symbols.Add(new LspDocumentSymbol
                        {
                            Name = s.Name,
                            Kind = LspSymbolKind.Struct,
                            Detail = "schema",
                            Range = ToLspRange(s.Span),
                            SelectionRange = ToLspRange(s.Span),
                            Children = fieldChildren
                        });
                    }
                    else if (decl is WorkflowDeclarationNode w)
                    {
                        symbols.Add(new LspDocumentSymbol
                        {
                            Name = w.Name,
                            Kind = LspSymbolKind.Function,
                            Detail = "workflow",
                            Range = ToLspRange(w.Span),
                            SelectionRange = ToLspRange(w.Span)
                        });
                    }
                    else if (decl is PipelineDeclarationNode pip)
                    {
                        symbols.Add(new LspDocumentSymbol
                        {
                            Name = pip.Name,
                            Kind = LspSymbolKind.Module,
                            Detail = "pipeline",
                            Range = ToLspRange(pip.Span),
                            SelectionRange = ToLspRange(pip.Span)
                        });
                    }
                    else if (decl is StateDeclarationNode st)
                    {
                        symbols.Add(new LspDocumentSymbol
                        {
                            Name = st.Name,
                            Kind = LspSymbolKind.Struct,
                            Detail = "state",
                            Range = ToLspRange(st.Span),
                            SelectionRange = ToLspRange(st.Span)
                        });
                    }
                }
            }
        }

        var response = LspResponse.Success(id, symbols);
        await _transport.SendMessageAsync(JsonSerializer.Serialize(response), ct);
    }

    private string GetDocumentationForWord(string word, string uri)
    {
        if (string.IsNullOrWhiteSpace(word)) return string.Empty;

        // 1. Built-in Keywords
        switch (word.ToLowerInvariant())
        {
            case "agent":
                return "### `agent <Name>`\n\nDefines an autonomous AI agent with model, persona, tools, memory, and reasoning strategies.";
            case "schema":
                return "### `schema <Name>`\n\nDefines a type-safe structured data contract with validation constraints (`min`, `max`, `regex`, `required`).";
            case "workflow":
                return "### `workflow <Name>(args) -> Type`\n\nOrchestrates multi-agent team execution with deterministic state transitions.";
            case "pipeline":
                return "### `pipeline <Name>`\n\nDefines linear agent collaboration with evaluation loops (`loop until ...`).";
            case "goal":
                return "### `goal <Name> = \"...\"`\n\nDeclares a high-level task objective to be solved by an agent via `.achieve(goal)`.";
            case "state":
                return "### `state <Name>`\n\nDefines a shared structured state object for team workflows.";
            case "decide":
                return "### `decide (condition) { ... }`\n\nExecutes dynamic semantic reasoning and conditional branching.";
            case "achieve":
                return "### `.achieve(goal)`\n\nExecutes the agent's reasoning loop to fulfill the specified goal objective.";
        }

        // 2. Check in-document AST symbols
        var analysis = Documents.GetAnalysis(uri);
        if (analysis?.Ast != null)
        {
            foreach (var decl in analysis.Ast.Declarations)
            {
                if (decl is AgentDeclarationNode a && a.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                {
                    return $"### Agent `{a.Name}`\n\nAutonomous agent declared in this file.";
                }
                if (decl is SchemaDeclarationNode s && s.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                {
                    return $"### Schema `{s.Name}`\n\nDefined structured contract with {s.Fields.Count} fields.";
                }
                if (decl is WorkflowDeclarationNode w && w.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                {
                    return $"### Workflow `{w.Name}`\n\nDeclared workflow orchestrator.";
                }
            }
        }

        return string.Empty;
    }

    private async Task HandleDefinitionAsync(object? id, JsonElement root, CancellationToken ct)
    {
        LspLocation? location = null;

        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp) &&
            p.TryGetProperty("position", out var posProp))
        {
            string uri = uriProp.GetString() ?? "";
            int line = posProp.GetProperty("line").GetInt32();
            int character = posProp.GetProperty("character").GetInt32();

            string? text = Documents.GetDocumentText(uri);
            if (!string.IsNullOrEmpty(text))
            {
                string word = GetWordAtPosition(text, line, character);
                var analysis = Documents.GetAnalysis(uri);

                if (analysis?.Ast != null && !string.IsNullOrWhiteSpace(word))
                {
                    foreach (var decl in analysis.Ast.Declarations)
                    {
                        if (decl is AgentDeclarationNode a && a.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            location = new LspLocation(uri, ToLspRange(a.Span));
                            break;
                        }
                        if (decl is SchemaDeclarationNode s && s.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            location = new LspLocation(uri, ToLspRange(s.Span));
                            break;
                        }
                        if (decl is WorkflowDeclarationNode w && w.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            location = new LspLocation(uri, ToLspRange(w.Span));
                            break;
                        }
                        if (decl is PipelineDeclarationNode pip && pip.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            location = new LspLocation(uri, ToLspRange(pip.Span));
                            break;
                        }
                        if (decl is StateDeclarationNode st && st.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            location = new LspLocation(uri, ToLspRange(st.Span));
                            break;
                        }
                        if (decl is FunctionDeclarationNode fn && fn.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            location = new LspLocation(uri, ToLspRange(fn.Span));
                            break;
                        }
                    }
                }
            }
        }

        var response = LspResponse.Success(id, location);
        await _transport.SendMessageAsync(JsonSerializer.Serialize(response), ct);
    }

    private async Task HandleFormattingAsync(object? id, JsonElement root, CancellationToken ct)
    {
        var edits = new List<LspTextEdit>();

        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp))
        {
            string uri = uriProp.GetString() ?? "";
            string? text = Documents.GetDocumentText(uri);

            if (!string.IsNullOrEmpty(text))
            {
                string formatted = FormatCode(text);
                var lines = text.Split('\n');
                int endLine = lines.Length - 1;
                int endChar = lines[endLine].TrimEnd('\r').Length;

                edits.Add(new LspTextEdit(new LspRange(0, 0, endLine, endChar), formatted));
            }
        }

        var response = LspResponse.Success(id, edits);
        await _transport.SendMessageAsync(JsonSerializer.Serialize(response), ct);
    }

    public static string FormatCode(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder();
        int indent = 0;
        const string indentStr = "    ";

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine();
                continue;
            }

            bool startsWithClose = line.StartsWith('}') || line.StartsWith(']');
            if (startsWithClose)
            {
                indent = Math.Max(0, indent - 1);
            }

            for (int k = 0; k < indent; k++)
            {
                sb.Append(indentStr);
            }
            sb.AppendLine(line);

            int openCount = line.Count(c => c == '{' || c == '[');
            int closeCount = line.Count(c => c == '}' || c == ']');

            if (!startsWithClose && openCount > closeCount)
            {
                indent += (openCount - closeCount);
            }
            else if (startsWithClose && openCount > (closeCount - 1))
            {
                indent += openCount - (closeCount - 1);
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private async Task HandleCodeActionAsync(object? id, JsonElement root, CancellationToken ct)
    {
        var actions = new List<LspCodeAction>();

        if (root.TryGetProperty("params", out var p) &&
            p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var uriProp) &&
            p.TryGetProperty("context", out var ctxProp) &&
            ctxProp.TryGetProperty("diagnostics", out var diagsProp))
        {
            string uri = uriProp.GetString() ?? "";
            string? text = Documents.GetDocumentText(uri);

            foreach (var diagElem in diagsProp.EnumerateArray())
            {
                string code = diagElem.TryGetProperty("code", out var cProp) ? cProp.GetString() ?? "" : "";

                if (code == "AL0038" && !string.IsNullOrEmpty(text))
                {
                    // Quick fix for duplicate schema field: remove duplicate line
                    var rangeElem = diagElem.GetProperty("range");
                    int line = rangeElem.GetProperty("start").GetProperty("line").GetInt32();

                    var lines = text.Split('\n').ToList();
                    if (line >= 0 && line < lines.Count)
                    {
                        var edit = new LspWorkspaceEdit();
                        edit.Changes[uri] = new List<LspTextEdit>
                        {
                            new LspTextEdit(new LspRange(line, 0, line + 1, 0), "")
                        };

                        actions.Add(new LspCodeAction
                        {
                            Title = "Remove duplicate field declaration",
                            Kind = "quickfix",
                            Edit = edit
                        });
                    }
                }
            }
        }

        var response = LspResponse.Success(id, actions);
        await _transport.SendMessageAsync(JsonSerializer.Serialize(response), ct);
    }

    private static string GetWordAtPosition(string text, int line, int character)
    {
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length) return string.Empty;

        string lineStr = lines[line].TrimEnd('\r');
        if (character < 0 || character >= lineStr.Length) return string.Empty;

        int start = character;
        while (start > 0 && (char.IsLetterOrDigit(lineStr[start - 1]) || lineStr[start - 1] == '_'))
        {
            start--;
        }

        int end = character;
        while (end < lineStr.Length && (char.IsLetterOrDigit(lineStr[end]) || lineStr[end] == '_'))
        {
            end++;
        }

        return lineStr.Substring(start, end - start);
    }

    private static LspRange ToLspRange(SourceSpan span)
    {
        int startLine = Math.Max(0, span.Start.Line - 1);
        int startChar = Math.Max(0, span.Start.Column - 1);
        int endLine = Math.Max(0, span.End.Line - 1);
        int endChar = Math.Max(0, span.End.Column - 1);

        if (endLine < startLine || (endLine == startLine && endChar <= startChar))
        {
            endLine = startLine;
            endChar = startChar + Math.Max(1, span.Length);
        }

        return new LspRange(startLine, startChar, endLine, endChar);
    }
}

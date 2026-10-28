using System.Text;
using System.Text.Json;
using AgentLang.VM.Lsp;
using Xunit;

namespace AgentLang.Tests;

public class V080Tests
{
    private static async Task<string?> ReadResponseAsync(MemoryStream streamOut)
    {
        streamOut.Position = 0;
        using var clientTransport = new LspTransport(streamOut, Stream.Null, leaveOpen: true);
        return await clientTransport.ReadMessageAsync();
    }

    // =========================================================================
    // 1. LSP TRANSPORT & JSON-RPC FRAMING TESTS
    // =========================================================================

    [Fact]
    public async Task LspTransport_ReadAndWriteMessage_FramesCorrectly()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();

        string sampleJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"test\"}";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(sampleJson);
        string header = $"Content-Length: {bodyBytes.Length}\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        await streamIn.WriteAsync(headerBytes);
        await streamIn.WriteAsync(bodyBytes);
        streamIn.Position = 0;

        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);

        // Read message
        string? readJson = await transport.ReadMessageAsync();
        Assert.Equal(sampleJson, readJson);

        // Send message
        string replyJson = "{\"jsonrpc\":\"2.0\",\"result\":\"ok\"}";
        await transport.SendMessageAsync(replyJson);

        streamOut.Position = 0;
        string outText = Encoding.UTF8.GetString(streamOut.ToArray());
        Assert.StartsWith("Content-Length: ", outText);
        Assert.Contains("\r\n\r\n" + replyJson, outText);
    }

    // =========================================================================
    // 2. LSP SERVER INITIALIZATION & CAPABILITIES
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_Initialize_ReturnsCapabilities()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string initReq = """
        {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "processId": 1234
            }
        }
        """;

        await server.HandleMessageAsync(initReq);

        string? rawResponse = await ReadResponseAsync(streamOut);
        Assert.NotNull(rawResponse);

        using var doc = JsonDocument.Parse(rawResponse);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt64());

        var result = root.GetProperty("result");
        var capabilities = result.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("hoverProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("documentSymbolProvider").GetBoolean());

        var serverInfo = result.GetProperty("serverInfo");
        Assert.Equal("agentlang-lsp", serverInfo.GetProperty("name").GetString());
        Assert.StartsWith("0.8.", serverInfo.GetProperty("version").GetString());
    }

    // =========================================================================
    // 3. LSP DIAGNOSTICS FOR SYNTAX & SEMANTIC ERRORS
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_DidOpen_PublishesDiagnostics_ForSemanticErrors()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string badCode = """
        schema SecurityProfile {
            email: string,
            email: string
        }
        """;

        var didOpenReq = new
        {
            jsonrpc = "2.0",
            method = "textDocument/didOpen",
            @params = new
            {
                textDocument = new
                {
                    uri = "file:///workspace/profile.agt",
                    languageId = "agentlang",
                    version = 1,
                    text = badCode
                }
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(didOpenReq));

        string? notificationJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(notificationJson);

        using var doc = JsonDocument.Parse(notificationJson);
        var root = doc.RootElement;

        Assert.Equal("textDocument/publishDiagnostics", root.GetProperty("method").GetString());
        var @params = root.GetProperty("params");
        Assert.Equal("file:///workspace/profile.agt", @params.GetProperty("uri").GetString());

        var diags = @params.GetProperty("diagnostics");
        Assert.True(diags.GetArrayLength() > 0);

        bool foundAl0038 = false;
        foreach (var d in diags.EnumerateArray())
        {
            if (d.TryGetProperty("code", out var code) && code.GetString() == "AL0038")
            {
                foundAl0038 = true;
                Assert.Contains("Duplicate field 'email'", d.GetProperty("message").GetString());
            }
        }
        Assert.True(foundAl0038, "Expected AL0038 duplicate field diagnostic");
    }

    // =========================================================================
    // 4. LSP COMPLETIONS (KEYWORDS, MODELS, SYMBOLS)
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_Completion_ReturnsKeywordsAndDocumentSymbols()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string docUri = "file:///workspace/test.agt";
        string docCode = """
        schema MetricsData {
            cpu: float
        }

        agent SentinelAgent {
        }
        """;

        // Feed document
        server.Documents.UpdateDocument(docUri, docCode);

        var completionReq = new
        {
            jsonrpc = "2.0",
            id = 42,
            method = "textDocument/completion",
            @params = new
            {
                textDocument = new { uri = docUri },
                position = new { line = 5, character = 0 }
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(completionReq));

        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);

        using var doc = JsonDocument.Parse(respJson);
        var items = doc.RootElement.GetProperty("result");
        Assert.True(items.GetArrayLength() > 0);

        var labels = items.EnumerateArray()
            .Select(i => i.GetProperty("label").GetString())
            .ToList();

        // Check keywords
        Assert.Contains("agent", labels);
        Assert.Contains("schema", labels);
        Assert.Contains("workflow", labels);

        // Check frontier & local models
        Assert.Contains("\"gpt-4o\"", labels);
        Assert.Contains("\"claude-3-7-sonnet\"", labels);
        Assert.Contains("\"gemini-2.5-pro\"", labels);
        Assert.Contains("\"llama3.2\"", labels);

        // Check in-document symbols
        Assert.Contains("MetricsData", labels);
        Assert.Contains("SentinelAgent", labels);
    }

    // =========================================================================
    // 5. LSP HOVER DOCUMENTATION
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_Hover_ReturnsDocumentationForKeyword()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string docUri = "file:///workspace/hover.agt";
        string docCode = "schema UserReport {\n    title: string\n}";
        server.Documents.UpdateDocument(docUri, docCode);

        var hoverReq = new
        {
            jsonrpc = "2.0",
            id = 10,
            method = "textDocument/hover",
            @params = new
            {
                textDocument = new { uri = docUri },
                position = new { line = 0, character = 2 } // hovering on 'schema'
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(hoverReq));

        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);

        using var doc = JsonDocument.Parse(respJson);
        var result = doc.RootElement.GetProperty("result");
        var contents = result.GetProperty("contents");
        string markdown = contents.GetProperty("value").GetString() ?? "";

        Assert.Contains("schema <Name>", markdown);
        Assert.Contains("structured data contract", markdown);
    }

    // =========================================================================
    // 6. LSP DOCUMENT SYMBOLS OUTLINE
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_DocumentSymbol_ReturnsHierarchy()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string docUri = "file:///workspace/symbols.agt";
        string docCode = """
        schema InventoryRecord {
            sku: string,
            qty: int
        }

        agent WarehouseManager {
        }
        """;

        server.Documents.UpdateDocument(docUri, docCode);

        var symbolReq = new
        {
            jsonrpc = "2.0",
            id = 77,
            method = "textDocument/documentSymbol",
            @params = new
            {
                textDocument = new { uri = docUri }
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(symbolReq));

        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);

        using var doc = JsonDocument.Parse(respJson);
        var symbols = doc.RootElement.GetProperty("result");
        Assert.Equal(2, symbols.GetArrayLength());

        var schemaSymbol = symbols[0];
        Assert.Equal("InventoryRecord", schemaSymbol.GetProperty("name").GetString());
        var children = schemaSymbol.GetProperty("children");
        Assert.Equal(2, children.GetArrayLength());
        Assert.Equal("sku", children[0].GetProperty("name").GetString());
        Assert.Equal("qty", children[1].GetProperty("name").GetString());

        var agentSymbol = symbols[1];
        Assert.Equal("WarehouseManager", agentSymbol.GetProperty("name").GetString());
    }

    // =========================================================================
    // 7. LSP SHUTDOWN AND EXIT LIFECYCLE
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_ShutdownAndExit_HandlesGracefully()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        // Send shutdown
        await server.HandleMessageAsync("{\"jsonrpc\":\"2.0\",\"id\":99,\"method\":\"shutdown\"}");
        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);
        using var doc = JsonDocument.Parse(respJson);
        Assert.Equal(99, doc.RootElement.GetProperty("id").GetInt64());

        // Send exit
        await server.HandleMessageAsync("{\"jsonrpc\":\"2.0\",\"method\":\"exit\"}");
        Assert.False(server.IsRunning);
    }

    [Fact]
    public void ModelRegistry_FrontierModelsAndAliases_ResolveCorrectly()
    {
        var registry = new AgentLang.Models.ModelRegistry();

        // Direct frontier model name resolution
        var gpt4Provider = registry.Resolve("gpt-4o");
        Assert.Equal("openai", gpt4Provider.ProviderId);

        var claudeSonnetProvider = registry.Resolve("claude-3-7-sonnet");
        Assert.Equal("anthropic", claudeSonnetProvider.ProviderId);

        // Alias resolution
        Assert.Equal("gpt-4o", registry.ResolveAlias("gpt"));
        Assert.Equal("gpt-4o", registry.ResolveAlias("gpt4"));
        Assert.Equal("claude-3-7-sonnet", registry.ResolveAlias("claude"));
        Assert.Equal("claude-3-7-sonnet", registry.ResolveAlias("sonnet"));

        var aliasProvider = registry.Resolve("claude");
        Assert.Equal("anthropic", aliasProvider.ProviderId);
    }
}

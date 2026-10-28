using System.Net.Http.Json;
using System.Text.Json;
using AgentLang.Cli;
using AgentLang.Models;
using AgentLang.VM;
using AgentLang.VM.Durable;
using AgentLang.VM.Lsp;
using AgentLang.VM.Server;
using Xunit;

namespace AgentLang.Tests;

public class V081Tests
{
    private static async Task<string?> ReadResponseAsync(MemoryStream streamOut)
    {
        streamOut.Position = 0;
        using var clientTransport = new LspTransport(streamOut, Stream.Null, leaveOpen: true);
        return await clientTransport.ReadMessageAsync();
    }

    // =========================================================================
    // 1. GEMINI 3.0 & FRONTIER MODEL ALIASES AND RESOLUTION
    // =========================================================================

    [Fact]
    public void ModelRegistry_Gemini3LineupAndAliases_ResolveCorrectly()
    {
        var registry = new ModelRegistry();

        // 1. Gemini 3.0 / 3.1 / 3.5 direct resolution
        var g3 = registry.Resolve("gemini-3.0-pro");
        Assert.Equal("gemini", g3.ProviderId);

        var g3Ultra = registry.Resolve("gemini-3.1-ultra");
        Assert.Equal("gemini", g3Ultra.ProviderId);

        var g3Flash = registry.Resolve("gemini-3.0-flash");
        Assert.Equal("gemini", g3Flash.ProviderId);

        var g3Omni = registry.Resolve("gemini-3.5-omni");
        Assert.Equal("gemini", g3Omni.ProviderId);

        // 2. Gemini Aliases
        Assert.Equal("gemini-3.0-pro", registry.ResolveAlias("gemini3"));
        Assert.Equal("gemini-3.0-pro", registry.ResolveAlias("gemini-3"));
        Assert.Equal("gemini-3.0-pro", registry.ResolveAlias("gemini"));
        Assert.Equal("gemini-3.0-pro", registry.ResolveAlias("gemini-pro"));
        Assert.Equal("gemini-3.1-ultra", registry.ResolveAlias("gemini-ultra"));
        Assert.Equal("gemini-3.0-flash", registry.ResolveAlias("gemini-flash"));
        Assert.Equal("gemini-3.5-omni", registry.ResolveAlias("gemini-omni"));

        // 3. Llama 4 Aliases
        Assert.Equal("llama-4-maverick", registry.ResolveAlias("llama4"));
        Assert.Equal("llama-4-maverick", registry.ResolveAlias("llama-4"));

        // 4. Default providers check
        var geminiProvider = Assert.IsType<GeminiModelProvider>(registry.GetProvider("gemini"));
        Assert.NotNull(geminiProvider);

        var openAiProvider = Assert.IsType<OpenAiModelProvider>(registry.GetProvider("openai"));
        Assert.NotNull(openAiProvider);

        var anthropicProvider = Assert.IsType<AnthropicModelProvider>(registry.GetProvider("anthropic"));
        Assert.NotNull(anthropicProvider);
    }

    // =========================================================================
    // 2. LSP DEFINITION PROVIDER (GO TO DEFINITION)
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_Definition_JumpsToDeclaration()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string docUri = "file:///workspace/app.agt";
        string docCode = """
        schema UserAccount {
            username: string,
            active: bool
        }

        agent AuditorAgent {
            model: "gemini-3.0-pro"
        }

        main {
            agent AuditorAgent
        }
        """;

        server.Documents.UpdateDocument(docUri, docCode);

        // Request definition on 'AuditorAgent' inside main (line 10, character 12)
        var defReq = new
        {
            jsonrpc = "2.0",
            id = 101,
            method = "textDocument/definition",
            @params = new
            {
                textDocument = new { uri = docUri },
                position = new { line = 10, character = 12 }
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(defReq));

        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);

        using var doc = JsonDocument.Parse(respJson);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal(docUri, result.GetProperty("uri").GetString());

        var range = result.GetProperty("range");
        // AuditorAgent declaration is at line 5 (0-indexed)
        Assert.Equal(5, range.GetProperty("start").GetProperty("line").GetInt32());
    }

    // =========================================================================
    // 3. LSP CODE FORMATTING
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_Formatting_ReturnsNormalizedIndentation()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string docUri = "file:///workspace/format_test.agt";
        string unformatted = "schema BadIndent {\nfieldA: string,\n    fieldB: int\n}";
        server.Documents.UpdateDocument(docUri, unformatted);

        var formatReq = new
        {
            jsonrpc = "2.0",
            id = 202,
            method = "textDocument/formatting",
            @params = new
            {
                textDocument = new { uri = docUri },
                options = new { tabSize = 4, insertSpaces = true }
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(formatReq));

        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);

        using var doc = JsonDocument.Parse(respJson);
        var edits = doc.RootElement.GetProperty("result");
        Assert.True(edits.GetArrayLength() > 0);

        string newText = edits[0].GetProperty("newText").GetString() ?? "";
        Assert.Contains("    fieldA: string,", newText);
        Assert.Contains("    fieldB: int", newText);
        Assert.EndsWith("}", newText.TrimEnd());
    }

    // =========================================================================
    // 4. LSP CODE ACTIONS (QUICK FIXES)
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_CodeAction_ProvidesQuickFixForDuplicateField()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string docUri = "file:///workspace/duplicate.agt";
        string code = "schema TestDup {\n    key: string,\n    key: string\n}";
        server.Documents.UpdateDocument(docUri, code);

        var codeActionReq = new
        {
            jsonrpc = "2.0",
            id = 303,
            method = "textDocument/codeAction",
            @params = new
            {
                textDocument = new { uri = docUri },
                range = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 15 } },
                context = new
                {
                    diagnostics = new[]
                    {
                        new
                        {
                            code = "AL0038",
                            message = "Duplicate field 'key' in schema 'TestDup'",
                            range = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 15 } }
                        }
                    }
                }
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(codeActionReq));

        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);

        using var doc = JsonDocument.Parse(respJson);
        var actions = doc.RootElement.GetProperty("result");
        Assert.True(actions.GetArrayLength() > 0);

        var firstAction = actions[0];
        Assert.Contains("Remove duplicate field", firstAction.GetProperty("title").GetString());
        Assert.Equal("quickfix", firstAction.GetProperty("kind").GetString());
    }

    // =========================================================================
    // 5. HOT-RELOAD WATCH COMMAND EVALUATION
    // =========================================================================

    [Fact]
    public async Task WatchCommand_EvaluateFile_DetectsValidAndInvalidScripts()
    {
        string tempValidFile = Path.Combine(Path.GetTempPath(), "agentlang_watch_valid_" + Guid.NewGuid().ToString("N") + ".agt");
        string tempInvalidFile = Path.Combine(Path.GetTempPath(), "agentlang_watch_invalid_" + Guid.NewGuid().ToString("N") + ".agt");

        try
        {
            // Valid code
            string validCode = """
            schema ValidSchema {
                id: int
            }

            main {
                val = 42
                print("Result: " + val)
            }
            """;
            await File.WriteAllTextAsync(tempValidFile, validCode);

            bool validResult = await WatchCommand.EvaluateFileAsync(tempValidFile, shouldRun: true);
            Assert.True(validResult);

            // Invalid code (unclosed brace)
            string invalidCode = """
            main {
                print("Missing brace"
            """;
            await File.WriteAllTextAsync(tempInvalidFile, invalidCode);

            bool invalidResult = await WatchCommand.EvaluateFileAsync(tempInvalidFile, shouldRun: false);
            Assert.False(invalidResult);
        }
        finally
        {
            if (File.Exists(tempValidFile)) File.Delete(tempValidFile);
            if (File.Exists(tempInvalidFile)) File.Delete(tempInvalidFile);
        }
    }

    // =========================================================================
    // 6. HTTP SERVER DURABLE WORKFLOW REST API (FILTER, GET BY ID, DELETE)
    // =========================================================================

    [Fact]
    public async Task AgentHttpServer_DurableRestEndpoints_SupportFilteringAndManagement()
    {
        int port = 9181;
        var vm = new AgentVirtualMachine();

        // 1. Create a suspended execution
        var suspended1 = await vm.Durable.StartAsync("CreditCheck", stepCtx =>
        {
            stepCtx.SetVariable("score", 720);
            stepCtx.Suspend(SuspensionReason.HumanApprovalRequired, "CreditOfficerApproval");
            return Task.CompletedTask;
        });

        // 2. Create a completed execution
        var completed1 = await vm.Durable.StartAsync("QuickReport", stepCtx =>
        {
            stepCtx.Complete(new { status = "done" });
            return Task.CompletedTask;
        });

        using var server = new AgentHttpServer(port, vm);
        using var cts = new CancellationTokenSource();
        await server.StartAsync(cts.Token);

        using var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

        try
        {
            // A. Filter by status: ?status=Suspended
            var filterResp = await client.GetAsync("/api/v1/durable?status=Suspended");
            Assert.True(filterResp.IsSuccessStatusCode);
            string filterJson = await filterResp.Content.ReadAsStringAsync();
            using var filterDoc = JsonDocument.Parse(filterJson);
            Assert.True(filterDoc.RootElement.GetArrayLength() > 0);
            foreach (var elem in filterDoc.RootElement.EnumerateArray())
            {
                Assert.Equal("Suspended", elem.GetProperty("status").GetString());
            }

            // B. GET single execution by ID
            var singleResp = await client.GetAsync($"/api/v1/durable/{suspended1.ExecutionId}");
            Assert.True(singleResp.IsSuccessStatusCode);
            string singleJson = await singleResp.Content.ReadAsStringAsync();
            using var singleDoc = JsonDocument.Parse(singleJson);
            Assert.Equal("CreditCheck", singleDoc.RootElement.GetProperty("workflowName").GetString());
            Assert.Equal(suspended1.ExecutionId, singleDoc.RootElement.GetProperty("executionId").GetString());

            // C. DELETE execution
            var deleteResp = await client.DeleteAsync($"/api/v1/durable/{completed1.ExecutionId}");
            Assert.True(deleteResp.IsSuccessStatusCode);

            // Verify deletion: GET now returns 404
            var getDeleted = await client.GetAsync($"/api/v1/durable/{completed1.ExecutionId}");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, getDeleted.StatusCode);
        }
        finally
        {
            server.Stop();
        }
    }
}

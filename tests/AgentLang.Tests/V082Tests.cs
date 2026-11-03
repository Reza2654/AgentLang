using System.Text.Json;
using AgentLang.AST;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Configuration;
using AgentLang.Semantic;
using AgentLang.VM;
using AgentLang.VM.Lsp;
using Xunit;

namespace AgentLang.Tests;

public class V082Tests
{
    private static async Task<string> RunCodeAsync(string code)
    {
        var sourceText = new SourceText(code);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var sem = new SemanticAnalyzer();
        sem.Analyze(program);

        using var sw = new StringWriter();
        var runtime = new AgentLangRuntime(output: sw);
        await runtime.ExecuteProgramAsync(program);
        return sw.ToString().Trim();
    }

    private static async Task<string?> ReadResponseAsync(MemoryStream streamOut)
    {
        streamOut.Position = 0;
        using var clientTransport = new LspTransport(streamOut, Stream.Null, leaveOpen: true);
        return await clientTransport.ReadMessageAsync();
    }

    // =========================================================================
    // 1. FRONTIER MODEL REGISTRY & DEEPSEEK PROVIDER RESOLUTION
    // =========================================================================

    [Fact]
    public void ModelRegistry_FrontierCatalogAndDeepSeek_ResolvesCorrectly()
    {
        var registry = new ModelRegistry();

        // Google Gemini 4 & 3.8
        var g4 = registry.Resolve("gemini-4-argon");
        Assert.Equal("gemini", g4.ProviderId);
        Assert.Equal("gemini-4-argon", registry.ResolveAlias("argon"));
        Assert.Equal("gemini-4-argon", registry.ResolveAlias("gemini4"));
        Assert.Equal("gemini-4-carbon", registry.ResolveAlias("carbon"));
        Assert.Equal("gemini-3.8-flash", registry.ResolveAlias("gemini-3-flash"));

        // OpenAI GPT-6 & Reasoning
        var gpt6 = registry.Resolve("gpt-6-astra");
        Assert.Equal("openai", gpt6.ProviderId);
        Assert.Equal("gpt-6-astra", registry.ResolveAlias("astra"));
        Assert.Equal("gpt-6-astra", registry.ResolveAlias("gpt6"));
        Assert.Equal("gpt-6.1-sol", registry.ResolveAlias("sol"));
        Assert.Equal("o4-mini", registry.ResolveAlias("o4"));

        // Anthropic Claude 5.5 & Fable
        var claude5 = registry.Resolve("claude-sonnet-5.5");
        Assert.Equal("anthropic", claude5.ProviderId);
        Assert.Equal("claude-sonnet-5.5", registry.ResolveAlias("claude5"));
        Assert.Equal("claude-opus-5.5", registry.ResolveAlias("opus5"));
        Assert.Equal("claude-fable-5.1", registry.ResolveAlias("fable"));

        // DeepSeek Native Provider
        var deepseekChat = registry.Resolve("deepseek-chat");
        Assert.Equal("deepseek", deepseekChat.ProviderId);
        var deepseekV4 = registry.Resolve("deepseek-v4");
        Assert.Equal("deepseek", deepseekV4.ProviderId);
        Assert.Equal("deepseek-chat", registry.ResolveAlias("deepseek"));
        Assert.Equal("deepseek-v4", registry.ResolveAlias("deepseek4"));
        Assert.Equal("deepseek-reasoner", registry.ResolveAlias("deepseek-r1"));

        // Meta Llama 4
        Assert.Equal("llama-4-maverick", registry.ResolveAlias("llama4"));
    }

    // =========================================================================
    // 2. STANDARD LIBRARY STRING & TEXT UTILITIES
    // =========================================================================

    [Fact]
    public async Task StandardLibrary_TextFunctions_ExecuteCorrectly()
    {
        string code = """
            let raw = "   AgentLang Autonomous AI   "
            let trimmed = trim(raw)
            let upper = to_upper(trimmed)
            let lower = to_lower(trimmed)
            let has_ai = contains(trimmed, "AI")
            let starts = starts_with(trimmed, "Agent")
            let ends = ends_with(trimmed, "AI")
            let replaced = text.replace(trimmed, "Autonomous", "Intelligent")

            print("Trimmed:", trimmed)
            print("Upper:", upper)
            print("Lower:", lower)
            print("HasAI:", has_ai)
            print("Starts:", starts)
            print("Ends:", ends)
            print("Replaced:", replaced)
            """;

        string output = await RunCodeAsync(code);
        Assert.Contains("Trimmed: AgentLang Autonomous AI", output);
        Assert.Contains("Upper: AGENTLANG AUTONOMOUS AI", output);
        Assert.Contains("Lower: agentlang autonomous ai", output);
        Assert.Contains("HasAI: True", output);
        Assert.Contains("Starts: True", output);
        Assert.Contains("Ends: True", output);
        Assert.Contains("Replaced: AgentLang Intelligent AI", output);
    }

    // =========================================================================
    // 3. STANDARD LIBRARY JSON PARSING & STRINGIFICATION
    // =========================================================================

    [Fact]
    public async Task StandardLibrary_JsonFunctions_ExecuteCorrectly()
    {
        string code = """
            let json_str = "{\"model\": \"gemini-4-argon\", \"score\": 95, \"verified\": true}"
            let is_valid = json.is_valid(json_str)
            let parsed = json.parse(json_str)
            let model_name = parsed["model"]
            let score = parsed["score"]
            let serialized = json.stringify(parsed)

            print("Valid:", is_valid)
            print("Model:", model_name)
            print("Score:", score)
            print("Serialized:", serialized)
            """;

        string output = await RunCodeAsync(code);
        Assert.Contains("Valid: True", output);
        Assert.Contains("Model: gemini-4-argon", output);
        Assert.Contains("Score: 95", output);
        Assert.Contains("Serialized:", output);
    }

    // =========================================================================
    // 4. STANDARD LIBRARY MATH & COLLECTIONS UTILITIES
    // =========================================================================

    [Fact]
    public async Task StandardLibrary_MathAndCollections_ExecuteCorrectly()
    {
        string code = """
            let rounded = math.round(3.14159, 2)
            let minimum = math.min(15, 8)
            let maximum = math.max(15, 8)
            let absolute = math.abs(-99)
            let clamped = math.clamp(150, 0, 100)

            let items = ["alpha", "beta", "gamma"]
            let count = len(items)
            let has_beta = contains(items, "beta")
            let joined = join(items, " -> ")

            print("Round:", rounded)
            print("Min:", minimum)
            print("Max:", maximum)
            print("Abs:", absolute)
            print("Clamp:", clamped)
            print("Count:", count)
            print("HasBeta:", has_beta)
            print("Joined:", joined)
            """;

        string output = await RunCodeAsync(code);
        Assert.Contains("Round: 3.14", output);
        Assert.Contains("Min: 8", output);
        Assert.Contains("Max: 15", output);
        Assert.Contains("Abs: 99", output);
        Assert.Contains("Clamp: 100", output);
        Assert.Contains("Count: 3", output);
        Assert.Contains("HasBeta: True", output);
        Assert.Contains("Joined: alpha -> beta -> gamma", output);
    }

    // =========================================================================
    // 5. PROJECT CONFIGURATION SYSTEM (agent.config.json)
    // =========================================================================

    [Fact]
    public void ProjectConfig_CreateAndSerialize_AppliesSettingsCorrectly()
    {
        var config = ProjectConfig.CreateDefault("sentinel-agent");
        Assert.Equal("sentinel-agent", config.Name);
        Assert.Equal("gemini-4-argon", config.DefaultModel);
        Assert.Equal("30s", config.Timeout);
        Assert.Equal(10.0, config.BudgetLimitUsd);

        config.Env["TEST_AGENT_KEY"] = "secret_agent_value_123";
        config.ApplyEnvironment();

        Assert.Equal("secret_agent_value_123", Environment.GetEnvironmentVariable("TEST_AGENT_KEY"));

        string json = config.ToJson();
        Assert.Contains("\"name\": \"sentinel-agent\"", json);
        Assert.Contains("\"default_model\": \"gemini-4-argon\"", json);

        string tempPath = Path.Combine(Path.GetTempPath(), $"agent.config.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempPath, json);
            var reloaded = ProjectConfig.LoadFromFile(tempPath);
            Assert.Equal("sentinel-agent", reloaded.Name);
            Assert.Equal("gemini-4-argon", reloaded.DefaultModel);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // =========================================================================
    // 6. LSP RENAME SYMBOL (textDocument/rename)
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_RenameSymbol_ReturnsWorkspaceEditsForAllOccurrences()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string uri = "file:///workspace/agent_test.agt";
        string code = """
            agent Sentinel {
                model: "gemini-4-argon"
            }

            task audit = "Audit system logs"
            Sentinel.solve(audit)
            """;

        server.Documents.UpdateDocument(uri, code);

        // Rename 'Sentinel' at line 0, char 6 to 'SecuritySentinel'
        string renameRequest = """
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "textDocument/rename",
                "params": {
                    "textDocument": { "uri": "file:///workspace/agent_test.agt" },
                    "position": { "line": 0, "character": 6 },
                    "newName": "SecuritySentinel"
                }
            }
            """;

        await server.HandleMessageAsync(renameRequest);

        string? responseJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(responseJson);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        var result = root.GetProperty("result");
        var changes = result.GetProperty("changes");
        var edits = changes.GetProperty(uri);

        Assert.Equal(2, edits.GetArrayLength()); // Declaration & usage
        Assert.Equal("SecuritySentinel", edits[0].GetProperty("newText").GetString());
        Assert.Equal("SecuritySentinel", edits[1].GetProperty("newText").GetString());
    }

    // =========================================================================
    // 7. LSP FIND ALL REFERENCES (textDocument/references)
    // =========================================================================

    [Fact]
    public async Task AgentLspServer_FindReferences_ReturnsAllLocations()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string uri = "file:///workspace/ref_test.agt";
        string code = """
            agent Assistant {
                model: "claude-sonnet-5.5"
            }

            Assistant.solve("Task 1")
            Assistant.solve("Task 2")
            """;

        server.Documents.UpdateDocument(uri, code);

        // Find references for 'Assistant' at line 0, char 7
        string refRequest = """
            {
                "jsonrpc": "2.0",
                "id": 2,
                "method": "textDocument/references",
                "params": {
                    "textDocument": { "uri": "file:///workspace/ref_test.agt" },
                    "position": { "line": 0, "character": 7 }
                }
            }
            """;

        await server.HandleMessageAsync(refRequest);

        string? responseJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(responseJson);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        var result = root.GetProperty("result");

        Assert.Equal(3, result.GetArrayLength()); // 1 declaration + 2 invocations
        Assert.Equal(uri, result[0].GetProperty("uri").GetString());
    }

    // =========================================================================
    // 8. HUMAN-FRIENDLY COMPILER DIAGNOSTICS ("DID YOU MEAN?")
    // =========================================================================

    [Fact]
    public void CompilerDiagnostics_TypoSuggestion_EmitsDidYouMeanWithFormattedSnippet()
    {
        string code = """
            agent TestAgent {
                model: "gemini-4-argon"
                tools: [websearch]
            }
            """;

        var sourceText = new SourceText(code, "test.agt");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var sem = new SemanticAnalyzer();
        sem.Analyze(program);

        Assert.True(sem.Diagnostics.HasErrors);
        var error = sem.Diagnostics.Errors.First();

        Assert.Contains("websearch", error.Message);
        Assert.NotNull(error.Suggestion);
        Assert.Contains("web_search", error.Suggestion);

        string formatted = sourceText.FormatDiagnostic(error);
        Assert.Contains("Error: Unknown tool 'websearch'", formatted);
        Assert.Contains("test.agt:3:", formatted);
        Assert.Contains("^^^^^^^^^", formatted);
        Assert.Contains("= suggestion: did you mean 'web_search'?", formatted);
    }
}

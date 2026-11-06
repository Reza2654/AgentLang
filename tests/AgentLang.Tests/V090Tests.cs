using System.Text.Json;
using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Errors;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Configuration;
using AgentLang.Semantic;
using AgentLang.VM.Streaming;
using Xunit;

namespace AgentLang.Tests;

public class V090Tests
{
    // =========================================================================
    // 1. AI & HUMAN-READABLE DIAGNOSTICS ENGINE
    // =========================================================================

    [Fact]
    public void Diagnostics_FormatDiagnosticsJson_EmitsValidMachineReadableJson()
    {
        string code = """
        agent Researcher (
            tools = [brower]
        ) { }
        """;
        var sourceText = new SourceText(code, "test_agent.agent");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();
        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);

        Assert.True(semantic.Diagnostics.HasErrors);
        string json = sourceText.FormatDiagnosticsJson(semantic.Diagnostics);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("test_agent.agent", root.GetProperty("file").GetString());
        Assert.True(root.GetProperty("has_errors").GetBoolean());

        var diagnostics = root.GetProperty("diagnostics");
        Assert.True(diagnostics.GetArrayLength() > 0);

        var errorDiag = diagnostics.EnumerateArray().First(d => d.GetProperty("severity").GetString() == "error");
        Assert.False(string.IsNullOrEmpty(errorDiag.GetProperty("code").GetString()));
        Assert.Equal("error", errorDiag.GetProperty("severity").GetString());
        Assert.Contains("brower", errorDiag.GetProperty("message").GetString());
        Assert.Equal(2, errorDiag.GetProperty("line").GetInt32());
    }

    [Fact]
    public void Diagnostics_FormatDiagnostic_ContainsDiagnosticCodeInHeader()
    {
        string code = """
        agent Researcher (
            tools = [brower]
        ) { }
        """;
        var sourceText = new SourceText(code, "calc.agent");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();
        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);

        var error = semantic.Diagnostics.Errors.First();
        string formatted = sourceText.FormatDiagnostic(error);

        Assert.Contains($"[{error.Id}]", formatted);
        Assert.Contains("calc.agent:2:", formatted);
    }

    // =========================================================================
    // 2. FAIL-FAST AUTHENTICATION & ZERO-MOCK BEHAVIOR
    // =========================================================================

    [Fact]
    public async Task Providers_FailFast_WhenMissingApiKeyAndNoMockAllowed()
    {
        // 1. OpenAI
        var openAi = new OpenAiModelProvider(apiKey: "", allowMockFallback: false);
        openAi.AllowMockFallback = false;
        var openAiRes = await openAi.GenerateAsync(new ModelRequest("gpt-4o", "Hello"));
        Assert.False(openAiRes.Success);
        Assert.Contains("[AL-AUTH-001]", openAiRes.ErrorMessage);
        Assert.Contains("OPENAI_API_KEY", openAiRes.ErrorMessage);

        // 2. Gemini
        var gemini = new GeminiModelProvider(apiKey: "", allowMockFallback: false);
        gemini.AllowMockFallback = false;
        var geminiRes = await gemini.GenerateAsync(new ModelRequest("gemini-2.5-pro", "Hello"));
        Assert.False(geminiRes.Success);
        Assert.Contains("[AL-AUTH-002]", geminiRes.ErrorMessage);
        Assert.Contains("GEMINI_API_KEY", geminiRes.ErrorMessage);

        // 3. Claude
        var claude = new AnthropicModelProvider(apiKey: "", allowMockFallback: false);
        claude.AllowMockFallback = false;
        var claudeRes = await claude.GenerateAsync(new ModelRequest("claude-3-5-sonnet", "Hello"));
        Assert.False(claudeRes.Success);
        Assert.Contains("[AL-AUTH-003]", claudeRes.ErrorMessage);
        Assert.Contains("ANTHROPIC_API_KEY", claudeRes.ErrorMessage);

        // 4. DeepSeek
        var deepseek = new DeepSeekModelProvider(apiKey: "", allowMockFallback: false);
        deepseek.AllowMockFallback = false;
        var deepseekRes = await deepseek.GenerateAsync(new ModelRequest("deepseek-chat", "Hello"));
        Assert.False(deepseekRes.Success);
        Assert.Contains("[AL-AUTH-004]", deepseekRes.ErrorMessage);
        Assert.Contains("DEEPSEEK_API_KEY", deepseekRes.ErrorMessage);
    }

    // =========================================================================
    // 3. REAL-TIME TOKEN STREAMING ENGINE
    // =========================================================================

    [Fact]
    public async Task MockModelProvider_StreamAsync_YieldsTokensProgressively()
    {
        var mock = new MockModelProvider();
        var request = new ModelRequest("mock", "What are latest AI trends?");

        var tokens = new List<string>();
        await foreach (var token in mock.StreamAsync(request))
        {
            tokens.Add(token);
        }

        Assert.True(tokens.Count > 1, "StreamAsync should yield multiple word-level tokens.");
        string joined = string.Concat(tokens).Trim();
        Assert.Contains("Trend Analysis", joined);
    }

    [Fact]
    public async Task AgentLangRuntime_TokenStreamCallback_ReceivesTokensDuringExecution()
    {
        var runtime = new AgentLangRuntime();
        var streamedTokens = new List<string>();
        runtime.TokenStreamCallback = token => streamedTokens.Add(token);

        string code = """
        agent Assistant {
            model: "mock"
            persona: "Friendly helper"
        }

        response = Assistant.achieve("Analyze trends in 2026")
        """;

        var sourceText = new SourceText(code);
        var program = new Parser.Parser(sourceText).ParseProgram();
        await runtime.ExecuteProgramAsync(program);

        Assert.NotEmpty(streamedTokens);
    }

    [Fact]
    public void Streaming_AgentStreamEvent_ToSseString_FormatsValidServerSentEvents()
    {
        var ev = new AgentStreamEvent(
            AgentStreamEventType.Token,
            "Hello world",
            NodeOrAgent: "Assistant"
        );

        string sse = ev.ToSseString();
        Assert.StartsWith("event: Token\n", sse);
        Assert.Contains("data: {", sse);
        Assert.Contains("\"content\":\"Hello world\"", sse);
        Assert.EndsWith("\n\n", sse);
    }

    // =========================================================================
    // 4. MULTI-FILE IMPORTS & DEPENDENCY ECOSYSTEM
    // =========================================================================

    [Fact]
    public void SemanticAnalyzer_MultiFileImport_ResolvesDeclaredAgentsAcrossFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"agentlang_test_import_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string helperFile = Path.Combine(tempDir, "writer_agent.agent");
            File.WriteAllText(helperFile, """
            agent Writer {
                model: "mock"
                persona: "Technical author"
            }
            """);

            string mainFile = Path.Combine(tempDir, "main.agent");
            File.WriteAllText(mainFile, """
            import "./writer_agent.agent"

            team EditorialTeam {
                coordinator: true
                members: [Writer]
            }
            """);

            var sourceText = new SourceText(File.ReadAllText(mainFile), mainFile);
            var parser = new Parser.Parser(sourceText);
            var program = parser.ParseProgram();

            var semantic = new SemanticAnalyzer();
            semantic.Analyze(program);

            Assert.False(semantic.Diagnostics.HasErrors,
                $"Expected zero errors when importing Writer agent, but got: {string.Join("; ", semantic.Diagnostics.Errors)}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProjectConfig_Dependencies_SerializesAndRestoresCorrectly()
    {
        var config = ProjectConfig.CreateDefault("test-system");
        config.Dependencies["@community/search-tools"] = "^2.0.0";
        config.Dependencies["@agentlang/stdlib"] = "latest";

        string json = config.ToJson();
        Assert.Contains("@community/search-tools", json);
        Assert.Contains("@agentlang/stdlib", json);

        var restored = JsonSerializer.Deserialize<ProjectConfig>(json);
        Assert.NotNull(restored);
        Assert.Equal(3, restored.Dependencies.Count);
        Assert.Equal("^2.0.0", restored.Dependencies["@community/search-tools"]);
    }
}

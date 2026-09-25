using AgentLang.AST;
using AgentLang.Models;
using AgentLang.Security;
using AgentLang.Tools;
using Xunit;

namespace AgentLang.Tests;

public class ModelAndToolTests
{
    [Fact]
    public async Task MockModelProviderGeneratesPredictableResponse()
    {
        var provider = new MockModelProvider();
        Assert.True(provider.CanHandle("mock-1"));

        var response = await provider.GenerateAsync(new ModelRequest("mock", "What are the latest AI trends?"));
        Assert.True(response.Success);
        Assert.Contains("Trend Analysis", response.Content);
        Assert.True(response.TokensUsed > 0);
    }

    [Fact]
    public void ModelRegistryResolvesProviders()
    {
        var registry = new ModelRegistry();
        var openai = new OpenAiModelProvider();
        var gemini = new GeminiModelProvider();
        var anthropic = new AnthropicModelProvider();

        registry.RegisterProvider(openai);
        registry.RegisterProvider(gemini);
        registry.RegisterProvider(anthropic);

        Assert.IsType<OpenAiModelProvider>(registry.Resolve("gpt-4o"));
        Assert.IsType<GeminiModelProvider>(registry.Resolve("gemini-1.5-pro"));
        Assert.IsType<AnthropicModelProvider>(registry.Resolve("claude-3-5-sonnet"));
        Assert.IsType<MockModelProvider>(registry.Resolve("mock"));
    }

    [Fact]
    public async Task FilesystemToolWritesAndReadsFiles()
    {
        var tool = new FilesystemTool();
        string tempFile = Path.Combine(Path.GetTempPath(), $"agentlang_test_{Guid.NewGuid():N}.txt");

        try
        {
            var writeRes = await tool.ExecuteAsync("filesystem.write", new Dictionary<string, object?>
            {
                { "path", tempFile },
                { "content", "Hello from AgentLang!" }
            });
            Assert.True(writeRes.Success);

            var existsRes = await tool.ExecuteAsync("filesystem.exists", new Dictionary<string, object?>
            {
                { "path", tempFile }
            });
            Assert.True(existsRes.Success);
            Assert.Equal(true, existsRes.Output);

            var readRes = await tool.ExecuteAsync("filesystem.read", new Dictionary<string, object?>
            {
                { "path", tempFile }
            });
            Assert.True(readRes.Success);
            Assert.Equal("Hello from AgentLang!", readRes.Output);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ToolRegistryBlocksUnauthorizedExecution()
    {
        var policy = new PermissionPolicy("Strict");
        policy.AddRule(PermissionAction.Cannot, "terminal");

        var sec = new SecurityEngine(new AutoApprovalProvider(false));
        sec.RegisterPolicy(policy);

        var toolRegistry = new ToolRegistry(sec);

        // Invoking terminal should fail with SecurityException
        await Assert.ThrowsAsync<SecurityException>(() =>
            toolRegistry.InvokeAsync("TestAgent", "Strict", "terminal.run", new Dictionary<string, object?>
            {
                { "command", "whoami" }
            }));
    }
}

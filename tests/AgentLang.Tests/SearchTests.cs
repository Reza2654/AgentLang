using System.Text.Json;
using AgentLang.Errors;
using AgentLang.Tools.Search;
using Xunit;

namespace AgentLang.Tests;

public class SearchTests
{
    [Fact]
    public async Task SearchThrowsWhenApiKeyMissing()
    {
        // Ensure no search keys are in environment
        string? oldTavily = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
        string? oldSerper = Environment.GetEnvironmentVariable("SERPER_API_KEY");
        string? oldBrave = Environment.GetEnvironmentVariable("BRAVE_API_KEY");
        string? oldSearch = Environment.GetEnvironmentVariable("SEARCH_API_KEY");
        string? oldMock = Environment.GetEnvironmentVariable("AGENTLANG_MOCK_SEARCH");

        try
        {
            Environment.SetEnvironmentVariable("TAVILY_API_KEY", null);
            Environment.SetEnvironmentVariable("SERPER_API_KEY", null);
            Environment.SetEnvironmentVariable("BRAVE_API_KEY", null);
            Environment.SetEnvironmentVariable("SEARCH_API_KEY", null);
            Environment.SetEnvironmentVariable("AGENTLANG_MOCK_SEARCH", null);

            var registry = new SearchProviderRegistry(allowMockFallback: false);
            var ex = await Assert.ThrowsAsync<AgentLangToolException>(() =>
                registry.SearchAsync("test query"));

            Assert.Equal("AGT500", ex.ErrorCode);
            Assert.Contains("Search API key required", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TAVILY_API_KEY", oldTavily);
            Environment.SetEnvironmentVariable("SERPER_API_KEY", oldSerper);
            Environment.SetEnvironmentVariable("BRAVE_API_KEY", oldBrave);
            Environment.SetEnvironmentVariable("SEARCH_API_KEY", oldSearch);
            Environment.SetEnvironmentVariable("AGENTLANG_MOCK_SEARCH", oldMock);
        }
    }

    [Fact]
    public async Task MockSearchProviderReturnsDeterministicResults()
    {
        var mock = new MockSearchProvider();
        Assert.False(mock.RequiresApiKey);
        Assert.True(mock.HasApiKey);

        var result = await mock.SearchAsync("AgentLang architecture", maxResults: 2);
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains("AgentLang architecture", result.Items[0].Title);
        Assert.False(string.IsNullOrWhiteSpace(result.Items[0].Url));
        Assert.False(string.IsNullOrWhiteSpace(result.Items[0].Snippet));

        string markdown = result.FormatMarkdown();
        Assert.Contains("Search results for 'AgentLang architecture'", markdown);
        Assert.Contains("https://agentlang.org", markdown);
    }

    [Fact]
    public async Task SearchRegistryResolvesMockWhenEnvFlagActive()
    {
        string? oldMock = Environment.GetEnvironmentVariable("AGENTLANG_MOCK_SEARCH");
        try
        {
            Environment.SetEnvironmentVariable("AGENTLANG_MOCK_SEARCH", "1");
            var registry = new SearchProviderRegistry(allowMockFallback: false);
            var response = await registry.SearchAsync("AI programming languages");

            Assert.True(response.Success);
            Assert.Equal("mock", response.ProviderId);
            Assert.NotEmpty(response.Items);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTLANG_MOCK_SEARCH", oldMock);
        }
    }

    [Fact]
    public void TavilySearchProviderRequiresApiKey()
    {
        var tavily = new TavilySearchProvider();
        Assert.True(tavily.RequiresApiKey);
        Assert.Equal("tavily", tavily.ProviderId);
    }

    [Fact]
    public void SerperSearchProviderRequiresApiKey()
    {
        var serper = new SerperSearchProvider();
        Assert.True(serper.RequiresApiKey);
        Assert.Equal("serper", serper.ProviderId);
    }

    [Fact]
    public void BraveSearchProviderRequiresApiKey()
    {
        var brave = new BraveSearchProvider();
        Assert.True(brave.RequiresApiKey);
        Assert.Equal("brave", brave.ProviderId);
    }

    [Fact]
    public void SearchResponseFormatMarkdownHandlesEmptyAndErrors()
    {
        var empty = SearchResponse.Succeeded("test", [], "mock");
        Assert.Equal("No search results found for 'test'.", empty.FormatMarkdown());

        var failed = SearchResponse.Failed("test", "Connection timeout", "tavily");
        Assert.Equal("Search failed (tavily): Connection timeout", failed.FormatMarkdown());
    }

    [Fact]
    public void CleanSnippetRemovesTraceIdAndNavGarbage()
    {
        string raw = "Trace Id is missing. Please contact the administrator. Skip to main content. Microsoft. Home. Home; Surface; Microsoft 365; Teams; Windows; Xbox; Deals; Small Business; Support; Software.";
        string cleaned = SearchResponse.CleanSnippet(raw);

        Assert.DoesNotContain("Trace Id is missing", cleaned);
        Assert.DoesNotContain("Skip to main content", cleaned);
        Assert.DoesNotContain("Please contact the administrator", cleaned);
    }

    [Fact]
    public void SearchResponseFormatsSummaryAndCleanOutput()
    {
        var items = new List<SearchItem>
        {
            new("Microsoft", "https://microsoft.com", "Leading cloud and software company.", null)
        };
        var response = SearchResponse.Succeeded("microsoft", items, "tavily", "Microsoft develops cloud and enterprise software.");
        string markdown = response.FormatMarkdown();

        Assert.Contains("🌐 Search results for 'microsoft' (via tavily):", markdown);
        Assert.Contains("💡 Quick Summary:", markdown);
        Assert.Contains("Microsoft develops cloud and enterprise software.", markdown);
        Assert.Contains("🔗 https://microsoft.com", markdown);
        Assert.Contains("Leading cloud and software company.", markdown);
    }

    [Fact]
    public void SearchProviderRegistrySetsDynamicApiKey()
    {
        var registry = new SearchProviderRegistry();
        registry.SetProviderApiKey("tavily", "tvly-test-12345");
        var tavily = Assert.IsType<TavilySearchProvider>(registry.GetProvider("tavily"));
        Assert.True(tavily.HasApiKey);
    }
}

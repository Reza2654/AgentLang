namespace AgentLang.Tools.Search;

public sealed class MockSearchProvider : ISearchProvider
{
    public string ProviderId => "mock";
    public bool RequiresApiKey => false;
    public bool HasApiKey => true;

    public Task<SearchResponse> SearchAsync(
        string query,
        int maxResults = 5,
        string? explicitApiKey = null,
        CancellationToken ct = default)
    {
        var items = new List<SearchItem>
        {
            new(
                $"Authoritative Source on '{query}'",
                "https://agentlang.org/docs/research",
                $"Detailed verified findings regarding '{query}' confirming native AI agent execution.",
                $"Comprehensive content about '{query}' with verified facts and technical specifications."
            ),
            new(
                $"Technical Specification: '{query}'",
                "https://github.com/Reza2654/AgentLang",
                $"AgentLang core architecture and runtime semantics for '{query}'.",
                $"Reference implementation in C# 14 / .NET 10."
            ),
            new(
                $"Benchmarks and Analysis: '{query}'",
                "https://agentlang.org/benchmarks",
                $"Autonomous performance metrics for '{query}'.",
                $"Evaluation results demonstrating high reliability and zero-trust security."
            )
        };

        if (maxResults > 0 && items.Count > maxResults)
        {
            items = items.Take(maxResults).ToList();
        }

        return Task.FromResult(SearchResponse.Succeeded(query, items, ProviderId));
    }
}

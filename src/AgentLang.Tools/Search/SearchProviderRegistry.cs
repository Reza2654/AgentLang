using AgentLang.Errors;

namespace AgentLang.Tools.Search;

public sealed class SearchProviderRegistry
{
    private readonly Dictionary<string, ISearchProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _allowMockFallback;

    public IReadOnlyCollection<ISearchProvider> Providers => _providers.Values;

    public SearchProviderRegistry(bool allowMockFallback = false)
    {
        _allowMockFallback = allowMockFallback;

        // Register default built-in providers
        RegisterProvider(new TavilySearchProvider());
        RegisterProvider(new SerperSearchProvider());
        RegisterProvider(new BraveSearchProvider());
        RegisterProvider(new MockSearchProvider());
    }

    public void RegisterProvider(ISearchProvider provider)
    {
        _providers[provider.ProviderId] = provider;
    }

    public ISearchProvider? GetProvider(string providerId) =>
        _providers.TryGetValue(providerId, out var p) ? p : null;

    public ISearchProvider ResolveProvider(string? preferredProvider = null, string? explicitApiKey = null)
    {
        // 1. If preferred provider explicitly specified
        if (!string.IsNullOrWhiteSpace(preferredProvider))
        {
            if (_providers.TryGetValue(preferredProvider, out var requested))
                return requested;
            throw new AgentLangToolException($"Search provider '{preferredProvider}' is not registered.", errorCode: "AGT500");
        }

        // 2. If explicit API key passed, default to Tavily if available
        if (!string.IsNullOrWhiteSpace(explicitApiKey))
        {
            if (_providers.TryGetValue("tavily", out var tavily))
                return tavily;
        }

        // 3. Auto-detect configured API keys in environment
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TAVILY_API_KEY")))
            return _providers["tavily"];

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SERPER_API_KEY")))
            return _providers["serper"];

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BRAVE_API_KEY")))
            return _providers["brave"];

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEARCH_API_KEY")))
            return _providers["tavily"];

        // 4. Check for mock/offline environment flag or fallback
        string? mockEnv = Environment.GetEnvironmentVariable("AGENTLANG_MOCK_SEARCH");
        if (_allowMockFallback || string.Equals(mockEnv, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(mockEnv, "true", StringComparison.OrdinalIgnoreCase))
        {
            return _providers["mock"];
        }

        // 5. Strict enforcement: Require API key
        throw new AgentLangToolException(
            "Search API key required: To perform web searches, configure an API key (TAVILY_API_KEY, SERPER_API_KEY, BRAVE_API_KEY, or SEARCH_API_KEY) in your environment or .env file. For offline testing without network access, set AGENTLANG_MOCK_SEARCH=1.",
            errorCode: "AGT500");
    }

    public async Task<SearchResponse> SearchAsync(
        string query,
        int maxResults = 5,
        string? preferredProvider = null,
        string? explicitApiKey = null,
        CancellationToken ct = default)
    {
        var provider = ResolveProvider(preferredProvider, explicitApiKey);

        if (provider.RequiresApiKey && string.IsNullOrWhiteSpace(explicitApiKey) && !provider.HasApiKey)
        {
            throw new AgentLangToolException(
                $"Search provider '{provider.ProviderId}' requires an API key, but none was provided. Set {provider.ProviderId.ToUpperInvariant()}_API_KEY or SEARCH_API_KEY in your environment or .env file.",
                errorCode: "AGT500");
        }

        var response = await provider.SearchAsync(query, maxResults, explicitApiKey, ct);
        if (!response.Success)
        {
            throw new AgentLangToolException(
                response.ErrorMessage ?? $"Search failed on provider '{provider.ProviderId}'",
                errorCode: "AGT500");
        }

        return response;
    }
}

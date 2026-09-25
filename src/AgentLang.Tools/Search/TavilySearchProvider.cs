using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentLang.Tools.Search;

public sealed class TavilySearchProvider : ISearchProvider
{
    public string ProviderId => "tavily";
    public bool RequiresApiKey => true;

    private readonly HttpClient _httpClient;
    private readonly string? _configuredApiKey;
    private readonly string _endpoint;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(GetEffectiveApiKey(null));

    public TavilySearchProvider(string? apiKey = null, string? endpoint = null, HttpClient? httpClient = null)
    {
        _configuredApiKey = apiKey;
        _endpoint = endpoint ?? "https://api.tavily.com/search";
        _httpClient = httpClient ?? new HttpClient();
    }

    private string? GetEffectiveApiKey(string? explicitApiKey) =>
        explicitApiKey ??
        _configuredApiKey ??
        Environment.GetEnvironmentVariable("TAVILY_API_KEY") ??
        Environment.GetEnvironmentVariable("SEARCH_API_KEY");

    public async Task<SearchResponse> SearchAsync(
        string query,
        int maxResults = 5,
        string? explicitApiKey = null,
        CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey(explicitApiKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            return SearchResponse.Failed(query, "Tavily API key is missing. Set TAVILY_API_KEY or SEARCH_API_KEY.", ProviderId);
        }

        try
        {
            var payload = new
            {
                api_key = key,
                query = query,
                max_results = maxResults,
                search_depth = "basic",
                include_answer = false
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(_endpoint, jsonContent, ct);
            string responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return SearchResponse.Failed(query, $"Tavily API returned status {response.StatusCode}: {responseBody}", ProviderId);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var items = new List<SearchItem>();

            if (root.TryGetProperty("results", out var resultsElem) && resultsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsElem.EnumerateArray())
                {
                    string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    string content = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

                    items.Add(new SearchItem(title, url, content, content));
                }
            }

            return SearchResponse.Succeeded(query, items, ProviderId);
        }
        catch (Exception ex)
        {
            return SearchResponse.Failed(query, $"Tavily search error: {ex.Message}", ProviderId);
        }
    }
}

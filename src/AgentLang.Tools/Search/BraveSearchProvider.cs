using System.Net.Http.Headers;
using System.Text.Json;

namespace AgentLang.Tools.Search;

public sealed class BraveSearchProvider : ISearchProvider
{
    public string ProviderId => "brave";
    public bool RequiresApiKey => true;

    private readonly HttpClient _httpClient;
    private readonly string? _configuredApiKey;
    private readonly string _endpoint;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(GetEffectiveApiKey(null));

    public BraveSearchProvider(string? apiKey = null, string? endpoint = null, HttpClient? httpClient = null)
    {
        _configuredApiKey = apiKey;
        _endpoint = endpoint ?? "https://api.search.brave.com/res/v1/web/search";
        _httpClient = httpClient ?? new HttpClient();
    }

    private string? GetEffectiveApiKey(string? explicitApiKey) =>
        explicitApiKey ??
        _configuredApiKey ??
        Environment.GetEnvironmentVariable("BRAVE_API_KEY") ??
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
            return SearchResponse.Failed(query, "Brave Search API key is missing. Set BRAVE_API_KEY or SEARCH_API_KEY.", ProviderId);
        }

        try
        {
            string requestUrl = $"{_endpoint}?q={Uri.EscapeDataString(query)}&count={maxResults}";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-Subscription-Token", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            string responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return SearchResponse.Failed(query, $"Brave Search API returned status {response.StatusCode}: {responseBody}", ProviderId);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var items = new List<SearchItem>();

            if (root.TryGetProperty("web", out var webElem) &&
                webElem.TryGetProperty("results", out var resultsElem) &&
                resultsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsElem.EnumerateArray())
                {
                    string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    string description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

                    items.Add(new SearchItem(title, url, description, description));
                }
            }

            return SearchResponse.Succeeded(query, items, ProviderId);
        }
        catch (Exception ex)
        {
            return SearchResponse.Failed(query, $"Brave search error: {ex.Message}", ProviderId);
        }
    }
}

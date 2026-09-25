using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentLang.Tools.Search;

public sealed class SerperSearchProvider : ISearchProvider
{
    public string ProviderId => "serper";
    public bool RequiresApiKey => true;

    private readonly HttpClient _httpClient;
    private readonly string? _configuredApiKey;
    private string? _dynamicApiKey;
    private readonly string _endpoint;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(GetEffectiveApiKey(null));

    public SerperSearchProvider(string? apiKey = null, string? endpoint = null, HttpClient? httpClient = null)
    {
        _configuredApiKey = apiKey;
        _endpoint = endpoint ?? "https://google.serper.dev/search";
        _httpClient = httpClient ?? new HttpClient();
    }

    public void SetApiKey(string apiKey) => _dynamicApiKey = apiKey;

    private string? GetEffectiveApiKey(string? explicitApiKey) =>
        explicitApiKey ??
        _dynamicApiKey ??
        _configuredApiKey ??
        Environment.GetEnvironmentVariable("SERPER_API_KEY") ??
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
            return SearchResponse.Failed(query, "Serper API key is missing. Set SERPER_API_KEY or SEARCH_API_KEY.", ProviderId);
        }

        try
        {
            var payload = new
            {
                q = query,
                num = maxResults
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            request.Headers.Add("X-API-KEY", key);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            string responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return SearchResponse.Failed(query, $"Serper API returned status {response.StatusCode}: {responseBody}", ProviderId);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var items = new List<SearchItem>();

            if (root.TryGetProperty("organic", out var organicElem) && organicElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in organicElem.EnumerateArray())
                {
                    string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string link = item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                    string snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";

                    items.Add(new SearchItem(title, link, snippet, snippet));
                }
            }

            return SearchResponse.Succeeded(query, items, ProviderId);
        }
        catch (Exception ex)
        {
            return SearchResponse.Failed(query, $"Serper search error: {ex.Message}", ProviderId);
        }
    }
}

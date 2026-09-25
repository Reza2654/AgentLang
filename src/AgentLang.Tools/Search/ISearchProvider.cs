using System.Text;

namespace AgentLang.Tools.Search;

public sealed record SearchItem(
    string Title,
    string Url,
    string Snippet,
    string? RawContent = null);

public sealed record SearchResponse(
    bool Success,
    string Query,
    IReadOnlyList<SearchItem> Items,
    string ProviderId,
    string? ErrorMessage = null)
{
    public static SearchResponse Succeeded(string query, IReadOnlyList<SearchItem> items, string providerId) =>
        new(true, query, items, providerId);

    public static SearchResponse Failed(string query, string error, string providerId) =>
        new(false, query, [], providerId, error);

    public string FormatMarkdown()
    {
        if (!Success)
            return $"Search failed ({ProviderId}): {ErrorMessage}";
        if (Items.Count == 0)
            return $"No search results found for '{Query}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"Search results for '{Query}' (via {ProviderId}):");
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            sb.AppendLine($"{i + 1}. [{item.Title}]({item.Url})");
            if (!string.IsNullOrWhiteSpace(item.Snippet))
                sb.AppendLine($"   {item.Snippet}");
        }
        return sb.ToString().TrimEnd();
    }
}

public interface ISearchProvider
{
    string ProviderId { get; }
    bool RequiresApiKey { get; }
    bool HasApiKey { get; }
    Task<SearchResponse> SearchAsync(string query, int maxResults = 5, string? explicitApiKey = null, CancellationToken ct = default);
}

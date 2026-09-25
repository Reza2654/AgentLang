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
    string? Answer = null,
    string? ErrorMessage = null)
{
    public static SearchResponse Succeeded(string query, IReadOnlyList<SearchItem> items, string providerId, string? answer = null) =>
        new(true, query, items, providerId, answer);

    public static SearchResponse Failed(string query, string error, string providerId) =>
        new(false, query, [], providerId, null, error);

    public string FormatMarkdown()
    {
        if (!Success)
            return $"Search failed ({ProviderId}): {ErrorMessage}";
        if (Items.Count == 0 && string.IsNullOrWhiteSpace(Answer))
            return $"No search results found for '{Query}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"🌐 Search results for '{Query}' (via {ProviderId}):");
        sb.AppendLine(new string('─', 50));

        if (!string.IsNullOrWhiteSpace(Answer))
        {
            sb.AppendLine("💡 Quick Summary:");
            sb.AppendLine(CleanSnippet(Answer));
            sb.AppendLine();
        }

        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            sb.AppendLine($"{i + 1}. {item.Title}");
            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                sb.AppendLine($"   🔗 {item.Url}");
            }
            string cleaned = CleanSnippet(item.Snippet);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                sb.AppendLine($"   {cleaned}");
            }
            if (i < Items.Count - 1)
            {
                sb.AppendLine();
            }
        }
        sb.AppendLine(new string('─', 50));
        return sb.ToString().TrimEnd();
    }

    public static string CleanSnippet(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string cleaned = raw;

        // Strip web scrape and navigation artifacts
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"Trace Id is missing\.?\s*(Please contact the administrator\.?)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"Skip to (main )?content\.?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b(Home;\s*)+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[\.\.\.\]", "...");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\(\s*([A-Z])", "$1");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @";\s*\(", "; ");

        // If semicolon-heavy navigation elements dominate, filter them out
        if (cleaned.Count(c => c == ';') >= 3)
        {
            var parts = cleaned.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
            var filtered = parts.Where(p => p.Count(c => c == ';') < 3).ToList();
            if (filtered.Count > 0)
            {
                cleaned = string.Join(". ", filtered);
            }
        }

        // Normalize whitespace
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

        // Truncate cleanly to ~250 characters on word boundary
        if (cleaned.Length > 260)
        {
            int cut = cleaned.LastIndexOf(' ', 250);
            if (cut <= 0) cut = 250;
            cleaned = cleaned[..cut].TrimEnd(',', ';', '.', ' ') + "...";
        }

        return cleaned;
    }
}

public interface ISearchProvider
{
    string ProviderId { get; }
    bool RequiresApiKey { get; }
    bool HasApiKey { get; }
    Task<SearchResponse> SearchAsync(string query, int maxResults = 5, string? explicitApiKey = null, CancellationToken ct = default);
}

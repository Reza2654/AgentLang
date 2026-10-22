using System.Collections.Concurrent;
using System.Text;
using AgentLang.Runtime.Values;

namespace AgentLang.VM.Optimization;

public static class ContextCompactor
{
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // Approximation: ~4 characters per token for English/code, ~2 for UTF-8 multi-byte
        int asciiChars = text.Count(c => c < 128);
        int nonAscii = text.Length - asciiChars;
        return (asciiChars / 4) + (nonAscii / 2) + 1;
    }

    public static List<ChatMessage> CompactHistory(
        IReadOnlyList<ChatMessage> history,
        int maxMessages = 8,
        int preserveRecent = 3)
    {
        if (history.Count <= maxMessages || history.Count <= preserveRecent)
        {
            return [.. history];
        }

        int toSummarizeCount = history.Count - preserveRecent;
        var toSummarize = history.Take(toSummarizeCount).ToList();
        var recent = history.Skip(toSummarizeCount).ToList();

        // Build consolidated summary of older turns
        var summaryBuilder = new StringBuilder();
        summaryBuilder.AppendLine("[Context Compaction Memo: Consolidated older conversation turns]");
        foreach (var msg in toSummarize)
        {
            string snippet = msg.Content?.ToString() ?? "";
            if (snippet.Length > 120) snippet = snippet[..117] + "...";
            summaryBuilder.AppendLine($"- {msg.Role}: {snippet}");
        }

        var consolidatedMessage = new ChatMessage("system", summaryBuilder.ToString().TrimEnd());
        var compacted = new List<ChatMessage> { consolidatedMessage };
        compacted.AddRange(recent);
        return compacted;
    }

    public static string PruneContext(string context, int maxTokens = 1000)
    {
        if (string.IsNullOrEmpty(context)) return string.Empty;
        if (EstimateTokens(context) <= maxTokens) return context;

        var lines = context.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i];
            if (EstimateTokens(sb.ToString() + line) > maxTokens)
                break;
            sb.Insert(0, line + "\n");
        }

        return "[... pruned earlier context ...]\n" + sb.ToString().TrimEnd();
    }
}

public sealed class TokenCache
{
    private sealed record CacheEntry(object? Value, DateTime Expiration);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private long _hits;
    private long _misses;

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public int Count => _cache.Count;

    public double HitRate
    {
        get
        {
            long total = Hits + Misses;
            return total == 0 ? 0.0 : Math.Round((double)Hits / total, 4);
        }
    }

    public void Set(string key, object? value, TimeSpan? ttl = null)
    {
        var expiration = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.MaxValue;
        _cache[key] = new CacheEntry(value, expiration);
    }

    public bool TryGet(string key, out object? value)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.Expiration > DateTime.UtcNow)
            {
                Interlocked.Increment(ref _hits);
                value = entry.Value;
                return true;
            }

            // Expired
            _cache.TryRemove(key, out _);
        }

        Interlocked.Increment(ref _misses);
        value = null;
        return false;
    }

    public void Clear()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }
}

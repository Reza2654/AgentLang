using System.Collections.Concurrent;

namespace AgentLang.VM.Memory;

public sealed class SemanticCacheEntry
{
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public int HitCount { get; set; }
}

public sealed class SemanticPromptCache
{
    private readonly ConcurrentBag<SemanticCacheEntry> _entries = new();

    public double DefaultThreshold { get; set; } = 0.90;
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int TotalRequests => Hits + Misses;
    public double HitRate => TotalRequests > 0 ? (double)Hits / TotalRequests : 0.0;
    public int EstimatedTokensSaved { get; private set; }

    public int EntryCount => _entries.Count;

    public async Task<string> GetOrComputeAsync(
        string prompt,
        Func<Task<string>> computer,
        double? customThreshold = null)
    {
        double threshold = customThreshold ?? DefaultThreshold;
        var queryEmbedding = HybridDocument.ComputeSimpleEmbedding(prompt);

        SemanticCacheEntry? bestMatch = null;
        double bestSim = -1.0;

        foreach (var entry in _entries)
        {
            double sim = ComputeCosineSimilarity(queryEmbedding, entry.Embedding);
            if (sim >= threshold && sim > bestSim)
            {
                bestSim = sim;
                bestMatch = entry;
            }
        }

        if (bestMatch != null)
        {
            bestMatch.HitCount++;
            Hits++;
            EstimatedTokensSaved += Math.Max(20, (prompt.Length + bestMatch.Response.Length) / 4);
            return bestMatch.Response;
        }

        Misses++;
        string generatedResponse = await computer();

        _entries.Add(new SemanticCacheEntry
        {
            Prompt = prompt,
            Response = generatedResponse,
            Embedding = queryEmbedding,
            Timestamp = DateTimeOffset.UtcNow
        });

        return generatedResponse;
    }

    private static double ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0.0;
        double dot = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }
        return Math.Clamp(dot, 0.0, 1.0);
    }

    public void Clear()
    {
        _entries.Clear();
        Hits = 0;
        Misses = 0;
        EstimatedTokensSaved = 0;
    }
}

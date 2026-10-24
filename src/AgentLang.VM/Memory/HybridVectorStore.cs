using System.Collections.Concurrent;

namespace AgentLang.VM.Memory;

public sealed class HybridDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> TermFrequencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int WordCount { get; set; }
    public float[]? DenseVector { get; set; }

    public static HybridDocument Create(string content, string? id = null, Dictionary<string, object?>? metadata = null)
    {
        var doc = new HybridDocument
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Content = content,
            Metadata = metadata ?? new()
        };

        var tokens = Tokenize(content);
        doc.WordCount = tokens.Count;
        foreach (var t in tokens)
        {
            doc.TermFrequencies[t] = doc.TermFrequencies.GetValueOrDefault(t, 0) + 1;
        }

        doc.DenseVector = ComputeSimpleEmbedding(content);
        return doc;
    }

    public static List<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '-', '_', '(', ')', '[', ']', '{', '}' },
                   StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    public static float[] ComputeSimpleEmbedding(string text, int dimensions = 64)
    {
        var vec = new float[dimensions];
        var tokens = Tokenize(text);
        if (tokens.Count == 0) return vec;

        foreach (var token in tokens)
        {
            int hash = Math.Abs(token.GetHashCode());
            int idx = hash % dimensions;
            vec[idx] += 1.0f;
        }

        // Normalize vector to unit length
        double sumSq = 0;
        for (int i = 0; i < dimensions; i++) sumSq += vec[i] * vec[i];
        float norm = (float)Math.Sqrt(sumSq);
        if (norm > 0)
        {
            for (int i = 0; i < dimensions; i++) vec[i] /= norm;
        }

        return vec;
    }
}

public sealed record SearchResult(
    HybridDocument Document,
    double DenseScore,
    double Bm25Score,
    double FusedScore);

public sealed class HybridVectorStore
{
    private readonly ConcurrentDictionary<string, HybridDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _docFrequencies = new(StringComparer.OrdinalIgnoreCase);
    private double _avgDocLength;

    public int Count => _documents.Count;

    public void Add(string content, string? id = null, Dictionary<string, object?>? metadata = null)
    {
        var doc = HybridDocument.Create(content, id, metadata);
        _documents[doc.Id] = doc;

        foreach (var term in doc.TermFrequencies.Keys)
        {
            _docFrequencies.AddOrUpdate(term, 1, (_, count) => count + 1);
        }

        _avgDocLength = _documents.Values.Average(d => d.WordCount);
    }

    public IReadOnlyList<SearchResult> Search(string query, int topK = 5, double rrfConstant = 60.0)
    {
        if (_documents.IsEmpty || string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        var queryTokens = HybridDocument.Tokenize(query);
        var queryVector = HybridDocument.ComputeSimpleEmbedding(query);

        int n = _documents.Count;
        double k1 = 1.2;
        double b = 0.75;

        // 1. Compute BM25 scores
        var bm25Scores = new Dictionary<string, double>();
        foreach (var doc in _documents.Values)
        {
            double score = 0;
            foreach (var term in queryTokens)
            {
                if (!doc.TermFrequencies.TryGetValue(term, out int tf)) continue;
                int df = _docFrequencies.GetValueOrDefault(term, 1);
                double idf = Math.Log((n - df + 0.5) / (df + 0.5) + 1.0);
                double tfNorm = (tf * (k1 + 1.0)) / (tf + k1 * (1.0 - b + b * (doc.WordCount / (_avgDocLength > 0 ? _avgDocLength : 1.0))));
                score += idf * tfNorm;
            }
            bm25Scores[doc.Id] = Math.Max(0, score);
        }

        // 2. Compute Dense Cosine Similarity
        var denseScores = new Dictionary<string, double>();
        foreach (var doc in _documents.Values)
        {
            double sim = 0;
            if (doc.DenseVector != null)
            {
                for (int i = 0; i < queryVector.Length; i++)
                {
                    sim += queryVector[i] * doc.DenseVector[i];
                }
            }
            denseScores[doc.Id] = Math.Clamp(sim, 0.0, 1.0);
        }

        // 3. Rank separately
        var bm25Ranked = bm25Scores.OrderByDescending(kv => kv.Value).Select((kv, index) => (kv.Key, Rank: index + 1)).ToDictionary(x => x.Key, x => x.Rank);
        var denseRanked = denseScores.OrderByDescending(kv => kv.Value).Select((kv, index) => (kv.Key, Rank: index + 1)).ToDictionary(x => x.Key, x => x.Rank);

        // 4. Reciprocal Rank Fusion (RRF)
        var results = new List<SearchResult>();
        foreach (var doc in _documents.Values)
        {
            int rBm25 = bm25Ranked.GetValueOrDefault(doc.Id, n);
            int rDense = denseRanked.GetValueOrDefault(doc.Id, n);

            double rrfScore = (1.0 / (rrfConstant + rBm25)) + (1.0 / (rrfConstant + rDense));
            results.Add(new SearchResult(doc, denseScores[doc.Id], bm25Scores[doc.Id], rrfScore));
        }

        return results.OrderByDescending(r => r.FusedScore).Take(topK).ToList();
    }
}

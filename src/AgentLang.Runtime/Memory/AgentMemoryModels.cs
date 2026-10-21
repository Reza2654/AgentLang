using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentLang.Runtime.Memory;

public interface IAgentMemory
{
    string MemoryType { get; }
    void Store(string text, Dictionary<string, object?>? metadata = null);
    IReadOnlyList<string> Retrieve(string query, int? topK = null);
    void Clear();
}

public sealed class VectorDocument
{
    public string Id { get; }
    public string Content { get; }
    public Dictionary<string, object?> Metadata { get; }
    public Dictionary<string, double> TermFrequencies { get; }
    public double VectorMagnitude { get; set; }

    public VectorDocument(string id, string content, Dictionary<string, object?>? metadata = null)
    {
        Id = id;
        Content = content;
        Metadata = metadata ?? new(StringComparer.OrdinalIgnoreCase);
        TermFrequencies = ComputeTermFrequencies(content);
    }

    private static Dictionary<string, double> ComputeTermFrequencies(string text)
    {
        var tf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var tokens = Tokenize(text);
        if (tokens.Count == 0) return tf;

        double count = tokens.Count;
        foreach (var tok in tokens)
        {
            tf[tok] = tf.TryGetValue(tok, out double val) ? val + 1.0 : 1.0;
        }

        // Normalize
        foreach (var k in tf.Keys.ToList())
        {
            tf[k] /= count;
        }

        return tf;
    }

    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        string normalized = text.ToLowerInvariant();

        // 1. Word tokens
        var words = Regex.Split(normalized, @"[^\p{L}\p{Nd}_]+");
        foreach (var w in words)
        {
            if (w.Length > 1) tokens.Add(w);
        }

        // 2. Character 3-grams for cross-lingual / subword matching
        for (int i = 0; i < normalized.Length - 2; i++)
        {
            string tri = normalized.Substring(i, 3);
            if (!char.IsWhiteSpace(tri[0]) && !char.IsWhiteSpace(tri[1]) && !char.IsWhiteSpace(tri[2]))
            {
                tokens.Add($"__tri_{tri}");
            }
        }

        return tokens;
    }
}

public sealed class VectorStoreMemory : IAgentMemory
{
    public string MemoryType => "VectorStore";
    public string Collection { get; }
    public int TopK { get; set; }
    public string SimilarityMetric { get; set; }

    private readonly List<VectorDocument> _documents = [];
    private readonly Dictionary<string, int> _documentFrequencies = new(StringComparer.OrdinalIgnoreCase);

    public VectorStoreMemory(string collection = "default", int topK = 4, string similarityMetric = "cosine")
    {
        Collection = collection;
        TopK = Math.Max(1, topK);
        SimilarityMetric = similarityMetric;
    }

    public void Store(string text, Dictionary<string, object?>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        string id = Guid.NewGuid().ToString("N")[..8];
        var doc = new VectorDocument(id, text, metadata);
        _documents.Add(doc);

        foreach (var term in doc.TermFrequencies.Keys)
        {
            _documentFrequencies[term] = _documentFrequencies.TryGetValue(term, out int df) ? df + 1 : 1;
        }

        RecomputeVectors();
    }

    private void RecomputeVectors()
    {
        int n = _documents.Count;
        foreach (var doc in _documents)
        {
            double sumSquares = 0.0;
            foreach (var (term, tf) in doc.TermFrequencies)
            {
                int df = _documentFrequencies.TryGetValue(term, out int val) ? val : 1;
                double idf = Math.Log(1.0 + ((double)n / (1.0 + df))) + 1.0;
                double weight = tf * idf;
                sumSquares += weight * weight;
            }
            doc.VectorMagnitude = Math.Sqrt(sumSquares);
        }
    }

    public IReadOnlyList<string> Retrieve(string query, int? topK = null)
    {
        int k = topK ?? TopK;
        if (_documents.Count == 0 || string.IsNullOrWhiteSpace(query))
            return [];

        var queryTokens = VectorDocument.Tokenize(query);
        if (queryTokens.Count == 0)
            return _documents.Take(k).Select(d => d.Content).ToList();

        // Compute query TF-IDF vector
        var qTf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var tok in queryTokens)
            qTf[tok] = qTf.TryGetValue(tok, out double c) ? c + 1.0 : 1.0;

        int totalDocs = _documents.Count;
        var qWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double qMagnitudeSq = 0.0;

        foreach (var (term, count) in qTf)
        {
            double tf = count / queryTokens.Count;
            int df = _documentFrequencies.TryGetValue(term, out int dVal) ? dVal : 1;
            double idf = Math.Log(1.0 + ((double)totalDocs / (1.0 + df))) + 1.0;
            double weight = tf * idf;
            qWeights[term] = weight;
            qMagnitudeSq += weight * weight;
        }

        double qMagnitude = Math.Sqrt(qMagnitudeSq);
        if (qMagnitude <= 0.000001)
            return _documents.Take(k).Select(d => d.Content).ToList();

        // Calculate cosine similarities
        var scored = new List<(double Score, VectorDocument Doc)>();
        foreach (var doc in _documents)
        {
            if (doc.VectorMagnitude <= 0.000001) continue;

            double dotProduct = 0.0;
            foreach (var (term, qW) in qWeights)
            {
                if (doc.TermFrequencies.TryGetValue(term, out double docTf))
                {
                    int df = _documentFrequencies.TryGetValue(term, out int val) ? val : 1;
                    double docIdf = Math.Log(1.0 + ((double)totalDocs / (1.0 + df))) + 1.0;
                    double docWeight = docTf * docIdf;
                    dotProduct += qW * docWeight;
                }
            }

            double similarity = dotProduct / (qMagnitude * doc.VectorMagnitude);
            scored.Add((similarity, doc));
        }

        var results = scored
            .OrderByDescending(s => s.Score)
            .Take(k)
            .Select(s => s.Doc.Content)
            .ToList();

        // Fallback: if similarity was zero across all, return top available
        if (results.Count == 0 && _documents.Count > 0)
        {
            return _documents.Take(k).Select(d => d.Content).ToList();
        }

        return results;
    }

    public void Clear()
    {
        _documents.Clear();
        _documentFrequencies.Clear();
    }
}

public sealed class ConversationBufferMemory : IAgentMemory
{
    public string MemoryType => "ConversationBuffer";
    public int Window { get; set; }
    private readonly List<(string Role, string Content)> _history = [];

    public ConversationBufferMemory(int window = 5)
    {
        Window = Math.Max(1, window);
    }

    public void Add(string role, string content)
    {
        _history.Add((role, content));
    }

    public IReadOnlyList<(string Role, string Content)> History => _history;

    public void Store(string text, Dictionary<string, object?>? metadata = null)
    {
        string role = metadata != null && metadata.TryGetValue("role", out var r) ? r?.ToString() ?? "user" : "user";
        _history.Add((role, text));
    }

    public IReadOnlyList<string> Retrieve(string query, int? topK = null)
    {
        int limit = (topK ?? Window) * 2;
        return _history.TakeLast(limit).Select(h => $"{h.Role}: {h.Content}").ToList();
    }

    public void Clear() => _history.Clear();
}

public sealed class EphemeralMemory : IAgentMemory
{
    public string MemoryType => "Ephemeral";
    private readonly List<string> _entries = [];
    private readonly Dictionary<string, object?> _kv = new(StringComparer.OrdinalIgnoreCase);

    public void Store(string text, Dictionary<string, object?>? metadata = null)
    {
        _entries.Add(text);
        if (metadata != null)
        {
            foreach (var (k, v) in metadata)
                _kv[k] = v;
        }
    }

    public IReadOnlyList<string> Retrieve(string query, int? topK = null)
    {
        int k = topK ?? 5;
        if (string.IsNullOrWhiteSpace(query))
            return _entries.TakeLast(k).ToList();

        return _entries.Where(e => e.Contains(query, StringComparison.OrdinalIgnoreCase))
                       .TakeLast(k)
                       .ToList();
    }

    public void Clear()
    {
        _entries.Clear();
        _kv.Clear();
    }

    public void Set(string key, object? value)
    {
        _kv[key] = value;
    }

    public object? Get(string key)
    {
        return _kv.TryGetValue(key, out var val) ? val : null;
    }
}

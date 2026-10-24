using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentLang.VM.Memory;

public sealed record EntityRecord(
    string Id,
    string Type,
    Dictionary<string, object?> Attributes,
    DateTime CreatedAt
);

public sealed record EntityRelation(
    string SourceId,
    string Relation,
    string TargetId,
    double Confidence,
    DateTime Timestamp,
    Dictionary<string, object?>? Properties = null
);

public sealed record MemoryEpisode(
    string Id,
    string Content,
    DateTime Timestamp,
    List<string> EntityIds
);

public sealed class EntitySubgraph
{
    public required string RootId { get; init; }
    public List<EntityRecord> Entities { get; init; } = [];
    public List<EntityRelation> Relations { get; init; } = [];
}

public sealed partial class EntityGraphMemory
{
    private readonly ConcurrentDictionary<string, EntityRecord> _entities = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EntityRelation> _relations = [];
    private readonly List<MemoryEpisode> _episodes = [];
    private readonly object _lock = new();

    public int EntityCount => _entities.Count;
    public int RelationCount
    {
        get { lock (_lock) return _relations.Count; }
    }
    public int EpisodeCount
    {
        get { lock (_lock) return _episodes.Count; }
    }

    public EntityRecord AddEntity(string id, string type, Dictionary<string, object?>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var entity = new EntityRecord(
            id.Trim(),
            type.Trim(),
            attributes != null ? new Dictionary<string, object?>(attributes, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase),
            DateTime.UtcNow
        );

        _entities[entity.Id] = entity;
        return entity;
    }

    public EntityRelation AddRelation(
        string sourceId,
        string relation,
        string targetId,
        double confidence = 1.0,
        Dictionary<string, object?>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relation);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        var rel = new EntityRelation(
            sourceId.Trim(),
            relation.Trim().ToLowerInvariant().Replace(' ', '_'),
            targetId.Trim(),
            Math.Clamp(confidence, 0.0, 1.0),
            DateTime.UtcNow,
            properties
        );

        lock (_lock)
        {
            _relations.RemoveAll(r =>
                r.SourceId.Equals(rel.SourceId, StringComparison.OrdinalIgnoreCase) &&
                r.Relation.Equals(rel.Relation, StringComparison.OrdinalIgnoreCase) &&
                r.TargetId.Equals(rel.TargetId, StringComparison.OrdinalIgnoreCase));

            _relations.Add(rel);
        }

        return rel;
    }

    public MemoryEpisode AddEpisode(string content, IEnumerable<string>? entityIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var episode = new MemoryEpisode(
            Guid.NewGuid().ToString("N")[..8],
            content.Trim(),
            DateTime.UtcNow,
            entityIds != null ? entityIds.Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : []
        );

        lock (_lock)
        {
            _episodes.Add(episode);
        }

        return episode;
    }

    public EntityRecord? GetEntity(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        _entities.TryGetValue(id.Trim(), out var entity);
        return entity;
    }

    public List<EntityRelation> GetRelations(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return [];
        var key = entityId.Trim();

        lock (_lock)
        {
            return _relations.Where(r =>
                r.SourceId.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                r.TargetId.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public EntitySubgraph QuerySubgraph(string rootEntityId, int maxDepth = 2)
    {
        var visitedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedRelations = new List<EntityRelation>();
        var queue = new Queue<(string Id, int Depth)>();

        var root = GetEntity(rootEntityId);
        if (root == null)
        {
            return new EntitySubgraph { RootId = rootEntityId };
        }

        queue.Enqueue((root.Id, 0));
        visitedEntities.Add(root.Id);

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();
            if (depth >= maxDepth) continue;

            var relations = GetRelations(currentId);
            foreach (var rel in relations)
            {
                if (!visitedRelations.Contains(rel))
                {
                    visitedRelations.Add(rel);
                }

                var neighborId = rel.SourceId.Equals(currentId, StringComparison.OrdinalIgnoreCase)
                    ? rel.TargetId
                    : rel.SourceId;

                if (visitedEntities.Add(neighborId))
                {
                    queue.Enqueue((neighborId, depth + 1));
                }
            }
        }

        var entitiesList = visitedEntities
            .Select(id => GetEntity(id) ?? new EntityRecord(id, "Unknown", new(), DateTime.UtcNow))
            .ToList();

        return new EntitySubgraph
        {
            RootId = rootEntityId,
            Entities = entitiesList,
            Relations = visitedRelations
        };
    }

    public int ExtractAndStoreFacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int extracted = 0;

        var sentences = text.Split(['.', '\n', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pattern = FactRegex();

        foreach (var sentence in sentences)
        {
            var match = pattern.Match(sentence);
            if (match.Success && match.Groups.Count >= 4)
            {
                var source = match.Groups[1].Value.Trim(' ', '.', ',', ';');
                var rel = match.Groups[2].Value.Trim().ToLowerInvariant().Replace(' ', '_');
                var target = match.Groups[3].Value.Trim(' ', '.', ',', ';');

                if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target) && source.Length > 1 && target.Length > 1)
                {
                    AddEntity(source, "Concept");
                    AddEntity(target, "Concept");
                    AddRelation(source, rel, target, 0.95);
                    extracted++;
                }
            }
        }

        AddEpisode(text);
        return extracted;
    }

    public string RecallContext(string query, int maxFacts = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;

        var tokens = query.Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .ToList();

        var matchedRelations = new List<(EntityRelation Rel, double Score)>();

        lock (_lock)
        {
            foreach (var rel in _relations)
            {
                double score = 0;
                foreach (var token in tokens)
                {
                    if (rel.SourceId.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 1.0;
                    if (rel.TargetId.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 1.0;
                    if (rel.Relation.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 0.5;
                }

                if (score > 0)
                {
                    matchedRelations.Add((rel, score * rel.Confidence));
                }
            }
        }

        var topRelations = matchedRelations
            .OrderByDescending(r => r.Score)
            .Take(maxFacts)
            .Select(r => r.Rel)
            .ToList();

        if (topRelations.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("=== ENTITY KNOWLEDGE GRAPH ===");
        foreach (var rel in topRelations)
        {
            sb.AppendLine($"- {rel.SourceId} {rel.Relation} {rel.TargetId} (confidence: {rel.Confidence:0.00})");
        }
        return sb.ToString().TrimEnd();
    }

    public string ExportJson()
    {
        var model = new SerializableGraph
        {
            Entities = _entities.Values.ToList(),
            Relations = [.. _relations],
            Episodes = [.. _episodes]
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
    }

    public void ImportJson(string json)
    {
        var model = JsonSerializer.Deserialize<SerializableGraph>(json);
        if (model == null) return;

        _entities.Clear();
        foreach (var entity in model.Entities)
        {
            _entities[entity.Id] = entity;
        }

        lock (_lock)
        {
            _relations.Clear();
            _relations.AddRange(model.Relations);

            _episodes.Clear();
            _episodes.AddRange(model.Episodes);
        }
    }

    [GeneratedRegex(@"\b([A-Za-z0-9_]+(?:\s+[A-Za-z0-9_]+)?)\s+(works\s+on|created|uses|likes|is\s+a|depends\s+on|maintains|belongs\s+to)\s+([A-Za-z0-9_]+(?:\s+[A-Za-z0-9_]+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FactRegex();

    private sealed class SerializableGraph
    {
        public List<EntityRecord> Entities { get; set; } = [];
        public List<EntityRelation> Relations { get; set; } = [];
        public List<MemoryEpisode> Episodes { get; set; } = [];
    }
}

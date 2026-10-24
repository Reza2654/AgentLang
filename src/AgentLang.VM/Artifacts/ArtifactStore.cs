using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentLang.VM.Artifacts;

public sealed class Artifact
{
    public string Id { get; init; } = $"art_{Guid.NewGuid():N}"[..12];
    public required string Name { get; init; }
    public required string MimeType { get; init; }
    public required byte[] Data { get; init; }
    public string TextContent => Encoding.UTF8.GetString(Data);
    public string Sha256 { get; init; }
    public int Version { get; init; } = 1;
    public string? ParentArtifactId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Dictionary<string, object?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Artifact()
    {
        Sha256 = string.Empty;
    }

    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}

public sealed class ArtifactStore
{
    private readonly ConcurrentDictionary<string, Artifact> _artifacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _storageDirectory;
    private readonly object _lock = new();

    public int Count => _artifacts.Count;

    public ArtifactStore(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory;
        if (!string.IsNullOrEmpty(_storageDirectory) && !Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }
    }

    public Artifact CreateArtifact(string name, string mimeType, byte[] data, Dictionary<string, object?>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(data);

        string hash = Artifact.ComputeSha256(data);
        var artifact = new Artifact
        {
            Name = name.Trim(),
            MimeType = mimeType.Trim(),
            Data = data,
            Sha256 = hash,
            Version = 1,
            Metadata = metadata != null ? new(metadata, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase)
        };

        _artifacts[artifact.Id] = artifact;
        PersistArtifact(artifact);
        return artifact;
    }

    public Artifact CreateTextArtifact(string name, string textContent, string mimeType = "text/plain", Dictionary<string, object?>? metadata = null)
    {
        return CreateArtifact(name, mimeType, Encoding.UTF8.GetBytes(textContent), metadata);
    }

    public Artifact CreateJsonArtifact(string name, object data, Dictionary<string, object?>? metadata = null)
    {
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return CreateArtifact(name, "application/json", Encoding.UTF8.GetBytes(json), metadata);
    }

    public Artifact? GetArtifact(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        _artifacts.TryGetValue(id.Trim(), out var artifact);
        return artifact;
    }

    public Artifact? GetArtifactByName(string name, int? version = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var matching = _artifacts.Values
            .Where(a => a.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count == 0) return null;

        if (version.HasValue)
        {
            return matching.FirstOrDefault(a => a.Version == version.Value);
        }

        return matching.OrderByDescending(a => a.Version).FirstOrDefault();
    }

    public Artifact UpdateArtifact(string id, byte[] newData, Dictionary<string, object?>? newMetadata = null)
    {
        var existing = GetArtifact(id) ?? throw new KeyNotFoundException($"Artifact with ID '{id}' was not found.");

        string hash = Artifact.ComputeSha256(newData);
        var updated = new Artifact
        {
            Name = existing.Name,
            MimeType = existing.MimeType,
            Data = newData,
            Sha256 = hash,
            Version = existing.Version + 1,
            ParentArtifactId = existing.Id,
            Metadata = newMetadata != null ? new(newMetadata, StringComparer.OrdinalIgnoreCase) : new(existing.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        _artifacts[updated.Id] = updated;
        PersistArtifact(updated);
        return updated;
    }

    public List<Artifact> ListArtifacts()
    {
        return _artifacts.Values.OrderByDescending(a => a.CreatedAt).ToList();
    }

    private void PersistArtifact(Artifact artifact)
    {
        if (string.IsNullOrEmpty(_storageDirectory)) return;

        try
        {
            string artifactDir = Path.Combine(_storageDirectory, artifact.Id);
            Directory.CreateDirectory(artifactDir);

            string dataFile = Path.Combine(artifactDir, artifact.Name);
            File.WriteAllBytes(dataFile, artifact.Data);

            string metaFile = Path.Combine(artifactDir, "metadata.json");
            var metaPayload = new
            {
                artifact.Id,
                artifact.Name,
                artifact.MimeType,
                artifact.Sha256,
                artifact.Version,
                artifact.ParentArtifactId,
                artifact.CreatedAt,
                artifact.Metadata
            };

            string json = JsonSerializer.Serialize(metaPayload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaFile, json);
        }
        catch
        {
            // Best effort persistence
        }
    }
}

using System.Text.Json;

namespace AgentLang.Runtime.Memory;

public interface IPersistentMemoryStore
{
    Task<IReadOnlyList<string>> LoadMemoryAsync(string agentName, CancellationToken ct = default);
    Task SaveMemoryAsync(string agentName, IReadOnlyList<string> entries, CancellationToken ct = default);
    Task ClearMemoryAsync(string agentName, CancellationToken ct = default);
}

public sealed class LocalFileMemoryStore : IPersistentMemoryStore
{
    private readonly string _baseDirectory;

    public string BaseDirectory => _baseDirectory;

    public LocalFileMemoryStore(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), ".agentlang", "memory");
    }

    private string GetFilePath(string agentName)
    {
        Directory.CreateDirectory(_baseDirectory);
        return Path.Combine(_baseDirectory, $"{agentName}.json");
    }

    public async Task<IReadOnlyList<string>> LoadMemoryAsync(string agentName, CancellationToken ct = default)
    {
        string filePath = GetFilePath(agentName);
        if (!File.Exists(filePath))
            return [];

        try
        {
            string json = await File.ReadAllTextAsync(filePath, ct);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveMemoryAsync(string agentName, IReadOnlyList<string> entries, CancellationToken ct = default)
    {
        string filePath = GetFilePath(agentName);
        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public Task ClearMemoryAsync(string agentName, CancellationToken ct = default)
    {
        string filePath = GetFilePath(agentName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryMemoryStore : IPersistentMemoryStore
{
    private readonly Dictionary<string, List<string>> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> LoadMemoryAsync(string agentName, CancellationToken ct = default)
    {
        if (_store.TryGetValue(agentName, out var list))
            return Task.FromResult<IReadOnlyList<string>>(list.ToList());
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task SaveMemoryAsync(string agentName, IReadOnlyList<string> entries, CancellationToken ct = default)
    {
        _store[agentName] = entries.ToList();
        return Task.CompletedTask;
    }

    public Task ClearMemoryAsync(string agentName, CancellationToken ct = default)
    {
        _store.Remove(agentName);
        return Task.CompletedTask;
    }
}

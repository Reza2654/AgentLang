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

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
                using var reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync(ct);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                return list ?? [];
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(30 * (attempt + 1), ct);
            }
            catch
            {
                return [];
            }
        }
        return [];
    }

    public async Task SaveMemoryAsync(string agentName, IReadOnlyList<string> entries, CancellationToken ct = default)
    {
        string filePath = GetFilePath(agentName);
        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
                using var writer = new StreamWriter(stream);
                await writer.WriteAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
                break;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(30 * (attempt + 1), ct);
            }
        }
    }

    public Task ClearMemoryAsync(string agentName, CancellationToken ct = default)
    {
        string filePath = GetFilePath(agentName);
        if (File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
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

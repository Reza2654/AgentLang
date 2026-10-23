using System.Collections.Concurrent;
using System.IO;

namespace AgentLang.VM.Checkpointing;

public interface ICheckpointStore
{
    Task SaveAsync(ExecutionCheckpoint checkpoint, CancellationToken ct = default);
    Task<ExecutionCheckpoint?> GetAsync(string checkpointId, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionCheckpoint>> ListAsync(CancellationToken ct = default);
    Task<bool> DeleteAsync(string checkpointId, CancellationToken ct = default);
}

public sealed class MemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, ExecutionCheckpoint> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveAsync(ExecutionCheckpoint checkpoint, CancellationToken ct = default)
    {
        _store[checkpoint.CheckpointId] = checkpoint;
        return Task.CompletedTask;
    }

    public Task<ExecutionCheckpoint?> GetAsync(string checkpointId, CancellationToken ct = default)
    {
        _store.TryGetValue(checkpointId, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    public Task<IReadOnlyList<ExecutionCheckpoint>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ExecutionCheckpoint> list = _store.Values.OrderByDescending(c => c.Timestamp).ToList();
        return Task.FromResult(list);
    }

    public Task<bool> DeleteAsync(string checkpointId, CancellationToken ct = default)
    {
        return Task.FromResult(_store.TryRemove(checkpointId, out _));
    }
}

public sealed class FileCheckpointStore : ICheckpointStore
{
    private readonly string _storageDir;

    public FileCheckpointStore(string? storageDir = null)
    {
        _storageDir = storageDir ?? Path.Combine(Directory.GetCurrentDirectory(), ".agent", "checkpoints");
        if (!Directory.Exists(_storageDir))
        {
            Directory.CreateDirectory(_storageDir);
        }
    }

    public async Task SaveAsync(ExecutionCheckpoint checkpoint, CancellationToken ct = default)
    {
        var filePath = GetFilePath(checkpoint.CheckpointId);
        var json = checkpoint.ToJson();
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public async Task<ExecutionCheckpoint?> GetAsync(string checkpointId, CancellationToken ct = default)
    {
        var filePath = GetFilePath(checkpointId);
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath, ct);
        return ExecutionCheckpoint.FromJson(json);
    }

    public async Task<IReadOnlyList<ExecutionCheckpoint>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<ExecutionCheckpoint>();
        if (!Directory.Exists(_storageDir)) return list;

        foreach (var file in Directory.GetFiles(_storageDir, "*.agtchk"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var cp = ExecutionCheckpoint.FromJson(json);
                list.Add(cp);
            }
            catch
            {
                // Skip corrupted or unreadable checkpoints
            }
        }

        return list.OrderByDescending(c => c.Timestamp).ToList();
    }

    public Task<bool> DeleteAsync(string checkpointId, CancellationToken ct = default)
    {
        var filePath = GetFilePath(checkpointId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private string GetFilePath(string checkpointId) =>
        Path.Combine(_storageDir, $"{checkpointId}.agtchk");
}

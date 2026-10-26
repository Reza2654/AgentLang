using System.Collections.Concurrent;
using System.Text.Json;

namespace AgentLang.VM.Durable;

public interface IDurableStateStore
{
    Task SaveAsync(DurableExecutionContext context, CancellationToken ct = default);
    Task<DurableExecutionContext?> GetAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyList<DurableExecutionContext>> ListAsync(DurableStatus? statusFilter = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(string executionId, CancellationToken ct = default);
}

public sealed class MemoryDurableStateStore : IDurableStateStore
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveAsync(DurableExecutionContext context, CancellationToken ct = default)
    {
        context.UpdatedAt = DateTimeOffset.UtcNow;
        _store[context.ExecutionId] = context.ToJson(indented: false);
        return Task.CompletedTask;
    }

    public Task<DurableExecutionContext?> GetAsync(string executionId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(executionId, out var json))
        {
            return Task.FromResult<DurableExecutionContext?>(DurableExecutionContext.FromJson(json));
        }
        return Task.FromResult<DurableExecutionContext?>(null);
    }

    public Task<IReadOnlyList<DurableExecutionContext>> ListAsync(DurableStatus? statusFilter = null, CancellationToken ct = default)
    {
        var list = _store.Values
            .Select(json => DurableExecutionContext.FromJson(json))
            .Where(ctx => statusFilter == null || ctx.Status == statusFilter.Value)
            .OrderByDescending(ctx => ctx.UpdatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<DurableExecutionContext>>(list);
    }

    public Task<bool> DeleteAsync(string executionId, CancellationToken ct = default)
    {
        return Task.FromResult(_store.TryRemove(executionId, out _));
    }
}

public sealed class FileDurableStateStore : IDurableStateStore
{
    private readonly string _storageDir;

    public FileDurableStateStore(string? baseDir = null)
    {
        _storageDir = baseDir ?? Path.Combine(Directory.GetCurrentDirectory(), ".agent", "durable");
        Directory.CreateDirectory(_storageDir);
    }

    private string GetFilePath(string executionId) =>
        Path.Combine(_storageDir, $"{executionId}.json");

    public async Task SaveAsync(DurableExecutionContext context, CancellationToken ct = default)
    {
        context.UpdatedAt = DateTimeOffset.UtcNow;
        string path = GetFilePath(context.ExecutionId);
        string json = context.ToJson(indented: true);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public async Task<DurableExecutionContext?> GetAsync(string executionId, CancellationToken ct = default)
    {
        string path = GetFilePath(executionId);
        if (!File.Exists(path)) return null;

        string json = await File.ReadAllTextAsync(path, ct);
        return DurableExecutionContext.FromJson(json);
    }

    public async Task<IReadOnlyList<DurableExecutionContext>> ListAsync(DurableStatus? statusFilter = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(_storageDir))
            return Array.Empty<DurableExecutionContext>();

        var files = Directory.GetFiles(_storageDir, "*.json");
        var list = new List<DurableExecutionContext>();

        foreach (var file in files)
        {
            try
            {
                string json = await File.ReadAllTextAsync(file, ct);
                var ctx = DurableExecutionContext.FromJson(json);
                if (statusFilter == null || ctx.Status == statusFilter.Value)
                {
                    list.Add(ctx);
                }
            }
            catch
            {
                // Ignore malformed files
            }
        }

        return list.OrderByDescending(ctx => ctx.UpdatedAt).ToList();
    }

    public Task<bool> DeleteAsync(string executionId, CancellationToken ct = default)
    {
        string path = GetFilePath(executionId);
        if (File.Exists(path))
        {
            File.Delete(path);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}

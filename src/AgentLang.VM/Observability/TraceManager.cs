using System.Collections.Concurrent;

namespace AgentLang.VM.Observability;

public sealed class TraceManager
{
    private readonly AsyncLocal<AgentSpan?> _currentSpan = new();
    private readonly ConcurrentBag<AgentSpan> _completedSpans = new();

    public AgentSpan? CurrentSpan => _currentSpan.Value;
    public IReadOnlyList<AgentSpan> CompletedSpans => _completedSpans.OrderBy(s => s.StartTime).ToList();

    public AgentSpan StartSpan(
        string name,
        string kind = "Internal",
        Dictionary<string, object?>? attributes = null)
    {
        var parent = _currentSpan.Value;
        var context = parent != null ? parent.Context.CreateChild() : TraceContext.CreateNew();

        var span = new AgentSpan(context, name, kind, this);

        if (attributes != null)
        {
            foreach (var (k, v) in attributes)
            {
                span.SetAttribute(k, v);
            }
        }

        _currentSpan.Value = span;
        return span;
    }

    internal void OnSpanEnded(AgentSpan span)
    {
        _completedSpans.Add(span);
        // Note: AsyncLocal flows down async calls, so ending a span naturally returns to parent scope
    }

    public void Clear()
    {
        _completedSpans.Clear();
        _currentSpan.Value = null;
    }

    public async Task ExportAsync(ITraceExporter exporter, CancellationToken ct = default)
    {
        await exporter.ExportAsync(CompletedSpans, ct);
    }
}

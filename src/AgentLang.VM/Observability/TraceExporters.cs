using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentLang.VM.Observability;

public interface ITraceExporter
{
    Task ExportAsync(IReadOnlyList<AgentSpan> spans, CancellationToken ct = default);
}

public sealed class ConsoleTraceExporter : ITraceExporter
{
    private readonly TextWriter _writer;

    public ConsoleTraceExporter(TextWriter? writer = null)
    {
        _writer = writer ?? Console.Out;
    }

    public Task ExportAsync(IReadOnlyList<AgentSpan> spans, CancellationToken ct = default)
    {
        if (spans.Count == 0)
        {
            _writer.WriteLine("[Trace] No spans recorded.");
            return Task.CompletedTask;
        }

        var rootSpans = spans.Where(s => s.Context.ParentSpanId == null).ToList();
        var childrenLookup = spans.Where(s => s.Context.ParentSpanId != null)
            .GroupBy(s => s.Context.ParentSpanId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StartTime).ToList());

        int totalTokens = spans.Sum(s => s.TokensTotal);
        double totalCost = spans.Sum(s => s.CostUsd);

        foreach (var root in rootSpans)
        {
            _writer.WriteLine($"[Trace: {root.Context.TraceId}] Total: {root.Duration.TotalMilliseconds:F1}ms | Tokens: {totalTokens:N0} | Cost: ${totalCost:F4}");
            RenderSpanTree(root, "", true, childrenLookup);
        }

        return Task.CompletedTask;
    }

    private void RenderSpanTree(
        AgentSpan span,
        string indent,
        bool isLast,
        Dictionary<string, List<AgentSpan>> childrenLookup)
    {
        var branch = isLast ? "└── " : "├── ";
        var tokenInfo = span.TokensTotal > 0 ? $" ({span.TokensTotal} tokens)" : "";
        var statusInfo = span.Status == "Error" ? $" [ERROR: {span.StatusMessage}]" : "";

        _writer.WriteLine($"{indent}{branch}{span.Name} [{span.Duration.TotalMilliseconds:F1}ms]{tokenInfo}{statusInfo}");

        if (childrenLookup.TryGetValue(span.Context.SpanId, out var children))
        {
            var nextIndent = indent + (isLast ? "    " : "│   ");
            for (int i = 0; i < children.Count; i++)
            {
                RenderSpanTree(children[i], nextIndent, i == children.Count - 1, childrenLookup);
            }
        }
    }
}

public sealed class JsonTraceExporter : ITraceExporter
{
    private readonly string _outputDirectory;

    public JsonTraceExporter(string? outputDirectory = null)
    {
        _outputDirectory = outputDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), ".agent", "traces");
        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
        }
    }

    public async Task ExportAsync(IReadOnlyList<AgentSpan> spans, CancellationToken ct = default)
    {
        if (spans.Count == 0) return;

        var traceId = spans[0].Context.TraceId;
        var filePath = Path.Combine(_outputDirectory, $"{traceId}.json");

        var exportData = spans.Select(s => new
        {
            traceId = s.Context.TraceId,
            spanId = s.Context.SpanId,
            parentSpanId = s.Context.ParentSpanId,
            name = s.Name,
            kind = s.Kind,
            startTime = s.StartTime,
            endTime = s.EndTime,
            durationMs = s.Duration.TotalMilliseconds,
            status = s.Status,
            statusMessage = s.StatusMessage,
            tokens = new
            {
                prompt = s.TokensPrompt,
                completion = s.TokensCompletion,
                total = s.TokensTotal
            },
            costUsd = s.CostUsd,
            attributes = s.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value),
            events = s.Events.Select(e => new { name = e.Name, timestamp = e.Timestamp, attributes = e.Attributes })
        });

        var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, ct);
    }
}

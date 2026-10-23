using System.Collections.Concurrent;

namespace AgentLang.VM.Observability;

public sealed record SpanEvent(
    string Name,
    DateTimeOffset Timestamp,
    Dictionary<string, object?>? Attributes = null);

public sealed class AgentSpan : IDisposable
{
    private readonly TraceManager? _manager;

    public TraceContext Context { get; }
    public string Name { get; }
    public string Kind { get; }
    public DateTimeOffset StartTime { get; }
    public DateTimeOffset? EndTime { get; private set; }
    public TimeSpan Duration => (EndTime ?? DateTimeOffset.UtcNow) - StartTime;

    public string Status { get; set; } = "Ok";
    public string? StatusMessage { get; set; }

    public ConcurrentDictionary<string, object?> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentBag<SpanEvent> Events { get; } = new();

    public int TokensPrompt { get; private set; }
    public int TokensCompletion { get; private set; }
    public int TokensTotal => TokensPrompt + TokensCompletion;
    public double CostUsd { get; private set; }

    public AgentSpan(
        TraceContext context,
        string name,
        string kind = "Internal",
        TraceManager? manager = null)
    {
        Context = context;
        Name = name;
        Kind = kind;
        _manager = manager;
        StartTime = DateTimeOffset.UtcNow;
    }

    public AgentSpan SetAttribute(string key, object? value)
    {
        Attributes[key] = value;
        return this;
    }

    public AgentSpan AddEvent(string name, Dictionary<string, object?>? attributes = null)
    {
        Events.Add(new SpanEvent(name, DateTimeOffset.UtcNow, attributes));
        return this;
    }

    public AgentSpan RecordTokens(int promptTokens, int completionTokens, double costUsd = 0.0)
    {
        TokensPrompt += promptTokens;
        TokensCompletion += completionTokens;
        CostUsd += costUsd;

        SetAttribute("tokens.prompt", TokensPrompt);
        SetAttribute("tokens.completion", TokensCompletion);
        SetAttribute("tokens.total", TokensTotal);
        SetAttribute("cost.usd", CostUsd);

        return this;
    }

    public AgentSpan SetError(string message)
    {
        Status = "Error";
        StatusMessage = message;
        SetAttribute("error", true);
        SetAttribute("error.message", message);
        return this;
    }

    public void End()
    {
        if (EndTime.HasValue) return;
        EndTime = DateTimeOffset.UtcNow;
        _manager?.OnSpanEnded(this);
    }

    public void Dispose()
    {
        End();
    }
}

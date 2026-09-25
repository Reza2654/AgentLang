using AgentLang.AST;

namespace AgentLang.Runtime.Values;

public sealed record OperationValue(
    string OperationName,
    string Status,
    object? Result,
    string? ModelUsed = null,
    TimeSpan Latency = default,
    IReadOnlyList<string>? ToolCalls = null,
    string? Error = null,
    string Type = "operation")
{
    public double Duration => Latency.TotalMilliseconds;
    public string? Model => ModelUsed;

    public static OperationValue Succeeded(string operation, object? result, string? model = null, TimeSpan latency = default, IReadOnlyList<string>? toolCalls = null) =>
        new(operation, "completed", result, model, latency, toolCalls, null, "operation");

    public static OperationValue Failed(string operation, string error, string? model = null) =>
        new(operation, "failed", null, model, TimeSpan.Zero, null, error, "operation");

    public override string ToString() =>
        Result?.ToString() ?? $"[Operation: {OperationName}, Status: {Status}]";
}

public sealed record AgentMessage(
    object? Content,
    string? Sender = null,
    object? Tag = null,
    DateTime Timestamp = default)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;

    public override string ToString() =>
        Tag != null ? $"[{Sender ?? "Anonymous"} ({Tag})]: {Content}" : $"[{Sender ?? "Anonymous"}]: {Content}";
}

public sealed class FunctionValue
{
    public string Name { get; }
    public IReadOnlyList<string> Parameters { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public RuntimeScope Closure { get; }

    public FunctionValue(string name, IReadOnlyList<string> parameters, IReadOnlyList<StatementNode> body, RuntimeScope closure)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
        Closure = closure;
    }

    public override string ToString() => $"[Function: {Name}({string.Join(", ", Parameters)})]";
}

public sealed class TaskValue
{
    public string Name { get; }
    public object? Result { get; set; }
    public string Status { get; set; } = "pending";

    public TaskValue(string name, object? result = null)
    {
        Name = name;
        Result = result;
    }

    public override string ToString() => Result?.ToString() ?? $"[Task: {Name}, Status: {Status}]";
}

public sealed class AgentValue
{
    public string Name { get; }
    public string? Model { get; set; }
    public string? PermissionPolicy { get; set; }
    public bool MemoryEnabled { get; set; } = true;
    public List<string> Memory { get; } = [];
    public List<AgentMessage> Inbox { get; } = [];
    public Dictionary<string, object?> Context { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, TaskValue> Tasks { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FunctionValue> Functions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object?> Variables { get; } = new(StringComparer.Ordinal);

    public AgentValue(string name)
    {
        Name = name;
    }

    public override string ToString() => $"[Agent: {Name}]";
}

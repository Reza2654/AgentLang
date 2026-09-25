namespace AgentLang.Runtime.Values;

public sealed record OperationValue(
    string OperationName,
    string Status,
    object? Result,
    string? ModelUsed = null,
    TimeSpan Latency = default,
    IReadOnlyList<string>? ToolCalls = null)
{
    public static OperationValue Succeeded(string operation, object? result, string? model = null, TimeSpan latency = default, IReadOnlyList<string>? toolCalls = null) =>
        new(operation, "completed", result, model, latency, toolCalls);

    public static OperationValue Failed(string operation, string error, string? model = null) =>
        new(operation, "failed", null, model, TimeSpan.Zero, null);

    public override string ToString() =>
        Result?.ToString() ?? $"[Operation: {OperationName}, Status: {Status}]";
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
    public Dictionary<string, object?> Context { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, TaskValue> Tasks { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object?> Variables { get; } = new(StringComparer.Ordinal);

    public AgentValue(string name)
    {
        Name = name;
    }

    public override string ToString() => $"[Agent: {Name}]";
}

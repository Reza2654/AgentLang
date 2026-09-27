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
    public string? Role { get; set; }
    public string? Instructions { get; set; }
    public string? Persona { get; set; }
    public string? Goal { get; set; }
    public double? Temperature { get; set; }
    public List<string> Fallbacks { get; } = [];
    public List<string> BoundTools { get; } = [];
    public int MaxSteps { get; set; } = 10;
    public string? PermissionPolicy { get; set; }
    public bool MemoryEnabled { get; set; } = true;
    public string MemoryMode { get; set; } = "long_term";
    public List<string> Memory { get; } = [];
    public List<AgentMessage> Inbox { get; } = [];
    public List<ChatMessage> History { get; } = [];
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public Dictionary<string, object?> Context { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, TaskValue> Tasks { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FunctionValue> Functions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object?> Variables { get; } = new(StringComparer.Ordinal);

    public AgentValue(string name)
    {
        Name = name;
    }

    public void ResetSession()
    {
        History.Clear();
        SessionId = Guid.NewGuid().ToString("N");
    }

    public override string ToString() => $"[Agent: {Name}]";
}

public sealed record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<string>? ToolCalls = null,
    DateTime Timestamp = default)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
    public override string ToString() => $"[{Role}]: {Content}";
}

public sealed record ReActStep(
    int StepNumber,
    string Thought,
    string? ToolName,
    IReadOnlyDictionary<string, object?>? ToolArguments,
    string? Observation);

public sealed record AgentReActResult(
    bool Success,
    string? FinalAnswer,
    IReadOnlyList<ReActStep> Steps,
    TimeSpan Duration,
    string? Error = null)
{
    public override string ToString() => FinalAnswer ?? Error ?? $"[AgentReActResult: Success={Success}, Steps={Steps.Count}]";
}

using AgentLang.AST;
using AgentLang.Runtime.Memory;

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

public sealed class BuiltinFunctionValue
{
    public string Name { get; }
    public Func<IReadOnlyList<object?>, Task<object?>> Handler { get; }

    public BuiltinFunctionValue(string name, Func<IReadOnlyList<object?>, Task<object?>> handler)
    {
        Name = name;
        Handler = handler;
    }

    public BuiltinFunctionValue(string name, Func<IReadOnlyList<object?>, object?> syncHandler)
    {
        Name = name;
        Handler = args => Task.FromResult(syncHandler(args));
    }

    public override string ToString() => $"[BuiltinFunction: {Name}]";
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
    public List<GuardrailRule> GuardrailRules { get; } = [];
    public IAgentMemory? AgentMemoryStore { get; set; }
    public Dictionary<string, OnEventDeclarationNode> EventHandlers { get; } = new(StringComparer.OrdinalIgnoreCase);

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

public sealed class BreakException : Exception { }
public sealed class ContinueException : Exception { }

public sealed class GoalValue
{
    public string Name { get; }
    public string Description { get; }

    public GoalValue(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class AgentExecutionResult
{
    public string Output { get; set; }
    public string Summary { get; set; }
    public string Status { get; set; }
    public List<string> Risks { get; set; } = [];
    public bool Passed { get; set; } = true;
    public List<string> Errors { get; set; } = [];
    public Dictionary<string, object?> Data { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AgentExecutionResult(string output, string? summary = null, string status = "DONE")
    {
        Output = output;
        Summary = summary ?? output;
        Status = status;
    }

    public AgentExecutionResult(string output, string? summary, string status, List<string>? risks)
        : this(output, summary, status)
    {
        if (risks != null) Risks = risks;
    }

    public void SetProperty(string name, object? value)
    {
        Data[name] = value;
    }

    public object? GetProperty(string name)
    {
        if (Data.TryGetValue(name, out var val)) return val;
        return null;
    }

    public override string ToString() => Output;
}

public sealed class StateDefinitionValue
{
    public StateDeclarationNode Declaration { get; }
    public object Runtime { get; }

    public StateDefinitionValue(StateDeclarationNode declaration, object runtime)
    {
        Declaration = declaration;
        Runtime = runtime;
    }

    public override string ToString() => $"[StateDefinition: {Declaration.Name}]";
}

public sealed class StateInstanceValue
{
    public string StateName { get; }
    public Dictionary<string, object?> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StateInstanceValue(string stateName)
    {
        StateName = stateName;
    }

    public object? GetField(string name)
    {
        Fields.TryGetValue(name, out var val);
        return val;
    }

    public void SetField(string name, object? value)
    {
        Fields[name] = value;
    }

    public override string ToString()
    {
        var entries = Fields.Select(kv => $"{kv.Key}: {kv.Value ?? "null"}");
        return $"{StateName} {{ {string.Join(", ", entries)} }}";
    }
}

public sealed class PipelineValue
{
    public PipelineDeclarationNode Declaration { get; }
    public RuntimeScope Closure { get; }

    public PipelineValue(PipelineDeclarationNode declaration, RuntimeScope closure)
    {
        Declaration = declaration;
        Closure = closure;
    }

    public override string ToString() => $"[Pipeline: {Declaration.Name}]";
}

public sealed class WorkflowValue
{
    public WorkflowDeclarationNode Declaration { get; }
    public RuntimeScope Closure { get; }
    public object Runtime { get; }

    public WorkflowValue(WorkflowDeclarationNode declaration, RuntimeScope closure, object runtime)
    {
        Declaration = declaration;
        Closure = closure;
        Runtime = runtime;
    }

    public override string ToString() => $"[Workflow: {Declaration.Name}]";
}

// --- Phase 2: Budget Control, Guardrails Engine, and HITL Runtime Values ---

public sealed class BudgetExceededException : Exception
{
    public int TokensUsed { get; }
    public double CostIncurred { get; }

    public BudgetExceededException(string message, int tokensUsed = 0, double costIncurred = 0.0)
        : base(message)
    {
        TokensUsed = tokensUsed;
        CostIncurred = costIncurred;
    }
}

public sealed class BudgetContext
{
    public int? MaxTokens { get; set; }
    public double? MaxCost { get; set; }
    public TimeSpan? MaxDuration { get; set; }

    public int UsedTokens { get; set; }
    public double IncurredCost { get; set; }
    public System.Diagnostics.Stopwatch Stopwatch { get; } = System.Diagnostics.Stopwatch.StartNew();

    public BudgetContext(int? maxTokens = null, double? maxCost = null, TimeSpan? maxDuration = null)
    {
        MaxTokens = maxTokens;
        MaxCost = maxCost;
        MaxDuration = maxDuration;
    }

    public void RecordUsage(int tokens, string? model) => RecordUsage(tokens, null, model);

    public void RecordUsage(int tokens, double? cost = null, string? model = null)
    {
        UsedTokens += tokens;
        double addedCost = cost ?? CalculateCost(tokens, model);
        IncurredCost += addedCost;

        CheckLimits();
    }

    public void CheckLimits()
    {
        if (MaxTokens.HasValue && UsedTokens > MaxTokens.Value)
        {
            throw new BudgetExceededException($"Token budget exceeded: limit is {MaxTokens.Value} tokens, but used {UsedTokens} tokens.", UsedTokens, IncurredCost);
        }

        if (MaxCost.HasValue && IncurredCost > MaxCost.Value)
        {
            throw new BudgetExceededException($"Cost budget exceeded: limit is ${MaxCost.Value:F4}, but incurred ${IncurredCost:F4}.", UsedTokens, IncurredCost);
        }

        if (MaxDuration.HasValue && Stopwatch.Elapsed > MaxDuration.Value)
        {
            throw new BudgetExceededException($"Time budget exceeded: limit is {MaxDuration.Value.TotalSeconds}s, but elapsed {Stopwatch.Elapsed.TotalSeconds:F2}s.", UsedTokens, IncurredCost);
        }
    }

    public static double CalculateCost(int tokens, string? model)
    {
        if (string.IsNullOrEmpty(model)) return (tokens / 1000.0) * 0.002;
        string m = model.ToLowerInvariant();
        if (m.Contains("opus")) return (tokens / 1_000_000.0) * 30.0;
        if (m.Contains("sonnet") || m.Contains("gpt-4o")) return (tokens / 1_000_000.0) * 5.0;
        if (m.Contains("mini") || m.Contains("flash")) return (tokens / 1_000_000.0) * 0.3;
        return (tokens / 1000.0) * 0.002;
    }
}

public sealed class BudgetErrorValue
{
    public string Message { get; }
    public int TokensUsed { get; }
    public double CostIncurred { get; }

    public BudgetErrorValue(string message, int tokensUsed = 0, double costIncurred = 0.0)
    {
        Message = message;
        TokensUsed = tokensUsed;
        CostIncurred = costIncurred;
    }

    public override string ToString() => Message;
}

public sealed class GuardrailViolationException : Exception
{
    public string Details { get; }
    public string RuleName { get; }

    public GuardrailViolationException(string message, string details = "", string ruleName = "GuardrailRule")
        : base(message)
    {
        Details = string.IsNullOrEmpty(details) ? message : details;
        RuleName = ruleName;
    }
}

public sealed class GuardrailErrorValue
{
    public string Message { get; }
    public string Details { get; }

    public GuardrailErrorValue(string message, string details)
    {
        Message = message;
        Details = details;
    }

    public override string ToString() => Details;
}

public abstract class GuardrailRule
{
    public abstract void Validate(string text, AgentExecutionResult result);
}

public sealed class ForbidGuardrailRule(IReadOnlyList<string> forbiddenPhrases) : GuardrailRule
{
    public IReadOnlyList<string> ForbiddenPhrases { get; } = forbiddenPhrases;

    public override void Validate(string text, AgentExecutionResult result)
    {
        foreach (var phrase in ForbiddenPhrases)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                throw new GuardrailViolationException(
                    $"Output violated guardrail: forbidden phrase '{phrase}' was detected.",
                    details: $"Forbidden content detected: '{phrase}'");
            }
        }
    }
}

public sealed class RequireFormatGuardrailRule(IReadOnlyList<string> requiredFields) : GuardrailRule
{
    public IReadOnlyList<string> RequiredFields { get; } = requiredFields;

    public override void Validate(string text, AgentExecutionResult result)
    {
        foreach (var field in RequiredFields)
        {
            bool hasField = result.Data.ContainsKey(field) ||
                            (field.Equals("summary", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(result.Summary)) ||
                            (field.Equals("risks", StringComparison.OrdinalIgnoreCase) && result.Risks.Count > 0);

            if (!hasField && !text.Contains(field, StringComparison.OrdinalIgnoreCase))
            {
                throw new GuardrailViolationException(
                    $"Output violated require_format guardrail: missing required format field '{field}'.",
                    details: $"Missing required field '{field}' in schema output.");
            }
        }
    }
}

// --- Phase 3: Memory Values ---

public sealed class VectorStoreMemoryValue
{
    public VectorStoreMemory Store { get; }
    public VectorStoreMemory Memory => Store;
    public string Collection => Store.Collection;
    public int TopK => Store.TopK;
    public string SimilarityMetric => Store.SimilarityMetric;

    public VectorStoreMemoryValue(VectorStoreMemory store)
    {
        Store = store;
    }

    public IReadOnlyList<string> Retrieve(string query, int? topK = null) => Store.Retrieve(query, topK);
    public void Add(string text) => Store.Store(text);
    public void StoreDocument(string text) => Store.Store(text);
    public void Clear() => Store.Clear();
    public override string ToString() => $"[VectorStoreMemory: {Collection}]";
}

public sealed class ConversationBufferValue
{
    public ConversationBufferMemory Buffer { get; }
    public ConversationBufferMemory Memory => Buffer;
    public int Window => Buffer.Window;

    public ConversationBufferValue(ConversationBufferMemory buffer)
    {
        Buffer = buffer;
    }

    public override string ToString() => $"[ConversationBuffer(window: {Window})]";
}

public sealed class EphemeralMemoryValue
{
    public EphemeralMemory Memory { get; }

    public EphemeralMemoryValue(EphemeralMemory memory)
    {
        Memory = memory;
    }

    public IReadOnlyList<string> Retrieve(string query, int? topK = null) => Memory.Retrieve(query, topK);
    public void Add(string text) => Memory.Store(text);
    public void Clear() => Memory.Clear();
    public override string ToString() => "[EphemeralMemory]";
}

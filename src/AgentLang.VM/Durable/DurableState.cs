using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLang.VM.Durable;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DurableStatus
{
    Pending,
    Running,
    Suspended,
    Completed,
    Faulted
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SuspensionReason
{
    WaitForSignal,
    HumanApprovalRequired,
    TimerExpired,
    ExternalWebhook
}

public sealed class ExecutionSignal
{
    [JsonPropertyName("signalName")]
    public string SignalName { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public ExecutionSignal() { }

    public ExecutionSignal(string signalName, object? payload = null)
    {
        SignalName = signalName;
        Payload = payload;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

public sealed class DurableExecutionContext
{
    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("workflowName")]
    public string WorkflowName { get; set; } = "DefaultDurableWorkflow";

    [JsonPropertyName("status")]
    public DurableStatus Status { get; set; } = DurableStatus.Pending;

    [JsonPropertyName("suspensionReason")]
    public SuspensionReason? SuspensionReason { get; set; }

    [JsonPropertyName("waitingForSignalName")]
    public string? WaitingForSignalName { get; set; }

    [JsonPropertyName("stepIndex")]
    public int StepIndex { get; set; }

    [JsonPropertyName("stateVariables")]
    public Dictionary<string, object?> StateVariables { get; set; } = new();

    [JsonPropertyName("agentHistories")]
    public Dictionary<string, List<string>> AgentHistories { get; set; } = new();

    [JsonPropertyName("receivedSignals")]
    public List<ExecutionSignal> ReceivedSignals { get; set; } = [];

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("finalOutput")]
    public object? FinalOutput { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonOptions) { WriteIndented = indented });

    public static DurableExecutionContext FromJson(string json) =>
        JsonSerializer.Deserialize<DurableExecutionContext>(json, JsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize durable execution context.");
}

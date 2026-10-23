using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLang.VM.Checkpointing;

public sealed class ExecutionCheckpoint
{
    [JsonPropertyName("checkpointId")]
    public string CheckpointId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("workflowName")]
    public string WorkflowName { get; set; } = "DefaultWorkflow";

    [JsonPropertyName("stepIndex")]
    public int StepIndex { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Active";

    [JsonPropertyName("stateVariables")]
    public Dictionary<string, object?> StateVariables { get; set; } = new();

    [JsonPropertyName("agentHistories")]
    public Dictionary<string, List<string>> AgentHistories { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = indented });

    public static ExecutionCheckpoint FromJson(string json) =>
        JsonSerializer.Deserialize<ExecutionCheckpoint>(json)
        ?? throw new InvalidOperationException("Failed to deserialize execution checkpoint JSON.");
}

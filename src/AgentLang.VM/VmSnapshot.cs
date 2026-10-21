using System.Text.Json;

namespace AgentLang.VM;

public sealed class VmSnapshot
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string? Label { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public long InstructionCount { get; init; }
    public string ExecutionStatus { get; init; } = "Suspended";
    public Dictionary<string, object?> GlobalVariables { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> AgentMemories { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> AgentModels { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static VmSnapshot FromJson(string json) => JsonSerializer.Deserialize<VmSnapshot>(json) ?? throw new InvalidOperationException("Failed to deserialize VM snapshot.");
}

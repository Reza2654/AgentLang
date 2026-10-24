using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentLang.VM.Testing;

public enum RecordMode
{
    None,
    Record,
    Replay,
    Auto
}

public sealed record CassetteInteraction(
    string ToolName,
    string ArgumentsHash,
    string Arguments,
    string Result,
    DateTime Timestamp,
    TimeSpan Latency
);

public sealed class CassetteInteractionNotFoundException : Exception
{
    public string ToolName { get; }
    public string Arguments { get; }

    public CassetteInteractionNotFoundException(string toolName, string arguments)
        : base($"No matching recorded interaction found for tool '{toolName}' with arguments '{arguments}'. Set RecordMode to Record or Auto to capture this interaction.")
    {
        ToolName = toolName;
        Arguments = arguments;
    }
}

public sealed class ToolCassette
{
    public string Name { get; set; }
    public List<CassetteInteraction> Interactions { get; set; } = [];

    public ToolCassette(string name = "default")
    {
        Name = name;
    }

    public static string HashArguments(string arguments)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(arguments.Trim()));
        return Convert.ToHexStringLower(bytes)[..16];
    }

    public void SaveToFile(string filePath)
    {
        string dir = Path.GetDirectoryName(filePath) ?? "";
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public static ToolCassette LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ToolCassette(Path.GetFileNameWithoutExtension(filePath));
        }

        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<ToolCassette>(json) ?? new ToolCassette();
    }
}

public sealed class CassetteRecorder
{
    public RecordMode Mode { get; set; } = RecordMode.Auto;
    public ToolCassette Cassette { get; set; }
    private readonly string? _cassetteFilePath;

    public CassetteRecorder(string cassetteName = "default", RecordMode mode = RecordMode.Auto, string? storageDir = null)
    {
        Mode = mode;
        if (!string.IsNullOrEmpty(storageDir))
        {
            _cassetteFilePath = Path.Combine(storageDir, $"{cassetteName}.cassette.json");
            Cassette = ToolCassette.LoadFromFile(_cassetteFilePath);
        }
        else
        {
            Cassette = new ToolCassette(cassetteName);
        }
    }

    public async Task<string> ExecuteWithCassetteAsync(
        string toolName,
        string arguments,
        Func<CancellationToken, Task<string>> realToolExecutor,
        CancellationToken ct = default)
    {
        if (Mode == RecordMode.None)
        {
            return await realToolExecutor(ct);
        }

        string hash = ToolCassette.HashArguments(arguments);

        // Try replay first if mode is Replay or Auto
        if (Mode is RecordMode.Replay or RecordMode.Auto)
        {
            var match = Cassette.Interactions.FirstOrDefault(i =>
                i.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase) &&
                i.ArgumentsHash.Equals(hash, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match.Result;
            }

            if (Mode == RecordMode.Replay)
            {
                throw new CassetteInteractionNotFoundException(toolName, arguments);
            }
        }

        // Record mode or fallback in Auto mode
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string realResult = await realToolExecutor(ct);
        sw.Stop();

        var interaction = new CassetteInteraction(
            toolName,
            hash,
            arguments,
            realResult,
            DateTime.UtcNow,
            sw.Elapsed
        );

        Cassette.Interactions.Add(interaction);

        if (!string.IsNullOrEmpty(_cassetteFilePath))
        {
            Cassette.SaveToFile(_cassetteFilePath);
        }

        return realResult;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLang.Runtime.Configuration;

public sealed class ProjectConfig
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; } = "https://agentlang.dev/schema/config.json";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "agentlang-project";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.1.0";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("default_model")]
    public string? DefaultModel { get; set; }

    [JsonPropertyName("budget_limit_usd")]
    public double? BudgetLimitUsd { get; set; }

    [JsonPropertyName("timeout")]
    public string? Timeout { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("tools")]
    public List<string> Tools { get; set; } = [];

    public static ProjectConfig? FindAndLoad(string? startDirectory = null)
    {
        string dir = string.IsNullOrWhiteSpace(startDirectory)
            ? Directory.GetCurrentDirectory()
            : (File.Exists(startDirectory) ? Path.GetDirectoryName(startDirectory)! : startDirectory);

        while (!string.IsNullOrEmpty(dir))
        {
            string cfgPath = Path.Combine(dir, "agent.config.json");
            if (File.Exists(cfgPath))
                return LoadFromFile(cfgPath);

            string altPath = Path.Combine(dir, "agent.json");
            if (File.Exists(altPath))
                return LoadFromFile(altPath);

            var parent = Directory.GetParent(dir);
            if (parent == null || parent.FullName == dir)
                break;
            dir = parent.FullName;
        }

        return null;
    }

    public static ProjectConfig LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonSerializer.Deserialize<ProjectConfig>(json, options) ?? new ProjectConfig();
    }

    public void ApplyEnvironment()
    {
        foreach (var (k, v) in Env)
        {
            if (!string.IsNullOrWhiteSpace(k) && Environment.GetEnvironmentVariable(k) == null)
            {
                Environment.SetEnvironmentVariable(k, v);
            }
        }
    }

    public static ProjectConfig CreateDefault(string projectName = "my-agent-system")
    {
        return new ProjectConfig
        {
            Name = projectName,
            Version = "0.1.0",
            Description = "Autonomous AI Agent Project built with AgentLang",
            DefaultModel = "gemini-4-argon",
            Timeout = "30s",
            BudgetLimitUsd = 10.0,
            Env = new Dictionary<string, string>
            {
                ["ENVIRONMENT"] = "development"
            },
            Tools = ["web_search", "document_summary"]
        };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}

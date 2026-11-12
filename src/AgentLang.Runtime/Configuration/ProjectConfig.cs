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

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
        var docOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        using var doc = JsonDocument.Parse(json, docOptions);
        var cfg = new ProjectConfig();
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return cfg;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            switch (prop.Name.ToLowerInvariant())
            {
                case "$schema":
                    cfg.Schema = prop.Value.GetString();
                    break;
                case "name":
                    cfg.Name = prop.Value.GetString() ?? cfg.Name;
                    break;
                case "version":
                    cfg.Version = prop.Value.GetString() ?? cfg.Version;
                    break;
                case "description":
                    cfg.Description = prop.Value.GetString();
                    break;
                case "defaultmodel" or "default_model":
                    cfg.DefaultModel = prop.Value.GetString();
                    break;
                case "budgetlimitusd" or "budget_limit_usd":
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        cfg.BudgetLimitUsd = prop.Value.GetDouble();
                    break;
                case "timeout":
                    cfg.Timeout = prop.Value.GetString();
                    break;
                case "env":
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var kv in prop.Value.EnumerateObject())
                            cfg.Env[kv.Name] = kv.Value.ToString();
                    }
                    break;
                case "tools":
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            string? s = item.GetString();
                            if (!string.IsNullOrEmpty(s)) cfg.Tools.Add(s);
                        }
                    }
                    break;
                case "dependencies":
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var kv in prop.Value.EnumerateObject())
                            cfg.Dependencies[kv.Name] = kv.Value.ToString();
                    }
                    break;
            }
        }
        return cfg;
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
            Tools = ["web_search", "document_summary"],
            Dependencies = new Dictionary<string, string>
            {
                ["stdlib"] = "latest"
            }
        };
    }

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            if (Schema != null) w.WriteString("$schema", Schema);
            w.WriteString("name", Name);
            w.WriteString("version", Version);
            if (Description != null) w.WriteString("description", Description);
            if (DefaultModel != null) w.WriteString("default_model", DefaultModel);
            if (BudgetLimitUsd.HasValue) w.WriteNumber("budget_limit_usd", BudgetLimitUsd.Value);
            if (Timeout != null) w.WriteString("timeout", Timeout);

            w.WriteStartObject("env");
            foreach (var kv in Env) w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();

            w.WriteStartArray("tools");
            foreach (var tool in Tools) w.WriteStringValue(tool);
            w.WriteEndArray();

            w.WriteStartObject("dependencies");
            foreach (var dep in Dependencies) w.WriteString(dep.Key, dep.Value);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}

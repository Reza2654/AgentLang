namespace AgentLang.Tools.MCP;

public sealed class McpToolAdapter : ITool
{
    private readonly McpClient _client;

    public string Name => _client.ServerName;

    public IReadOnlyList<string> SupportedCapabilities
    {
        get
        {
            var list = new List<string> { Name };
            foreach (var tool in _client.DiscoveredTools)
            {
                list.Add($"{Name}.{tool.Name}");
                list.Add(tool.Name);
            }
            return list;
        }
    }

    public McpClient Client => _client;

    public McpToolAdapter(McpClient client)
    {
        _client = client;
    }

    public async Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string toolName = capability;
        if (toolName.StartsWith(Name + ".", StringComparison.OrdinalIgnoreCase))
        {
            toolName = toolName[(Name.Length + 1)..];
        }

        // If the capability is just the server name, look for default tool or action
        if (toolName.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.TryGetValue("action", out var action) || arguments.TryGetValue("tool", out action))
            {
                toolName = action?.ToString() ?? "";
            }
            else if (_client.DiscoveredTools.Count > 0)
            {
                toolName = _client.DiscoveredTools[0].Name;
            }
        }

        return await _client.CallToolAsync(toolName, arguments, ct);
    }
}

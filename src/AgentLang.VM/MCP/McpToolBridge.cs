using AgentLang.Tools;

namespace AgentLang.VM.MCP;

public sealed class McpToolBridge : ITool
{
    private readonly McpClient _client;
    private readonly McpToolDefinition _definition;

    public string Name { get; }
    public string Description => _definition.Description ?? string.Empty;
    public IReadOnlyList<string> SupportedCapabilities => new[] { Name };

    public McpToolBridge(McpClient client, McpToolDefinition definition, string? prefix = null)
    {
        _client = client;
        _definition = definition;
        Name = string.IsNullOrEmpty(prefix)
            ? $"mcp__{definition.Name}"
            : $"mcp__{prefix}_{definition.Name}";
    }

    public async Task<ToolResult> ExecuteAsync(
        string capability,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _client.CallToolAsync(_definition.Name, arguments, ct);
            if (result.IsError)
            {
                var errorText = string.Join("\n", result.Content.Select(c => c.Text));
                return ToolResult.Fail(string.IsNullOrEmpty(errorText) ? "MCP tool execution failed." : errorText);
            }

            var outputText = string.Join("\n", result.Content.Select(c => c.Text));
            return ToolResult.Ok(outputText);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"MCP bridge error: {ex.Message}");
        }
    }
}

using AgentLang.Security;

namespace AgentLang.Tools;

public sealed record ToolResult(
    bool Success,
    object? Output,
    string? Error = null)
{
    public static ToolResult Ok(object? output) => new(true, output, null);
    public static ToolResult Fail(string error) => new(false, null, error);
}

public interface ITool
{
    string Name { get; }
    IReadOnlyList<string> SupportedCapabilities { get; }
    Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default);
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly SecurityEngine _securityEngine;

    public ToolRegistry(SecurityEngine? securityEngine = null)
    {
        _securityEngine = securityEngine ?? new SecurityEngine();

        // Register default tools
        RegisterTool(new FilesystemTool());
        RegisterTool(new BrowserTool());
        RegisterTool(new TerminalTool());
        RegisterTool(new HttpTool());
        RegisterTool(new CalculatorTool());
    }

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public ITool? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    public async Task<ToolResult> InvokeAsync(
        string agentName,
        string? policyName,
        string capability,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        // 1. Authorize capability via SecurityEngine
        string details = string.Join(", ", arguments.Select(kv => $"{kv.Key}={kv.Value}"));
        await _securityEngine.AuthorizeAsync(agentName, policyName, capability, details, ct);

        // 2. Locate tool by prefix (e.g. 'browser.search' -> tool 'browser')
        string toolName = capability.Contains('.') ? capability.Split('.')[0] : capability;
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return ToolResult.Fail($"Tool '{toolName}' is not registered");
        }

        // 3. Execute tool
        return await tool.ExecuteAsync(capability, arguments, ct);
    }
}

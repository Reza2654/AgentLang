using AgentLang.AST;
using AgentLang.Security;

namespace AgentLang.Tools;

public sealed class CustomAgentLangTool : ITool
{
    private readonly CustomToolDeclarationNode _declaration;
    private readonly Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object?>>? _executor;

    public string Name => _declaration.Name;
    public string? Description => _declaration.Description;
    public IReadOnlyList<ToolParameterNode> Inputs => _declaration.Inputs;
    public CustomToolDeclarationNode Declaration => _declaration;

    public IReadOnlyList<string> SupportedCapabilities { get; }

    public CustomAgentLangTool(
        CustomToolDeclarationNode declaration,
        Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object?>>? executor = null)
    {
        _declaration = declaration;
        _executor = executor;
        SupportedCapabilities = [declaration.Name, $"{declaration.Name}.execute"];
    }

    public async Task<ToolResult> ExecuteAsync(
        string capability,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        if (_executor == null)
        {
            return ToolResult.Fail($"Tool '{Name}' has no runtime executor attached");
        }

        try
        {
            var result = await _executor(arguments, ct);
            return ToolResult.Ok(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Error executing tool '{Name}': {ex.Message}");
        }
    }
}

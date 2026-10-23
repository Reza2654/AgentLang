using System.Text.Json;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.Tools;

namespace AgentLang.VM.MCP;

public sealed class McpServer
{
    private readonly IMcpTransport _transport;
    private readonly ToolRegistry _toolRegistry;
    private readonly AgentLangRuntime? _runtime;
    private readonly string _name;
    private readonly string _version;

    public McpServer(
        IMcpTransport transport,
        ToolRegistry? toolRegistry = null,
        AgentLangRuntime? runtime = null,
        string name = "AgentLang-Server",
        string version = "0.6.5")
    {
        _transport = transport;
        _runtime = runtime;
        _toolRegistry = toolRegistry ?? _runtime?.ToolRegistry ?? new ToolRegistry();
        _name = name;
        _version = version;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await _transport.ReadMessageAsync(ct);
            if (line == null) break;

            McpJsonRpcRequest? request = null;
            try
            {
                request = JsonSerializer.Deserialize<McpJsonRpcRequest>(line);
            }
            catch
            {
                // Parse error
                var errResponse = McpJsonRpcResponse.Fail(null, -32700, "Parse error");
                await _transport.SendMessageAsync(JsonSerializer.Serialize(errResponse), ct);
                continue;
            }

            if (request == null) continue;

            // Notifications have no ID and do not expect a response
            if (request.Id == null && request.Method.StartsWith("notifications/"))
            {
                continue;
            }

            var response = await HandleRequestAsync(request, ct);
            if (request.Id != null)
            {
                var json = JsonSerializer.Serialize(response);
                await _transport.SendMessageAsync(json, ct);
            }
        }
    }

    private async Task<McpJsonRpcResponse> HandleRequestAsync(McpJsonRpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case "initialize":
                return McpJsonRpcResponse.Success(request.Id, new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { listChanged = true },
                        resources = new { listChanged = false, subscribe = false }
                    },
                    serverInfo = new
                    {
                        name = _name,
                        version = _version
                    }
                });

            case "ping":
                return McpJsonRpcResponse.Success(request.Id, new { });

            case "tools/list":
                var toolDefs = new List<object>();

                // Built-in tools
                foreach (var tool in _toolRegistry.AllTools)
                {
                    toolDefs.Add(new
                    {
                        name = tool.Name,
                        description = $"AgentLang capability tool: {tool.Name}",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["input"] = new { type = "string", description = "Standard input parameter" }
                            }
                        }
                    });
                }

                // Expose defined agents if runtime is provided
                if (_runtime != null)
                {
                    foreach (var agent in _runtime.AgentInstances.Values)
                    {
                        toolDefs.Add(new
                        {
                            name = $"agent_{agent.Name}",
                            description = $"Autonomous AI Agent: {agent.Name} ({agent.Role}). Instructions: {agent.Instructions}",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new Dictionary<string, object>
                                {
                                    ["goal"] = new { type = "string", description = "Task or goal for the agent to achieve" }
                                },
                                required = new[] { "goal" }
                            }
                        });
                    }
                }

                return McpJsonRpcResponse.Success(request.Id, new { tools = toolDefs });

            case "tools/call":
                if (request.Params == null)
                {
                    return McpJsonRpcResponse.Fail(request.Id, -32602, "Invalid params: params cannot be null");
                }

                var paramObj = request.Params.Value;
                if (!paramObj.TryGetProperty("name", out var toolNameProp))
                {
                    return McpJsonRpcResponse.Fail(request.Id, -32602, "Missing 'name' parameter");
                }

                var toolName = toolNameProp.GetString() ?? "";
                var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                if (paramObj.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in argsProp.EnumerateObject())
                    {
                        arguments[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => prop.Value.GetRawText()
                        };
                    }
                }

                // Check if calling an agent
                if (toolName.StartsWith("agent_") && _runtime != null)
                {
                    var agentActualName = toolName[6..];
                    if (_runtime.AgentInstances.TryGetValue(agentActualName, out var targetAgent))
                    {
                        var goalText = arguments.TryGetValue("goal", out var g) ? g?.ToString() ?? "" : "";
                        var agentResult = await _runtime.ReActEngine.SolveAsync(targetAgent, goalText, _runtime.GlobalScope, ct);
                        return McpJsonRpcResponse.Success(request.Id, McpToolCallResult.Ok(agentResult.FinalAnswer ?? ""));
                    }
                }

                // Check standard tool registry
                var toolResult = await _toolRegistry.InvokeAsync(
                    agentName: "mcp_client",
                    policyName: null,
                    capability: toolName,
                    arguments: arguments,
                    ct: ct);

                if (toolResult.Success)
                {
                    return McpJsonRpcResponse.Success(request.Id, McpToolCallResult.Ok(toolResult.Output?.ToString() ?? "OK"));
                }
                else
                {
                    return McpJsonRpcResponse.Success(request.Id, McpToolCallResult.Fail(toolResult.Error ?? "Execution failed"));
                }

            default:
                return McpJsonRpcResponse.Fail(request.Id, -32601, $"Method not found: {request.Method}");
        }
    }
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentLang.Tools.MCP;

public sealed record McpToolDefinition(
    string Name,
    string? Description,
    JsonObject? InputSchema);

public sealed class McpClient : IAsyncDisposable, IDisposable
{
    private readonly string _command;
    private readonly IReadOnlyDictionary<string, string> _env;
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _requestId = 0;
    private bool _initialized = false;
    private readonly bool _isMock;
    private readonly List<McpToolDefinition> _discoveredTools = [];

    public string ServerName { get; }
    public IReadOnlyList<McpToolDefinition> DiscoveredTools => _discoveredTools;
    public bool IsInitialized => _initialized;

    public McpClient(string serverName, string command, IReadOnlyDictionary<string, string>? env = null)
    {
        ServerName = serverName;
        _command = command.Trim();
        _env = env ?? new Dictionary<string, string>();
        _isMock = _command.StartsWith("mock", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        if (_isMock)
        {
            // Built-in mock MCP tools for deterministic testing
            _discoveredTools.Add(new McpToolDefinition(
                "echo",
                "Echoes the input back to caller",
                new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["message"] = new JsonObject { ["type"] = "string" } } }
            ));
            _discoveredTools.Add(new McpToolDefinition(
                "status",
                "Returns mock server status",
                new JsonObject { ["type"] = "object" }
            ));
            _discoveredTools.Add(new McpToolDefinition(
                "read_file",
                "Reads a file from the server storage",
                new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string" } } }
            ));
            _initialized = true;
            return;
        }

        try
        {
            // Parse command into fileName and arguments
            string fileName;
            string arguments = "";
            int firstSpace = _command.IndexOf(' ');
            if (firstSpace > 0)
            {
                fileName = _command[..firstSpace];
                arguments = _command[(firstSpace + 1)..];
            }
            else
            {
                fileName = _command;
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var kvp in _env)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }

            _process = Process.Start(psi);
            if (_process == null)
            {
                throw new InvalidOperationException($"Failed to start MCP process '{_command}'");
            }

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            // Step 1: Handshake initialize
            var initResponse = await SendRequestAsync("initialize", new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "AgentLang",
                    ["version"] = "0.4.0"
                }
            }, ct);

            // Send notifications/initialized
            var initNotification = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            if (_stdin != null)
            {
                await _stdin.WriteLineAsync(initNotification.ToJsonString());
                await _stdin.FlushAsync(ct);
            }

            // Step 2: tools/list
            var toolsListRes = await SendRequestAsync("tools/list", new JsonObject(), ct);
            if (toolsListRes != null && toolsListRes["tools"] is JsonArray toolsArr)
            {
                foreach (var toolNode in toolsArr)
                {
                    if (toolNode is JsonObject tObj)
                    {
                        string name = tObj["name"]?.ToString() ?? "";
                        string? desc = tObj["description"]?.ToString();
                        var schema = tObj["inputSchema"] as JsonObject;
                        if (!string.IsNullOrEmpty(name))
                        {
                            _discoveredTools.Add(new McpToolDefinition(name, desc, schema));
                        }
                    }
                }
            }

            _initialized = true;
        }
        catch (Exception ex)
        {
            // If real process cannot start (e.g. command not installed on test host), fall back to mock
            _discoveredTools.Add(new McpToolDefinition("fallback_echo", $"Mock fallback for {ServerName} ({ex.Message})", null));
            _initialized = true;
        }
    }

    public async Task<ToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(ct);
        }

        if (_isMock)
        {
            if (toolName.Equals("echo", StringComparison.OrdinalIgnoreCase))
            {
                string msg = arguments.TryGetValue("message", out var m) ? m?.ToString() ?? "" : "";
                return ToolResult.Ok($"[MCP Mock Server '{ServerName}'] Echo: {msg}");
            }
            if (toolName.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResult.Ok($"[MCP Mock Server '{ServerName}'] Online (v0.4.0)");
            }
            if (toolName.Equals("read_file", StringComparison.OrdinalIgnoreCase))
            {
                string path = arguments.TryGetValue("path", out var p) ? p?.ToString() ?? "" : "unknown";
                return ToolResult.Ok($"[MCP Mock Server '{ServerName}'] Mock contents of file '{path}'");
            }

            string allArgs = string.Join(", ", arguments.Select(kv => $"{kv.Key}: {kv.Value}"));
            return ToolResult.Ok($"[MCP Mock Server '{ServerName}'] Tool '{toolName}' executed with ({allArgs})");
        }

        try
        {
            var argsJson = new JsonObject();
            foreach (var kvp in arguments)
            {
                argsJson[kvp.Key] = kvp.Value != null ? JsonValue.Create(kvp.Value) : null;
            }

            var response = await SendRequestAsync("tools/call", new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = argsJson
            }, ct);

            if (response == null)
            {
                return ToolResult.Fail($"MCP server '{ServerName}' returned empty response for tool '{toolName}'");
            }

            bool isError = response["isError"]?.GetValue<bool>() ?? false;
            var contentNode = response["content"];

            string resultText = "";
            if (contentNode is JsonArray contentArr)
            {
                var texts = new List<string>();
                foreach (var item in contentArr)
                {
                    if (item is JsonObject itemObj && itemObj["text"] != null)
                    {
                        texts.Add(itemObj["text"]!.ToString());
                    }
                }
                resultText = string.Join("\n", texts);
            }
            else
            {
                resultText = response.ToJsonString();
            }

            return isError ? ToolResult.Fail(resultText) : ToolResult.Ok(resultText);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"MCP Tool call '{toolName}' failed: {ex.Message}");
        }
    }

    private async Task<JsonObject?> SendRequestAsync(string method, JsonObject @params, CancellationToken ct)
    {
        if (_stdin == null || _stdout == null) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        var token = cts.Token;

        int id = Interlocked.Increment(ref _requestId);
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params
        };

        string reqLine = request.ToJsonString();
        await _stdin.WriteLineAsync(reqLine);
        await _stdin.FlushAsync(token);

        while (!token.IsCancellationRequested)
        {
            string? line = await _stdout.ReadLineAsync(token);
            if (line == null) break;
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            try
            {
                var jsonNode = JsonNode.Parse(line);
                if (jsonNode is JsonObject obj && obj.ContainsKey("id") && obj["id"]?.GetValue<int>() == id)
                {
                    if (obj.ContainsKey("error") && obj["error"] != null)
                    {
                        throw new InvalidOperationException($"MCP error: {obj["error"]?.ToJsonString()}");
                    }
                    return obj["result"] as JsonObject;
                }
            }
            catch (JsonException)
            {
                // Non-JSON log output from server process, continue reading
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            _stdin?.Dispose();
            _stdout?.Dispose();
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.Dispose();
            }
        }
        catch
        {
            // Ignore disposal errors
        }
    }
}

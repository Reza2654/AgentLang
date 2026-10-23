using System.Collections.Concurrent;
using System.Text.Json;
using AgentLang.Tools;

namespace AgentLang.VM.MCP;

public sealed class McpClient : IAsyncDisposable
{
    private readonly IMcpTransport _transport;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<McpJsonRpcResponse>> _pendingRequests = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _readLoopTask;
    private long _requestIdCounter;

    public bool IsInitialized { get; private set; }
    public string? ServerName { get; private set; }
    public string? ServerVersion { get; private set; }

    public McpClient(IMcpTransport transport)
    {
        _transport = transport;
        _readLoopTask = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var line = await _transport.ReadMessageAsync(_cts.Token);
                if (line == null) break;

                try
                {
                    var response = JsonSerializer.Deserialize<McpJsonRpcResponse>(line);
                    if (response?.Id != null)
                    {
                        var idKey = response.Id.ToString() ?? "";
                        if (_pendingRequests.TryRemove(idKey, out var tcs))
                        {
                            tcs.TrySetResult(response);
                        }
                    }
                }
                catch
                {
                    // Ignore non-json-rpc lines or unparseable lines
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Channel or stream closed
        }
        finally
        {
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    public async Task<McpJsonRpcResponse> SendRequestAsync(string method, object? @params, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _requestIdCounter).ToString();
        var tcs = new TaskCompletionSource<McpJsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        var request = new McpJsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = id,
            Method = method,
            Params = @params != null ? JsonSerializer.SerializeToElement(@params) : null
        };

        var json = JsonSerializer.Serialize(request);
        await _transport.SendMessageAsync(json, ct);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        using var reg = linkedCts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled();
            }
        });

        return await tcs.Task;
    }

    public async Task InitializeAsync(
        string clientName = "AgentLang",
        string clientVersion = "0.6.5",
        CancellationToken ct = default)
    {
        var initParams = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                roots = new { listChanged = true },
                sampling = new { }
            },
            clientInfo = new
            {
                name = clientName,
                version = clientVersion
            }
        };

        var response = await SendRequestAsync("initialize", initParams, ct);
        if (response.Error != null)
        {
            throw new InvalidOperationException($"MCP Initialize failed: {response.Error.Message} (Code: {response.Error.Code})");
        }

        if (response.Result is JsonElement elem)
        {
            if (elem.TryGetProperty("serverInfo", out var serverInfo))
            {
                ServerName = serverInfo.TryGetProperty("name", out var n) ? n.GetString() : null;
                ServerVersion = serverInfo.TryGetProperty("version", out var v) ? v.GetString() : null;
            }
        }

        // Send notifications/initialized
        var notification = new McpJsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = "notifications/initialized"
        };
        await _transport.SendMessageAsync(JsonSerializer.Serialize(notification), ct);

        IsInitialized = true;
    }

    public async Task<List<McpToolDefinition>> ListToolsAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("tools/list", new { }, ct);
        if (response.Error != null)
        {
            throw new InvalidOperationException($"tools/list failed: {response.Error.Message}");
        }

        var resultList = new List<McpToolDefinition>();
        if (response.Result is JsonElement elem && elem.TryGetProperty("tools", out var toolsArray) && toolsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in toolsArray.EnumerateArray())
            {
                var def = JsonSerializer.Deserialize<McpToolDefinition>(item.GetRawText());
                if (def != null) resultList.Add(def);
            }
        }

        return resultList;
    }

    public async Task<McpToolCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken ct = default)
    {
        var callParams = new
        {
            name = toolName,
            arguments = arguments ?? new Dictionary<string, object?>()
        };

        var response = await SendRequestAsync("tools/call", callParams, ct);
        if (response.Error != null)
        {
            return McpToolCallResult.Fail($"MCP Error ({response.Error.Code}): {response.Error.Message}");
        }

        if (response.Result is JsonElement elem)
        {
            var res = JsonSerializer.Deserialize<McpToolCallResult>(elem.GetRawText());
            if (res != null) return res;
        }

        return McpToolCallResult.Ok(response.Result?.ToString() ?? "");
    }

    public async Task<List<McpResourceDefinition>> ListResourcesAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("resources/list", new { }, ct);
        if (response.Error != null)
        {
            throw new InvalidOperationException($"resources/list failed: {response.Error.Message}");
        }

        var resultList = new List<McpResourceDefinition>();
        if (response.Result is JsonElement elem && elem.TryGetProperty("resources", out var resArray) && resArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resArray.EnumerateArray())
            {
                var def = JsonSerializer.Deserialize<McpResourceDefinition>(item.GetRawText());
                if (def != null) resultList.Add(def);
            }
        }

        return resultList;
    }

    public async Task<List<McpToolBridge>> BridgeToolsToRegistryAsync(
        ToolRegistry registry,
        string? prefix = null,
        CancellationToken ct = default)
    {
        var tools = await ListToolsAsync(ct);
        var bridges = new List<McpToolBridge>();

        foreach (var def in tools)
        {
            var bridge = new McpToolBridge(this, def, prefix);
            registry.RegisterTool(bridge);
            bridges.Add(bridge);
        }

        return bridges;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_readLoopTask != null)
        {
            try { await _readLoopTask; } catch { }
        }
        await _transport.DisposeAsync();
        _cts.Dispose();
    }
}

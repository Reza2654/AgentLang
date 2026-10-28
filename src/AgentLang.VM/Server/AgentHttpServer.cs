using System.Net;
using System.Text;
using System.Text.Json;
using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.VM.Durable;

namespace AgentLang.VM.Server;

public sealed class AgentHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly AgentVirtualMachine _vm;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public int Port { get; }
    public string BaseUrl { get; }
    public bool IsRunning => _listener.IsListening;

    public AgentHttpServer(int port = 8080, AgentVirtualMachine? vm = null)
    {
        Port = port;
        BaseUrl = $"http://localhost:{port}/";
        _vm = vm ?? new AgentVirtualMachine();
        _listener.Prefixes.Add(BaseUrl);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_listener.IsListening) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener.Start();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
        catch
        {
            // Ignore during shutdown
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts?.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                // Continue listener loop
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        // CORS headers
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

        if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        string rawPath = req.Url?.AbsolutePath ?? "/";
        string path = rawPath.TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";

        try
        {
            if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                if (path is "/" or "/playground")
                {
                    await ServePlaygroundAsync(res, ct);
                    return;
                }
                if (path is "/api/v1/status" or "/health")
                {
                    await ServeStatusAsync(res, ct);
                    return;
                }
                if (path is "/api/v1/durable")
                {
                    await ServeDurableListAsync(req, res, ct);
                    return;
                }
                if (path.StartsWith("/api/v1/durable/"))
                {
                    string id = path.Substring("/api/v1/durable/".Length);
                    await ServeSingleDurableAsync(id, res, ct);
                    return;
                }
            }
            else if (req.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                if (path.StartsWith("/api/v1/durable/"))
                {
                    string id = path.Substring("/api/v1/durable/".Length);
                    await DeleteDurableAsync(id, res, ct);
                    return;
                }
            }
            else if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                if (path is "/api/v1/run")
                {
                    await HandleRunEndpointAsync(req, res, ct);
                    return;
                }
                if (path is "/api/v1/stream")
                {
                    await HandleStreamEndpointAsync(req, res, ct);
                    return;
                }
                if (path is "/api/v1/resume")
                {
                    await HandleResumeEndpointAsync(req, res, ct);
                    return;
                }
            }

            res.StatusCode = 404;
            await WriteJsonAsync(res, new { error = $"Endpoint '{rawPath}' not found." }, ct);
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            await WriteJsonAsync(res, new { error = ex.Message }, ct);
        }
        finally
        {
            try
            {
                res.Close();
            }
            catch
            {
                // Ignore client disconnect
            }
        }
    }

    private async Task ServeStatusAsync(HttpListenerResponse res, CancellationToken ct)
    {
        var durableList = await _vm.Durable.ListAsync(ct: ct);
        var status = new
        {
            status = "healthy",
            version = "0.7.0",
            uptimeSeconds = (DateTimeOffset.UtcNow - _startTime).TotalSeconds,
            durableExecutions = new
            {
                total = durableList.Count,
                running = durableList.Count(d => d.Status == DurableStatus.Running),
                suspended = durableList.Count(d => d.Status == DurableStatus.Suspended),
                completed = durableList.Count(d => d.Status == DurableStatus.Completed),
                faulted = durableList.Count(d => d.Status == DurableStatus.Faulted)
            },
            serverTime = DateTimeOffset.UtcNow
        };

        await WriteJsonAsync(res, status, ct);
    }

    private async Task ServeDurableListAsync(HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        DurableStatus? statusFilter = null;
        string? statusParam = req.QueryString["status"];
        if (!string.IsNullOrEmpty(statusParam) && Enum.TryParse<DurableStatus>(statusParam, ignoreCase: true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        string? workflowParam = req.QueryString["workflow"];

        var list = await _vm.Durable.ListAsync(statusFilter, ct);
        if (!string.IsNullOrEmpty(workflowParam))
        {
            list = list.Where(d => d.WorkflowName.Equals(workflowParam, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        await WriteJsonAsync(res, list, ct);
    }

    private async Task ServeSingleDurableAsync(string id, HttpListenerResponse res, CancellationToken ct)
    {
        var item = await _vm.Durable.GetAsync(id, ct);
        if (item == null)
        {
            res.StatusCode = 404;
            await WriteJsonAsync(res, new { error = $"Durable execution '{id}' not found." }, ct);
            return;
        }

        await WriteJsonAsync(res, item, ct);
    }

    private async Task DeleteDurableAsync(string id, HttpListenerResponse res, CancellationToken ct)
    {
        bool deleted = await _vm.Durable.DeleteAsync(id, ct);
        if (!deleted)
        {
            res.StatusCode = 404;
            await WriteJsonAsync(res, new { error = $"Durable execution '{id}' not found." }, ct);
            return;
        }

        await WriteJsonAsync(res, new { deleted = true, id }, ct);
    }

    private async Task HandleRunEndpointAsync(HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("code", out var codeProp))
        {
            res.StatusCode = 400;
            await WriteJsonAsync(res, new { error = "Missing 'code' field in request body." }, ct);
            return;
        }

        string code = codeProp.GetString() ?? "";
        var outputWriter = new StringWriter();

        var runtime = new AgentLangRuntime(
            output: outputWriter,
            modelRegistry: _vm.Runtime.ModelRegistry,
            toolRegistry: _vm.Runtime.ToolRegistry,
            securityEngine: _vm.Runtime.SecurityEngine);

        // Inject initial variables if passed
        if (doc.RootElement.TryGetProperty("variables", out var varsProp) && varsProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in varsProp.EnumerateObject())
            {
                runtime.GlobalScope.SetLocal(prop.Name, prop.Value.ToString());
            }
        }

        var sourceText = new SourceText(code);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        if (parser.Diagnostics.HasErrors)
        {
            res.StatusCode = 400;
            var errors = parser.Diagnostics.Diagnostics.Select(d => d.Message).ToList();
            await WriteJsonAsync(res, new { status = "syntax_error", errors }, ct);
            return;
        }

        await runtime.ExecuteProgramAsync(program, ct);

        var outputVars = runtime.GlobalScope.GetLocalVariables()
            .ToDictionary(k => k.Key, v => v.Value?.ToString());

        await WriteJsonAsync(res, new
        {
            status = "completed",
            output = outputWriter.ToString().TrimEnd(),
            variables = outputVars
        }, ct);
    }

    private async Task HandleStreamEndpointAsync(HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct);

        string code = "";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("code", out var codeProp))
                {
                    code = codeProp.GetString() ?? "";
                }
            }
            catch
            {
                code = body;
            }
        }

        res.ContentType = "text/event-stream; charset=utf-8";
        res.Headers.Add("Cache-Control", "no-cache");
        res.Headers.Add("Connection", "keep-alive");

        await using var writer = new StreamWriter(res.OutputStream, Encoding.UTF8, leaveOpen: true);

        // Emit start event
        await writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "start", message = "Agent execution stream started" })}\n\n");
        await writer.FlushAsync(ct);

        // Execute code and stream lines
        var outputWriter = new StringWriter();
        var runtime = new AgentLangRuntime(
            output: outputWriter,
            modelRegistry: _vm.Runtime.ModelRegistry,
            toolRegistry: _vm.Runtime.ToolRegistry,
            securityEngine: _vm.Runtime.SecurityEngine);

        try
        {
            var sourceText = new SourceText(code);
            var program = new Parser.Parser(sourceText).ParseProgram();
            await runtime.ExecuteProgramAsync(program, ct);

            string consoleOut = outputWriter.ToString();
            foreach (var line in consoleOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                await writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "output", content = line.TrimEnd() })}\n\n");
                await writer.FlushAsync(ct);
            }

            await writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "finish", status = "completed" })}\n\n");
            await writer.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            await writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", error = ex.Message })}\n\n");
            await writer.FlushAsync(ct);
        }
    }

    private async Task HandleResumeEndpointAsync(HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("executionId", out var idProp) ||
            !doc.RootElement.TryGetProperty("signal", out var sigProp))
        {
            res.StatusCode = 400;
            await WriteJsonAsync(res, new { error = "Fields 'executionId' and 'signal' are required." }, ct);
            return;
        }

        string executionId = idProp.GetString() ?? "";
        string signalName = sigProp.GetString() ?? "";
        object? payload = doc.RootElement.TryGetProperty("payload", out var pProp) ? pProp.ToString() : null;

        var signal = new ExecutionSignal(signalName, payload);
        try
        {
            var updated = await _vm.Durable.ResumeAsync(executionId, signal, stepCtx =>
            {
                stepCtx.Complete(new { message = $"Resumed with signal {signalName}", payload });
                return Task.CompletedTask;
            }, ct);

            await WriteJsonAsync(res, updated, ct);
        }
        catch (Exception ex)
        {
            res.StatusCode = 400;
            await WriteJsonAsync(res, new { error = ex.Message }, ct);
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, object data, CancellationToken ct)
    {
        res.ContentType = "application/json; charset=utf-8";
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = buffer.Length;
        await res.OutputStream.WriteAsync(buffer, ct);
    }

    private static async Task ServePlaygroundAsync(HttpListenerResponse res, CancellationToken ct)
    {
        res.ContentType = "text/html; charset=utf-8";
        string html = PlaygroundHtml;
        byte[] buffer = Encoding.UTF8.GetBytes(html);
        res.ContentLength64 = buffer.Length;
        await res.OutputStream.WriteAsync(buffer, ct);
    }

    private const string PlaygroundHtml = """
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>AgentLang Playground — v0.7.0</title>
        <style>
            :root {
                --bg: #0d1117;
                --panel: #161b22;
                --border: #30363d;
                --text: #c9d1d9;
                --accent: #58a6ff;
                --accent-hover: #1f6feb;
                --success: #238636;
                --error: #da3633;
                --font: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
            }
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { background: var(--bg); color: var(--text); font-family: var(--font); height: 100vh; display: flex; flex-direction: column; overflow: hidden; }
            header { background: var(--panel); border-bottom: 1px solid var(--border); padding: 12px 24px; display: flex; justify-content: space-between; align-items: center; }
            .logo { font-size: 1.25rem; font-weight: 700; color: #fff; display: flex; align-items: center; gap: 8px; }
            .badge { background: #238636; color: #fff; padding: 2px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: 600; }
            .main { display: flex; flex: 1; overflow: hidden; }
            .editor-pane { flex: 1; display: flex; flex-direction: column; border-right: 1px solid var(--border); }
            .toolbar { background: var(--panel); padding: 8px 16px; border-bottom: 1px solid var(--border); display: flex; gap: 10px; }
            button { background: var(--accent); color: #fff; border: none; border-radius: 6px; padding: 6px 14px; font-weight: 600; cursor: pointer; display: flex; align-items: center; gap: 6px; font-size: 0.85rem; }
            button:hover { background: var(--accent-hover); }
            button.stream-btn { background: #8957e5; }
            button.stream-btn:hover { background: #6e40c9; }
            textarea { flex: 1; background: var(--bg); color: #e6edf3; font-family: 'Consolas', 'Courier New', monospace; font-size: 14px; padding: 16px; border: none; outline: none; resize: none; line-height: 1.5; }
            .output-pane { flex: 1; display: flex; flex-direction: column; background: var(--panel); }
            .output-header { padding: 10px 16px; border-bottom: 1px solid var(--border); font-size: 0.85rem; font-weight: 600; color: #8b949e; text-transform: uppercase; }
            pre { flex: 1; padding: 16px; overflow-y: auto; font-family: 'Consolas', 'Courier New', monospace; font-size: 13px; color: #7ee787; white-space: pre-wrap; }
        </style>
    </head>
    <body>
        <header>
            <div class="logo">
                ⚡ AgentLang Playground
                <span class="badge">v0.7.0</span>
            </div>
            <div style="font-size: 0.85rem; color: #8b949e;">
                Durable Engine & Microservice Gateway Active
            </div>
        </header>
        <div class="main">
            <div class="editor-pane">
                <div class="toolbar">
                    <button onclick="runCode()">▶ Run Script</button>
                    <button class="stream-btn" onclick="streamCode()">⚡ Stream SSE</button>
                    <button style="background: #21262d; border: 1px solid var(--border);" onclick="clearLogs()">Clear</button>
                </div>
                <textarea id="code" spellcheck="false">schema SecurityAudit {
        target: string,
        score: int(min: 0, max: 100),
        status: string
    }

    main {
        print("Executing AgentLang v0.7.0 Microservice Server...")
        x = 40
        y = 2
        result = x + y
        print("Calculated Result: " + result)
    }</textarea>
            </div>
            <div class="output-pane">
                <div class="output-header">Live Execution Console</div>
                <pre id="output">// Console ready. Click 'Run Script' or 'Stream SSE' to execute.</pre>
            </div>
        </div>
        <script>
            async function runCode() {
                const out = document.getElementById('output');
                out.innerText = "// Executing POST /api/v1/run ...\n";
                try {
                    const res = await fetch('/api/v1/run', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ code: document.getElementById('code').value })
                    });
                    const data = await res.json();
                    out.innerText = JSON.stringify(data, null, 2);
                } catch (e) {
                    out.innerText = "Error: " + e.message;
                }
            }

            async function streamCode() {
                const out = document.getElementById('output');
                out.innerText = "// Connecting to POST /api/v1/stream ...\n";
                try {
                    const res = await fetch('/api/v1/stream', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ code: document.getElementById('code').value })
                    });
                    const reader = res.body.getReader();
                    const decoder = new TextDecoder();
                    while (true) {
                        const { value, done } = await reader.read();
                        if (done) break;
                        out.innerText += decoder.decode(value);
                    }
                } catch (e) {
                    out.innerText += "\nStream Error: " + e.message;
                }
            }

            function clearLogs() {
                document.getElementById('output').innerText = "// Ready.";
            }
        </script>
    </body>
    </html>
    """;
}

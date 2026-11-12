using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AgentLang.AST;
using AgentLang.Errors;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Data;
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

    public async Task LoadScriptAsync(string sourceCode, string filePath = "<server>", CancellationToken ct = default)
    {
        await _vm.ExecuteAsync(sourceCode, filePath, ct);
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
                if (path is "/openapi.json" or "/api/v1/openapi.json")
                {
                    await WriteRawJsonAsync(res, BuildOpenApiJson(), ct);
                    return;
                }
                if (path is "/api/v1/entities")
                {
                    await ServeEntitySchemasAsync(res, ct);
                    return;
                }
                if (path.StartsWith("/api/v1/entities/", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleEntityGetAsync(path, req, res, ct);
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
            else if (req.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase))
            {
                if (path.StartsWith("/api/v1/entities/", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleEntityPutAsync(path, req, res, ct);
                    return;
                }
            }
            else if (req.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                if (path.StartsWith("/api/v1/entities/", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleEntityDeleteAsync(path, res, ct);
                    return;
                }
                if (path.StartsWith("/api/v1/durable/"))
                {
                    string id = path.Substring("/api/v1/durable/".Length);
                    await DeleteDurableAsync(id, res, ct);
                    return;
                }
            }
            else if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                if (path.StartsWith("/api/v1/entities/", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleEntityPostAsync(path, req, res, ct);
                    return;
                }
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
            await WriteErrorJsonAsync(res, $"Endpoint '{rawPath}' not found.", ct);
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            await WriteErrorJsonAsync(res, ex.Message, ct);
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
        string json = BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteString("status", "healthy");
            w.WriteString("version", "0.9.2");
            w.WriteNumber("uptimeSeconds", (DateTimeOffset.UtcNow - _startTime).TotalSeconds);
            w.WriteNumber("registeredSchemas", _vm.Runtime.EntityStore.GetSchemas().Count);
            w.WriteStartObject("durableExecutions");
            w.WriteNumber("total", durableList.Count);
            w.WriteNumber("running", durableList.Count(d => d.Status == DurableStatus.Running));
            w.WriteNumber("suspended", durableList.Count(d => d.Status == DurableStatus.Suspended));
            w.WriteNumber("completed", durableList.Count(d => d.Status == DurableStatus.Completed));
            w.WriteNumber("faulted", durableList.Count(d => d.Status == DurableStatus.Faulted));
            w.WriteEndObject();
            w.WriteString("serverTime", DateTimeOffset.UtcNow);
            w.WriteEndObject();
        });

        await WriteRawJsonAsync(res, json, ct);
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

        string json = BuildJsonString(w =>
        {
            w.WriteStartArray();
            foreach (var item in list)
            {
                WriteDurableContext(w, item);
            }
            w.WriteEndArray();
        });
        await WriteRawJsonAsync(res, json, ct);
    }

    private async Task ServeSingleDurableAsync(string id, HttpListenerResponse res, CancellationToken ct)
    {
        var item = await _vm.Durable.GetAsync(id, ct);
        if (item == null)
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Durable execution '{id}' not found.", ct);
            return;
        }

        string json = BuildJsonString(w => WriteDurableContext(w, item));
        await WriteRawJsonAsync(res, json, ct);
    }

    private async Task DeleteDurableAsync(string id, HttpListenerResponse res, CancellationToken ct)
    {
        bool deleted = await _vm.Durable.DeleteAsync(id, ct);
        if (!deleted)
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Durable execution '{id}' not found.", ct);
            return;
        }

        string json = BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteBoolean("deleted", true);
            w.WriteString("id", id);
            w.WriteEndObject();
        });
        await WriteRawJsonAsync(res, json, ct);
    }

    private async Task ServeEntitySchemasAsync(HttpListenerResponse res, CancellationToken ct)
    {
        var schemas = _vm.Runtime.EntityStore.GetSchemas();
        string json = BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("schemas");
            foreach (var schema in schemas)
            {
                w.WriteStartObject();
                w.WriteString("name", schema.Name);
                w.WriteNumber("count", _vm.Runtime.EntityStore.Count(schema.Name));
                w.WriteString("endpoint", $"/api/v1/entities/{schema.Name}");
                w.WriteStartArray("fields");
                foreach (var f in schema.Fields)
                {
                    w.WriteStartObject();
                    w.WriteString("name", f.Name);
                    w.WriteString("type", f.TypeName);
                    w.WriteBoolean("required", f.IsRequired);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
        await WriteRawJsonAsync(res, json, ct);
    }

    private static (string Schema, string? Id) ParseEntityRoute(string path)
    {
        string rest = path.Substring("/api/v1/entities/".Length).Trim('/');
        int slash = rest.IndexOf('/');
        if (slash < 0)
            return (rest, null);
        return (rest.Substring(0, slash), rest.Substring(slash + 1));
    }

    private async Task HandleEntityGetAsync(string path, HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        var (schema, id) = ParseEntityRoute(path);
        if (!_vm.Runtime.EntityStore.TryGetSchema(schema, out _))
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Schema '{schema}' is not registered.", ct);
            return;
        }

        if (id == null)
        {
            var items = _vm.Runtime.EntityStore.List(schema);
            string json = BuildJsonString(w =>
            {
                w.WriteStartArray();
                foreach (var item in items)
                    SchemaEntityStore.WriteJsonValue(w, item);
                w.WriteEndArray();
            });
            await WriteRawJsonAsync(res, json, ct);
            return;
        }

        var found = _vm.Runtime.EntityStore.Get(schema, id);
        if (found == null)
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Entity '{schema}/{id}' not found.", ct);
            return;
        }

        await WriteRawJsonAsync(res, BuildJsonString(w => SchemaEntityStore.WriteJsonValue(w, found)), ct);
    }

    private async Task HandleEntityPostAsync(string path, HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        var (schema, _) = ParseEntityRoute(path);
        if (!_vm.Runtime.EntityStore.TryGetSchema(schema, out _))
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Schema '{schema}' is not registered.", ct);
            return;
        }

        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            res.StatusCode = 400;
            await WriteErrorJsonAsync(res, "Request body must be a JSON object.", ct);
            return;
        }

        var record = ParseJsonObject(doc.RootElement);
        string? customId = record.TryGetValue("id", out var idVal) ? idVal?.ToString() : null;
        try
        {
            var inserted = _vm.Runtime.EntityStore.Insert(schema, record, customId);
            res.StatusCode = 201;
            await WriteRawJsonAsync(res, BuildJsonString(w => SchemaEntityStore.WriteJsonValue(w, inserted)), ct);
        }
        catch (AgentLangRuntimeException ex)
        {
            res.StatusCode = 400;
            await WriteErrorJsonAsync(res, ex.Message, ct);
        }
    }

    private async Task HandleEntityPutAsync(string path, HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        var (schema, id) = ParseEntityRoute(path);
        if (string.IsNullOrEmpty(id) || !_vm.Runtime.EntityStore.TryGetSchema(schema, out _))
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Entity route '{path}' requires a valid schema and id.", ct);
            return;
        }

        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            res.StatusCode = 400;
            await WriteErrorJsonAsync(res, "Request body must be a JSON object.", ct);
            return;
        }

        var patch = ParseJsonObject(doc.RootElement);
        try
        {
            var updated = _vm.Runtime.EntityStore.Update(schema, id, patch);
            if (updated == null)
            {
                res.StatusCode = 404;
                await WriteErrorJsonAsync(res, $"Entity '{schema}/{id}' not found.", ct);
                return;
            }
            await WriteRawJsonAsync(res, BuildJsonString(w => SchemaEntityStore.WriteJsonValue(w, updated)), ct);
        }
        catch (AgentLangRuntimeException ex)
        {
            res.StatusCode = 400;
            await WriteErrorJsonAsync(res, ex.Message, ct);
        }
    }

    private async Task HandleEntityDeleteAsync(string path, HttpListenerResponse res, CancellationToken ct)
    {
        var (schema, id) = ParseEntityRoute(path);
        if (string.IsNullOrEmpty(id) || !_vm.Runtime.EntityStore.TryGetSchema(schema, out _))
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Entity '{schema}/{id}' not found.", ct);
            return;
        }

        if (!_vm.Runtime.EntityStore.Delete(schema, id))
        {
            res.StatusCode = 404;
            await WriteErrorJsonAsync(res, $"Entity '{schema}/{id}' not found.", ct);
            return;
        }

        string json = BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteBoolean("deleted", true);
            w.WriteString("id", id);
            w.WriteEndObject();
        });
        await WriteRawJsonAsync(res, json, ct);
    }

    public string BuildOpenApiJson()
    {
        var schemas = _vm.Runtime.EntityStore.GetSchemas();
        return BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteString("openapi", "3.0.3");
            w.WriteStartObject("info");
            w.WriteString("title", "AgentLang Auto-Generated Microservice API");
            w.WriteString("version", "0.9.2");
            w.WriteEndObject();

            w.WriteStartObject("paths");

            // Core VM endpoints
            w.WriteStartObject("/api/v1/status");
            w.WriteStartObject("get");
            w.WriteString("summary", "Server health and runtime metrics");
            w.WriteStartObject("responses");
            w.WriteStartObject("200");
            w.WriteString("description", "OK");
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("/api/v1/run");
            w.WriteStartObject("post");
            w.WriteString("summary", "Execute an AgentLang script");
            w.WriteStartObject("responses");
            w.WriteStartObject("200");
            w.WriteString("description", "Execution completed");
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();

            // Schema CRUD paths
            foreach (var schema in schemas)
            {
                string collectionPath = $"/api/v1/entities/{schema.Name}";
                string itemPath = $"/api/v1/entities/{schema.Name}/{{id}}";
                string refPath = $"#/components/schemas/{schema.Name}";

                w.WriteStartObject(collectionPath);
                w.WriteStartObject("get");
                w.WriteString("summary", $"List {schema.Name} records");
                w.WriteStartObject("responses");
                w.WriteStartObject("200");
                w.WriteString("description", "List of entities");
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();

                w.WriteStartObject("post");
                w.WriteString("summary", $"Create a validated {schema.Name} record");
                w.WriteStartObject("requestBody");
                w.WriteBoolean("required", true);
                w.WriteStartObject("content");
                w.WriteStartObject("application/json");
                w.WriteStartObject("schema");
                w.WriteString("$ref", refPath);
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteStartObject("responses");
                w.WriteStartObject("201");
                w.WriteString("description", "Created");
                w.WriteEndObject();
                w.WriteStartObject("400");
                w.WriteString("description", "Schema validation failed");
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();

                w.WriteStartObject(itemPath);
                foreach (var method in new[] { "get", "put", "delete" })
                {
                    w.WriteStartObject(method);
                    w.WriteString("summary", $"{method.ToUpperInvariant()} {schema.Name} by id");
                    w.WriteStartArray("parameters");
                    w.WriteStartObject();
                    w.WriteString("name", "id");
                    w.WriteString("in", "path");
                    w.WriteBoolean("required", true);
                    w.WriteStartObject("schema");
                    w.WriteString("type", "string");
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteEndArray();
                    w.WriteStartObject("responses");
                    w.WriteStartObject("200");
                    w.WriteString("description", "OK");
                    w.WriteEndObject();
                    w.WriteStartObject("404");
                    w.WriteString("description", "Not found");
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }

            w.WriteEndObject(); // paths

            w.WriteStartObject("components");
            w.WriteStartObject("schemas");
            foreach (var schema in schemas)
            {
                w.WriteStartObject(schema.Name);
                w.WriteString("type", "object");
                w.WriteStartObject("properties");
                w.WriteStartObject("id");
                w.WriteString("type", "string");
                w.WriteEndObject();
                foreach (var field in schema.Fields)
                {
                    w.WriteStartObject(field.Name);
                    w.WriteString("type", MapFieldOpenApiType(field.TypeName));
                    w.WriteEndObject();
                }
                w.WriteEndObject();
                w.WriteStartArray("required");
                foreach (var field in schema.Fields.Where(f => f.IsRequired))
                {
                    w.WriteStringValue(field.Name);
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject(); // schemas
            w.WriteEndObject(); // components

            w.WriteEndObject();
        });
    }

    private static string MapFieldOpenApiType(string fieldType)
    {
        string t = fieldType.Trim().ToLowerInvariant();
        if (t is "int" or "integer") return "integer";
        if (t is "float" or "double" or "number" or "decimal") return "number";
        if (t is "bool" or "boolean") return "boolean";
        if (t.StartsWith("list", StringComparison.Ordinal) || t.EndsWith("[]", StringComparison.Ordinal)) return "array";
        return "string";
    }

    private static Dictionary<string, object?> ParseJsonObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonElement(prop.Value);
        }
        return dict;
    }

    private static object? ConvertJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out long l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => el.EnumerateArray().Select(ConvertJsonElement).ToList(),
        JsonValueKind.Object => ParseJsonObject(el),
        _ => el.ToString()
    };

    private async Task HandleRunEndpointAsync(HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("code", out var codeProp))
        {
            res.StatusCode = 400;
            await WriteErrorJsonAsync(res, "Missing 'code' field in request body.", ct);
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
            string errJson = BuildJsonString(w =>
            {
                w.WriteStartObject();
                w.WriteString("status", "syntax_error");
                w.WriteStartArray("errors");
                foreach (var err in errors) w.WriteStringValue(err);
                w.WriteEndArray();
                w.WriteEndObject();
            });
            await WriteRawJsonAsync(res, errJson, ct);
            return;
        }

        await runtime.ExecuteProgramAsync(program, ct);

        var outputVars = runtime.GlobalScope.GetLocalVariables();
        string resultJson = BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteString("status", "completed");
            w.WriteString("output", outputWriter.ToString().TrimEnd());
            w.WriteStartObject("variables");
            foreach (var kv in outputVars)
            {
                w.WriteString(kv.Key, kv.Value?.ToString());
            }
            w.WriteEndObject();
            w.WriteEndObject();
        });
        await WriteRawJsonAsync(res, resultJson, ct);
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

        static string BuildSseEvent(string type, string key, string value) => BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteString(key, value);
            w.WriteEndObject();
        }, indented: false);

        // Emit start event
        await writer.WriteAsync($"data: {BuildSseEvent("start", "message", "Agent execution stream started")}\n\n");
        await writer.FlushAsync(ct);

        // Execute code and stream lines
        var outputWriter = new StringWriter();
        var runtime = new AgentLangRuntime(
            output: outputWriter,
            modelRegistry: _vm.Runtime.ModelRegistry,
            toolRegistry: _vm.Runtime.ToolRegistry,
            securityEngine: _vm.Runtime.SecurityEngine);

        runtime.TokenStreamCallback = token =>
        {
            try
            {
                writer.Write($"data: {BuildSseEvent("token", "content", token)}\n\n");
                writer.Flush();
            }
            catch { }
        };

        try
        {
            var sourceText = new SourceText(code);
            var program = new Parser.Parser(sourceText).ParseProgram();
            await runtime.ExecuteProgramAsync(program, ct);

            string consoleOut = outputWriter.ToString();
            foreach (var line in consoleOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                await writer.WriteAsync($"data: {BuildSseEvent("output", "content", line.TrimEnd())}\n\n");
                await writer.FlushAsync(ct);
            }

            await writer.WriteAsync($"data: {BuildSseEvent("finish", "status", "completed")}\n\n");
            await writer.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            await writer.WriteAsync($"data: {BuildSseEvent("error", "error", ex.Message)}\n\n");
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
            await WriteErrorJsonAsync(res, "Fields 'executionId' and 'signal' are required.", ct);
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
                stepCtx.Complete(new Dictionary<string, object?> { ["message"] = $"Resumed with signal {signalName}", ["payload"] = payload });
                return Task.CompletedTask;
            }, ct);

            await WriteRawJsonAsync(res, BuildJsonString(w => WriteDurableContext(w, updated)), ct);
        }
        catch (Exception ex)
        {
            res.StatusCode = 400;
            await WriteErrorJsonAsync(res, ex.Message, ct);
        }
    }

    private static string BuildJsonString(Action<Utf8JsonWriter> writeAction, bool indented = true)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented }))
        {
            writeAction(writer);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteDurableContext(Utf8JsonWriter w, DurableExecutionContext ctx)
    {
        w.WriteStartObject();
        w.WriteString("executionId", ctx.ExecutionId);
        w.WriteString("workflowName", ctx.WorkflowName);
        w.WriteString("status", ctx.Status.ToString());
        if (ctx.SuspensionReason.HasValue)
            w.WriteString("suspensionReason", ctx.SuspensionReason.Value.ToString());
        else
            w.WriteNull("suspensionReason");
        w.WriteString("waitingForSignalName", ctx.WaitingForSignalName);
        w.WriteNumber("stepIndex", ctx.StepIndex);
        w.WritePropertyName("stateVariables");
        SchemaEntityStore.WriteJsonValue(w, ctx.StateVariables);
        w.WritePropertyName("metadata");
        SchemaEntityStore.WriteJsonValue(w, ctx.Metadata);
        w.WritePropertyName("finalOutput");
        SchemaEntityStore.WriteJsonValue(w, ctx.FinalOutput);
        w.WriteString("errorMessage", ctx.ErrorMessage);
        w.WriteString("createdAt", ctx.CreatedAt);
        w.WriteString("updatedAt", ctx.UpdatedAt);
        w.WriteEndObject();
    }

    private static Task WriteErrorJsonAsync(HttpListenerResponse res, string message, CancellationToken ct) =>
        WriteRawJsonAsync(res, BuildJsonString(w =>
        {
            w.WriteStartObject();
            w.WriteString("error", message);
            w.WriteEndObject();
        }), ct);

    private static async Task WriteRawJsonAsync(HttpListenerResponse res, string json, CancellationToken ct)
    {
        res.ContentType = "application/json; charset=utf-8";
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
        <title>AgentLang Playground — v0.9.2</title>
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
                <span class="badge">v0.9.2</span>
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
        print("Executing AgentLang v0.9.2 Microservice Server...")
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

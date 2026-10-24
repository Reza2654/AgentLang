using System.Text.Json;

namespace AgentLang.Tools;

public sealed class OpenApiToolGenerator
{
    public static List<CustomApiTool> GenerateFromOpenApiJson(
        string openApiJson,
        string toolName = "external_api",
        HttpClient? httpClient = null)
    {
        using var doc = JsonDocument.Parse(openApiJson);
        var root = doc.RootElement;

        string serverUrl = "https://api.example.com";
        if (root.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0)
        {
            if (servers[0].TryGetProperty("url", out var urlProp))
            {
                serverUrl = urlProp.GetString() ?? serverUrl;
            }
        }

        var methods = new List<CustomApiMethod>();
        if (root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object)
        {
            foreach (var pathProp in paths.EnumerateObject())
            {
                string path = pathProp.Name;
                foreach (var opProp in pathProp.Value.EnumerateObject())
                {
                    string httpMethod = opProp.Name.ToUpperInvariant();
                    if (httpMethod is not ("GET" or "POST" or "PUT" or "DELETE" or "PATCH")) continue;

                    string opId = opProp.Value.TryGetProperty("operationId", out var idProp)
                        ? idProp.GetString() ?? ""
                        : $"{httpMethod.ToLowerInvariant()}_{path.Trim('/').Replace('/', '_')}";

                    if (string.IsNullOrEmpty(opId))
                    {
                        opId = $"{httpMethod.ToLowerInvariant()}_endpoint";
                    }

                    var paramList = new List<string>();
                    if (opProp.Value.TryGetProperty("parameters", out var paramsArray) && paramsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in paramsArray.EnumerateArray())
                        {
                            if (p.TryGetProperty("name", out var pName))
                            {
                                var n = pName.GetString();
                                if (!string.IsNullOrEmpty(n)) paramList.Add(n);
                            }
                        }
                    }

                    methods.Add(new CustomApiMethod(httpMethod, opId, path, paramList));
                }
            }
        }

        var tool = new CustomApiTool(toolName, serverUrl, "rest", methods: methods, httpClient: httpClient);
        return new List<CustomApiTool> { tool };
    }

    public static List<CustomApiTool> RegisterOpenApiIntoRegistry(
        ToolRegistry registry,
        string openApiJson,
        string toolName = "external_api",
        HttpClient? httpClient = null)
    {
        var tools = GenerateFromOpenApiJson(openApiJson, toolName, httpClient);
        foreach (var t in tools)
        {
            registry.RegisterTool(t);
        }
        return tools;
    }
}

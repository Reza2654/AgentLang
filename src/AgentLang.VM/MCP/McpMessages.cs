using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLang.VM.MCP;

public sealed class McpJsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public sealed class McpJsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpJsonRpcError? Error { get; set; }

    public static McpJsonRpcResponse Success(object? id, object? result) =>
        new() { Id = id, Result = result };

    public static McpJsonRpcResponse Fail(object? id, int code, string message, object? data = null) =>
        new() { Id = id, Error = new McpJsonRpcError { Code = code, Message = message, Data = data } };
}

public sealed class McpJsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public sealed class McpToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("inputSchema")]
    public object? InputSchema { get; set; }
}

public sealed class McpContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    public static McpContentItem TextContent(string text) =>
        new() { Type = "text", Text = text };
}

public sealed class McpToolCallResult
{
    [JsonPropertyName("content")]
    public List<McpContentItem> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    public static McpToolCallResult Ok(string text) =>
        new() { Content = new List<McpContentItem> { McpContentItem.TextContent(text) }, IsError = false };

    public static McpToolCallResult Fail(string errorText) =>
        new() { Content = new List<McpContentItem> { McpContentItem.TextContent(errorText) }, IsError = true };
}

public sealed class McpResourceDefinition
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

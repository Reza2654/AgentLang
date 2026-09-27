using System.Diagnostics;
using System.Text.Json;

namespace AgentLang.Tools;

public sealed class FilesystemTool : ITool
{
    public string Name => "filesystem";

    public IReadOnlyList<string> SupportedCapabilities { get; } =
    [
        "filesystem.read",
        "filesystem.write",
        "filesystem.list",
        "filesystem.exists"
    ];

    public async Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string cap = capability.ToLowerInvariant();
        try
        {
            if (cap == "filesystem.read")
            {
                string path = arguments.TryGetValue("path", out var p) ? p?.ToString() ?? "" : "";
                if (!File.Exists(path))
                    return ToolResult.Fail($"File not found: '{path}'");

                string text = await File.ReadAllTextAsync(path, ct);
                return ToolResult.Ok(text);
            }

            if (cap == "filesystem.write")
            {
                string path = arguments.TryGetValue("path", out var p) ? p?.ToString() ?? "" : "";
                string content = arguments.TryGetValue("content", out var c) ? c?.ToString() ?? "" : "";

                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                await File.WriteAllTextAsync(path, content, ct);
                return ToolResult.Ok($"Successfully wrote {content.Length} characters to '{path}'");
            }

            if (cap == "filesystem.list")
            {
                string path = arguments.TryGetValue("path", out var p) ? p?.ToString() ?? "." : ".";
                if (!Directory.Exists(path))
                    return ToolResult.Fail($"Directory not found: '{path}'");

                var entries = Directory.GetFileSystemEntries(path);
                return ToolResult.Ok(entries);
            }

            if (cap == "filesystem.exists")
            {
                string path = arguments.TryGetValue("path", out var p) ? p?.ToString() ?? "" : "";
                return ToolResult.Ok(File.Exists(path) || Directory.Exists(path));
            }

            return ToolResult.Fail($"Unknown filesystem capability: '{capability}'");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Filesystem error: {ex.Message}");
        }
    }
}

public sealed class BrowserTool : ITool
{
    public string Name => "browser";

    public IReadOnlyList<string> SupportedCapabilities { get; } =
    [
        "browser.search",
        "browser.fetch"
    ];

    private readonly Search.SearchProviderRegistry _searchRegistry;
    private readonly HttpClient _httpClient;

    public BrowserTool(Search.SearchProviderRegistry? searchRegistry = null, HttpClient? httpClient = null)
    {
        _searchRegistry = searchRegistry ?? new Search.SearchProviderRegistry();
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string cap = capability.ToLowerInvariant();
        string query = arguments.TryGetValue("query", out var q) ? q?.ToString() ?? "" :
                       arguments.TryGetValue("url", out var u) ? u?.ToString() ?? "" : "";

        if (cap == "browser.search")
        {
            if (string.IsNullOrWhiteSpace(query))
                return ToolResult.Fail("Search query is empty");

            string? apiKey = arguments.TryGetValue("apiKey", out var ak) ? ak?.ToString() : null;
            string? provider = arguments.TryGetValue("provider", out var pv) ? pv?.ToString() : null;
            int maxResults = arguments.TryGetValue("maxResults", out var mr) && int.TryParse(mr?.ToString(), out var mrInt) ? mrInt : 5;

            try
            {
                var response = await _searchRegistry.SearchAsync(query, maxResults, provider, apiKey, ct);
                return ToolResult.Ok(response.FormatMarkdown());
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(ex.Message);
            }
        }

        if (cap == "browser.fetch")
        {
            if (string.IsNullOrWhiteSpace(query))
                return ToolResult.Fail("URL is empty for browser.fetch");

            try
            {
                string html = await _httpClient.GetStringAsync(query, ct);
                return ToolResult.Ok(html);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to fetch content from '{query}': {ex.Message}");
            }
        }

        return ToolResult.Fail($"Unknown browser capability: '{capability}'");
    }
}

public sealed class TerminalTool : ITool
{
    public string Name => "terminal";

    public IReadOnlyList<string> SupportedCapabilities { get; } =
    [
        "terminal.run"
    ];

    public async Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string command = arguments.TryGetValue("command", out var cmd) ? cmd?.ToString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(command))
            return ToolResult.Fail("Command is empty");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "sh",
                Arguments = OperatingSystem.IsWindows() ? $"-NoProfile -Command \"{command}\"" : $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return ToolResult.Fail("Failed to start terminal process");

            string stdout = await process.StandardOutput.ReadToEndAsync(ct);
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return ToolResult.Ok(new
            {
                ExitCode = process.ExitCode,
                Output = stdout.Trim(),
                Error = stderr.Trim()
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Terminal execution error: {ex.Message}");
        }
    }
}

public sealed class HttpTool : ITool
{
    public string Name => "http";

    public IReadOnlyList<string> SupportedCapabilities { get; } =
    [
        "http.get",
        "http.post"
    ];

    private readonly HttpClient _httpClient = new();

    public async Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string url = arguments.TryGetValue("url", out var u) ? u?.ToString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url))
            return ToolResult.Fail("URL is empty");

        try
        {
            if (capability.Equals("http.get", StringComparison.OrdinalIgnoreCase))
            {
                var response = await _httpClient.GetStringAsync(url, ct);
                return ToolResult.Ok(response);
            }

            if (capability.Equals("http.post", StringComparison.OrdinalIgnoreCase))
            {
                string body = arguments.TryGetValue("body", out var b) ? b?.ToString() ?? "" : "";
                string contentType = arguments.TryGetValue("contentType", out var ctHeader) ? ctHeader?.ToString() ?? "application/json" : "application/json";
                using var postContent = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, contentType);
                var postRes = await _httpClient.PostAsync(url, postContent, ct);
                string responseBody = await postRes.Content.ReadAsStringAsync(ct);
                return ToolResult.Ok(responseBody);
            }

            return ToolResult.Fail($"Unsupported HTTP capability '{capability}'");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"HTTP error: {ex.Message}");
        }
    }
}

public sealed class CalculatorTool : ITool
{
    public string Name => "calculator";

    public IReadOnlyList<string> SupportedCapabilities { get; } =
    [
        "calculator.eval"
    ];

    public Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string expr = arguments.TryGetValue("expr", out var e) ? e?.ToString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(expr))
            return Task.FromResult(ToolResult.Ok("0"));

        try
        {
            using var table = new System.Data.DataTable();
            var val = table.Compute(expr, string.Empty);
            return Task.FromResult(ToolResult.Ok(val?.ToString() ?? "0"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Calculation error for '{expr}': {ex.Message}"));
        }
    }
}

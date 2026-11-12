using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentLang.Models;

internal static class ModelJsonHelper
{
    private static string WriteJson(Action<Utf8JsonWriter> writeAction)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writeAction(writer);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string BuildChatCompletionsPayload(
        string model, string prompt, string? systemInstruction, double temperature, bool stream) =>
        WriteJson(w =>
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteStartArray("messages");
            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                w.WriteStartObject();
                w.WriteString("role", "system");
                w.WriteString("content", systemInstruction);
                w.WriteEndObject();
            }
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", prompt);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteNumber("temperature", temperature);
            if (stream) w.WriteBoolean("stream", true);
            w.WriteEndObject();
        });

    public static string BuildGeminiPayload(string prompt, string? systemInstruction, double temperature) =>
        WriteJson(w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("contents");
            w.WriteStartObject();
            w.WriteStartArray("parts");
            w.WriteStartObject();
            w.WriteString("text", prompt);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteStartObject("generationConfig");
            w.WriteNumber("temperature", temperature);
            w.WriteEndObject();

            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                w.WriteStartObject("system_instruction");
                w.WriteStartArray("parts");
                w.WriteStartObject();
                w.WriteString("text", systemInstruction);
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
        });

    public static string BuildAnthropicPayload(
        string model, string prompt, string? systemInstruction, double temperature, bool stream, int maxTokens = 1024) =>
        WriteJson(w =>
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("max_tokens", maxTokens);
            w.WriteNumber("temperature", temperature);
            if (stream) w.WriteBoolean("stream", true);
            w.WriteStartArray("messages");
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", prompt);
            w.WriteEndObject();
            w.WriteEndArray();
            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                w.WriteString("system", systemInstruction);
            }
            w.WriteEndObject();
        });

    public static string BuildOllamaPayload(
        string model, string prompt, string? systemInstruction, double temperature, bool stream) =>
        WriteJson(w =>
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteString("prompt", prompt);
            if (!string.IsNullOrWhiteSpace(systemInstruction))
                w.WriteString("system", systemInstruction);
            w.WriteBoolean("stream", stream);
            w.WriteStartObject("options");
            w.WriteNumber("temperature", temperature);
            w.WriteEndObject();
            w.WriteEndObject();
        });
}

public sealed class OpenAiModelProvider : IModelProvider
{
    public string ProviderId => "openai";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private string? _dynamicApiKey;
    private string _defaultModel = "gpt-4o";
    private readonly string _endpoint;
    public bool? AllowMockFallback { get; set; }

    private bool ShouldAllowMockFallback =>
        AllowMockFallback ?? (
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_OFFLINE_MOCK"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase) ||
            AppDomain.CurrentDomain.FriendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase)
        );

    public OpenAiModelProvider(string? apiKey = null, string? endpoint = null, HttpClient? httpClient = null, bool? allowMockFallback = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _endpoint = endpoint ?? Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? "https://api.openai.com/v1/chat/completions";
        _httpClient = httpClient ?? new HttpClient();
        AllowMockFallback = allowMockFallback;
    }

    public void SetApiKey(string key) => _dynamicApiKey = key;
    public void SetDefaultModel(string model) => _defaultModel = model;

    private string? GetEffectiveApiKey() =>
        _dynamicApiKey ??
        _apiKey ??
        Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public bool CanHandle(string modelName) =>
        modelName.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) ||
        modelName.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
        modelName.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
        modelName.StartsWith("o4", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("GPT", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("openai", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey();
        string actualModel = request.ModelName.Equals("GPT", StringComparison.OrdinalIgnoreCase) ||
                             request.ModelName.Equals("openai", StringComparison.OrdinalIgnoreCase)
            ? _defaultModel
            : request.ModelName;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (ShouldAllowMockFallback)
            {
                return new ModelResponse(
                    $"[OpenAI Mock Response: OPENAI_API_KEY not set] Processed '{request.Prompt}' using model '{actualModel}'",
                    actualModel,
                    TokensUsed: 42,
                    Latency: TimeSpan.FromMilliseconds(50),
                    Success: true
                );
            }

            return ModelResponse.Failed(
                $"[AL-AUTH-001] Missing OpenAI API Key: Model '{actualModel}' requires 'OPENAI_API_KEY'. " +
                "Please configure your key via environment variable (PowerShell: $env:OPENAI_API_KEY = \"sk-...\"; Bash: export OPENAI_API_KEY=\"sk-...\") " +
                "or declare it in 'agent.config.json'.",
                actualModel
            );
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            string json = ModelJsonHelper.BuildChatCompletionsPayload(
                actualModel, request.Prompt, request.SystemInstruction, request.Temperature, stream: false);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                return ModelResponse.Failed($"OpenAI API HTTP {response.StatusCode}: {err}", actualModel);
            }

            string responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            string content = string.Empty;
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msgElem) &&
                msgElem.TryGetProperty("content", out var contentElem))
            {
                content = contentElem.GetString() ?? string.Empty;
            }

            int tokens = doc.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var totalTokens)
                ? totalTokens.GetInt32()
                : 0;

            return new ModelResponse(content, actualModel, tokens, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            return ModelResponse.Failed(ex.Message, actualModel);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey();
        string actualModel = request.ModelName.Equals("GPT", StringComparison.OrdinalIgnoreCase) ||
                             request.ModelName.Equals("openai", StringComparison.OrdinalIgnoreCase)
            ? _defaultModel
            : request.ModelName;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (ShouldAllowMockFallback)
            {
                yield return $"[OpenAI Mock Response: OPENAI_API_KEY not set] Processed '{request.Prompt}'";
                yield break;
            }

            throw new InvalidOperationException($"[AL-AUTH-001] Missing OpenAI API Key: Model '{actualModel}' requires 'OPENAI_API_KEY'. Please configure it via environment variable or in 'agent.config.json'.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        string streamJson = ModelJsonHelper.BuildChatCompletionsPayload(
            actualModel, request.Prompt, request.SystemInstruction, request.Temperature, stream: true);
        httpRequest.Content = new StringContent(streamJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"OpenAI API HTTP {response.StatusCode}: {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            line = line.Trim();
            if (line.StartsWith("data: "))
            {
                string data = line[6..].Trim();
                if (data == "[DONE]") break;
                if (string.IsNullOrEmpty(data)) continue;

                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentElem))
                {
                    string? token = contentElem.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        yield return token;
                    }
                }
            }
        }
    }
}

public sealed class GeminiModelProvider : IModelProvider
{
    public string ProviderId => "gemini";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private string? _dynamicApiKey;
    private string _defaultModel = "gemini-2.5-pro";
    public bool? AllowMockFallback { get; set; }

    private bool ShouldAllowMockFallback =>
        AllowMockFallback ?? (
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_OFFLINE_MOCK"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase) ||
            AppDomain.CurrentDomain.FriendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase)
        );

    public GeminiModelProvider(string? apiKey = null, HttpClient? httpClient = null, bool? allowMockFallback = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _httpClient = httpClient ?? new HttpClient();
        AllowMockFallback = allowMockFallback;
    }

    public void SetApiKey(string key) => _dynamicApiKey = key;
    public void SetDefaultModel(string model) => _defaultModel = model;

    private string? GetEffectiveApiKey() =>
        _dynamicApiKey ??
        _apiKey ??
        Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    public bool CanHandle(string modelName) =>
        modelName.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("google", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        string? apiKey = GetEffectiveApiKey();
        string envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "";
        string actualModel;

        if (request.ModelName.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ||
            request.ModelName.Equals("google", StringComparison.OrdinalIgnoreCase) ||
            request.ModelName.Equals("gemini-pro", StringComparison.OrdinalIgnoreCase) ||
            request.ModelName.Equals("gemini-flash", StringComparison.OrdinalIgnoreCase))
        {
            actualModel = !string.IsNullOrWhiteSpace(envModel) ? envModel : _defaultModel;
        }
        else
        {
            actualModel = request.ModelName;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (ShouldAllowMockFallback)
            {
                return new ModelResponse(
                    $"[Gemini Mock Response: GEMINI_API_KEY not set] Processed '{request.Prompt}' using model '{actualModel}'",
                    actualModel,
                    TokensUsed: 42,
                    Latency: TimeSpan.FromMilliseconds(50),
                    Success: true
                );
            }

            return ModelResponse.Failed(
                $"[AL-AUTH-002] Missing Google Gemini API Key: Model '{actualModel}' requires 'GEMINI_API_KEY'. " +
                "Please configure your key via environment variable (PowerShell: $env:GEMINI_API_KEY = \"...\"; Bash: export GEMINI_API_KEY=\"...\") " +
                "or declare it in 'agent.config.json'.",
                actualModel
            );
        }

        var sw = Stopwatch.StartNew();
        try
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{actualModel}:generateContent?key={apiKey}";

            string json = ModelJsonHelper.BuildGeminiPayload(request.Prompt, request.SystemInstruction, request.Temperature);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                return ModelResponse.Failed($"Gemini API HTTP {response.StatusCode} ({actualModel}): {err}", actualModel);
            }

            string responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            string resultText = string.Empty;
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var candContent) &&
                candContent.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElem))
            {
                resultText = textElem.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("promptFeedback", out var pf))
            {
                return ModelResponse.Failed($"Gemini blocked prompt: {pf}", actualModel);
            }

            return new ModelResponse(resultText, actualModel, 0, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            return ModelResponse.Failed(ex.Message, actualModel);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? apiKey = GetEffectiveApiKey();
        string envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "";
        string actualModel;

        if (request.ModelName.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ||
            request.ModelName.Equals("google", StringComparison.OrdinalIgnoreCase) ||
            request.ModelName.Equals("gemini-pro", StringComparison.OrdinalIgnoreCase) ||
            request.ModelName.Equals("gemini-flash", StringComparison.OrdinalIgnoreCase))
        {
            actualModel = !string.IsNullOrWhiteSpace(envModel) ? envModel : _defaultModel;
        }
        else
        {
            actualModel = request.ModelName;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (ShouldAllowMockFallback)
            {
                yield return $"[Gemini Mock Response: GEMINI_API_KEY not set] Processed '{request.Prompt}'";
                yield break;
            }

            throw new InvalidOperationException($"[AL-AUTH-002] Missing Google Gemini API Key: Model '{actualModel}' requires 'GEMINI_API_KEY'. Please configure it via environment variable or in 'agent.config.json'.");
        }

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{actualModel}:streamGenerateContent?alt=sse&key={apiKey}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        string streamGeminiJson = ModelJsonHelper.BuildGeminiPayload(request.Prompt, request.SystemInstruction, request.Temperature);
        httpRequest.Content = new StringContent(streamGeminiJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gemini API HTTP {response.StatusCode}: {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            line = line.Trim();
            if (line.StartsWith("data: "))
            {
                string data = line[6..].Trim();
                if (string.IsNullOrEmpty(data)) continue;

                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0 &&
                    candidates[0].TryGetProperty("content", out var candContent) &&
                    candContent.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textElem))
                {
                    string? token = textElem.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        yield return token;
                    }
                }
            }
        }
    }
}

public sealed class AnthropicModelProvider : IModelProvider
{
    public string ProviderId => "anthropic";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private string? _dynamicApiKey;
    private string _defaultModel = "claude-3-7-sonnet-20250219";
    public bool? AllowMockFallback { get; set; }

    private bool ShouldAllowMockFallback =>
        AllowMockFallback ?? (
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_OFFLINE_MOCK"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase) ||
            AppDomain.CurrentDomain.FriendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase)
        );

    public AnthropicModelProvider(string? apiKey = null, HttpClient? httpClient = null, bool? allowMockFallback = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _httpClient = httpClient ?? new HttpClient();
        AllowMockFallback = allowMockFallback;
    }

    public void SetApiKey(string key) => _dynamicApiKey = key;
    public void SetDefaultModel(string model) => _defaultModel = model;

    private string? GetEffectiveApiKey() =>
        _dynamicApiKey ??
        _apiKey ??
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public bool CanHandle(string modelName) =>
        modelName.StartsWith("claude", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("Claude", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("anthropic", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey();
        string actualModel = request.ModelName.Equals("Claude", StringComparison.OrdinalIgnoreCase) ||
                             request.ModelName.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
            ? _defaultModel
            : request.ModelName;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (ShouldAllowMockFallback)
            {
                return new ModelResponse(
                    $"[Claude Mock Response: ANTHROPIC_API_KEY not set] Processed '{request.Prompt}' using model '{actualModel}'",
                    actualModel,
                    TokensUsed: 42,
                    Latency: TimeSpan.FromMilliseconds(50),
                    Success: true
                );
            }

            return ModelResponse.Failed(
                $"[AL-AUTH-003] Missing Anthropic Claude API Key: Model '{actualModel}' requires 'ANTHROPIC_API_KEY'. " +
                "Please configure your key via environment variable (PowerShell: $env:ANTHROPIC_API_KEY = \"sk-ant-...\"; Bash: export ANTHROPIC_API_KEY=\"sk-ant-...\") " +
                "or declare it in 'agent.config.json'.",
                actualModel
            );
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", key);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");

            string json = ModelJsonHelper.BuildAnthropicPayload(
                actualModel, request.Prompt, request.SystemInstruction, request.Temperature, stream: false);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                return ModelResponse.Failed($"Anthropic API HTTP {response.StatusCode}: {err}", actualModel);
            }

            string responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            string resultText = string.Empty;
            if (doc.RootElement.TryGetProperty("content", out var contentArr) &&
                contentArr.GetArrayLength() > 0 &&
                contentArr[0].TryGetProperty("text", out var textElem))
            {
                resultText = textElem.GetString() ?? string.Empty;
            }

            return new ModelResponse(resultText, actualModel, 0, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            return ModelResponse.Failed(ex.Message, actualModel);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey();
        string actualModel = request.ModelName.Equals("Claude", StringComparison.OrdinalIgnoreCase) ||
                             request.ModelName.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
            ? _defaultModel
            : request.ModelName;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (ShouldAllowMockFallback)
            {
                yield return $"[Claude Mock Response: ANTHROPIC_API_KEY not set] Processed '{request.Prompt}'";
                yield break;
            }

            throw new InvalidOperationException($"[AL-AUTH-003] Missing Anthropic Claude API Key: Model '{actualModel}' requires 'ANTHROPIC_API_KEY'. Please configure it via environment variable or in 'agent.config.json'.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", key);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        string streamClaudeJson = ModelJsonHelper.BuildAnthropicPayload(
            actualModel, request.Prompt, request.SystemInstruction, request.Temperature, stream: true);
        httpRequest.Content = new StringContent(streamClaudeJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Anthropic API HTTP {response.StatusCode}: {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            line = line.Trim();

            if (line.StartsWith("data: "))
            {
                string data = line[6..].Trim();
                if (string.IsNullOrEmpty(data)) continue;

                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "content_block_delta" &&
                    doc.RootElement.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("text", out var textProp))
                {
                    string? token = textProp.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        yield return token;
                    }
                }
            }
        }
    }
}

public sealed class DeepSeekModelProvider : IModelProvider
{
    public string ProviderId => "deepseek";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private string? _dynamicApiKey;
    private string _defaultModel = "deepseek-chat";
    private readonly string _endpoint;
    public bool? AllowMockFallback { get; set; }

    private bool ShouldAllowMockFallback =>
        AllowMockFallback ?? (
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_OFFLINE_MOCK"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("AGENTLANG_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase) ||
            AppDomain.CurrentDomain.FriendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase)
        );

    public DeepSeekModelProvider(string? apiKey = null, string? endpoint = null, HttpClient? httpClient = null, bool? allowMockFallback = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        _endpoint = endpoint ?? Environment.GetEnvironmentVariable("DEEPSEEK_ENDPOINT") ?? "https://api.deepseek.com/chat/completions";
        _httpClient = httpClient ?? new HttpClient();
        AllowMockFallback = allowMockFallback;
    }

    public void SetApiKey(string key) => _dynamicApiKey = key;
    public void SetDefaultModel(string model) => _defaultModel = model;

    private string? GetEffectiveApiKey() =>
        _dynamicApiKey ??
        _apiKey ??
        Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

    public bool CanHandle(string modelName) =>
        modelName.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("r1", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey();
        string actualModel = request.ModelName.Equals("deepseek", StringComparison.OrdinalIgnoreCase)
            ? _defaultModel
            : request.ModelName;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (ShouldAllowMockFallback)
            {
                return new ModelResponse(
                    $"[DeepSeek Mock Response: DEEPSEEK_API_KEY not set] Processed '{request.Prompt}' using model '{actualModel}'",
                    actualModel,
                    TokensUsed: 42,
                    Latency: TimeSpan.FromMilliseconds(50),
                    Success: true
                );
            }

            return ModelResponse.Failed(
                $"[AL-AUTH-004] Missing DeepSeek API Key: Model '{actualModel}' requires 'DEEPSEEK_API_KEY'. " +
                "Please configure your key via environment variable (PowerShell: $env:DEEPSEEK_API_KEY = \"sk-...\"; Bash: export DEEPSEEK_API_KEY=\"sk-...\") " +
                "or declare it in 'agent.config.json'.",
                actualModel
            );
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            string json = ModelJsonHelper.BuildChatCompletionsPayload(
                actualModel, request.Prompt, request.SystemInstruction, request.Temperature, stream: false);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                return ModelResponse.Failed($"DeepSeek API HTTP {response.StatusCode}: {err}", actualModel);
            }

            string responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            string content = string.Empty;
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msgElem) &&
                msgElem.TryGetProperty("content", out var contentElem))
            {
                content = contentElem.GetString() ?? string.Empty;
            }

            int tokens = doc.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var totalTokens)
                ? totalTokens.GetInt32()
                : 0;

            return new ModelResponse(content, actualModel, tokens, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            return ModelResponse.Failed(ex.Message, actualModel);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey();
        string actualModel = request.ModelName.Equals("deepseek", StringComparison.OrdinalIgnoreCase)
            ? _defaultModel
            : request.ModelName;

        if (string.IsNullOrWhiteSpace(key))
        {
            if (ShouldAllowMockFallback)
            {
                yield return $"[DeepSeek Mock Response: DEEPSEEK_API_KEY not set] Processed '{request.Prompt}'";
                yield break;
            }

            throw new InvalidOperationException($"[AL-AUTH-004] Missing DeepSeek API Key: Model '{actualModel}' requires 'DEEPSEEK_API_KEY'. Please configure it via environment variable or in 'agent.config.json'.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        string streamJson = ModelJsonHelper.BuildChatCompletionsPayload(
            actualModel, request.Prompt, request.SystemInstruction, request.Temperature, stream: true);
        httpRequest.Content = new StringContent(streamJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"DeepSeek API HTTP {response.StatusCode}: {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            line = line.Trim();
            if (line.StartsWith("data: "))
            {
                string data = line[6..].Trim();
                if (data == "[DONE]") break;
                if (string.IsNullOrEmpty(data)) continue;

                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentElem))
                {
                    string? token = contentElem.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        yield return token;
                    }
                }
            }
        }
    }
}

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentLang.Models;

public sealed class OpenAiModelProvider : IModelProvider
{
    public string ProviderId => "openai";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private string? _dynamicApiKey;
    private string _defaultModel = "gpt-4o";
    private readonly string _endpoint;

    public OpenAiModelProvider(string? apiKey = null, string? endpoint = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _endpoint = endpoint ?? Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? "https://api.openai.com/v1/chat/completions";
        _httpClient = httpClient ?? new HttpClient();
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
        modelName.Equals("GPT", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("openai", StringComparison.OrdinalIgnoreCase);

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        string? key = GetEffectiveApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            // If API key is not configured, return an informative fallback response
            return new ModelResponse(
                $"[OpenAI Mock Response: OPENAI_API_KEY not set] Processed '{request.Prompt}' using model '{request.ModelName}'",
                request.ModelName,
                TokensUsed: 42,
                Latency: TimeSpan.FromMilliseconds(50),
                Success: true
            );
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            string actualModel = request.ModelName.Equals("GPT", StringComparison.OrdinalIgnoreCase) ||
                                 request.ModelName.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? _defaultModel
                : request.ModelName;

            var payload = new
            {
                model = actualModel,
                messages = new[]
                {
                    new { role = "user", content = request.Prompt }
                },
                temperature = request.Temperature
            };

            string json = JsonSerializer.Serialize(payload);
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
            string content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            int tokens = doc.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var totalTokens)
                ? totalTokens.GetInt32()
                : 0;

            return new ModelResponse(content, actualModel, tokens, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            return ModelResponse.Failed(ex.Message, request.ModelName);
        }
    }
}

public sealed class GeminiModelProvider : IModelProvider
{
    public string ProviderId => "gemini";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private string? _dynamicApiKey;
    private string _defaultModel = "gemini-1.5-flash";

    public GeminiModelProvider(string? apiKey = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _httpClient = httpClient ?? new HttpClient();
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
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ModelResponse(
                $"[Gemini Mock Response: GEMINI_API_KEY not set] Processed '{request.Prompt}' using model '{request.ModelName}'",
                request.ModelName,
                TokensUsed: 42,
                Latency: TimeSpan.FromMilliseconds(50),
                Success: true
            );
        }

        var sw = Stopwatch.StartNew();
        try
        {
            string envModel = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "";
            string actualModel;

            if (request.ModelName.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ||
                request.ModelName.Equals("google", StringComparison.OrdinalIgnoreCase) ||
                request.ModelName.Equals("gemini-flash", StringComparison.OrdinalIgnoreCase))
            {
                actualModel = !string.IsNullOrWhiteSpace(envModel) ? envModel : _defaultModel;
            }
            else
            {
                actualModel = request.ModelName;
            }

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{actualModel}:generateContent?key={apiKey}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = request.Prompt } }
                    }
                }
            };

            string json = JsonSerializer.Serialize(payload);
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
            string resultText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            return new ModelResponse(resultText, actualModel, 0, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            return ModelResponse.Failed(ex.Message, request.ModelName);
        }
    }
}

public sealed class AnthropicModelProvider : IModelProvider
{
    public string ProviderId => "anthropic";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private string? _dynamicApiKey;
    private string _defaultModel = "claude-3-5-sonnet-20241022";

    public AnthropicModelProvider(string? apiKey = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _httpClient = httpClient ?? new HttpClient();
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
        if (string.IsNullOrWhiteSpace(key))
        {
            return new ModelResponse(
                $"[Claude Mock Response: ANTHROPIC_API_KEY not set] Processed '{request.Prompt}' using model '{request.ModelName}'",
                request.ModelName,
                TokensUsed: 42,
                Latency: TimeSpan.FromMilliseconds(50),
                Success: true
            );
        }

        var sw = Stopwatch.StartNew();
        try
        {
            string actualModel = request.ModelName.Equals("Claude", StringComparison.OrdinalIgnoreCase) ||
                                 request.ModelName.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
                ? _defaultModel
                : request.ModelName;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", key);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");

            var payload = new
            {
                model = actualModel,
                max_tokens = 1024,
                messages = new[]
                {
                    new { role = "user", content = request.Prompt }
                }
            };

            string json = JsonSerializer.Serialize(payload);
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
            string resultText = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            return new ModelResponse(resultText, actualModel, 0, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            return ModelResponse.Failed(ex.Message, request.ModelName);
        }
    }
}

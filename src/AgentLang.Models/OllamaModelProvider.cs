using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgentLang.Models;

public sealed class OllamaModelProvider : IModelProvider
{
    public string ProviderId => "ollama";
    public string BaseUrl { get; set; }
    private readonly HttpClient _httpClient;

    public OllamaModelProvider(string? baseUrl = null, HttpClient? httpClient = null)
    {
        BaseUrl = baseUrl ?? Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
        _httpClient = httpClient ?? new HttpClient();
    }

    public bool CanHandle(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return false;
        string lower = modelName.Trim().ToLowerInvariant();

        return lower.StartsWith("ollama/") ||
               lower.StartsWith("llama") ||
               lower.StartsWith("mistral") ||
               lower.StartsWith("qwen") ||
               lower.StartsWith("phi") ||
               lower.StartsWith("deepseek") ||
               lower.StartsWith("gemma") ||
               lower.StartsWith("codellama");
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        string actualModel = request.ModelName.StartsWith("ollama/", StringComparison.OrdinalIgnoreCase)
            ? request.ModelName[7..]
            : request.ModelName;

        var payload = new
        {
            model = actualModel,
            prompt = request.Prompt,
            system = request.SystemInstruction,
            stream = false,
            options = new
            {
                temperature = request.Temperature
            }
        };

        var sw = Stopwatch.StartNew();
        try
        {
            string endpoint = $"{BaseUrl.TrimEnd('/')}/api/generate";
            var response = await _httpClient.PostAsJsonAsync(endpoint, payload, ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                return ModelResponse.Failed($"Ollama returned HTTP {response.StatusCode}: {err}", actualModel);
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            string content = root.TryGetProperty("response", out var respProp) ? respProp.GetString() ?? "" : "";
            int tokens = root.TryGetProperty("eval_count", out var evalProp) ? evalProp.GetInt32() : 0;

            return new ModelResponse(content, actualModel, tokens, sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ModelResponse.Failed($"Failed to connect to local Ollama instance at {BaseUrl}: {ex.Message}", actualModel);
        }
    }
}

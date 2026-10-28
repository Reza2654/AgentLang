using System.Diagnostics;

namespace AgentLang.Models;

public sealed record ModelRequest(
    string ModelName,
    string Prompt,
    string? SystemInstruction = null,
    IReadOnlyList<string>? Context = null,
    double Temperature = 0.7,
    IReadOnlyList<string>? Images = null);

public sealed record ModelResponse(
    string Content,
    string Model,
    int TokensUsed = 0,
    TimeSpan Latency = default,
    bool Success = true,
    string? ErrorMessage = null)
{
    public static ModelResponse Failed(string error, string model) =>
        new(string.Empty, model, 0, TimeSpan.Zero, false, error);
}

public interface IModelProvider
{
    string ProviderId { get; }
    bool CanHandle(string modelName);
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default);
}

public sealed class ModelRegistry
{
    private readonly List<IModelProvider> _providers = [];
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private IModelProvider _defaultProvider;

    public IReadOnlyDictionary<string, string> Aliases => _aliases;
    public IReadOnlyList<IModelProvider> Providers => _providers;

    public ModelRegistry()
    {
        var mock = new MockModelProvider();
        _providers.Add(mock);
        _defaultProvider = mock;

        // Register default REST providers
        _providers.Add(new OpenAiModelProvider());
        _providers.Add(new GeminiModelProvider());
        _providers.Add(new AnthropicModelProvider());
        _providers.Add(new OllamaModelProvider());

        // Register default frontier model aliases
        RegisterAlias("gpt6", "gpt-6-astra");
        RegisterAlias("gpt-6", "gpt-6-astra");
        RegisterAlias("astra", "gpt-6-astra");
        RegisterAlias("gpt5", "gpt-5-omni");
        RegisterAlias("claude-fable", "claude-fable-5.1");
        RegisterAlias("fable", "claude-fable-5.1");
        RegisterAlias("claude5", "claude-fable-5.1");
        RegisterAlias("gemini3", "gemini-3.0-pro");
        RegisterAlias("gemini-3", "gemini-3.0-pro");
        RegisterAlias("gemini", "gemini-3.0-pro");
        RegisterAlias("gemini-pro", "gemini-3.0-pro");
        RegisterAlias("gemini-ultra", "gemini-3.1-ultra");
        RegisterAlias("gemini-flash", "gemini-3.0-flash");
        RegisterAlias("gemini-omni", "gemini-3.5-omni");
        RegisterAlias("gemini2", "gemini-2.5-pro");
        RegisterAlias("llama4", "llama-4-maverick");
        RegisterAlias("llama-4", "llama-4-maverick");
    }

    public IModelProvider? GetProvider(string providerId) =>
        _providers.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    public void RegisterProvider(IModelProvider provider, bool makeDefault = false)
    {
        _providers.Insert(0, provider);
        if (makeDefault)
            _defaultProvider = provider;
    }

    public void RegisterAlias(string alias, string targetModel)
    {
        _aliases[alias] = targetModel;
    }

    public string ResolveAlias(string modelOrAlias)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = modelOrAlias;
        while (_aliases.TryGetValue(current, out var target))
        {
            if (!visited.Add(current))
                break; // Cycle guard
            current = target;
        }
        return current;
    }

    public IModelProvider Resolve(string modelName)
    {
        string effectiveModel = ResolveAlias(modelName);
        foreach (var provider in _providers)
        {
            if (provider.CanHandle(effectiveModel) || provider.CanHandle(modelName))
                return provider;
        }

        return _defaultProvider;
    }
}

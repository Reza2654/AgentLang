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

        // Register official frontier model aliases
        // OpenAI
        RegisterAlias("gpt4", "gpt-4o");
        RegisterAlias("gpt-4", "gpt-4o");
        RegisterAlias("gpt", "gpt-4o");
        RegisterAlias("gpt4.5", "gpt-4.5-preview");
        RegisterAlias("gpt-4.5", "gpt-4.5-preview");
        RegisterAlias("o3", "o3-mini");
        RegisterAlias("o1", "o1-mini");

        // Anthropic Claude
        RegisterAlias("claude", "claude-3-7-sonnet");
        RegisterAlias("claude3", "claude-3-7-sonnet");
        RegisterAlias("claude-sonnet", "claude-3-7-sonnet");
        RegisterAlias("sonnet", "claude-3-7-sonnet");
        RegisterAlias("claude-haiku", "claude-3-5-haiku");
        RegisterAlias("claude-opus", "claude-3-opus");

        // Google Gemini
        RegisterAlias("gemini", "gemini-2.5-pro");
        RegisterAlias("gemini-pro", "gemini-2.5-pro");
        RegisterAlias("gemini-flash", "gemini-2.5-flash");
        RegisterAlias("gemini-flash-lite", "gemini-2.5-flash-lite");
        RegisterAlias("gemini2", "gemini-2.5-pro");
        RegisterAlias("gemini-2", "gemini-2.5-pro");
        RegisterAlias("gemini3", "gemini-3.1-pro-preview");
        RegisterAlias("gemini-3", "gemini-3.1-pro-preview");
        RegisterAlias("gemini-3-flash", "gemini-3.8-flash");
        RegisterAlias("gemini4", "gemini-4-argon");
        RegisterAlias("gemini-4", "gemini-4-argon");

        // Meta Llama
        RegisterAlias("llama", "llama-3.3-70b-instruct");
        RegisterAlias("llama3", "llama-3.3-70b-instruct");
        RegisterAlias("llama-3", "llama-3.3-70b-instruct");
        RegisterAlias("llama3.3", "llama-3.3-70b-instruct");
        RegisterAlias("llama3.2", "llama-3.2-3b-instruct");

        // DeepSeek
        RegisterAlias("deepseek", "deepseek-chat");
        RegisterAlias("deepseek-r1", "deepseek-reasoner");
        RegisterAlias("deepseek-v3", "deepseek-chat");
        RegisterAlias("r1", "deepseek-reasoner");
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

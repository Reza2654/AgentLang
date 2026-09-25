using System.Diagnostics;

namespace AgentLang.Models;

public sealed record ModelRequest(
    string ModelName,
    string Prompt,
    string? SystemInstruction = null,
    IReadOnlyList<string>? Context = null,
    double Temperature = 0.7);

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
    private IModelProvider _defaultProvider;

    public ModelRegistry()
    {
        var mock = new MockModelProvider();
        _providers.Add(mock);
        _defaultProvider = mock;
    }

    public void RegisterProvider(IModelProvider provider, bool makeDefault = false)
    {
        _providers.Insert(0, provider);
        if (makeDefault)
            _defaultProvider = provider;
    }

    public IModelProvider Resolve(string modelName)
    {
        foreach (var provider in _providers)
        {
            if (provider.CanHandle(modelName))
                return provider;
        }

        return _defaultProvider;
    }
}

# AgentLang Model Providers

AgentLang decouples the language syntax from any single AI vendor.

## 1. Provider Abstraction

All models implement the `IModelProvider` interface:
```csharp
public interface IModelProvider
{
    string ProviderId { get; }
    bool CanHandle(string modelName);
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default);
}
```

## 2. Supported Providers

### Mock Provider (Default)
- Zero-cost, 100% offline, deterministic responses for tests and local development.
- Automatically handles requests when external API keys are not present.

### OpenAI & Compatible
- Handles models: `GPT`, `gpt-4o`, `gpt-4o-mini`, `o1`, `o3-mini`.
- Supports third-party OpenAI-compatible endpoints (Ollama, vLLM, LocalAI) via `OPENAI_ENDPOINT`.
- Configured via: `OPENAI_API_KEY`.

### Google Gemini
- Handles models: `Gemini`, `gemini-2.5-flash`, `gemini-1.5-pro`.
- Configured via: `GEMINI_API_KEY`.

### Anthropic Claude
- Handles models: `Claude`, `claude-3-5-sonnet`, `claude-3-haiku`.
- Configured via: `ANTHROPIC_API_KEY`.

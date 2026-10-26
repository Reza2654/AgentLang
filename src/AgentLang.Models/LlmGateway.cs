using System.Collections.Concurrent;
using System.Diagnostics;

namespace AgentLang.Models;

public enum CircuitState
{
    Closed,     // Normal operations
    Open,       // Trip/Failing, requests bypassed
    HalfOpen    // Cooldown elapsed, testing single probe
}

public sealed class ProviderHealth
{
    public required string ProviderId { get; init; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastFailureTime { get; set; }
    public CircuitState State { get; set; } = CircuitState.Closed;
    public long TotalRequests { get; set; }
    public long TotalTokens { get; set; }
    public double TotalCostUsd { get; set; }
    public TimeSpan TotalLatency { get; set; }

    public TimeSpan AverageLatency => TotalRequests > 0 ? TotalLatency / TotalRequests : TimeSpan.Zero;
}

public sealed class LlmGatewayRouter : IModelProvider
{
    public string ProviderId => "gateway";
    private readonly ModelRegistry _registry;
    private readonly ConcurrentDictionary<string, ProviderHealth> _healthMap = new(StringComparer.OrdinalIgnoreCase);

    public List<string> FallbackChain { get; } = [];
    public int MaxConsecutiveFailures { get; set; } = 3;
    public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromSeconds(30);

    public IReadOnlyDictionary<string, ProviderHealth> HealthMap => _healthMap;

    public LlmGatewayRouter(ModelRegistry? registry = null, IEnumerable<string>? fallbackChain = null)
    {
        _registry = registry ?? new ModelRegistry();
        if (fallbackChain != null)
        {
            FallbackChain.AddRange(fallbackChain);
        }
    }

    public bool CanHandle(string modelName)
    {
        // The Gateway can handle any model by delegating to its registry or fallback chain
        return true;
    }

    public ProviderHealth GetOrCreateHealth(string providerId)
    {
        return _healthMap.GetOrAdd(providerId, id => new ProviderHealth { ProviderId = id });
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        var candidateModels = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ModelName))
        {
            candidateModels.Add(request.ModelName);
        }

        foreach (var m in FallbackChain)
        {
            if (!candidateModels.Contains(m, StringComparer.OrdinalIgnoreCase))
            {
                candidateModels.Add(m);
            }
        }

        if (candidateModels.Count == 0)
        {
            candidateModels.Add("mock");
        }

        var errors = new List<string>();

        foreach (var model in candidateModels)
        {
            var provider = _registry.Resolve(model);
            var health = GetOrCreateHealth(provider.ProviderId);

            // Circuit breaker check
            if (health.State == CircuitState.Open)
            {
                if (health.LastFailureTime.HasValue && DateTime.UtcNow - health.LastFailureTime.Value > CooldownPeriod)
                {
                    health.State = CircuitState.HalfOpen;
                }
                else
                {
                    errors.Add($"Provider '{provider.ProviderId}' circuit is OPEN (skipped).");
                    continue;
                }
            }

            var subRequest = new ModelRequest(
                model,
                request.Prompt,
                request.SystemInstruction,
                request.Context,
                request.Temperature,
                request.Images
            );

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await provider.GenerateAsync(subRequest, ct);
                sw.Stop();

                if (response.Success)
                {
                    // Success: heal health
                    health.ConsecutiveFailures = 0;
                    health.State = CircuitState.Closed;
                    health.TotalRequests++;
                    health.TotalTokens += response.TokensUsed;
                    health.TotalLatency += sw.Elapsed;

                    return response;
                }
                else
                {
                    RecordFailure(health, response.ErrorMessage ?? "Generation failed", errors);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                RecordFailure(health, ex.Message, errors);
            }
        }

        return ModelResponse.Failed(
            $"All providers in gateway fallback chain failed:\n- {string.Join("\n- ", errors)}",
            request.ModelName
        );
    }

    private void RecordFailure(ProviderHealth health, string error, List<string> errorLog)
    {
        health.ConsecutiveFailures++;
        health.LastFailureTime = DateTime.UtcNow;
        if (health.ConsecutiveFailures >= MaxConsecutiveFailures)
        {
            health.State = CircuitState.Open;
        }
        errorLog.Add($"[{health.ProviderId}] {error}");
    }
}

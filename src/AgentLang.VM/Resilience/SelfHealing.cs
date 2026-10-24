using AgentLang.Tools;

namespace AgentLang.VM.Resilience;

public enum RemediationStrategy
{
    InitialAttempt,
    RetryWithBackoff,
    FallbackTool,
    ReflectorRepair
}

public sealed record RemediationStep(
    int Attempt,
    RemediationStrategy Strategy,
    string ToolName,
    bool Success,
    string? Error = null);

public sealed class SelfHealingPolicy
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(50);
    public Dictionary<string, List<string>> ToolFallbacks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, string, IReadOnlyDictionary<string, object?>, Dictionary<string, object?>>? ArgumentRepairer { get; set; }

    public static SelfHealingPolicy Default => new();
}

public sealed class SelfHealingResult
{
    public bool Success { get; set; }
    public object? Output { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public List<RemediationStep> RemediationTrail { get; set; } = new();

    public static SelfHealingResult Ok(object? output, int attempts, List<RemediationStep> trail) =>
        new() { Success = true, Output = output, Attempts = attempts, RemediationTrail = trail };

    public static SelfHealingResult Fail(string error, int attempts, List<RemediationStep> trail) =>
        new() { Success = false, Error = error, Attempts = attempts, RemediationTrail = trail };
}

public sealed class AutoRemediator
{
    public async Task<SelfHealingResult> ExecuteWithRemediationAsync(
        Func<string, IReadOnlyDictionary<string, object?>, Task<ToolResult>> toolInvoker,
        string toolName,
        IReadOnlyDictionary<string, object?> initialArguments,
        SelfHealingPolicy? policy = null,
        CancellationToken ct = default)
    {
        policy ??= SelfHealingPolicy.Default;
        var trail = new List<RemediationStep>();
        string currentTool = toolName;
        var currentArgs = new Dictionary<string, object?>(initialArguments, StringComparer.OrdinalIgnoreCase);

        for (int attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            var strategy = attempt == 1 ? RemediationStrategy.InitialAttempt :
                           (currentTool != toolName ? RemediationStrategy.FallbackTool :
                           (policy.ArgumentRepairer != null ? RemediationStrategy.ReflectorRepair : RemediationStrategy.RetryWithBackoff));

            try
            {
                var result = await toolInvoker(currentTool, currentArgs);
                if (result.Success)
                {
                    trail.Add(new RemediationStep(attempt, strategy, currentTool, true));
                    return SelfHealingResult.Ok(result.Output, attempt, trail);
                }

                trail.Add(new RemediationStep(attempt, strategy, currentTool, false, result.Error));

                // If not last attempt, execute remediation strategy
                if (attempt < policy.MaxAttempts)
                {
                    // 1. Check if tool fallback exists
                    if (policy.ToolFallbacks.TryGetValue(toolName, out var fallbacks) && fallbacks.Count > 0)
                    {
                        int fallbackIdx = (attempt - 1) % fallbacks.Count;
                        currentTool = fallbacks[fallbackIdx];
                    }
                    // 2. Check if argument repairer function is configured
                    else if (policy.ArgumentRepairer != null)
                    {
                        currentArgs = policy.ArgumentRepairer(currentTool, result.Error ?? "", currentArgs);
                    }
                    // 3. Otherwise back off
                    else
                    {
                        var delay = TimeSpan.FromMilliseconds(policy.InitialBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));
                        await Task.Delay(delay, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                trail.Add(new RemediationStep(attempt, strategy, currentTool, false, ex.Message));
                if (attempt == policy.MaxAttempts)
                {
                    return SelfHealingResult.Fail(ex.Message, attempt, trail);
                }
            }
        }

        string finalError = trail.LastOrDefault()?.Error ?? "Auto-remediation exhausted all attempts without recovery.";
        return SelfHealingResult.Fail(finalError, policy.MaxAttempts, trail);
    }
}

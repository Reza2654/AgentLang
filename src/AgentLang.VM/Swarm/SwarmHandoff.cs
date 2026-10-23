using AgentLang.Runtime;
using AgentLang.Runtime.Values;

namespace AgentLang.VM.Swarm;

public sealed record SwarmHandoffRequest(
    string FromAgent,
    string ToAgent,
    string Reason,
    object? Payload = null,
    bool ReturnOnComplete = true,
    DateTimeOffset Timestamp = default)
{
    public DateTimeOffset Timestamp { get; init; } = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp;
}

public sealed class SwarmHandoffResult
{
    public bool Success { get; set; } = true;
    public string FinalResponse { get; set; } = string.Empty;
    public List<SwarmHandoffRequest> HandoffTrail { get; set; } = new();
    public string? Error { get; set; }

    public static SwarmHandoffResult Ok(string response, List<SwarmHandoffRequest> trail) =>
        new() { Success = true, FinalResponse = response, HandoffTrail = trail };

    public static SwarmHandoffResult Fail(string error, List<SwarmHandoffRequest> trail) =>
        new() { Success = false, Error = error, HandoffTrail = trail };
}

public sealed class SwarmHandoffRouter
{
    private readonly AgentLangRuntime _runtime;

    public SwarmHandoffRouter(AgentLangRuntime runtime)
    {
        _runtime = runtime;
    }

    public async Task<SwarmHandoffResult> HandoffAsync(
        SwarmHandoffRequest request,
        CancellationToken ct = default)
    {
        var trail = new List<SwarmHandoffRequest> { request };

        if (!_runtime.AgentInstances.TryGetValue(request.ToAgent, out var targetAgent))
        {
            return SwarmHandoffResult.Fail($"Target agent '{request.ToAgent}' not found in runtime.", trail);
        }

        // Build context for delegated agent
        var contextPrompt = $"[HANDOFF from {request.FromAgent}]\nReason: {request.Reason}\nPayload: {request.Payload}";
        var targetResult = await _runtime.ReActEngine.SolveAsync(targetAgent, contextPrompt, _runtime.GlobalScope, ct);

        if (!request.ReturnOnComplete)
        {
            return SwarmHandoffResult.Ok(targetResult.FinalAnswer ?? "", trail);
        }

        // Return-on-complete: Feed findings back to the originating agent to synthesize
        if (_runtime.AgentInstances.TryGetValue(request.FromAgent, out var fromAgent))
        {
            var returnPrompt = $"[DELEGATE RESULT from {request.ToAgent}]\nOutput: {targetResult.FinalAnswer}\nSynthesize final answer based on these findings.";
            var finalSynthesis = await _runtime.ReActEngine.SolveAsync(fromAgent, returnPrompt, _runtime.GlobalScope, ct);
            return SwarmHandoffResult.Ok(finalSynthesis.FinalAnswer ?? "", trail);
        }

        return SwarmHandoffResult.Ok(targetResult.FinalAnswer ?? "", trail);
    }
}

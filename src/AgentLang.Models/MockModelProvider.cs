using System.Diagnostics;

namespace AgentLang.Models;

public sealed class MockModelProvider : IModelProvider
{
    public string ProviderId => "mock";

    public Func<ModelRequest, string>? CustomResponder { get; set; }

    public bool CanHandle(string modelName) =>
        modelName.StartsWith("mock", StringComparison.OrdinalIgnoreCase) ||
        modelName.Equals("test", StringComparison.OrdinalIgnoreCase);

    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        string content;
        if (CustomResponder != null)
        {
            content = CustomResponder(request);
        }
        else
        {
            content = GenerateDeterministicResponse(request.Prompt);
        }

        sw.Stop();
        return Task.FromResult(new ModelResponse(
            Content: content,
            Model: request.ModelName,
            TokensUsed: Math.Max(10, content.Length / 4),
            Latency: sw.Elapsed,
            Success: true
        ));
    }

    private static string GenerateDeterministicResponse(string prompt)
    {
        string p = prompt.ToLowerInvariant();
        if (p.Contains("trends"))
            return "Trend Analysis: Agentic AI frameworks and autonomous agent architectures are growing rapidly in 2026.";
        if (p.Contains("risks"))
            return "Risk Evaluation: Uncontrolled tool execution and prompt injection are critical security vectors.";
        if (p.Contains("synthesize") || p.Contains("combined"))
            return "Executive Synthesis: Combining high adoption with strict permission controls creates resilient agent systems.";
        if (p.Contains("research") || p.Contains("question") || p.Contains("agentlang"))
            return "Research Findings: AgentLang is an agent-native programming language designed for robust autonomous systems.";

        return $"Analysis of '{prompt.Trim()}': Completed successfully with verified factual confidence.";
    }
}

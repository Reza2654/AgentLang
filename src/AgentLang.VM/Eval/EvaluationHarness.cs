using System.Diagnostics;
using System.Text.Json;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.VM.Memory;

namespace AgentLang.VM.Eval;

public sealed class BenchmarkCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Input { get; set; } = string.Empty;
    public string? ExpectedOutput { get; set; }
    public List<string> ExpectedTools { get; set; } = new();
    public string? Criteria { get; set; }
}

public sealed class BenchmarkDataset
{
    public string Name { get; set; } = "DefaultDataset";
    public List<BenchmarkCase> Cases { get; set; } = new();

    public static BenchmarkDataset FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var dataset = new BenchmarkDataset();

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var bc = JsonSerializer.Deserialize<BenchmarkCase>(item.GetRawText());
                if (bc != null) dataset.Cases.Add(bc);
            }
        }
        else if (doc.RootElement.TryGetProperty("cases", out var casesProp) && casesProp.ValueKind == JsonValueKind.Array)
        {
            if (doc.RootElement.TryGetProperty("name", out var n)) dataset.Name = n.GetString() ?? dataset.Name;
            foreach (var item in casesProp.EnumerateArray())
            {
                var bc = JsonSerializer.Deserialize<BenchmarkCase>(item.GetRawText());
                if (bc != null) dataset.Cases.Add(bc);
            }
        }

        return dataset;
    }
}

public sealed record CaseEvaluationResult(
    string CaseId,
    bool Passed,
    double Score,
    string ActualOutput,
    IReadOnlyList<string> ActualTools,
    TimeSpan Latency,
    string Notes);

public sealed class BenchmarkScorecard
{
    public string AgentName { get; set; } = string.Empty;
    public string DatasetName { get; set; } = string.Empty;
    public int TotalCases { get; set; }
    public int PassedCases { get; set; }
    public int FailedCases => TotalCases - PassedCases;
    public double PassRate => TotalCases > 0 ? (double)PassedCases / TotalCases : 0.0;
    public double AverageLatencyMs { get; set; }
    public List<CaseEvaluationResult> Results { get; set; } = new();

    public string ToMarkdownReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Benchmark Scorecard: {AgentName}");
        sb.AppendLine($"**Dataset:** {DatasetName} | **Date:** {DateTime.UtcNow:u}");
        sb.AppendLine($"**Overall Score:** {PassRate * 100:F1}% ({PassedCases}/{TotalCases} passed) | **Avg Latency:** {AverageLatencyMs:F1}ms\n");
        sb.AppendLine("| Case ID | Status | Score | Latency | Notes |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :--- |");

        foreach (var r in Results)
        {
            string status = r.Passed ? "✅ PASS" : "❌ FAIL";
            sb.AppendLine($"| `{r.CaseId}` | {status} | {r.Score * 100:F0}% | {r.Latency.TotalMilliseconds:F0}ms | {r.Notes} |");
        }

        return sb.ToString();
    }
}

public sealed class AgentBenchmarkRunner
{
    public async Task<BenchmarkScorecard> RunBenchmarkAsync(
        AgentValue agent,
        BenchmarkDataset dataset,
        AgentLangRuntime runtime,
        double similarityThreshold = 0.70,
        CancellationToken ct = default)
    {
        var scorecard = new BenchmarkScorecard
        {
            AgentName = agent.Name,
            DatasetName = dataset.Name,
            TotalCases = dataset.Cases.Count
        };

        double totalMs = 0;

        foreach (var tc in dataset.Cases)
        {
            var sw = Stopwatch.StartNew();
            var solveResult = await runtime.ReActEngine.SolveAsync(agent, tc.Input, runtime.GlobalScope, ct);
            sw.Stop();

            totalMs += sw.ElapsedMilliseconds;

            var actualOutput = solveResult.FinalAnswer ?? "";
            var actualTools = solveResult.Steps
                .Where(s => !string.IsNullOrEmpty(s.ToolName))
                .Select(s => s.ToolName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Evaluate Tool Precision
            bool toolsSatisfied = true;
            if (tc.ExpectedTools.Count > 0)
            {
                toolsSatisfied = tc.ExpectedTools.All(exp => actualTools.Contains(exp, StringComparer.OrdinalIgnoreCase));
            }

            // Evaluate Output Similarity
            double outputSim = 1.0;
            if (!string.IsNullOrEmpty(tc.ExpectedOutput))
            {
                var vecA = HybridDocument.ComputeSimpleEmbedding(actualOutput);
                var vecB = HybridDocument.ComputeSimpleEmbedding(tc.ExpectedOutput);

                double dot = 0;
                for (int i = 0; i < vecA.Length; i++) dot += vecA[i] * vecB[i];
                outputSim = Math.Clamp(dot, 0.0, 1.0);
            }

            bool passed = toolsSatisfied && (outputSim >= similarityThreshold || actualOutput.Contains(tc.ExpectedOutput ?? "", StringComparison.OrdinalIgnoreCase));
            if (passed) scorecard.PassedCases++;

            string notes = passed
                ? $"Criteria met (similarity: {outputSim:F2})"
                : (!toolsSatisfied ? $"Missing expected tools: [{string.Join(", ", tc.ExpectedTools)}]" : $"Similarity {outputSim:F2} below threshold {similarityThreshold:F2}");

            scorecard.Results.Add(new CaseEvaluationResult(
                tc.Id,
                passed,
                outputSim,
                actualOutput,
                actualTools,
                sw.Elapsed,
                notes));
        }

        scorecard.AverageLatencyMs = dataset.Cases.Count > 0 ? totalMs / dataset.Cases.Count : 0.0;
        return scorecard;
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace AgentLang.VM.Planning;

// =============================================================================
// REWOO: REASONING WITHOUT OBSERVATION
// =============================================================================

public sealed record ReWooStep(
    string EvidenceVar,
    string Description,
    string ToolName,
    Dictionary<string, string> Arguments
);

public sealed class ReWooPlan
{
    public string Goal { get; }
    public List<ReWooStep> Steps { get; }

    public ReWooPlan(string goal, IEnumerable<ReWooStep>? steps = null)
    {
        Goal = goal;
        Steps = steps?.ToList() ?? [];
    }
}

public sealed class ReWooResult
{
    public required string Goal { get; init; }
    public Dictionary<string, string> Evidence { get; init; } = [];
    public required string FinalOutput { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class ReWooEngine
{
    public async Task<ReWooResult> ExecuteAsync(
        ReWooPlan plan,
        Func<string, Dictionary<string, string>, CancellationToken, Task<string>> toolExecutor,
        Func<string, Dictionary<string, string>, CancellationToken, Task<string>>? solver = null,
        CancellationToken ct = default)
    {
        var start = DateTime.UtcNow;
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Worker phase: execute deterministic tool calls, substituting variable placeholders
        foreach (var step in plan.Steps)
        {
            var resolvedArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in step.Arguments)
            {
                string substituted = v;
                foreach (var (eKey, eVal) in evidence)
                {
                    if (substituted.Contains(eKey, StringComparison.OrdinalIgnoreCase))
                    {
                        substituted = substituted.Replace(eKey, eVal, StringComparison.OrdinalIgnoreCase);
                    }
                }
                resolvedArgs[k] = substituted;
            }

            var output = await toolExecutor(step.ToolName, resolvedArgs, ct);
            evidence[step.EvidenceVar] = output;
        }

        // 2. Solver phase: assemble evidence into final synthesis
        string finalOutput;
        if (solver != null)
        {
            finalOutput = await solver(plan.Goal, evidence, ct);
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Solved: {plan.Goal}\n");
            foreach (var (k, v) in evidence)
            {
                sb.AppendLine($"{k}: {v}");
            }
            finalOutput = sb.ToString().TrimEnd();
        }

        return new ReWooResult
        {
            Goal = plan.Goal,
            Evidence = evidence,
            FinalOutput = finalOutput,
            Duration = DateTime.UtcNow - start
        };
    }
}

// =============================================================================
// TREE OF THOUGHTS (ToT)
// =============================================================================

public sealed class ThoughtNode
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Content { get; }
    public double Score { get; set; }
    public int Depth { get; }
    public ThoughtNode? Parent { get; }
    public List<ThoughtNode> Children { get; } = [];

    public ThoughtNode(string content, double score = 0.0, int depth = 0, ThoughtNode? parent = null)
    {
        Content = content;
        Score = score;
        Depth = depth;
        Parent = parent;
    }
}

public sealed class TreeOfThoughtsEngine
{
    public int MaxDepth { get; set; } = 3;
    public int BeamWidth { get; set; } = 3;
    public double PruningThreshold { get; set; } = 0.40;

    public async Task<List<ThoughtNode>> SearchAsync(
        string rootProblem,
        Func<ThoughtNode, CancellationToken, Task<List<string>>> thoughtGenerator,
        Func<ThoughtNode, string, CancellationToken, Task<double>> thoughtEvaluator,
        CancellationToken ct = default)
    {
        var root = new ThoughtNode(rootProblem, 1.0, 0);
        var currentLevel = new List<ThoughtNode> { root };

        for (int depth = 1; depth <= MaxDepth; depth++)
        {
            var candidateNodes = new List<ThoughtNode>();

            foreach (var parent in currentLevel)
            {
                var candidateTexts = await thoughtGenerator(parent, ct);
                foreach (var text in candidateTexts)
                {
                    double score = await thoughtEvaluator(parent, text, ct);
                    if (score >= PruningThreshold)
                    {
                        var child = new ThoughtNode(text, score, depth, parent);
                        parent.Children.Add(child);
                        candidateNodes.Add(child);
                    }
                }
            }

            if (candidateNodes.Count == 0)
            {
                // No viable thoughts survived evaluation
                break;
            }

            // Beam search: keep top-scoring thoughts
            currentLevel = candidateNodes
                .OrderByDescending(n => n.Score)
                .Take(BeamWidth)
                .ToList();
        }

        // Find the highest-scoring leaf in the search tree
        var bestNode = currentLevel.OrderByDescending(n => n.Score).FirstOrDefault() ?? root;

        // Trace path back to root
        var path = new List<ThoughtNode>();
        var curr = bestNode;
        while (curr != null)
        {
            path.Insert(0, curr);
            curr = curr.Parent;
        }

        return path;
    }
}

using System.Text.Json;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;

namespace AgentLang.VM.Swarm;

public enum VotingStrategy
{
    Majority,
    Weighted,
    Unanimous
}

public sealed record AgentVote(
    string AgentName,
    string Choice,
    double Confidence = 1.0,
    string Reasoning = "",
    double Weight = 1.0);

public sealed class ConsensusResult
{
    public string Winner { get; set; } = string.Empty;
    public bool IsConsensusReached { get; set; }
    public Dictionary<string, double> VoteBreakdown { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AgentVote> DetailedVotes { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public sealed class AgentConsensusEngine
{
    public static ConsensusResult Tally(IEnumerable<AgentVote> votes, VotingStrategy strategy, double majorityThreshold = 0.5)
    {
        var voteList = votes.ToList();
        var result = new ConsensusResult { DetailedVotes = voteList };

        if (voteList.Count == 0)
        {
            result.Winner = "None";
            result.IsConsensusReached = false;
            result.Summary = "No votes cast.";
            return result;
        }

        var tally = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var vote in voteList)
        {
            double points = strategy switch
            {
                VotingStrategy.Weighted => vote.Weight * Math.Clamp(vote.Confidence, 0.1, 1.0),
                _ => 1.0
            };

            tally[vote.Choice] = tally.GetValueOrDefault(vote.Choice, 0) + points;
        }

        result.VoteBreakdown = tally;
        double totalPoints = tally.Values.Sum();
        var topChoice = tally.OrderByDescending(kv => kv.Value).First();

        switch (strategy)
        {
            case VotingStrategy.Unanimous:
                bool isUnanimous = tally.Count == 1;
                result.Winner = isUnanimous ? topChoice.Key : "Deadlock";
                result.IsConsensusReached = isUnanimous;
                result.Summary = isUnanimous
                    ? $"Unanimous consensus reached on '{topChoice.Key}' ({voteList.Count}/{voteList.Count} votes)."
                    : $"Unanimous consensus failed. Votes split across {tally.Count} options.";
                break;

            case VotingStrategy.Weighted:
            case VotingStrategy.Majority:
            default:
                double ratio = totalPoints > 0 ? topChoice.Value / totalPoints : 0;
                bool isMajority = ratio >= majorityThreshold;
                result.Winner = isMajority ? topChoice.Key : "Tie/Deadlock";
                result.IsConsensusReached = isMajority;
                result.Summary = isMajority
                    ? $"Consensus reached on '{topChoice.Key}' with {topChoice.Value:F1}/{totalPoints:F1} points ({ratio * 100:F0}%)."
                    : $"Consensus not reached. Top choice '{topChoice.Key}' only received {ratio * 100:F0}% (threshold is {majorityThreshold * 100:F0}%).";
                break;
        }

        return result;
    }

    public async Task<ConsensusResult> ReachConsensusAsync(
        AgentLangRuntime runtime,
        IEnumerable<string> agentNames,
        string question,
        IReadOnlyList<string> options,
        VotingStrategy strategy = VotingStrategy.Majority,
        CancellationToken ct = default)
    {
        var votes = new List<AgentVote>();
        var optionsText = string.Join(", ", options);

        foreach (var name in agentNames)
        {
            if (!runtime.AgentInstances.TryGetValue(name, out var agent)) continue;

            var prompt = $"[CONSENSUS VOTE]\nQuestion: {question}\nOptions: [{optionsText}]\nRespond in JSON format: {{\"choice\": \"<one of options>\", \"confidence\": 0.9, \"reasoning\": \"brief rationale\"}}";
            var solveResult = await runtime.ReActEngine.SolveAsync(agent, prompt, runtime.GlobalScope, ct);
            var output = solveResult.FinalAnswer ?? "";

            string choice = options.FirstOrDefault() ?? "Unknown";
            double confidence = 0.8;
            string reasoning = output;

            try
            {
                // Attempt JSON extraction
                var startIdx = output.IndexOf('{');
                var endIdx = output.LastIndexOf('}');
                if (startIdx >= 0 && endIdx > startIdx)
                {
                    var json = output.Substring(startIdx, endIdx - startIdx + 1);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("choice", out var ch)) choice = ch.GetString() ?? choice;
                    if (doc.RootElement.TryGetProperty("confidence", out var conf)) confidence = conf.GetDouble();
                    if (doc.RootElement.TryGetProperty("reasoning", out var r)) reasoning = r.GetString() ?? reasoning;
                }
                else
                {
                    // Fallback substring search
                    foreach (var opt in options)
                    {
                        if (output.Contains(opt, StringComparison.OrdinalIgnoreCase))
                        {
                            choice = opt;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            votes.Add(new AgentVote(name, choice, confidence, reasoning));
        }

        return Tally(votes, strategy);
    }
}

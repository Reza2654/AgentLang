using AgentLang.Runtime;
using AgentLang.Runtime.Values;

namespace AgentLang.VM.Swarm;

public sealed record DebateRound(
    int Round,
    string Speaker,
    string Argument,
    string? RebuttalTo = null);

public sealed class DebateResult
{
    public List<DebateRound> Rounds { get; set; } = new();
    public string Verdict { get; set; } = string.Empty;
    public string JudgeName { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
}

public sealed class DebateEngine
{
    public async Task<DebateResult> ConductDebateAsync(
        AgentLangRuntime runtime,
        string proponentName,
        string opponentName,
        string? judgeName,
        string topic,
        int maxRounds = 2,
        CancellationToken ct = default)
    {
        var result = new DebateResult();

        if (!runtime.AgentInstances.TryGetValue(proponentName, out var proponent) ||
            !runtime.AgentInstances.TryGetValue(opponentName, out var opponent))
        {
            result.Verdict = "Error";
            result.Rationale = "Debating agents not found in runtime.";
            return result;
        }

        string lastArgument = "";

        for (int r = 1; r <= maxRounds; r++)
        {
            // Proponent Turn
            var propPrompt = r == 1
                ? $"[DEBATE ROUND {r}] Topic: '{topic}'. Present your opening argument in favor."
                : $"[DEBATE ROUND {r}] Topic: '{topic}'. Rebut the opponent's prior argument: '{lastArgument}'. Strengthen your position.";

            var propResult = await runtime.ReActEngine.SolveAsync(proponent, propPrompt, runtime.GlobalScope, ct);
            var propAnswer = propResult.FinalAnswer ?? "";
            result.Rounds.Add(new DebateRound(r, proponentName, propAnswer, r > 1 ? lastArgument : null));
            lastArgument = propAnswer;

            // Opponent Turn
            var oppPrompt = $"[DEBATE ROUND {r}] Topic: '{topic}'. Rebut the proponent's argument: '{lastArgument}'. Present counter-evidence.";
            var oppResult = await runtime.ReActEngine.SolveAsync(opponent, oppPrompt, runtime.GlobalScope, ct);
            var oppAnswer = oppResult.FinalAnswer ?? "";
            result.Rounds.Add(new DebateRound(r, opponentName, oppAnswer, lastArgument));
            lastArgument = oppAnswer;
        }

        // Judge verdict
        string judge = judgeName ?? "Synthesizer";
        if (judgeName != null && runtime.AgentInstances.TryGetValue(judgeName, out var judgeAgent))
        {
            result.JudgeName = judgeName;
            var transcript = string.Join("\n\n", result.Rounds.Select(rd => $"[Round {rd.Round}] {rd.Speaker}: {rd.Argument}"));
            var judgePrompt = $"[DEBATE JUDGMENT]\nTopic: '{topic}'\nTranscript:\n{transcript}\n\nDeliver the final verdict and rationale.";
            var judgeResult = await runtime.ReActEngine.SolveAsync(judgeAgent, judgePrompt, runtime.GlobalScope, ct);
            result.Verdict = judgeResult.FinalAnswer ?? "";
            result.Rationale = $"Judgment delivered by {judgeName} after {maxRounds} debate rounds.";
        }
        else
        {
            result.JudgeName = "AutonomousSynthesizer";
            result.Verdict = $"Debate concluded across {maxRounds} rounds between {proponentName} and {opponentName}.";
            result.Rationale = "Synthesis based on round transcripts.";
        }

        return result;
    }
}

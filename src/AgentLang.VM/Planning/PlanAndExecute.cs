using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentLang.VM.Planning;

public enum PlanStepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

public sealed record PlanStep
{
    public required int StepId { get; init; }
    public required string Description { get; init; }
    public string? ToolName { get; init; }
    public Dictionary<string, object?>? Arguments { get; init; }
    public List<int> DependsOn { get; init; } = [];
    public PlanStepStatus Status { get; set; } = PlanStepStatus.Pending;
    public string? Result { get; set; }
    public string? Error { get; set; }
}

public sealed class TaskPlan
{
    public string Goal { get; }
    public List<PlanStep> Steps { get; }

    public bool IsComplete => Steps.All(s => s.Status is PlanStepStatus.Completed or PlanStepStatus.Skipped);
    public bool HasFailed => Steps.Any(s => s.Status == PlanStepStatus.Failed);

    public TaskPlan(string goal, IEnumerable<PlanStep>? steps = null)
    {
        Goal = goal;
        Steps = steps?.ToList() ?? [];
    }

    public List<PlanStep> GetExecutableSteps()
    {
        var completedIds = Steps
            .Where(s => s.Status == PlanStepStatus.Completed)
            .Select(s => s.StepId)
            .ToHashSet();

        return Steps
            .Where(s => s.Status == PlanStepStatus.Pending && s.DependsOn.All(dep => completedIds.Contains(dep)))
            .ToList();
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Plan: {Goal}");
        foreach (var s in Steps)
        {
            string statusIcon = s.Status switch
            {
                PlanStepStatus.Completed => "[x]",
                PlanStepStatus.Running => "[~]",
                PlanStepStatus.Failed => "[!]",
                PlanStepStatus.Skipped => "[-]",
                _ => "[ ]"
            };

            string toolInfo = s.ToolName != null ? $" (Tool: `{s.ToolName}`)" : "";
            string depInfo = s.DependsOn.Count > 0 ? $" [Depends on: {string.Join(", ", s.DependsOn)}]" : "";
            sb.AppendLine($"{statusIcon} {s.StepId}. {s.Description}{toolInfo}{depInfo}");
            if (!string.IsNullOrEmpty(s.Result))
            {
                sb.AppendLine($"   → Result: {s.Result}");
            }
            if (!string.IsNullOrEmpty(s.Error))
            {
                sb.AppendLine($"   → Error: {s.Error}");
            }
        }
        return sb.ToString().TrimEnd();
    }
}

public interface IPlanner
{
    Task<TaskPlan> CreatePlanAsync(string goal, IEnumerable<string> availableTools, CancellationToken ct = default);
    Task<TaskPlan> ReplanAsync(TaskPlan currentPlan, int failedStepId, string errorReason, CancellationToken ct = default);
}

public sealed class HeuristicPlanner : IPlanner
{
    public Task<TaskPlan> CreatePlanAsync(string goal, IEnumerable<string> availableTools, CancellationToken ct = default)
    {
        var toolsList = availableTools.ToList();
        var steps = new List<PlanStep>();

        if (goal.Contains("search", StringComparison.OrdinalIgnoreCase) || goal.Contains("find", StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new PlanStep
            {
                StepId = 1,
                Description = "Gather relevant documents and search query results",
                ToolName = toolsList.FirstOrDefault(t => t.Contains("search", StringComparison.OrdinalIgnoreCase)) ?? "web_search",
                Arguments = new() { ["query"] = goal }
            });

            steps.Add(new PlanStep
            {
                StepId = 2,
                Description = "Analyze and extract key information from retrieved results",
                DependsOn = [1]
            });

            steps.Add(new PlanStep
            {
                StepId = 3,
                Description = "Synthesize final answer and format output",
                DependsOn = [2]
            });
        }
        else
        {
            steps.Add(new PlanStep
            {
                StepId = 1,
                Description = "Deconstruct initial objective and parameters"
            });

            steps.Add(new PlanStep
            {
                StepId = 2,
                Description = "Execute required core domain logic",
                DependsOn = [1]
            });

            steps.Add(new PlanStep
            {
                StepId = 3,
                Description = "Review and finalize results",
                DependsOn = [2]
            });
        }

        return Task.FromResult(new TaskPlan(goal, steps));
    }

    public Task<TaskPlan> ReplanAsync(TaskPlan currentPlan, int failedStepId, string errorReason, CancellationToken ct = default)
    {
        var newSteps = new List<PlanStep>();
        foreach (var s in currentPlan.Steps)
        {
            if (s.StepId < failedStepId)
            {
                newSteps.Add(s);
            }
            else if (s.StepId == failedStepId)
            {
                // Fallback attempt
                newSteps.Add(new PlanStep
                {
                    StepId = failedStepId,
                    Description = $"{s.Description} [Remediated: alternative approach]",
                    DependsOn = s.DependsOn,
                    Status = PlanStepStatus.Pending
                });
            }
            else
            {
                newSteps.Add(new PlanStep
                {
                    StepId = s.StepId,
                    Description = s.Description,
                    ToolName = s.ToolName,
                    Arguments = s.Arguments,
                    DependsOn = s.DependsOn,
                    Status = PlanStepStatus.Pending
                });
            }
        }

        return Task.FromResult(new TaskPlan(currentPlan.Goal, newSteps));
    }
}

public sealed class PlanExecutionResult
{
    public required TaskPlan Plan { get; init; }
    public TimeSpan Duration { get; init; }
    public int ReplansCount { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed class PlanAndExecuteEngine
{
    private readonly IPlanner _planner;
    public bool EnableReplanning { get; set; } = true;
    public int MaxReplans { get; set; } = 3;

    public PlanAndExecuteEngine(IPlanner? planner = null)
    {
        _planner = planner ?? new HeuristicPlanner();
    }

    public async Task<PlanExecutionResult> ExecuteAsync(
        string goal,
        Func<PlanStep, CancellationToken, Task<string>> stepExecutor,
        IEnumerable<string>? availableTools = null,
        CancellationToken ct = default)
    {
        var start = DateTime.UtcNow;
        var plan = await _planner.CreatePlanAsync(goal, availableTools ?? [], ct);
        int replans = 0;

        while (!plan.IsComplete && !ct.IsCancellationRequested)
        {
            var executableSteps = plan.GetExecutableSteps();
            if (executableSteps.Count == 0)
            {
                // No remaining executable steps and not complete -> deadlock or unhandled failure
                break;
            }

            // Run executable steps (independent dependencies can execute concurrently)
            var tasks = executableSteps.Select(async step =>
            {
                step.Status = PlanStepStatus.Running;
                try
                {
                    var result = await stepExecutor(step, ct);
                    step.Result = result;
                    step.Status = PlanStepStatus.Completed;
                    return (Step: step, Success: true, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    step.Error = ex.Message;
                    step.Status = PlanStepStatus.Failed;
                    return (Step: step, Success: false, Error: (string?)ex.Message);
                }
            });

            var executionBatch = await Task.WhenAll(tasks);

            // Check if any step failed
            var failure = executionBatch.FirstOrDefault(r => !r.Success);
            if (failure.Step != null)
            {
                if (EnableReplanning && replans < MaxReplans)
                {
                    replans++;
                    plan = await _planner.ReplanAsync(plan, failure.Step.StepId, failure.Error ?? "Execution failure", ct);
                    continue;
                }
                else
                {
                    break;
                }
            }
        }

        var duration = DateTime.UtcNow - start;
        string summary = plan.IsComplete
            ? $"Goal successfully achieved in {plan.Steps.Count} steps with {replans} replans."
            : $"Execution stopped. Completed: {plan.Steps.Count(s => s.Status == PlanStepStatus.Completed)}/{plan.Steps.Count} steps.";

        return new PlanExecutionResult
        {
            Plan = plan,
            Duration = duration,
            ReplansCount = replans,
            Summary = summary
        };
    }
}

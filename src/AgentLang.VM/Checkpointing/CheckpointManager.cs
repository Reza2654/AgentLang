using AgentLang.Runtime.Values;

namespace AgentLang.VM.Checkpointing;

public sealed class CheckpointManager
{
    public ICheckpointStore Store { get; }

    public CheckpointManager(ICheckpointStore? store = null)
    {
        Store = store ?? new MemoryCheckpointStore();
    }

    public async Task<ExecutionCheckpoint> CreateCheckpointAsync(
        AgentVirtualMachine vm,
        string workflowName = "DefaultWorkflow",
        int stepIndex = 0,
        string status = "Active",
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var checkpoint = new ExecutionCheckpoint
        {
            WorkflowName = workflowName,
            StepIndex = stepIndex,
            Status = status,
            Metadata = metadata ?? new()
        };

        // Snapshot VM global scope variables
        foreach (var kvp in vm.Runtime.GlobalScope.GetLocalVariables())
        {
            checkpoint.StateVariables[kvp.Key] = kvp.Value;
        }

        // Snapshot Agent histories
        foreach (var (name, agent) in vm.Runtime.AgentInstances)
        {
            var historyLines = agent.History.Select(h => $"{h.Role}: {h.Content}").ToList();
            checkpoint.AgentHistories[name] = historyLines;
        }

        await Store.SaveAsync(checkpoint, ct);
        return checkpoint;
    }

    public async Task<bool> RestoreCheckpointAsync(
        AgentVirtualMachine vm,
        string checkpointId,
        CancellationToken ct = default)
    {
        var checkpoint = await Store.GetAsync(checkpointId, ct);
        if (checkpoint == null) return false;

        // Restore global scope variables
        foreach (var (k, v) in checkpoint.StateVariables)
        {
            vm.Runtime.GlobalScope.SetLocal(k, v);
        }

        // Restore Agent histories
        foreach (var (name, historyLines) in checkpoint.AgentHistories)
        {
            if (vm.Runtime.AgentInstances.TryGetValue(name, out var agent))
            {
                agent.History.Clear();
                foreach (var line in historyLines)
                {
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var role = line[..colonIdx].Trim();
                        var content = line[(colonIdx + 1)..].Trim();
                        agent.History.Add(new ChatMessage(role, content));
                    }
                    else
                    {
                        agent.History.Add(new ChatMessage("user", line));
                    }
                }
            }
        }

        return true;
    }
}

namespace AgentLang.VM.Durable;

public sealed class DurableStepContext
{
    public DurableExecutionContext Context { get; }
    public ExecutionSignal? LastSignal => Context.ReceivedSignals.LastOrDefault();
    public bool IsSuspensionRequested { get; private set; }

    public DurableStepContext(DurableExecutionContext context)
    {
        Context = context;
    }

    public T? GetVariable<T>(string name, T? defaultValue = default)
    {
        if (Context.StateVariables.TryGetValue(name, out var val))
        {
            if (val is T typed) return typed;
            if (val is System.Text.Json.JsonElement je)
            {
                try
                {
                    if (typeof(T) == typeof(string)) return (T)(object)je.GetString()!;
                    if (typeof(T) == typeof(int) && je.TryGetInt32(out int i)) return (T)(object)i;
                    if (typeof(T) == typeof(long) && je.TryGetInt64(out long l)) return (T)(object)l;
                    if (typeof(T) == typeof(double) && je.TryGetDouble(out double d)) return (T)(object)d;
                    if (typeof(T) == typeof(bool) && (je.ValueKind == System.Text.Json.JsonValueKind.True || je.ValueKind == System.Text.Json.JsonValueKind.False)) return (T)(object)je.GetBoolean();
                    return System.Text.Json.JsonSerializer.Deserialize<T>(je.GetRawText()) ?? defaultValue;
                }
                catch
                {
                    return defaultValue;
                }
            }
            if (val != null)
            {
                try
                {
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
        }
        return defaultValue;
    }

    public void SetVariable(string name, object? value)
    {
        Context.StateVariables[name] = value;
    }

    public void Suspend(SuspensionReason reason, string? signalName = null)
    {
        IsSuspensionRequested = true;
        Context.Status = DurableStatus.Suspended;
        Context.SuspensionReason = reason;
        Context.WaitingForSignalName = signalName;
    }

    public void Complete(object? finalOutput = null)
    {
        Context.Status = DurableStatus.Completed;
        Context.FinalOutput = finalOutput;
        Context.SuspensionReason = null;
        Context.WaitingForSignalName = null;
    }
}

public sealed class DurableExecutionEngine
{
    public IDurableStateStore Store { get; }

    public DurableExecutionEngine(IDurableStateStore? store = null)
    {
        Store = store ?? new MemoryDurableStateStore();
    }

    public async Task<DurableExecutionContext> StartAsync(
        string workflowName,
        Func<DurableStepContext, Task> stepExecutor,
        Dictionary<string, object?>? initialVars = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var context = new DurableExecutionContext
        {
            WorkflowName = workflowName,
            Status = DurableStatus.Running,
            Metadata = metadata ?? new(),
            StateVariables = initialVars != null ? new Dictionary<string, object?>(initialVars) : new()
        };

        var stepContext = new DurableStepContext(context);

        try
        {
            await stepExecutor(stepContext);

            if (stepContext.IsSuspensionRequested)
            {
                context.Status = DurableStatus.Suspended;
            }
            else if (context.Status == DurableStatus.Running)
            {
                context.Status = DurableStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            context.Status = DurableStatus.Faulted;
            context.ErrorMessage = ex.Message;
        }

        context.StepIndex++;
        await Store.SaveAsync(context, ct);
        return context;
    }

    public async Task<DurableExecutionContext> ResumeAsync(
        string executionId,
        ExecutionSignal signal,
        Func<DurableStepContext, Task> stepExecutor,
        CancellationToken ct = default)
    {
        var context = await Store.GetAsync(executionId, ct)
            ?? throw new KeyNotFoundException($"Durable execution '{executionId}' not found.");

        if (context.Status != DurableStatus.Suspended)
        {
            throw new InvalidOperationException($"Cannot resume execution '{executionId}' with status '{context.Status}'. Only Suspended executions can be resumed.");
        }

        if (!string.IsNullOrEmpty(context.WaitingForSignalName) &&
            !string.Equals(context.WaitingForSignalName, signal.SignalName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Execution '{executionId}' is waiting for signal '{context.WaitingForSignalName}', but received '{signal.SignalName}'.");
        }

        context.ReceivedSignals.Add(signal);
        context.Status = DurableStatus.Running;
        context.SuspensionReason = null;
        context.WaitingForSignalName = null;

        var stepContext = new DurableStepContext(context);

        try
        {
            await stepExecutor(stepContext);

            if (stepContext.IsSuspensionRequested)
            {
                context.Status = DurableStatus.Suspended;
            }
            else if (context.Status == DurableStatus.Running)
            {
                context.Status = DurableStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            context.Status = DurableStatus.Faulted;
            context.ErrorMessage = ex.Message;
        }

        context.StepIndex++;
        await Store.SaveAsync(context, ct);
        return context;
    }

    public async Task<DurableExecutionContext?> GetAsync(string executionId, CancellationToken ct = default) =>
        await Store.GetAsync(executionId, ct);

    public async Task<IReadOnlyList<DurableExecutionContext>> ListAsync(DurableStatus? statusFilter = null, CancellationToken ct = default) =>
        await Store.ListAsync(statusFilter, ct);

    public async Task<bool> DeleteAsync(string executionId, CancellationToken ct = default) =>
        await Store.DeleteAsync(executionId, ct);
}

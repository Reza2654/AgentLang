using AgentLang.AST;
using AgentLang.Lexer;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;

namespace AgentLang.VM;

public sealed class AgentVirtualMachine
{
    public VmOptions Options { get; }
    public AgentLangRuntime Runtime { get; }
    public long CyclesExecuted { get; private set; }
    public string Status { get; private set; } = "Ready";

    public AgentVirtualMachine(VmOptions? options = null, AgentLangRuntime? runtime = null)
    {
        Options = options ?? new VmOptions();
        Runtime = runtime ?? new AgentLangRuntime();
        ConfigureRuntime();
    }

    private void ConfigureRuntime()
    {
        Runtime.StepHook = (stmt, scope) =>
        {
            CyclesExecuted++;
            if (CyclesExecuted > Options.MaxInstructions)
            {
                Status = "Halted:CycleLimitExceeded";
                throw new VmCycleLimitExceededException(Options.MaxInstructions, CyclesExecuted);
            }
        };

        Runtime.CapabilityCheck = (capability) =>
        {
            if (!Options.EnableSandboxing) return true;
            if (Options.AllowedCapabilities.Contains("*")) return true;

            foreach (var allowed in Options.AllowedCapabilities)
            {
                if (allowed == "*" ||
                    allowed.Equals(capability, StringComparison.OrdinalIgnoreCase) ||
                    (allowed.EndsWith(".*") && capability.StartsWith(allowed[..^2] + ".", StringComparison.OrdinalIgnoreCase)) ||
                    (allowed.EndsWith("*") && capability.StartsWith(allowed[..^1], StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            Status = "Halted:SandboxViolation";
            throw new VmSandboxViolationException(capability);
        };
    }

    public async Task ExecuteAsync(string sourceCode, string filePath = "<vm>", CancellationToken ct = default)
    {
        Status = "Running";
        using var cts = Options.ExecutionTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, new CancellationTokenSource(Options.ExecutionTimeout.Value).Token)
            : null;
        var effectiveToken = cts?.Token ?? ct;

        try
        {
            var sourceText = new SourceText(sourceCode, filePath);
            var lexer = new Lexer.Lexer(sourceText);
            var tokens = lexer.TokenizeAll();
            var parser = new Parser.Parser(tokens, sourceText, new DiagnosticBag());
            var program = parser.ParseProgram();

            Runtime.CurrentSourceFile = filePath;
            await Runtime.ExecuteProgramAsync(program, effectiveToken);
            Status = "Completed";
        }
        catch (VmCycleLimitExceededException)
        {
            Status = "Halted:CycleLimitExceeded";
            throw;
        }
        catch (VmSandboxViolationException)
        {
            Status = "Halted:SandboxViolation";
            throw;
        }
        catch
        {
            if (Status == "Running") Status = "Faulted";
            throw;
        }
    }

    public async Task ExecuteAsync(ProgramNode program, CancellationToken ct = default)
    {
        Status = "Running";
        using var cts = Options.ExecutionTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, new CancellationTokenSource(Options.ExecutionTimeout.Value).Token)
            : null;
        var effectiveToken = cts?.Token ?? ct;

        try
        {
            await Runtime.ExecuteProgramAsync(program, effectiveToken);
            Status = "Completed";
        }
        catch (VmCycleLimitExceededException)
        {
            Status = "Halted:CycleLimitExceeded";
            throw;
        }
        catch (VmSandboxViolationException)
        {
            Status = "Halted:SandboxViolation";
            throw;
        }
        catch
        {
            if (Status == "Running") Status = "Faulted";
            throw;
        }
    }

    public VmSnapshot CreateSnapshot(string? label = null)
    {
        var snapshot = new VmSnapshot
        {
            Label = label,
            InstructionCount = CyclesExecuted,
            ExecutionStatus = Status,
            GlobalVariables = new Dictionary<string, object?>(Runtime.GlobalScope.GetLocalVariables(), StringComparer.Ordinal)
        };

        foreach (var (name, agent) in Runtime.AgentInstances)
        {
            snapshot.AgentMemories[name] = [.. agent.Memory];
            snapshot.AgentModels[name] = agent.Model;
        }

        return snapshot;
    }

    public void RestoreSnapshot(VmSnapshot snapshot)
    {
        CyclesExecuted = snapshot.InstructionCount;
        Status = snapshot.ExecutionStatus;

        Runtime.GlobalScope.SetVariables(snapshot.GlobalVariables);
        foreach (var (name, memory) in snapshot.AgentMemories)
        {
            if (Runtime.AgentInstances.TryGetValue(name, out var agent))
            {
                agent.Memory.Clear();
                agent.Memory.AddRange(memory);
            }
        }
    }

    public void Reset()
    {
        CyclesExecuted = 0;
        Status = "Ready";
    }
}

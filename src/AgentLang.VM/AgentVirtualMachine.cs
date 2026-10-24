using AgentLang.AST;
using AgentLang.Lexer;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.Security.Policy;
using AgentLang.VM.Actors;
using AgentLang.VM.Artifacts;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Checkpointing;
using AgentLang.VM.Eval;
using AgentLang.VM.Graphs;
using AgentLang.VM.MCP;
using AgentLang.VM.Memory;
using AgentLang.VM.Observability;
using AgentLang.VM.Optimization;
using AgentLang.VM.Planning;
using AgentLang.VM.Resilience;
using AgentLang.VM.Schemas;
using AgentLang.VM.Scheduling;
using AgentLang.VM.Streaming;
using AgentLang.VM.Swarm;
using AgentLang.VM.Testing;

namespace AgentLang.VM;

public sealed class AgentVirtualMachine
{
    public VmOptions Options { get; }
    public AgentLangRuntime Runtime { get; }
    public AgentActorSystem ActorSystem { get; } = new();
    public TokenCache Cache { get; } = new();
    public ToolScheduler Scheduler { get; } = new();
    public CheckpointManager Checkpoints { get; } = new();
    public TraceManager Tracer { get; } = new();
    public BytecodeCompiler Compiler { get; } = new();
    public BytecodeExecutor BytecodeExecutor => _bytecodeExecutor ??= new BytecodeExecutor(Runtime);
    private BytecodeExecutor? _bytecodeExecutor;
    public SwarmHandoffRouter SwarmRouter => _swarmRouter ??= new SwarmHandoffRouter(Runtime);
    private SwarmHandoffRouter? _swarmRouter;
    public HybridVectorStore HybridStore { get; } = new();
    public SemanticPromptCache SemanticCache { get; } = new();
    public AutoRemediator Remediator { get; } = new();
    public AgentBenchmarkRunner Evaluator { get; } = new();
    public EntityGraphMemory EntityMemory { get; } = new();
    public ArtifactStore Artifacts { get; } = new();
    public PlanAndExecuteEngine Planner { get; } = new();
    public ReWooEngine ReWoo { get; } = new();
    public TreeOfThoughtsEngine TreeOfThoughts { get; } = new();
    public ResourceAccessPolicy ResourcePolicy { get; } = new();
    public CassetteRecorder CassetteRecorder { get; set; } = new();

    public StateGraph CreateStateGraph(string name = "StateGraph") => new(name);
    public StateGraph<TState> CreateStateGraph<TState>(string name = "StateGraph") => new(name);

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

        Runtime.ReActEngine.OnExecutionCompleted = (agentName, input, tokens, cost) =>
        {
            using var agentSpan = Tracer.StartSpan($"agent.solve:{agentName}");
            agentSpan.SetAttribute("agent.name", agentName);
            agentSpan.SetAttribute("agent.input", input);
            agentSpan.RecordTokens(tokens / 2, tokens / 2, cost);
        };
    }

    public async Task ExecuteAsync(string sourceCode, string filePath = "<vm>", CancellationToken ct = default)
    {
        Status = "Running";
        using var span = Tracer.StartSpan($"vm.execute:{Path.GetFileName(filePath)}");
        span.SetAttribute("file.path", filePath);

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
            span.SetAttribute("cycles", CyclesExecuted);
        }
        catch (VmCycleLimitExceededException ex)
        {
            Status = "Halted:CycleLimitExceeded";
            span.SetError(ex.Message);
            throw;
        }
        catch (VmSandboxViolationException ex)
        {
            Status = "Halted:SandboxViolation";
            span.SetError(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            if (Status == "Running") Status = "Faulted";
            span.SetError(ex.Message);
            throw;
        }
    }

    public async Task<string> StreamExecuteAsync(
        string sourceCode,
        AgentStreamChannel streamChannel,
        string filePath = "<vm-stream>",
        CancellationToken ct = default)
    {
        await streamChannel.EmitStatusChangeAsync("Initializing", "VM", ct);
        using var span = Tracer.StartSpan($"vm.stream.execute:{Path.GetFileName(filePath)}");

        Action<StatementNode, RuntimeScope>? prevStep = Runtime.StepHook;
        Runtime.StepHook = (stmt, scope) =>
        {
            prevStep?.Invoke(stmt, scope);
            _ = streamChannel.EmitThoughtAsync($"Step [{stmt.Span.Start.Line}]: {stmt.GetType().Name}", "Runtime", ct);
        };

        try
        {
            await ExecuteAsync(sourceCode, filePath, ct);
            await streamChannel.EmitCompleteAsync("Execution completed successfully.", CyclesExecuted, 0, "VM", ct);
            return "Completed";
        }
        catch (Exception ex)
        {
            await streamChannel.EmitErrorAsync(ex.Message, "VM", ct);
            throw;
        }
        finally
        {
            Runtime.StepHook = prevStep;
            streamChannel.Complete();
        }
    }

    public async Task ExecuteAsync(ProgramNode program, CancellationToken ct = default)
    {
        Status = "Running";
        using var span = Tracer.StartSpan("vm.program.execute");
        span.SetAttribute("program.declarations", program.Declarations.Count);

        using var cts = Options.ExecutionTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, new CancellationTokenSource(Options.ExecutionTimeout.Value).Token)
            : null;
        var effectiveToken = cts?.Token ?? ct;

        try
        {
            await Runtime.ExecuteProgramAsync(program, effectiveToken);
            Status = "Completed";
            span.SetAttribute("cycles", CyclesExecuted);
        }
        catch (VmCycleLimitExceededException ex)
        {
            Status = "Halted:CycleLimitExceeded";
            span.SetError(ex.Message);
            throw;
        }
        catch (VmSandboxViolationException ex)
        {
            Status = "Halted:SandboxViolation";
            span.SetError(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            if (Status == "Running") Status = "Faulted";
            span.SetError(ex.Message);
            throw;
        }
    }

    public async Task<object?> ExecuteBytecodeAsync(AgentBytecodeModule module, CancellationToken ct = default)
    {
        Status = "Running";
        using var span = Tracer.StartSpan($"vm.bytecode.execute:{module.ModuleName}");
        span.SetAttribute("bytecode.instructions", module.Instructions.Count);
        span.SetAttribute("bytecode.constants", module.ConstantPool.Count);

        try
        {
            var result = await BytecodeExecutor.ExecuteAsync(module, ct);
            Status = "Completed";
            return result;
        }
        catch (Exception ex)
        {
            Status = "Faulted";
            span.SetError(ex.Message);
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

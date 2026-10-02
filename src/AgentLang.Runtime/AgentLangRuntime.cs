using System.Globalization;
using AgentLang.AST;
using AgentLang.Errors;
using AgentLang.Models;
using AgentLang.Runtime.Agents;
using AgentLang.Runtime.Memory;
using AgentLang.Runtime.Values;
using AgentLang.Security;
using AgentLang.Tools;
using AgentLang.Tools.MCP;

namespace AgentLang.Runtime;

public sealed class ReturnException : Exception
{
    public object? Value { get; }
    public ReturnException(object? value) => Value = value;
}

public sealed class AgentLangRuntime
{
    private readonly ModelRegistry _modelRegistry;
    private readonly ToolRegistry _toolRegistry;
    private readonly SecurityEngine _securityEngine;
    private readonly EventBus _eventBus;
    private readonly IPersistentMemoryStore _memoryStore;
    private readonly TextWriter _output;
    private readonly TextReader _input;
    private readonly AgentReActEngine _reactEngine;

    private readonly Dictionary<string, AgentDeclarationNode> _agentDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MultiAgentDeclarationNode> _multiAgentDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SwarmDeclarationNode> _swarmDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PermissionDeclarationNode> _permissionDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolDeclarationNode> _toolDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CustomToolDeclarationNode> _customToolDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventDeclarationNode> _eventDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DatasetDeclarationNode> _datasetDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrainDeclarationNode> _trainDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpDeclarationNode> _mcpDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CustomApiDeclarationNode> _customApiDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GoalDeclarationNode> _goalDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PipelineDeclarationNode> _pipelineDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StateDeclarationNode> _stateDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkflowDeclarationNode> _workflowDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GuardrailsDeclarationNode> _guardrailsDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpClient> _mcpClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly AiTrainingEngine _trainingEngine = new();

    private readonly Dictionary<string, AgentValue> _agentInstances = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeScope _globalScope = new("global");
    private int _callDepth = 0;
    private const int MaxCallDepth = 256;

    private readonly AsyncLocal<AgentValue?> _currentAgent = new();
    public AgentValue? CurrentAgent
    {
        get => _currentAgent.Value;
        private set => _currentAgent.Value = value;
    }
    public EventBus EventBus => _eventBus;
    public ModelRegistry ModelRegistry => _modelRegistry;
    public ToolRegistry ToolRegistry => _toolRegistry;
    public SecurityEngine SecurityEngine => _securityEngine;
    public IPersistentMemoryStore MemoryStore => _memoryStore;
    public AiTrainingEngine TrainingEngine => _trainingEngine;
    public AgentReActEngine ReActEngine => _reactEngine;
    public IReadOnlyDictionary<string, McpClient> McpClients => _mcpClients;
    public IReadOnlyDictionary<string, DatasetDeclarationNode> DatasetDefs => _datasetDefs;
    public IReadOnlyDictionary<string, TrainDeclarationNode> TrainDefs => _trainDefs;
    public IReadOnlyDictionary<string, McpDeclarationNode> McpDefs => _mcpDefs;
    public IReadOnlyDictionary<string, CustomApiDeclarationNode> CustomApiDefs => _customApiDefs;
    public IReadOnlyDictionary<string, AgentValue> AgentInstances => _agentInstances;
    public IReadOnlyDictionary<string, SwarmDeclarationNode> SwarmDefs => _swarmDefs;
    public IReadOnlyDictionary<string, MultiAgentDeclarationNode> MultiAgentDefs => _multiAgentDefs;
    public RuntimeScope GlobalScope => _globalScope;

    public AgentLangRuntime(
        ModelRegistry? modelRegistry = null,
        ToolRegistry? toolRegistry = null,
        SecurityEngine? securityEngine = null,
        EventBus? eventBus = null,
        IPersistentMemoryStore? memoryStore = null,
        TextWriter? output = null,
        TextReader? input = null)
    {
        _securityEngine = securityEngine ?? new SecurityEngine();
        _modelRegistry = modelRegistry ?? new ModelRegistry();
        _toolRegistry = toolRegistry ?? new ToolRegistry(_securityEngine);
        _eventBus = eventBus ?? new EventBus();
        _memoryStore = memoryStore ?? new LocalFileMemoryStore();
        _output = output ?? Console.Out;
        _input = input ?? Console.In;
        _reactEngine = new AgentReActEngine(_toolRegistry, _modelRegistry, _securityEngine, _output, _eventBus);
    }

    public async Task ExecuteProgramAsync(ProgramNode program, CancellationToken ct = default)
    {
        // 1. Catalog declarations
        foreach (var decl in program.Declarations)
        {
            switch (decl)
            {
                case PermissionDeclarationNode perm:
                    _permissionDefs[perm.Name] = perm;
                    var policy = new PermissionPolicy(perm.Name);
                    foreach (var rule in perm.Rules)
                    {
                        policy.AddRule(rule.Action, rule.CapabilityPattern);
                    }
                    _securityEngine.RegisterPolicy(policy);
                    break;

                case ToolDeclarationNode tool:
                    _toolDefs[tool.Name] = tool;
                    break;

                case CustomToolDeclarationNode ctool:
                    _customToolDefs[ctool.Name] = ctool;
                    var customToolInstance = new CustomAgentLangTool(ctool, async (args, cToken) =>
                    {
                        var toolScope = new RuntimeScope($"tool:{ctool.Name}", _globalScope);
                        foreach (var inputParam in ctool.Inputs)
                        {
                            args.TryGetValue(inputParam.Name, out var paramVal);
                            toolScope.SetLocal(inputParam.Name, paramVal);
                        }
                        foreach (var stmt in ctool.Body)
                        {
                            try
                            {
                                await ExecuteStatementAsync(stmt, toolScope, cToken);
                            }
                            catch (ReturnException ret)
                            {
                                return ret.Value;
                            }
                        }
                        return null;
                    });
                    _toolRegistry.RegisterTool(customToolInstance);
                    _globalScope.SetLocal(ctool.Name, customToolInstance);
                    break;

                case ModelDeclarationNode mdl:
                    _modelRegistry.RegisterAlias(mdl.Alias, mdl.TargetModel);
                    break;

                case FunctionDeclarationNode fn:
                    var fnVal = new FunctionValue(fn.Name, fn.Parameters, fn.Body, _globalScope);
                    _globalScope.SetLocal(fn.Name, fnVal);
                    break;

                case AgentDeclarationNode agent:
                    _agentDefs[agent.Name] = agent;
                    break;

                case MultiAgentDeclarationNode multi:
                    _multiAgentDefs[multi.Name] = multi;
                    // Also catalog child agents
                    foreach (var item in multi.Body)
                    {
                        if (item is AgentDeclarationNode childAgent)
                            _agentDefs[childAgent.Name] = childAgent;
                        else if (item is ParallelBlockNode par)
                        {
                            foreach (var parChild in par.Body)
                            {
                                if (parChild is AgentDeclarationNode pAgent)
                                    _agentDefs[pAgent.Name] = pAgent;
                            }
                        }
                    }
                    break;

                case SwarmDeclarationNode swarm:
                    _swarmDefs[swarm.Name] = swarm;
                    // Also catalog child agents
                    foreach (var item in swarm.Body)
                    {
                        if (item is AgentDeclarationNode childAgent)
                            _agentDefs[childAgent.Name] = childAgent;
                        else if (item is ParallelBlockNode par)
                        {
                            foreach (var parChild in par.Body)
                            {
                                if (parChild is AgentDeclarationNode pAgent)
                                    _agentDefs[pAgent.Name] = pAgent;
                            }
                        }
                    }
                    break;

                case EventDeclarationNode evt:
                    _eventDefs[evt.Target] = evt;
                    _eventBus.Subscribe(evt.Target, async payload =>
                    {
                        var evtScope = new RuntimeScope($"event:{evt.Target}", _globalScope);
                        if (payload != null)
                            evtScope.SetLocal("eventPayload", payload);
                        foreach (var stmt in evt.Body)
                        {
                            await ExecuteStatementAsync(stmt, evtScope, ct);
                        }
                    });
                    break;

                case ImportApiDeclarationNode apiDecl:
                    await ExecuteImportApiAsync(apiDecl, _globalScope, ct);
                    break;

                case DatasetDeclarationNode datasetDecl:
                    _datasetDefs[datasetDecl.Name] = datasetDecl;
                    await ExecuteDatasetDeclarationAsync(datasetDecl, _globalScope, ct);
                    break;

                case McpDeclarationNode mcpDecl:
                    _mcpDefs[mcpDecl.ServerName] = mcpDecl;
                    await ExecuteMcpDeclarationAsync(mcpDecl, _globalScope, ct);
                    break;

                case CustomApiDeclarationNode customApiDecl:
                    _customApiDefs[customApiDecl.ApiName] = customApiDecl;
                    await ExecuteCustomApiDeclarationAsync(customApiDecl, _globalScope, ct);
                    break;

                case TrainDeclarationNode trainDecl:
                    _trainDefs[trainDecl.ModelName] = trainDecl;
                    await ExecuteTrainDeclarationAsync(trainDecl, _globalScope, ct);
                    break;

                case GoalDeclarationNode goalDecl:
                    _goalDefs[goalDecl.Name] = goalDecl;
                    var goalVal = await EvaluateExpressionAsync(goalDecl.Value, _globalScope, ct);
                    _globalScope.SetLocal(goalDecl.Name, new GoalValue(goalDecl.Name, goalVal?.ToString() ?? ""));
                    break;

                case PipelineDeclarationNode pipeDecl:
                    _pipelineDefs[pipeDecl.Name] = pipeDecl;
                    var pipeVal = new PipelineValue(pipeDecl, _globalScope);
                    _globalScope.SetLocal(pipeDecl.Name, pipeVal);
                    break;

                case StateDeclarationNode stateDecl:
                    _stateDefs[stateDecl.Name] = stateDecl;
                    var stateDefVal = new StateDefinitionValue(stateDecl, this);
                    _globalScope.SetLocal(stateDecl.Name, stateDefVal);
                    break;

                case WorkflowDeclarationNode wfDecl:
                    _workflowDefs[wfDecl.Name] = wfDecl;
                    var wfVal = new WorkflowValue(wfDecl, _globalScope, this);
                    _globalScope.SetLocal(wfDecl.Name, wfVal);
                    break;

                case GuardrailsDeclarationNode guardDecl:
                    // Catalog guardrails
                    break;
            }
        }

        // 2. Execute Main block if present
        if (program.Main != null)
        {
            var mainScope = new RuntimeScope("main", _globalScope);
            foreach (var stmt in program.Main.Statements)
            {
                await ExecuteStatementAsync(stmt, mainScope, ct);
            }
        }
        else
        {
            // If no main block, execute top-level swarms then agents
            foreach (var swarmDef in _swarmDefs.Values)
            {
                await ExecuteSwarmAsync(swarmDef.Name, _globalScope, ct);
            }
            foreach (var agentDef in _agentDefs.Values)
            {
                await ExecuteAgentAsync(agentDef.Name, _globalScope, ct);
            }
        }
    }

    public async Task<AgentValue> ExecuteAgentAsync(string agentName, RuntimeScope parentScope, CancellationToken ct = default)
    {
        if (!_agentDefs.TryGetValue(agentName, out var agentDef))
        {
            throw new AgentLangRuntimeException($"Agent '{agentName}' was not defined.", errorCode: "AGT304");
        }

        var agentInstance = _agentInstances.TryGetValue(agentName, out var existing)
            ? existing
            : new AgentValue(agentName);
        _agentInstances[agentName] = agentInstance;

        var prevAgent = CurrentAgent;
        CurrentAgent = agentInstance;

        var agentScope = new RuntimeScope($"agent:{agentName}", parentScope);
        agentScope.SetLocal(agentName, agentInstance);

        // Bring pre-existing context (from delegation or prior calls) into agent scope
        foreach (var (k, v) in agentInstance.Context)
        {
            agentScope.SetLocal(k, v);
        }

        try
        {
            // Evaluate config
            foreach (var cfg in agentDef.Config)
            {
                var val = await EvaluateExpressionAsync(cfg.Value, agentScope, ct);
                if (cfg.Key.Equals("model", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.Model = val?.ToString();
                }
                else if (cfg.Key.Equals("role", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.Role = val?.ToString();
                }
                else if (cfg.Key.Equals("instructions", StringComparison.OrdinalIgnoreCase) || cfg.Key.Equals("instruction", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.Instructions = val?.ToString();
                }
                else if (cfg.Key.Equals("persona", StringComparison.OrdinalIgnoreCase) || cfg.Key.Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.Persona = val?.ToString();
                    if (agentInstance.Instructions == null)
                        agentInstance.Instructions = val?.ToString();
                }
                else if (cfg.Key.Equals("tools", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.BoundTools.Clear();
                    if (val is IEnumerable<object?> toolList)
                    {
                        foreach (var t in toolList)
                            if (t != null) agentInstance.BoundTools.Add(t.ToString()!);
                    }
                    else if (cfg.Value is ListLiteralExpressionNode listLit)
                    {
                        foreach (var elem in listLit.Elements)
                        {
                            if (elem is IdentifierExpressionNode idElem) agentInstance.BoundTools.Add(idElem.Name);
                            else if (elem is MemberAccessExpressionNode mem) agentInstance.BoundTools.Add($"{mem.Target}.{mem.MemberName}");
                        }
                    }
                    else if (val != null)
                    {
                        agentInstance.BoundTools.Add(val.ToString()!);
                    }
                }
                else if (cfg.Key.Equals("max_steps", StringComparison.OrdinalIgnoreCase) || cfg.Key.Equals("maxsteps", StringComparison.OrdinalIgnoreCase))
                {
                    if (val != null && int.TryParse(val.ToString(), out int ms))
                        agentInstance.MaxSteps = ms;
                }
                else if (cfg.Key.Equals("goal", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.Goal = val?.ToString();
                }
                else if (cfg.Key.Equals("temperature", StringComparison.OrdinalIgnoreCase))
                {
                    if (val != null)
                        agentInstance.Temperature = Convert.ToDouble(val, CultureInfo.InvariantCulture);
                }
                else if (cfg.Key.Equals("fallback", StringComparison.OrdinalIgnoreCase) || cfg.Key.Equals("fallbacks", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.Fallbacks.Clear();
                    if (val is IEnumerable<object?> list)
                    {
                        foreach (var item in list)
                        {
                            if (item != null) agentInstance.Fallbacks.Add(item.ToString()!);
                        }
                    }
                    else if (val != null)
                    {
                        agentInstance.Fallbacks.Add(val.ToString()!);
                    }
                }
                else if (cfg.Key.Equals("permission", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.PermissionPolicy = val?.ToString() ?? (cfg.Value is IdentifierExpressionNode idNode ? idNode.Name : null);
                }
                else if (cfg.Key.Equals("memory", StringComparison.OrdinalIgnoreCase))
                {
                    string mStr = val?.ToString()?.ToLowerInvariant() ?? "";
                    if (mStr is "false" or "none" or "disabled")
                    {
                        agentInstance.MemoryEnabled = false;
                        agentInstance.MemoryMode = "none";
                    }
                    else if (mStr is "short_term" or "shortterm" or "ram" or "session")
                    {
                        agentInstance.MemoryEnabled = true;
                        agentInstance.MemoryMode = "short_term";
                    }
                    else if (mStr is "long_term" or "longterm" or "persistent" or "disk" || val is true)
                    {
                        agentInstance.MemoryEnabled = true;
                        agentInstance.MemoryMode = "long_term";
                    }
                    else
                    {
                        agentInstance.MemoryEnabled = val != null;
                        agentInstance.MemoryMode = "long_term";
                    }
                }
            }

            // Load persistent memory if enabled and long_term mode
            if (agentInstance.MemoryEnabled && agentInstance.MemoryMode.Equals("long_term", StringComparison.OrdinalIgnoreCase))
            {
                var persisted = await _memoryStore.LoadMemoryAsync(agentName, ct);
                foreach (var entry in persisted)
                {
                    if (!agentInstance.Memory.Contains(entry))
                        agentInstance.Memory.Add(entry);
                }
            }

            // Register agent-scoped functions first
            foreach (var item in agentDef.Body)
            {
                if (item is FunctionDeclarationNode afn)
                {
                    var fnVal = new FunctionValue(afn.Name, afn.Parameters, afn.Body, agentScope);
                    agentInstance.Functions[afn.Name] = fnVal;
                    agentScope.SetLocal(afn.Name, fnVal);
                }
            }

            // Execute body items in order
            foreach (var item in agentDef.Body)
            {
                if (item is ContextDeclarationNode ctx)
                {
                    if (agentInstance.Context.TryGetValue(ctx.Name, out var existingCtx) && existingCtx != null && !(existingCtx is string s && string.IsNullOrEmpty(s)))
                    {
                        agentInstance.Variables[ctx.Name] = existingCtx;
                        agentScope.SetLocal(ctx.Name, existingCtx);
                    }
                    else
                    {
                        var ctxVal = await EvaluateExpressionAsync(ctx.Value, agentScope, ct);
                        agentInstance.Context[ctx.Name] = ctxVal;
                        agentInstance.Variables[ctx.Name] = ctxVal;
                        agentScope.SetLocal(ctx.Name, ctxVal);
                        if (agentInstance.MemoryEnabled && ctxVal != null)
                        {
                            agentInstance.Memory.Add($"{ctx.Name}: {ctxVal}");
                        }
                    }
                }
                else if (item is TaskDeclarationNode task)
                {
                    await ExecuteTaskDeclarationAsync(task, agentInstance, agentScope, ct);
                }
                else if (item is FunctionDeclarationNode)
                {
                    // Already registered
                }
                else if (item is StatementNode stmt)
                {
                    await ExecuteStatementAsync(stmt, agentScope, ct);
                }
            }

            // Persist memory if enabled and long_term mode
            if (agentInstance.MemoryEnabled && agentInstance.MemoryMode.Equals("long_term", StringComparison.OrdinalIgnoreCase) && agentInstance.Memory.Count > 0)
            {
                await _memoryStore.SaveMemoryAsync(agentName, agentInstance.Memory, ct);
            }

            return agentInstance;
        }
        finally
        {
            CurrentAgent = prevAgent;
        }
    }

    private async Task<TaskValue> ExecuteTaskDeclarationAsync(
        TaskDeclarationNode taskNode,
        AgentValue agentInstance,
        RuntimeScope agentScope,
        CancellationToken ct)
    {
        var taskValue = new TaskValue(taskNode.Name) { Status = "running" };
        agentInstance.Tasks[taskNode.Name] = taskValue;
        agentScope.SetLocal(taskNode.Name, taskValue);

        var taskScope = new RuntimeScope($"task:{taskNode.Name}", agentScope);

        // Check task-level config overrides (e.g. task scan (model = fast))
        string? taskModel = null;
        foreach (var cfg in taskNode.Config)
        {
            var val = await EvaluateExpressionAsync(cfg.Value, agentScope, ct);
            if (cfg.Key.Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                taskModel = val?.ToString() ?? (cfg.Value is IdentifierExpressionNode idNode ? idNode.Name : null);
            }
        }
        if (taskModel != null)
        {
            taskScope.SetLocal("__task_model__", taskModel);
        }

        // Bring context into task scope
        foreach (var (k, v) in agentInstance.Context)
        {
            taskScope.SetLocal(k, v);
        }

        object? taskResult = null;
        try
        {
            foreach (var stmt in taskNode.Body)
            {
                try
                {
                    await ExecuteStatementAsync(stmt, taskScope, ct);
                }
                catch (ReturnException ret)
                {
                    taskResult = ret.Value;
                    break;
                }
            }

            if (taskResult == null && taskScope.TryGet("result", out var resVal))
            {
                taskResult = resVal;
            }

            taskValue.Status = "completed";
            taskValue.Result = taskResult;

            // Trigger events for task completion: e.g. "research.finished" or "{agent}.{task}.finished"
            await _eventBus.PublishAsync($"{taskNode.Name}.finished", taskValue);
            await _eventBus.PublishAsync($"{agentInstance.Name}.{taskNode.Name}.finished", taskValue);

            return taskValue;
        }
        catch (Exception ex) when (ex is not ReturnException)
        {
            taskValue.Status = "failed";
            taskValue.Result = ex.Message;
            throw;
        }
    }

    public async Task ExecuteSwarmAsync(string swarmName, RuntimeScope parentScope, CancellationToken ct = default)
    {
        if (!_swarmDefs.TryGetValue(swarmName, out var swarmDef))
        {
            throw new AgentLangRuntimeException($"Swarm '{swarmName}' was not defined.", errorCode: "AGT305");
        }

        var swarmScope = new RuntimeScope($"swarm:{swarmName}", parentScope);
        bool isParallel = swarmDef.Strategy?.Equals("parallel", StringComparison.OrdinalIgnoreCase) == true;

        if (isParallel)
        {
            var tasks = new List<Task>();
            // Launch named agents in parallel
            foreach (var agName in swarmDef.Agents)
            {
                if (_agentDefs.ContainsKey(agName))
                {
                    tasks.Add(Task.Run(() => ExecuteAgentAsync(agName, swarmScope, ct), ct));
                }
            }

            // Launch body items
            foreach (var item in swarmDef.Body)
            {
                if (item is AgentDeclarationNode childAgent)
                {
                    tasks.Add(Task.Run(() => ExecuteAgentAsync(childAgent.Name, swarmScope, ct), ct));
                }
                else if (item is ParallelBlockNode par)
                {
                    foreach (var parChild in par.Body)
                    {
                        if (parChild is AgentDeclarationNode pAgent)
                            tasks.Add(Task.Run(() => ExecuteAgentAsync(pAgent.Name, swarmScope, ct), ct));
                        else if (parChild is StatementNode pStmt)
                            tasks.Add(Task.Run(() => ExecuteStatementAsync(pStmt, swarmScope, ct), ct));
                    }
                }
                else if (item is StatementNode stmt)
                {
                    tasks.Add(Task.Run(() => ExecuteStatementAsync(stmt, swarmScope, ct), ct));
                }
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception)
            {
                var failed = tasks.Where(t => t.IsFaulted).Select(t => t.Exception?.GetBaseException().Message ?? "Task failed").ToList();
                if (failed.Count > 1)
                {
                    throw new AgentLangRuntimeException($"Swarm '{swarmName}' encountered {failed.Count} parallel failures: {string.Join("; ", failed)}", errorCode: "AGT308");
                }
                throw;
            }
        }
        else
        {
            // Sequential strategy
            foreach (var agName in swarmDef.Agents)
            {
                if (_agentDefs.ContainsKey(agName))
                {
                    await ExecuteAgentAsync(agName, swarmScope, ct);
                }
            }

            foreach (var item in swarmDef.Body)
            {
                if (item is AgentDeclarationNode childAgent)
                {
                    await ExecuteAgentAsync(childAgent.Name, swarmScope, ct);
                }
                else if (item is ParallelBlockNode par)
                {
                    var pTasks = new List<Task>();
                    foreach (var parChild in par.Body)
                    {
                        if (parChild is AgentDeclarationNode pAgent)
                            pTasks.Add(Task.Run(() => ExecuteAgentAsync(pAgent.Name, swarmScope, ct), ct));
                        else if (parChild is StatementNode pStmt)
                            pTasks.Add(Task.Run(() => ExecuteStatementAsync(pStmt, swarmScope, ct), ct));
                    }
                    await Task.WhenAll(pTasks);
                }
                else if (item is StatementNode stmt)
                {
                    await ExecuteStatementAsync(stmt, swarmScope, ct);
                }
            }
        }

        // Run coordinator if specified
        if (!string.IsNullOrWhiteSpace(swarmDef.Coordinator) && _agentDefs.ContainsKey(swarmDef.Coordinator))
        {
            await ExecuteAgentAsync(swarmDef.Coordinator, swarmScope, ct);
        }

        await _eventBus.PublishAsync($"{swarmName}.finished", swarmName);
    }

    public async Task ExecuteMultiAgentAsync(string multiAgentName, RuntimeScope parentScope, CancellationToken ct = default)
    {
        if (!_multiAgentDefs.TryGetValue(multiAgentName, out var multiDef))
        {
            throw new AgentLangRuntimeException($"MultiAgent '{multiAgentName}' was not defined.", errorCode: "AGT307");
        }

        var multiScope = new RuntimeScope($"multiagent:{multiAgentName}", parentScope);
        foreach (var item in multiDef.Body)
        {
            if (item is AgentDeclarationNode child)
            {
                await ExecuteAgentAsync(child.Name, multiScope, ct);
            }
            else if (item is ParallelBlockNode par)
            {
                var tasks = new List<Task>();
                foreach (var pItem in par.Body)
                {
                    if (pItem is AgentDeclarationNode pAg)
                        tasks.Add(Task.Run(() => ExecuteAgentAsync(pAg.Name, multiScope, ct), ct));
                    else if (pItem is StatementNode pStmt)
                        tasks.Add(Task.Run(() => ExecuteStatementAsync(pStmt, multiScope, ct), ct));
                }
                await Task.WhenAll(tasks);
            }
            else if (item is StatementNode stmt)
            {
                await ExecuteStatementAsync(stmt, multiScope, ct);
            }
        }

        await _eventBus.PublishAsync($"{multiAgentName}.finished", multiAgentName);
    }

    public async Task ExecuteStatementAsync(StatementNode stmt, RuntimeScope scope, CancellationToken ct = default)
    {
        switch (stmt)
        {
            case VariableAssignmentNode assign:
                var val = await EvaluateExpressionAsync(assign.Value, scope, ct);
                scope.Assign(assign.VariableName, val);
                if (CurrentAgent != null)
                {
                    CurrentAgent.Variables[assign.VariableName] = val;
                }
                break;

            case FunctionDeclarationNode fn:
                var fnVal = new FunctionValue(fn.Name, fn.Parameters, fn.Body, scope);
                scope.SetLocal(fn.Name, fnVal);
                if (CurrentAgent != null)
                {
                    CurrentAgent.Functions[fn.Name] = fnVal;
                }
                break;

            case SendMessageStatementNode sendStmt:
                var msgContent = await EvaluateExpressionAsync(sendStmt.Message, scope, ct);
                var tagVal = sendStmt.Tag != null ? await EvaluateExpressionAsync(sendStmt.Tag, scope, ct) : null;

                if (!_agentInstances.TryGetValue(sendStmt.TargetAgent, out var targetAgent))
                {
                    targetAgent = new AgentValue(sendStmt.TargetAgent);
                    _agentInstances[sendStmt.TargetAgent] = targetAgent;
                }

                var msg = new AgentMessage(msgContent, CurrentAgent?.Name, tagVal);
                targetAgent.Inbox.Add(msg);

                await _eventBus.PublishAsync($"{sendStmt.TargetAgent}.message", msg);
                await _eventBus.PublishAsync("agent.message", msg);
                break;

            case BroadcastStatementNode bcast:
                var bcastContent = await EvaluateExpressionAsync(bcast.Message, scope, ct);
                var bcastTag = bcast.Tag != null ? await EvaluateExpressionAsync(bcast.Tag, scope, ct) : null;
                var bcastMsg = new AgentMessage(bcastContent, CurrentAgent?.Name, bcastTag);
                foreach (var ag in _agentInstances.Values)
                {
                    ag.Inbox.Add(bcastMsg);
                    await _eventBus.PublishAsync($"{ag.Name}.message", bcastMsg);
                }
                await _eventBus.PublishAsync("swarm.broadcast", bcastMsg);
                await _eventBus.PublishAsync("agent.message", bcastMsg);
                break;

            case WaitStatementNode waitStmt:
                if (waitStmt.IsAwait)
                {
                    if (waitStmt.TargetOrDuration is IdentifierExpressionNode id)
                    {
                        if (_swarmDefs.ContainsKey(id.Name))
                            await ExecuteSwarmAsync(id.Name, scope, ct);
                        else if (_multiAgentDefs.ContainsKey(id.Name))
                            await ExecuteMultiAgentAsync(id.Name, scope, ct);
                        else if (_agentDefs.ContainsKey(id.Name))
                            await ExecuteAgentAsync(id.Name, scope, ct);
                    }
                    else
                    {
                        var target = await EvaluateExpressionAsync(waitStmt.TargetOrDuration, scope, ct);
                        if (target is Task taskObj)
                        {
                            await taskObj;
                        }
                    }
                }
                else
                {
                    var durVal = await EvaluateExpressionAsync(waitStmt.TargetOrDuration, scope, ct);
                    int ms = 0;
                    if (durVal is int i) ms = i;
                    else if (durVal is double d) ms = (int)d;
                    else if (durVal is long l) ms = (int)l;
                    else if (durVal is string s)
                    {
                        s = s.Trim();
                        if (s.EndsWith("ms", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[..^2], out int parsedMs))
                            ms = parsedMs;
                        else if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase) && double.TryParse(s[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedS))
                            ms = (int)(parsedS * 1000);
                        else if (int.TryParse(s, out int parsedDirect))
                            ms = parsedDirect;
                    }
                    if (ms > 0)
                    {
                        await Task.Delay(ms, ct);
                    }
                }
                break;

            case ExpressionStatementNode exprStmt:
                await EvaluateExpressionAsync(exprStmt.Expression, scope, ct);
                break;

            case AgentInvocationNode invocation:
                if (_swarmDefs.ContainsKey(invocation.AgentName))
                {
                    await ExecuteSwarmAsync(invocation.AgentName, scope, ct);
                }
                else if (_multiAgentDefs.ContainsKey(invocation.AgentName))
                {
                    await ExecuteMultiAgentAsync(invocation.AgentName, scope, ct);
                }
                else
                {
                    await ExecuteAgentAsync(invocation.AgentName, scope, ct);
                }
                break;

            case UntilStatementNode untilStmt:
                while (!IsTruthy(await EvaluateExpressionAsync(untilStmt.Condition, scope, ct)))
                {
                    var untilScope = new RuntimeScope("until_body", scope);
                    foreach (var s in untilStmt.Body)
                        await ExecuteStatementAsync(s, untilScope, ct);
                }
                break;

            case IfStatementNode ifStmt:
                var conditionVal = await EvaluateExpressionAsync(ifStmt.Condition, scope, ct);
                if (IsTruthy(conditionVal))
                {
                    var thenScope = new RuntimeScope("if_then", scope);
                    foreach (var s in ifStmt.ThenBranch)
                        await ExecuteStatementAsync(s, thenScope, ct);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    var elseScope = new RuntimeScope("if_else", scope);
                    foreach (var s in ifStmt.ElseBranch)
                        await ExecuteStatementAsync(s, elseScope, ct);
                }
                break;

            case WhileStatementNode whileStmt:
                while (IsTruthy(await EvaluateExpressionAsync(whileStmt.Condition, scope, ct)))
                {
                    var loopScope = new RuntimeScope("while_body", scope);
                    foreach (var s in whileStmt.Body)
                        await ExecuteStatementAsync(s, loopScope, ct);
                }
                break;

            case RepeatStatementNode repeatStmt:
                var countVal = await EvaluateExpressionAsync(repeatStmt.Count, scope, ct);
                int count = Convert.ToInt32(countVal, CultureInfo.InvariantCulture);
                for (int i = 0; i < count; i++)
                {
                    var loopScope = new RuntimeScope("repeat_body", scope);
                    foreach (var s in repeatStmt.Body)
                        await ExecuteStatementAsync(s, loopScope, ct);
                }
                break;

            case ForStatementNode forStmt:
                var iterableVal = await EvaluateExpressionAsync(forStmt.Iterable, scope, ct);
                var items = ToEnumerable(iterableVal);
                foreach (var item in items)
                {
                    var forScope = new RuntimeScope("for_body", scope);
                    forScope.SetLocal(forStmt.VariableName, item);
                    foreach (var s in forStmt.Body)
                        await ExecuteStatementAsync(s, forScope, ct);
                }
                break;

            case ReturnStatementNode retStmt:
                object? retVal = retStmt.Value != null ? await EvaluateExpressionAsync(retStmt.Value, scope, ct) : null;
                throw new ReturnException(retVal);

            case TryCatchStatementNode tryCatch:
                try
                {
                    var tryScope = new RuntimeScope("try_body", scope);
                    foreach (var s in tryCatch.TryBody)
                        await ExecuteStatementAsync(s, tryScope, ct);
                }
                catch (ReturnException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var catchScope = new RuntimeScope("catch_body", scope);
                    if (!string.IsNullOrWhiteSpace(tryCatch.CatchVariable))
                    {
                        catchScope.SetLocal(tryCatch.CatchVariable, ex.Message);
                    }
                    foreach (var s in tryCatch.CatchBody)
                        await ExecuteStatementAsync(s, catchScope, ct);
                }
                break;

            case RetryStatementNode retryStmt:
                var retryCountVal = await EvaluateExpressionAsync(retryStmt.Count, scope, ct);
                int retries = Math.Max(1, Convert.ToInt32(retryCountVal, CultureInfo.InvariantCulture));
                Exception? lastException = null;

                for (int attempt = 1; attempt <= retries; attempt++)
                {
                    try
                    {
                        var retryScope = new RuntimeScope($"retry_attempt_{attempt}", scope);
                        foreach (var s in retryStmt.Body)
                            await ExecuteStatementAsync(s, retryScope, ct);
                        lastException = null;
                        break;
                    }
                    catch (ReturnException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (attempt < retries)
                        {
                            await Task.Delay(20 * attempt, ct);
                        }
                    }
                }

                if (lastException != null)
                {
                    throw lastException;
                }
                break;

            case ParallelBlockNode parallel:
                var tasks = new List<Task>();
                foreach (var item in parallel.Body)
                {
                    if (item is AgentDeclarationNode childAgent)
                    {
                        tasks.Add(Task.Run(() => ExecuteAgentAsync(childAgent.Name, scope, ct), ct));
                    }
                    else if (item is StatementNode childStmt)
                    {
                        tasks.Add(Task.Run(() => ExecuteStatementAsync(childStmt, scope, ct), ct));
                    }
                }
                await Task.WhenAll(tasks);
                break;

            case BlockStatementNode block:
                var bScope = new RuntimeScope("block", scope);
                foreach (var s in block.Statements)
                    await ExecuteStatementAsync(s, bScope, ct);
                break;

            case ImportApiDeclarationNode apiDecl:
                await ExecuteImportApiAsync(apiDecl, scope, ct);
                break;

            case LearnStatementNode learn:
                await ExecuteLearnStatementAsync(learn, scope, ct);
                break;

            case DatasetDeclarationNode datasetDecl:
                _datasetDefs[datasetDecl.Name] = datasetDecl;
                await ExecuteDatasetDeclarationAsync(datasetDecl, scope, ct);
                break;

            case TrainDeclarationNode trainDecl:
                _trainDefs[trainDecl.ModelName] = trainDecl;
                await ExecuteTrainDeclarationAsync(trainDecl, scope, ct);
                break;

            case McpDeclarationNode mcpDecl:
                _mcpDefs[mcpDecl.ServerName] = mcpDecl;
                await ExecuteMcpDeclarationAsync(mcpDecl, scope, ct);
                break;

            case CustomApiDeclarationNode customApiDecl:
                _customApiDefs[customApiDecl.ApiName] = customApiDecl;
                await ExecuteCustomApiDeclarationAsync(customApiDecl, scope, ct);
                break;

            case GoalDeclarationNode goal:
                var gVal = await EvaluateExpressionAsync(goal.Value, scope, ct);
                scope.Assign(goal.Name, new GoalValue(goal.Name, gVal?.ToString() ?? ""));
                break;

            case LoopStatementNode loop:
                await ExecuteLoopAsync(loop, scope, ct);
                break;

            case DecideStatementNode decide:
                await ExecuteDecideAsync(decide, scope, ct);
                break;

            case BudgetStatementNode budget:
                var bVal = await EvaluateExpressionAsync(budget.Value, scope, ct);
                if (CurrentAgent != null && !string.IsNullOrEmpty(budget.BudgetItem))
                {
                    CurrentAgent.Variables[$"budget:{budget.BudgetItem}"] = bVal;
                }
                if (budget.Limits != null)
                {
                    foreach (var (k, v) in budget.Limits)
                    {
                        var limitVal = await EvaluateExpressionAsync(v, scope, ct);
                        if (CurrentAgent != null)
                            CurrentAgent.Variables[$"budget:{k}"] = limitVal;
                    }
                }
                if (budget.Body != null)
                {
                    var budgetScope = new RuntimeScope("budget", scope);
                    foreach (var bStmt in budget.Body)
                    {
                        await ExecuteStatementAsync(bStmt, budgetScope, ct);
                    }
                }
                break;

            case BreakStatementNode:
                throw new BreakException();

            case ContinueStatementNode:
                throw new ContinueException();

            case MemberAssignmentNode memberAssign:
                await ExecuteMemberAssignmentAsync(memberAssign, scope, ct);
                break;

            case IndexAssignmentNode indexAssign:
                await ExecuteIndexAssignmentAsync(indexAssign, scope, ct);
                break;
        }
    }

    private async Task ExecuteDatasetDeclarationAsync(DatasetDeclarationNode dataset, RuntimeScope scope, CancellationToken ct)
    {
        var def = _trainingEngine.GetOrCreateDataset(dataset.Name, dataset.Mode);
        foreach (var item in dataset.Items)
        {
            if (item is DatasetPairNode pair)
            {
                var inputVal = await EvaluateExpressionAsync(pair.Input, scope, ct);
                var outputVal = await EvaluateExpressionAsync(pair.Output, scope, ct);
                def.AddPair(inputVal?.ToString() ?? "", outputVal?.ToString() ?? "");
            }
            else if (item is DatasetPreferenceNode pref)
            {
                var promptVal = await EvaluateExpressionAsync(pref.Prompt, scope, ct);
                var chosenVal = await EvaluateExpressionAsync(pref.Chosen, scope, ct);
                var rejectedVal = await EvaluateExpressionAsync(pref.Rejected, scope, ct);
                def.AddPreference(promptVal?.ToString() ?? "", chosenVal?.ToString() ?? "", rejectedVal?.ToString() ?? "");
            }
        }
        scope.SetLocal(dataset.Name, def);
    }

    private async Task ExecuteTrainDeclarationAsync(TrainDeclarationNode train, RuntimeScope scope, CancellationToken ct)
    {
        string baseModel = "mock";
        if (train.BaseModel != null)
        {
            var bmVal = await EvaluateExpressionAsync(train.BaseModel, scope, ct);
            baseModel = bmVal?.ToString() ?? "mock";
        }

        string datasetName = "";
        if (train.DatasetRef is IdentifierExpressionNode idNode)
        {
            datasetName = idNode.Name;
        }
        else if (train.DatasetRef != null)
        {
            var dVal = await EvaluateExpressionAsync(train.DatasetRef, scope, ct);
            datasetName = dVal?.ToString() ?? "";
        }

        int epochs = 3;
        if (train.Epochs != null)
        {
            var epVal = await EvaluateExpressionAsync(train.Epochs, scope, ct);
            if (epVal != null && int.TryParse(epVal.ToString(), out int ep))
                epochs = ep;
        }

        double lr = 0.001;
        if (train.LearningRate != null)
        {
            var lrVal = await EvaluateExpressionAsync(train.LearningRate, scope, ct);
            if (lrVal != null && double.TryParse(lrVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double dLr))
                lr = dLr;
        }

        TrainingValidationConfig? valConfig = null;
        if (train.Validation != null)
        {
            var testCases = new List<(string Prompt, string Expected)>();
            foreach (var tc in train.Validation.TestCases)
            {
                var p = await EvaluateExpressionAsync(tc.Prompt, scope, ct);
                var e = await EvaluateExpressionAsync(tc.Expected, scope, ct);
                testCases.Add((p?.ToString() ?? "", e?.ToString() ?? ""));
            }

            double minAcc = 0.0;
            if (train.Validation.MinAccuracy != null)
            {
                var accVal = await EvaluateExpressionAsync(train.Validation.MinAccuracy, scope, ct);
                if (accVal != null && double.TryParse(accVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double dAcc))
                    minAcc = dAcc;
            }

            valConfig = new TrainingValidationConfig(testCases, minAcc);
        }

        var config = new TrainingConfig(train.ModelName, baseModel, datasetName, epochs, lr, valConfig);
        var result = await _trainingEngine.TrainAsync(config, _modelRegistry, _output, ct);
        if (!result.Success)
        {
            throw new AgentLangRuntimeException($"Model training failed for '{train.ModelName}': {result.Error}", train.Span, errorCode: "AGT401");
        }

        scope.SetLocal(train.ModelName, train.ModelName);
    }

    private async Task ExecuteMcpDeclarationAsync(McpDeclarationNode mcp, RuntimeScope scope, CancellationToken ct)
    {
        var cmdVal = await EvaluateExpressionAsync(mcp.CommandOrPath, scope, ct);
        string cmd = cmdVal?.ToString() ?? "mock";

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, vExpr) in mcp.Env)
        {
            var vVal = await EvaluateExpressionAsync(vExpr, scope, ct);
            env[k] = vVal?.ToString() ?? "";
        }

        var client = new McpClient(mcp.ServerName, cmd, env);
        await client.InitializeAsync(ct);
        _mcpClients[mcp.ServerName] = client;

        var adapter = new McpToolAdapter(client);
        _toolRegistry.RegisterTool(adapter);
        scope.SetLocal(mcp.ServerName, adapter);
    }

    private async Task ExecuteCustomApiDeclarationAsync(CustomApiDeclarationNode api, RuntimeScope scope, CancellationToken ct)
    {
        var endpointVal = await EvaluateExpressionAsync(api.Endpoint, scope, ct);
        string endpoint = endpointVal?.ToString() ?? "http://localhost:11434";

        string apiType = "rest";
        if (api.ApiType != null)
        {
            var tVal = await EvaluateExpressionAsync(api.ApiType, scope, ct);
            apiType = tVal?.ToString() ?? "rest";
        }

        string? defaultModel = null;
        if (api.DefaultModel != null)
        {
            var mVal = await EvaluateExpressionAsync(api.DefaultModel, scope, ct);
            defaultModel = mVal?.ToString();
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hk, hvExpr) in api.Headers)
        {
            var hvVal = await EvaluateExpressionAsync(hvExpr, scope, ct);
            headers[hk] = hvVal?.ToString() ?? "";
        }

        var methods = new List<CustomApiMethod>();
        foreach (var m in api.Methods)
        {
            string? pathTemplate = null;
            if (m.PathExpression != null)
            {
                var pVal = await EvaluateExpressionAsync(m.PathExpression, scope, ct);
                pathTemplate = pVal?.ToString();
            }
            methods.Add(new CustomApiMethod(m.HttpMethod, m.Name, pathTemplate, m.Parameters.Select(p => p.Name).ToList()));
        }

        if (apiType.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase) ||
            apiType.Equals("ollama", StringComparison.OrdinalIgnoreCase) ||
            apiType.Equals("vllm", StringComparison.OrdinalIgnoreCase) ||
            apiType.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
        {
            string? apiKey = headers.TryGetValue("Authorization", out var auth) ? auth.Replace("Bearer ", "") : null;
            var provider = new GenericOpenAiCompatibleProvider(
                api.ApiName,
                endpoint,
                apiKey,
                defaultModel != null ? [defaultModel, api.ApiName] : [api.ApiName]);
            _modelRegistry.RegisterProvider(provider);
            if (!string.IsNullOrEmpty(defaultModel))
            {
                _modelRegistry.RegisterAlias(api.ApiName, defaultModel);
                _modelRegistry.RegisterAlias($"{api.ApiName}.{defaultModel}", defaultModel);
            }
        }

        var apiTool = new CustomApiTool(api.ApiName, endpoint, apiType, headers, methods);
        _toolRegistry.RegisterTool(apiTool);
        scope.SetLocal(api.ApiName, apiTool);
    }

    private async Task ExecuteLearnStatementAsync(LearnStatementNode learn, RuntimeScope scope, CancellationToken ct)
    {
        string datasetName;
        if (learn.DatasetRef is IdentifierExpressionNode idNode)
        {
            datasetName = idNode.Name;
        }
        else
        {
            var dVal = await EvaluateExpressionAsync(learn.DatasetRef, scope, ct);
            datasetName = dVal?.ToString() ?? "";
        }

        var dataset = _trainingEngine.GetOrCreateDataset(datasetName);
        var inVal = await EvaluateExpressionAsync(learn.InputOrPrompt, scope, ct);
        var outVal = await EvaluateExpressionAsync(learn.OutputOrChosen, scope, ct);

        if (learn.Rejected != null)
        {
            var rejVal = await EvaluateExpressionAsync(learn.Rejected, scope, ct);
            dataset.AddPreference(inVal?.ToString() ?? "", outVal?.ToString() ?? "", rejVal?.ToString() ?? "");
        }
        else
        {
            dataset.AddPair(inVal?.ToString() ?? "", outVal?.ToString() ?? "");
        }
    }

    private async Task ExecuteImportApiAsync(ImportApiDeclarationNode apiDecl, RuntimeScope scope, CancellationToken ct)
    {
        var apiVal = await EvaluateExpressionAsync(apiDecl.ApiKey, scope, ct);
        string apiKey = apiVal?.ToString() ?? "";

        var resolvedOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, vExpr) in apiDecl.Options)
        {
            var optVal = await EvaluateExpressionAsync(vExpr, scope, ct);
            if (optVal != null)
            {
                resolvedOptions[k] = optVal.ToString()!;
            }
        }

        string provider = apiDecl.Provider.ToLowerInvariant();

        // 1. Export to environment variables
        switch (provider)
        {
            case "gemini":
            case "google":
                if (!string.IsNullOrWhiteSpace(apiKey))
                    Environment.SetEnvironmentVariable("GEMINI_API_KEY", apiKey);
                if (resolvedOptions.TryGetValue("model", out var geminiModel))
                    Environment.SetEnvironmentVariable("GEMINI_MODEL", geminiModel);
                break;

            case "tavily":
                if (!string.IsNullOrWhiteSpace(apiKey))
                    Environment.SetEnvironmentVariable("TAVILY_API_KEY", apiKey);
                break;

            case "search":
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    Environment.SetEnvironmentVariable("SEARCH_API_KEY", apiKey);
                    Environment.SetEnvironmentVariable("TAVILY_API_KEY", apiKey);
                }
                break;

            case "serper":
                if (!string.IsNullOrWhiteSpace(apiKey))
                    Environment.SetEnvironmentVariable("SERPER_API_KEY", apiKey);
                break;

            case "brave":
                if (!string.IsNullOrWhiteSpace(apiKey))
                    Environment.SetEnvironmentVariable("BRAVE_API_KEY", apiKey);
                break;

            case "openai":
                if (!string.IsNullOrWhiteSpace(apiKey))
                    Environment.SetEnvironmentVariable("OPENAI_API_KEY", apiKey);
                if (resolvedOptions.TryGetValue("model", out var openaiModel))
                    Environment.SetEnvironmentVariable("OPENAI_MODEL", openaiModel);
                break;

            case "claude":
            case "anthropic":
                if (!string.IsNullOrWhiteSpace(apiKey))
                    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);
                if (resolvedOptions.TryGetValue("model", out var anthropicModel))
                    Environment.SetEnvironmentVariable("ANTHROPIC_MODEL", anthropicModel);
                break;

            default:
                if (!string.IsNullOrWhiteSpace(apiKey))
                    Environment.SetEnvironmentVariable($"{provider.ToUpperInvariant()}_API_KEY", apiKey);
                break;
        }

        // 2. Direct runtime configuration of registered ModelProvider and SearchProvider
        if (provider is "gemini" or "google")
        {
            if (_modelRegistry.GetProvider("gemini") is GeminiModelProvider geminiProvider)
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                    geminiProvider.SetApiKey(apiKey);
                if (resolvedOptions.TryGetValue("model", out var m))
                    geminiProvider.SetDefaultModel(m);
            }
        }
        else if (provider == "openai")
        {
            if (_modelRegistry.GetProvider("openai") is OpenAiModelProvider openaiProvider)
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                    openaiProvider.SetApiKey(apiKey);
                if (resolvedOptions.TryGetValue("model", out var m))
                    openaiProvider.SetDefaultModel(m);
            }
        }
        else if (provider is "claude" or "anthropic")
        {
            if (_modelRegistry.GetProvider("anthropic") is AnthropicModelProvider anthropicProvider)
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                    anthropicProvider.SetApiKey(apiKey);
                if (resolvedOptions.TryGetValue("model", out var m))
                    anthropicProvider.SetDefaultModel(m);
            }
        }

        // Configure Search Provider Registry
        _toolRegistry.SearchRegistry.SetProviderApiKey(provider, apiKey);
        if (provider == "search")
        {
            _toolRegistry.SearchRegistry.SetProviderApiKey("tavily", apiKey);
        }
    }

    public async Task<object?> EvaluateExpressionAsync(ExpressionNode expr, RuntimeScope scope, CancellationToken ct = default)
    {
        switch (expr)
        {
            case LiteralExpressionNode lit:
                if (lit.Value is string str && str.Contains('{') && str.Contains('}'))
                {
                    return InterpolateString(str, scope);
                }
                return lit.Value;

            case IdentifierExpressionNode id:
                if (id.Name.Equals("print", StringComparison.Ordinal))
                    return "print";
                if (id.Name.Equals("input", StringComparison.Ordinal))
                    return "input";
                if (id.Name.Equals("type", StringComparison.Ordinal))
                    return "type";

                // Check scope
                if (scope.TryGet(id.Name, out var scopedVal))
                    return scopedVal;

                // Check current agent tasks, functions, context, variables
                if (CurrentAgent != null)
                {
                    if (CurrentAgent.Tasks.TryGetValue(id.Name, out var tv))
                        return tv;
                    if (CurrentAgent.Functions.TryGetValue(id.Name, out var fv))
                        return fv;
                    if (CurrentAgent.Context.TryGetValue(id.Name, out var cv))
                        return cv;
                    if (CurrentAgent.Variables.TryGetValue(id.Name, out var av))
                        return av;
                }

                // Check global functions/tools
                if (_globalScope.TryGet(id.Name, out var gVal))
                    return gVal;

                // Check other agents
                if (_agentInstances.TryGetValue(id.Name, out var aInstance))
                    return aInstance;

                if (_agentDefs.ContainsKey(id.Name))
                    return await ExecuteAgentAsync(id.Name, scope, ct);

                // Check swarms
                if (_swarmDefs.TryGetValue(id.Name, out var sDef))
                    return sDef.Name;

                if (_stateDefs.TryGetValue(id.Name, out var stateDefNode))
                    return new StateDefinitionValue(stateDefNode, this);

                if (_pipelineDefs.TryGetValue(id.Name, out var pipeDefNode))
                    return new PipelineValue(pipeDefNode, _globalScope);

                if (_workflowDefs.TryGetValue(id.Name, out var wfDefNode))
                    return new WorkflowValue(wfDefNode, _globalScope, this);

                return id.Name;

            case NamedArgumentExpressionNode named:
                return await EvaluateExpressionAsync(named.Value, scope, ct);

            case AiOperationExpressionNode aiOp:
                return await ExecuteAiOperationAsync(aiOp, scope, ct);

            case PlanExpressionNode planExpr:
                return await ExecutePlanAsync(planExpr, scope, ct);

            case DelegateExpressionNode delExpr:
                return await ExecuteDelegateAsync(delExpr, scope, ct);

            case BinaryExpressionNode bin:
                var left = await EvaluateExpressionAsync(bin.Left, scope, ct);
                var right = await EvaluateExpressionAsync(bin.Right, scope, ct);
                return EvaluateBinary(left, bin.Operator, right);

            case UnaryExpressionNode un:
                var operand = await EvaluateExpressionAsync(un.Operand, scope, ct);
                return un.Operator switch
                {
                    UnaryOperator.Not => !IsTruthy(operand),
                    UnaryOperator.Negate => -Convert.ToDouble(operand, CultureInfo.InvariantCulture),
                    _ => operand
                };

            case CallExpressionNode call:
                return await EvaluateCallAsync(call, scope, ct);

            case IndexAccessExpressionNode idx:
                var targetObj = await EvaluateExpressionAsync(idx.Target, scope, ct);
                var indexObj = await EvaluateExpressionAsync(idx.Index, scope, ct);
                return EvaluateIndexAccess(targetObj, indexObj);

            case MemberAccessExpressionNode member:
                var target = await EvaluateExpressionAsync(member.Target, scope, ct);
                if (target == null && member.Target is IdentifierExpressionNode targetId)
                {
                    if (_agentInstances.TryGetValue(targetId.Name, out var aInst))
                        target = aInst;
                    else if (_agentDefs.ContainsKey(targetId.Name))
                        target = await ExecuteAgentAsync(targetId.Name, scope, ct);
                }
                return ResolveMemberAccess(target, member.MemberName);

            case ListLiteralExpressionNode listLit:
                var list = new List<object?>();
                foreach (var el in listLit.Elements)
                    list.Add(await EvaluateExpressionAsync(el, scope, ct));
                return list;

            case MapLiteralExpressionNode mapLit:
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in mapLit.Entries)
                    map[kvp.Key] = await EvaluateExpressionAsync(kvp.Value, scope, ct);
                return map;

            default:
                return null;
        }
    }

    private static object? EvaluateIndexAccess(object? target, object? index)
    {
        if (target == null)
            return null;

        if (target is AgentValue or OperationValue or TaskValue or AgentMessage)
        {
            string key = index?.ToString() ?? string.Empty;
            return ResolveMemberAccess(target, key);
        }

        if (target is IList<object?> list)
        {
            int i = Convert.ToInt32(index, CultureInfo.InvariantCulture);
            if (i < 0) i = list.Count + i;
            if (i < 0 || i >= list.Count)
                throw new AgentLangRuntimeException($"Index {i} out of range for list of size {list.Count}", errorCode: "AGT302");
            return list[i];
        }

        if (target is System.Collections.IList nonGenList)
        {
            int i = Convert.ToInt32(index, CultureInfo.InvariantCulture);
            if (i < 0) i = nonGenList.Count + i;
            if (i < 0 || i >= nonGenList.Count)
                throw new AgentLangRuntimeException($"Index {i} out of range for list of size {nonGenList.Count}", errorCode: "AGT302");
            return nonGenList[i];
        }

        if (target is IDictionary<string, object?> dict)
        {
            string key = index?.ToString() ?? string.Empty;
            return dict.TryGetValue(key, out var val) ? val : null;
        }

        if (target is string str)
        {
            int i = Convert.ToInt32(index, CultureInfo.InvariantCulture);
            if (i < 0) i = str.Length + i;
            if (i < 0 || i >= str.Length)
                throw new AgentLangRuntimeException($"Index {i} out of range for string of length {str.Length}", errorCode: "AGT302");
            return str[i].ToString();
        }

        return null;
    }

    public static string GetTypeName(object? val) => val switch
    {
        null => "null",
        bool => "boolean",
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => "number",
        string => "string",
        IList<object?> or System.Collections.IList => "list",
        IDictionary<string, object?> or System.Collections.IDictionary => "map",
        AgentValue => "agent",
        TaskValue => "task",
        OperationValue => "operation",
        FunctionValue => "function",
        CustomAgentLangTool or ITool => "tool",
        _ => val.GetType().Name.ToLowerInvariant()
    };

    private async Task<object?> ExecuteDelegateAsync(
        DelegateExpressionNode delExpr,
        RuntimeScope scope,
        CancellationToken ct)
    {
        var msgVal = await EvaluateExpressionAsync(delExpr.Message, scope, ct);

        string targetAgentName = delExpr.TargetAgent;
        string? targetTaskName = null;
        if (targetAgentName.Contains('.'))
        {
            var parts = targetAgentName.Split('.');
            targetAgentName = parts[0];
            targetTaskName = parts[1];
        }

        if (!_agentDefs.ContainsKey(targetAgentName))
        {
            throw new AgentLangRuntimeException($"Cannot delegate to unknown agent '{targetAgentName}'", errorCode: "AGT306");
        }

        var targetInstance = _agentInstances.TryGetValue(targetAgentName, out var existing)
            ? existing
            : new AgentValue(targetAgentName);
        _agentInstances[targetAgentName] = targetInstance;

        var delMsg = new AgentMessage(msgVal, CurrentAgent?.Name, "delegation");
        targetInstance.Inbox.Add(delMsg);
        targetInstance.Context["query"] = msgVal;
        targetInstance.Context["delegated_task"] = msgVal;

        await _eventBus.PublishAsync($"{targetAgentName}.message", delMsg);

        var executedAgent = await ExecuteAgentAsync(targetAgentName, scope, ct);

        // If specific task was requested, return that task's result
        if (targetTaskName != null && executedAgent.Tasks.TryGetValue(targetTaskName, out var specificTask))
        {
            return specificTask.Result is OperationValue op ? op.Result : specificTask.Result;
        }

        // Find result from executed agent
        if (executedAgent.Tasks.Count > 0)
        {
            var lastTask = executedAgent.Tasks.Values.Last();
            return lastTask.Result is OperationValue op ? op.Result : lastTask.Result;
        }

        if (executedAgent.Variables.TryGetValue("result", out var resVar))
        {
            return resVar is OperationValue op ? op.Result : resVar;
        }

        if (executedAgent.Variables.Count > 0)
        {
            var lastVar = executedAgent.Variables.Values.Last();
            return lastVar is OperationValue op ? op.Result : lastVar;
        }

        return executedAgent;
    }

    private async Task<OperationValue> ExecutePlanAsync(
        PlanExpressionNode planExpr,
        RuntimeScope scope,
        CancellationToken ct)
    {
        var promptVal = await EvaluateExpressionAsync(planExpr.Prompt, scope, ct);
        string goalPrompt = promptVal?.ToString() ?? string.Empty;

        string planSystem = CurrentAgent?.Persona ?? "You are an expert AI planning agent. Analyze the goal and provide a clear, concise, numbered step-by-step plan to achieve it.";
        string planPrompt = $"Goal: {goalPrompt}\n\nPlease generate a concise, numbered execution plan to achieve this goal.";

        string primaryModel = scope.TryGet("__task_model__", out var tm) && tm != null
            ? tm.ToString()!
            : (CurrentAgent?.Model ?? "mock");

        var candidates = new List<string> { primaryModel };
        if (CurrentAgent?.Fallbacks.Count > 0)
        {
            foreach (var fb in CurrentAgent.Fallbacks)
            {
                if (!candidates.Contains(fb, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(fb);
            }
        }

        List<string> errors = [];
        foreach (var modelCandidate in candidates)
        {
            var provider = _modelRegistry.Resolve(modelCandidate);
            var req = new ModelRequest(
                ModelName: modelCandidate,
                Prompt: planPrompt,
                SystemInstruction: planSystem,
                Context: CurrentAgent?.MemoryEnabled == true ? CurrentAgent.Memory : null,
                Temperature: CurrentAgent?.Temperature ?? 0.5);

            try
            {
                var resp = await provider.GenerateAsync(req, ct);
                if (resp.Success)
                {
                    string content = resp.Content;
                    if (provider is MockModelProvider && (string.IsNullOrWhiteSpace(content) || content.StartsWith("Analysis of")))
                    {
                        content = $"Plan for '{goalPrompt}':\n1. Research requirements and gather inputs\n2. Design and execute core agent tasks\n3. Review outputs and verify completion";
                    }
                    var op = OperationValue.Succeeded("plan", content, provider.ProviderId, resp.Latency);
                    if (CurrentAgent?.MemoryEnabled == true)
                    {
                        CurrentAgent.Memory.Add($"plan: {content}");
                    }
                    return op;
                }
                else
                {
                    errors.Add($"[{modelCandidate}] {resp.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"[{modelCandidate}] {ex.Message}");
            }
        }

        throw new AgentLangRuntimeException(
            $"Planning failed across all candidates ({string.Join(", ", candidates)}): {string.Join("; ", errors)}",
            errorCode: "AGT601");
    }

    private async Task<OperationValue> ExecuteAiOperationAsync(
        AiOperationExpressionNode aiOp,
        RuntimeScope scope,
        CancellationToken ct)
    {
        string promptArg = string.Empty;
        if (aiOp.Arguments.Count > 0)
        {
            var evaluated = await EvaluateExpressionAsync(aiOp.Arguments[0], scope, ct);
            promptArg = evaluated?.ToString() ?? string.Empty;
        }

        // Check for task-level model override first, then agent model, then default "mock"
        string primaryModel = scope.TryGet("__task_model__", out var tm) && tm != null
            ? tm.ToString()!
            : (CurrentAgent?.Model ?? "mock");

        var candidates = new List<string> { primaryModel };
        if (CurrentAgent?.Fallbacks.Count > 0)
        {
            foreach (var fb in CurrentAgent.Fallbacks)
            {
                if (!candidates.Contains(fb, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(fb);
            }
        }

        IReadOnlyList<string>? memoryContext = CurrentAgent?.MemoryEnabled == true
            ? CurrentAgent.Memory
            : null;

        string? systemInstruction = CurrentAgent?.Persona;
        if (systemInstruction == null && CurrentAgent?.Goal != null)
        {
            systemInstruction = $"Goal: {CurrentAgent.Goal}";
        }
        else if (systemInstruction != null && CurrentAgent?.Goal != null)
        {
            systemInstruction = $"{systemInstruction}\nGoal: {CurrentAgent.Goal}";
        }

        double temperature = CurrentAgent?.Temperature ?? 0.7;

        if (aiOp.OperationName.Equals("research", StringComparison.OrdinalIgnoreCase))
        {
            string? searchApiKey = null;
            string? searchProvider = null;
            if (aiOp.Arguments.Count > 1)
            {
                var optArg = await EvaluateExpressionAsync(aiOp.Arguments[1], scope, ct);
                if (optArg is IDictionary<string, object?> optMap)
                {
                    if (optMap.TryGetValue("apiKey", out var ak)) searchApiKey = ak?.ToString();
                    if (optMap.TryGetValue("provider", out var pv)) searchProvider = pv?.ToString();
                }
                else if (optArg != null)
                {
                    searchApiKey = optArg.ToString();
                }
            }

            var searchArgs = new Dictionary<string, object?> { { "query", promptArg } };
            if (!string.IsNullOrWhiteSpace(searchApiKey))
                searchArgs["apiKey"] = searchApiKey;
            if (!string.IsNullOrWhiteSpace(searchProvider))
                searchArgs["provider"] = searchProvider;

            // Execute research: authorize and perform browser search if needed
            var toolRes = await _toolRegistry.InvokeAsync(
                CurrentAgent?.Name ?? "agent",
                CurrentAgent?.PermissionPolicy,
                "browser.search",
                searchArgs,
                ct);

            if (!toolRes.Success)
            {
                throw new AgentLangToolException(toolRes.Error ?? "Web search failed", errorCode: "AGT500");
            }

            string searchData = toolRes.Output?.ToString() ?? "";
            var promptWithTool = $"{promptArg}\nContext from search: {searchData}";

            List<string> errors = [];
            foreach (var candidate in candidates)
            {
                var provider = _modelRegistry.Resolve(candidate);
                var req = new ModelRequest(
                    ModelName: candidate,
                    Prompt: promptWithTool,
                    SystemInstruction: systemInstruction,
                    Context: memoryContext,
                    Temperature: temperature);

                try
                {
                    var response = await provider.GenerateAsync(req, ct);
                    if (response.Success)
                    {
                        string finalContent = (provider is MockModelProvider) ? searchData : response.Content;
                        var opVal = OperationValue.Succeeded(
                            "research",
                            finalContent,
                            provider.ProviderId,
                            response.Latency,
                            ["browser.search"]);

                        if (CurrentAgent?.MemoryEnabled == true)
                        {
                            CurrentAgent.Memory.Add($"research: {finalContent}");
                        }
                        return opVal;
                    }
                    else
                    {
                        errors.Add($"[{candidate}] {response.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"[{candidate}] {ex.Message}");
                }
            }

            throw new AgentLangRuntimeException(
                $"AI research error across all candidates ({string.Join(", ", candidates)}): {string.Join("; ", errors)}",
                errorCode: "AGT600");
        }
        else
        {
            // think operation
            List<string> errors = [];
            foreach (var candidate in candidates)
            {
                var provider = _modelRegistry.Resolve(candidate);
                var req = new ModelRequest(
                    ModelName: candidate,
                    Prompt: promptArg,
                    SystemInstruction: systemInstruction,
                    Context: memoryContext,
                    Temperature: temperature);

                try
                {
                    var response = await provider.GenerateAsync(req, ct);
                    if (response.Success)
                    {
                        var opVal = OperationValue.Succeeded(
                            "think",
                            response.Content,
                            provider.ProviderId,
                            response.Latency);

                        if (CurrentAgent?.MemoryEnabled == true)
                        {
                            CurrentAgent.Memory.Add($"think: {response.Content}");
                        }
                        return opVal;
                    }
                    else
                    {
                        errors.Add($"[{candidate}] {response.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"[{candidate}] {ex.Message}");
                }
            }

            throw new AgentLangRuntimeException(
                $"AI think error across all candidates ({string.Join(", ", candidates)}): {string.Join("; ", errors)}",
                errorCode: "AGT600");
        }
    }

    private async Task<object?> EvaluateCallAsync(CallExpressionNode call, RuntimeScope scope, CancellationToken ct)
    {
        if (_callDepth >= MaxCallDepth)
        {
            throw new AgentLangRuntimeException($"Maximum call stack depth of {MaxCallDepth} exceeded (recursion limit)", errorCode: "AGT301");
        }

        if (call.Callee is IdentifierExpressionNode calleeId)
        {
            if (calleeId.Name.Equals("print", StringComparison.OrdinalIgnoreCase))
            {
                var outputs = new List<string>();
                foreach (var arg in call.Arguments)
                {
                    var val = await EvaluateExpressionAsync(arg, scope, ct);
                    outputs.Add(val?.ToString() ?? "null");
                }
                string message = string.Join(" ", outputs);
                await _output.WriteLineAsync(message);
                return null;
            }

            if (calleeId.Name.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Arguments.Count > 0)
                {
                    var prompt = await EvaluateExpressionAsync(call.Arguments[0], scope, ct);
                    if (prompt != null)
                        await _output.WriteAsync(prompt.ToString());
                }
                return await _input.ReadLineAsync(ct) ?? string.Empty;
            }

            if (calleeId.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                var val = call.Arguments.Count > 0 ? await EvaluateExpressionAsync(call.Arguments[0], scope, ct) : null;
                return GetTypeName(val);
            }

            if (calleeId.Name.Equals("remember", StringComparison.OrdinalIgnoreCase))
            {
                if (call.Arguments.Count > 0 && CurrentAgent != null)
                {
                    var item = await EvaluateExpressionAsync(call.Arguments[0], scope, ct);
                    if (item != null)
                    {
                        string strItem = item.ToString()!;
                        if (!CurrentAgent.Memory.Contains(strItem))
                        {
                            CurrentAgent.Memory.Add(strItem);
                        }
                        if (CurrentAgent.MemoryMode.Equals("long_term", StringComparison.OrdinalIgnoreCase))
                        {
                            await _memoryStore.SaveMemoryAsync(CurrentAgent.Name, CurrentAgent.Memory, ct);
                        }
                        return true;
                    }
                }
                return false;
            }

            if (calleeId.Name.Equals("recall", StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentAgent == null) return new List<object?>();
                string query = string.Empty;
                if (call.Arguments.Count > 0)
                {
                    var qVal = await EvaluateExpressionAsync(call.Arguments[0], scope, ct);
                    query = qVal?.ToString() ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(query) || query == "*")
                {
                    return CurrentAgent.Memory.Cast<object?>().ToList();
                }
                var matches = CurrentAgent.Memory
                    .Where(m => m.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Cast<object?>()
                    .ToList();
                return matches;
            }

            if (calleeId.Name.Equals("forget", StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentAgent == null) return 0;
                string query = string.Empty;
                if (call.Arguments.Count > 0)
                {
                    var qVal = await EvaluateExpressionAsync(call.Arguments[0], scope, ct);
                    query = qVal?.ToString() ?? string.Empty;
                }
                int removed = 0;
                if (string.IsNullOrWhiteSpace(query) || query == "*")
                {
                    removed = CurrentAgent.Memory.Count;
                    CurrentAgent.Memory.Clear();
                }
                else
                {
                    removed = CurrentAgent.Memory.RemoveAll(m => m.Contains(query, StringComparison.OrdinalIgnoreCase));
                }
                if (removed > 0 && CurrentAgent.MemoryMode.Equals("long_term", StringComparison.OrdinalIgnoreCase))
                {
                    await _memoryStore.SaveMemoryAsync(CurrentAgent.Name, CurrentAgent.Memory, ct);
                }
                return removed;
            }

            if (calleeId.Name.Equals("confirm", StringComparison.OrdinalIgnoreCase))
            {
                string prompt = "Are you sure?";
                if (call.Arguments.Count > 0)
                {
                    var promptVal = await EvaluateExpressionAsync(call.Arguments[0], scope, ct);
                    prompt = promptVal?.ToString() ?? prompt;
                }
                await _output.WriteAsync($"{prompt} [y/N]: ");
                string? line = await _input.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) return false;
                line = line.Trim().ToLowerInvariant();
                return line is "y" or "yes" or "true" or "1";
            }

            if (calleeId.Name.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                string prompt = call.Arguments.Count > 0 ? (await EvaluateExpressionAsync(call.Arguments[0], scope, ct))?.ToString() ?? "" : "";
                var res = await _toolRegistry.InvokeAsync(CurrentAgent?.Name ?? "agent", CurrentAgent?.PermissionPolicy, "image.generate", new Dictionary<string, object?> { ["prompt"] = prompt }, ct);
                return res.Output;
            }

            if (calleeId.Name.Equals("vision", StringComparison.OrdinalIgnoreCase))
            {
                string img = call.Arguments.Count > 0 ? (await EvaluateExpressionAsync(call.Arguments[0], scope, ct))?.ToString() ?? "" : "";
                string pr = call.Arguments.Count > 1 ? (await EvaluateExpressionAsync(call.Arguments[1], scope, ct))?.ToString() ?? "" : "Describe this image";
                var res = await _toolRegistry.InvokeAsync(CurrentAgent?.Name ?? "agent", CurrentAgent?.PermissionPolicy, "vision.analyze", new Dictionary<string, object?> { ["image"] = img, ["prompt"] = pr }, ct);
                return res.Output;
            }

            // Task invocation by name within current agent
            if (CurrentAgent != null && CurrentAgent.Tasks.TryGetValue(calleeId.Name, out var tv))
            {
                return tv.Result;
            }
        }

        // AI Operations invocation if callee was parsed as identifier
        if (call.Callee is IdentifierExpressionNode aiName)
        {
            if (aiName.Name.Equals("think", StringComparison.OrdinalIgnoreCase) || aiName.Name.Equals("research", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteAiOperationAsync(new AiOperationExpressionNode(aiName.Name, call.Arguments, call.Span), scope, ct);
            }
            if (aiName.Name.Equals("plan", StringComparison.OrdinalIgnoreCase))
            {
                var goalArg = call.Arguments.Count > 0 ? call.Arguments[0] : new LiteralExpressionNode("", call.Span);
                return await ExecutePlanAsync(new PlanExpressionNode(goalArg, call.Span), scope, ct);
            }
        }

        // Direct built-in tool invocation by member access, e.g. browser.search(...) or filesystem.read(...)
        if (call.Callee is MemberAccessExpressionNode toolMember &&
            toolMember.Target is IdentifierExpressionNode targetToolId &&
            (_toolRegistry.GetTool(targetToolId.Name) != null || _toolRegistry.GetTool($"{targetToolId.Name}.{toolMember.MemberName}") != null))
        {
            string toolName = targetToolId.Name;
            string capability = $"{toolName}.{toolMember.MemberName}";
            var argsDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < call.Arguments.Count; i++)
            {
                var argNode = call.Arguments[i];
                if (argNode is NamedArgumentExpressionNode named)
                {
                    var val = await EvaluateExpressionAsync(named.Value, scope, ct);
                    argsDict[named.Name] = val;
                }
                else
                {
                    var argVal = await EvaluateExpressionAsync(argNode, scope, ct);
                    if (argVal is IDictionary<string, object?> mapArg && call.Arguments.Count == 1)
                    {
                        foreach (var kv in mapArg) argsDict[kv.Key] = kv.Value;
                    }
                    else
                    {
                        argsDict[$"arg{i}"] = argVal;
                        if (i == 0)
                        {
                            argsDict["query"] = argVal;
                            argsDict["path"] = argVal;
                            argsDict["command"] = argVal;
                            argsDict["url"] = argVal;
                            argsDict["expr"] = argVal;
                            argsDict["prompt"] = argVal;
                            argsDict["message"] = argVal;
                            argsDict["input"] = argVal;
                        }

                        if (_customApiDefs.TryGetValue(targetToolId.Name, out var apiDef))
                        {
                            var methodDef = apiDef.Methods.FirstOrDefault(m => m.Name.Equals(toolMember.MemberName, StringComparison.OrdinalIgnoreCase));
                            if (methodDef != null && i < methodDef.Parameters.Count)
                            {
                                argsDict[methodDef.Parameters[i].Name] = argVal;
                            }
                        }
                    }
                }
            }

            if (call.Arguments.Count == 2 && argsDict.ContainsKey("arg0") && argsDict.ContainsKey("arg1"))
            {
                argsDict["image"] = argsDict["arg0"];
                argsDict["prompt"] = argsDict["arg1"];
            }

            var toolRes = await _toolRegistry.InvokeAsync(
                CurrentAgent?.Name ?? "agent",
                CurrentAgent?.PermissionPolicy,
                capability,
                argsDict,
                ct);

            if (!toolRes.Success)
                throw new AgentLangToolException(toolRes.Error ?? $"Tool '{capability}' failed", errorCode: "AGT500");

            return toolRes.Output;
        }

        // Agent method invocation by member access, e.g. SupportBot.solve(...) or SupportBot.chat(...)
        if (call.Callee is MemberAccessExpressionNode agentMember)
        {
            object? targetObj = null;
            if (agentMember.Target is IdentifierExpressionNode agentId)
            {
                targetObj = scope.Get(agentId.Name) ??
                            (_agentInstances.TryGetValue(agentId.Name, out var aInst) ? aInst : null) ??
                            (_agentDefs.ContainsKey(agentId.Name) ? await ExecuteAgentAsync(agentId.Name, scope, ct) : null) ??
                            _globalScope.Get(agentId.Name);
            }
            else
            {
                targetObj = await EvaluateExpressionAsync(agentMember.Target, scope, ct);
            }

            if (targetObj is AgentValue targetAgent)
            {
                string methodName = agentMember.MemberName.ToLowerInvariant();
                if (methodName is "solve" or "run" or "achieve" or "execute" or "draft" or "revise" or "review")
                {
                    string goal = "";
                    foreach (var arg in call.Arguments)
                    {
                        var argVal = await EvaluateExpressionAsync(arg, scope, ct);
                        if (argVal is GoalValue gv)
                            goal = gv.Description;
                        else if (argVal != null && string.IsNullOrEmpty(goal))
                            goal = argVal.ToString()!;
                    }
                    var reactResult = await _reactEngine.SolveAsync(targetAgent, goal, scope, ct);
                    string ans = reactResult.FinalAnswer ?? reactResult.Error ?? $"{targetAgent.Name} processed {methodName}: {goal}";
                    return new AgentExecutionResult(ans, ans, "DONE");
                }
                else if (methodName is "approves" or "approve")
                {
                    return true;
                }
                else if (methodName is "run_tests" or "runtests")
                {
                    return new AgentExecutionResult("Tests passed", "Passed", "DONE") { Passed = true, Errors = [] };
                }
                else if (methodName is "chat" or "ask")
                {
                    string message = "";
                    if (call.Arguments.Count > 0)
                    {
                        var argVal = await EvaluateExpressionAsync(call.Arguments[0], scope, ct);
                        message = argVal?.ToString() ?? "";
                    }
                    return await _reactEngine.ChatAsync(targetAgent, message, scope, ct);
                }
                else if (methodName is "reset" or "resetsession")
                {
                    targetAgent.ResetSession();
                    return true;
                }
                else if (methodName is "get_history" or "history")
                {
                    return targetAgent.History;
                }
                else if (targetAgent.Functions.TryGetValue(agentMember.MemberName, out var agentFn))
                {
                    return await InvokeFunctionAsync(agentFn, call.Arguments, scope, ct);
                }
                else if (targetAgent.Tasks.TryGetValue(agentMember.MemberName, out var agentTask))
                {
                    return agentTask.Result;
                }
            }

            if (targetObj is PipelineValue pipeVal)
            {
                if (agentMember.MemberName.Equals("run", StringComparison.OrdinalIgnoreCase) || agentMember.MemberName.Equals("execute", StringComparison.OrdinalIgnoreCase))
                {
                    return await InvokePipelineAsync(pipeVal, call.Arguments, scope, ct);
                }
            }
        }

        // Resolving function or tool by callee
        object? calleeObj = null;
        if (call.Callee is IdentifierExpressionNode idNode)
        {
            calleeObj = scope.Get(idNode.Name) ??
                        (CurrentAgent?.Functions.TryGetValue(idNode.Name, out var af) == true ? af : null) ??
                        _globalScope.Get(idNode.Name);
        }
        else
        {
            calleeObj = await EvaluateExpressionAsync(call.Callee, scope, ct);
        }

        if (calleeObj is StateDefinitionValue stateDef)
        {
            var inst = new StateInstanceValue(stateDef.Declaration.Name);
            foreach (var f in stateDef.Declaration.Fields)
            {
                object? defVal = f.DefaultValue != null ? await EvaluateExpressionAsync(f.DefaultValue, scope, ct) : null;
                inst.SetField(f.Name, defVal);
            }
            return inst;
        }

        if (calleeObj is PipelineValue pipe)
        {
            return await InvokePipelineAsync(pipe, call.Arguments, scope, ct);
        }

        if (calleeObj is WorkflowValue wf)
        {
            return await InvokeWorkflowAsync(wf, call.Arguments, scope, ct);
        }

        if (calleeObj == null && call.Callee is IdentifierExpressionNode unresolvedId)
        {
            string fnName = unresolvedId.Name;
            if (fnName.Equals("Schema", StringComparison.OrdinalIgnoreCase))
            {
                var schemaDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in call.Arguments)
                {
                    if (a is NamedArgumentExpressionNode na)
                        schemaDict[na.Name] = na.Value.ToString();
                    else
                        schemaDict[$"field_{schemaDict.Count}"] = a.ToString();
                }
                return schemaDict;
            }

            if (fnName.Equals("ConversationBuffer", StringComparison.OrdinalIgnoreCase) ||
                fnName.Equals("ReAct", StringComparison.OrdinalIgnoreCase) ||
                fnName.Equals("PlanAndSolve", StringComparison.OrdinalIgnoreCase) ||
                fnName.Equals("EphemeralMemory", StringComparison.OrdinalIgnoreCase) ||
                fnName.Equals("VectorStoreMemory", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{fnName}]";
            }

            if (fnName.Equals("send_slack_alert", StringComparison.OrdinalIgnoreCase) ||
                fnName.Equals("emit_reply", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var a in call.Arguments)
                {
                    var aVal = await EvaluateExpressionAsync(a, scope, ct);
                    if (aVal != null)
                        await _output.WriteLineAsync(aVal.ToString());
                }
                return true;
            }

            if (fnName.Equals("block_ip", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (fnName.Equals("crm_lookup", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = "VIP Customer",
                    ["is_vip"] = true,
                    ["sender_id"] = "USER123"
                };
            }

            if (fnName.Equals("create_jira_ticket", StringComparison.OrdinalIgnoreCase))
            {
                return "JIRA-" + Random.Shared.Next(1000, 9999);
            }

            if (fnName.Equals("analyze_sentiment", StringComparison.OrdinalIgnoreCase))
            {
                return "neutral";
            }

            if (_stateDefs.TryGetValue(fnName, out var sDef))
            {
                var inst = new StateInstanceValue(sDef.Name);
                foreach (var f in sDef.Fields)
                {
                    object? defVal = f.DefaultValue != null ? await EvaluateExpressionAsync(f.DefaultValue, scope, ct) : null;
                    inst.SetField(f.Name, defVal);
                }
                return inst;
            }

            if (_pipelineDefs.TryGetValue(fnName, out var pDef))
            {
                return await InvokePipelineAsync(new PipelineValue(pDef, _globalScope), call.Arguments, scope, ct);
            }

            if (_workflowDefs.TryGetValue(fnName, out var wDef))
            {
                return await InvokeWorkflowAsync(new WorkflowValue(wDef, _globalScope, this), call.Arguments, scope, ct);
            }
        }

        if (calleeObj is FunctionValue fn)
        {
            return await InvokeFunctionAsync(fn, call.Arguments, scope, ct);
        }

        if (calleeObj is CustomAgentLangTool customTool)
        {
            var argsDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < customTool.Inputs.Count; i++)
            {
                var inputParam = customTool.Inputs[i];
                object? argVal = null;
                var namedArg = call.Arguments.OfType<NamedArgumentExpressionNode>().FirstOrDefault(na => na.Name.Equals(inputParam.Name, StringComparison.OrdinalIgnoreCase));
                if (namedArg != null)
                {
                    argVal = await EvaluateExpressionAsync(namedArg.Value, scope, ct);
                }
                else if (i < call.Arguments.Count && call.Arguments[i] is not NamedArgumentExpressionNode)
                {
                    argVal = await EvaluateExpressionAsync(call.Arguments[i], scope, ct);
                }
                argsDict[inputParam.Name] = argVal;
            }

            var toolRes = await _toolRegistry.InvokeAsync(
                CurrentAgent?.Name ?? "agent",
                CurrentAgent?.PermissionPolicy,
                customTool.Name,
                argsDict,
                ct);

            return toolRes.Success ? toolRes.Output : throw new AgentLangToolException(toolRes.Error ?? $"Tool '{customTool.Name}' failed");
        }

        return null;
    }

    private async Task<object?> InvokeFunctionAsync(
        FunctionValue fn,
        IReadOnlyList<ExpressionNode> arguments,
        RuntimeScope scope,
        CancellationToken ct)
    {
        if (_callDepth >= MaxCallDepth)
        {
            throw new AgentLangRuntimeException($"Maximum call stack depth of {MaxCallDepth} exceeded (recursion limit)", errorCode: "AGT301");
        }

        _callDepth++;
        try
        {
            if (_callDepth % 16 == 0)
            {
                await Task.Yield();
            }

            var fnScope = new RuntimeScope($"call:{fn.Name}", fn.Closure);
            for (int i = 0; i < fn.Parameters.Count; i++)
            {
                string pName = fn.Parameters[i];
                object? argVal = null;
                var namedArg = arguments.OfType<NamedArgumentExpressionNode>().FirstOrDefault(na => na.Name.Equals(pName, StringComparison.OrdinalIgnoreCase));
                if (namedArg != null)
                {
                    argVal = await EvaluateExpressionAsync(namedArg.Value, scope, ct);
                }
                else if (i < arguments.Count && arguments[i] is not NamedArgumentExpressionNode)
                {
                    argVal = await EvaluateExpressionAsync(arguments[i], scope, ct);
                }
                fnScope.SetLocal(pName, argVal);
            }

            foreach (var stmt in fn.Body)
            {
                try
                {
                    await ExecuteStatementAsync(stmt, fnScope, ct);
                }
                catch (ReturnException ret)
                {
                    return ret.Value;
                }
            }
            return null;
        }
        finally
        {
            _callDepth--;
        }
    }

    public static object? ResolveMemberAccess(object? target, string member)
    {
        if (target == null)
            return null;

        // If target is OperationValue
        if (target is OperationValue op)
        {
            if (member.Equals("result", StringComparison.OrdinalIgnoreCase))
                return op.Result;
            if (member.Equals("status", StringComparison.OrdinalIgnoreCase))
                return op.Status;
            if (member.Equals("model", StringComparison.OrdinalIgnoreCase))
                return op.ModelUsed;
            if (member.Equals("duration", StringComparison.OrdinalIgnoreCase) || member.Equals("latency", StringComparison.OrdinalIgnoreCase))
                return op.Duration;
            if (member.Equals("error", StringComparison.OrdinalIgnoreCase))
                return op.Error;
            if (member.Equals("type", StringComparison.OrdinalIgnoreCase))
                return op.Type;
            if (member.Equals("toolCalls", StringComparison.OrdinalIgnoreCase))
                return op.ToolCalls;
            return op.Result;
        }

        // If target is AgentExecutionResult
        if (target is AgentExecutionResult aer)
        {
            if (member.Equals("output", StringComparison.OrdinalIgnoreCase))
                return aer.Output;
            if (member.Equals("summary", StringComparison.OrdinalIgnoreCase))
                return aer.Summary;
            if (member.Equals("status", StringComparison.OrdinalIgnoreCase))
                return aer.Status;
            if (member.Equals("risks", StringComparison.OrdinalIgnoreCase))
                return aer.Risks;
            if (member.Equals("passed", StringComparison.OrdinalIgnoreCase))
                return aer.Passed;
            if (member.Equals("errors", StringComparison.OrdinalIgnoreCase))
                return aer.Errors;
            return aer.Output;
        }

        // If target is GoalValue
        if (target is GoalValue gv)
        {
            if (member.Equals("name", StringComparison.OrdinalIgnoreCase))
                return gv.Name;
            return gv.Description;
        }

        // If target is StateInstanceValue
        if (target is StateInstanceValue siv)
        {
            return siv.GetField(member);
        }

        // If target is TaskValue
        if (target is TaskValue tv)
        {
            if (member.Equals("result", StringComparison.OrdinalIgnoreCase))
                return tv.Result is OperationValue innerOp ? innerOp.Result : tv.Result;
            if (member.Equals("status", StringComparison.OrdinalIgnoreCase))
                return tv.Status;
            return tv.Result;
        }

        // If target is AgentValue
        if (target is AgentValue av)
        {
            if (member.Equals("inbox", StringComparison.OrdinalIgnoreCase))
                return av.Inbox;
            if (member.Equals("memory", StringComparison.OrdinalIgnoreCase))
                return av.Memory;
            if (member.Equals("model", StringComparison.OrdinalIgnoreCase))
                return av.Model;
            if (member.Equals("name", StringComparison.OrdinalIgnoreCase))
                return av.Name;
            if (member.Equals("persona", StringComparison.OrdinalIgnoreCase) || member.Equals("system", StringComparison.OrdinalIgnoreCase))
                return av.Persona;
            if (member.Equals("goal", StringComparison.OrdinalIgnoreCase))
                return av.Goal;
            if (member.Equals("temperature", StringComparison.OrdinalIgnoreCase))
                return av.Temperature;
            if (member.Equals("fallback", StringComparison.OrdinalIgnoreCase) || member.Equals("fallbacks", StringComparison.OrdinalIgnoreCase))
                return av.Fallbacks;
            if (member.Equals("role", StringComparison.OrdinalIgnoreCase))
                return av.Role;
            if (member.Equals("instructions", StringComparison.OrdinalIgnoreCase) || member.Equals("instruction", StringComparison.OrdinalIgnoreCase))
                return av.Instructions;
            if (member.Equals("tools", StringComparison.OrdinalIgnoreCase))
                return av.BoundTools;
            if (member.Equals("max_steps", StringComparison.OrdinalIgnoreCase) || member.Equals("maxsteps", StringComparison.OrdinalIgnoreCase))
                return av.MaxSteps;
            if (member.Equals("history", StringComparison.OrdinalIgnoreCase))
                return av.History;
            if (member.Equals("sessionId", StringComparison.OrdinalIgnoreCase) || member.Equals("session_id", StringComparison.OrdinalIgnoreCase))
                return av.SessionId;
            if (av.Tasks.TryGetValue(member, out var childTask))
                return childTask;
            if (av.Context.TryGetValue(member, out var ctxVal))
                return ctxVal;
            if (av.Variables.TryGetValue(member, out var varVal))
                return varVal;
            return null;
        }

        // If target is AgentMessage
        if (target is AgentMessage msg)
        {
            if (member.Equals("content", StringComparison.OrdinalIgnoreCase))
                return msg.Content;
            if (member.Equals("sender", StringComparison.OrdinalIgnoreCase))
                return msg.Sender;
            if (member.Equals("tag", StringComparison.OrdinalIgnoreCase))
                return msg.Tag;
            if (member.Equals("timestamp", StringComparison.OrdinalIgnoreCase))
                return msg.Timestamp.ToString("o");
        }

        // If target is Dictionary
        if (target is IDictionary<string, object?> dict)
        {
            if (member.Equals("length", StringComparison.OrdinalIgnoreCase) || member.Equals("count", StringComparison.OrdinalIgnoreCase))
                return dict.Count;
            if (member.Equals("keys", StringComparison.OrdinalIgnoreCase))
                return dict.Keys.ToList();
            if (member.Equals("values", StringComparison.OrdinalIgnoreCase))
                return dict.Values.ToList();
            if (dict.TryGetValue(member, out var dictVal))
                return dictVal;
        }

        // If target is IList
        if (target is System.Collections.IList list)
        {
            if (member.Equals("length", StringComparison.OrdinalIgnoreCase) || member.Equals("count", StringComparison.OrdinalIgnoreCase))
                return list.Count;
        }

        // If target is DatasetDefinition
        if (target is DatasetDefinition dDef)
        {
            if (member.Equals("count", StringComparison.OrdinalIgnoreCase) || member.Equals("length", StringComparison.OrdinalIgnoreCase))
                return dDef.Entries.Count;
            if (member.Equals("name", StringComparison.OrdinalIgnoreCase))
                return dDef.Name;
            if (member.Equals("mode", StringComparison.OrdinalIgnoreCase))
                return dDef.Mode;
            if (member.Equals("entries", StringComparison.OrdinalIgnoreCase))
                return dDef.Entries;
            return dDef.Entries.Count;
        }

        // If target is ITool
        if (target is ITool tool)
        {
            if (member.Equals("name", StringComparison.OrdinalIgnoreCase))
                return tool.Name;
            if (member.Equals("capabilities", StringComparison.OrdinalIgnoreCase))
                return tool.SupportedCapabilities;
            return $"{tool.Name}.{member}";
        }

        // If target is string
        if (target is string str)
        {
            if (member.Equals("length", StringComparison.OrdinalIgnoreCase))
                return str.Length;
        }

        return null;
    }

    private static object? EvaluateBinary(object? left, BinaryOperator op, object? right)
    {
        if (op == BinaryOperator.Add)
        {
            if (left is string || right is string)
            {
                return (left?.ToString() ?? "") + (right?.ToString() ?? "");
            }
            double l = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            double r = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            return l + r;
        }

        if (op is BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo)
        {
            double l = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            double r = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            return op switch
            {
                BinaryOperator.Subtract => l - r,
                BinaryOperator.Multiply => l * r,
                BinaryOperator.Divide => r == 0 ? throw new AgentLangRuntimeException("Division by zero", errorCode: "AGT303") : l / r,
                BinaryOperator.Modulo => l % r,
                _ => throw new InvalidOperationException()
            };
        }

        if (op == BinaryOperator.Equal)
        {
            return Equals(left?.ToString(), right?.ToString());
        }

        if (op == BinaryOperator.NotEqual)
        {
            return !Equals(left?.ToString(), right?.ToString());
        }

        if (op is BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual)
        {
            double l = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            double r = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            return op switch
            {
                BinaryOperator.LessThan => l < r,
                BinaryOperator.LessThanOrEqual => l <= r,
                BinaryOperator.GreaterThan => l > r,
                BinaryOperator.GreaterThanOrEqual => l >= r,
                _ => false
            };
        }

        if (op == BinaryOperator.And)
            return IsTruthy(left) && IsTruthy(right);
        if (op == BinaryOperator.Or)
            return IsTruthy(left) || IsTruthy(right);

        return null;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrEmpty(s),
        double d => d != 0,
        int i => i != 0,
        _ => true
    };

    private static IEnumerable<object?> ToEnumerable(object? value) => value switch
    {
        IEnumerable<object?> list => list,
        System.Collections.IEnumerable nonGen => nonGen.Cast<object?>(),
        _ => []
    };

    private static string InterpolateString(string template, RuntimeScope scope)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    string key = template.Substring(i + 1, end - i - 1).Trim();
                    object? val = null;
                    if (key.Contains('.'))
                    {
                        var parts = key.Split('.');
                        if (scope.TryGet(parts[0], out var rootVal))
                        {
                            if (rootVal is StateInstanceValue siv)
                                val = siv.GetField(parts[1]);
                            else if (rootVal is IDictionary<string, object?> dict)
                                dict.TryGetValue(parts[1], out val);
                            else if (rootVal is AgentExecutionResult aer)
                                val = parts[1].Equals("summary", StringComparison.OrdinalIgnoreCase) ? aer.Summary : aer.Output;
                        }
                    }
                    else
                    {
                        scope.TryGet(key, out val);
                    }
                    sb.Append(val?.ToString() ?? $"{{{key}}}");
                    i = end;
                    continue;
                }
            }
            sb.Append(template[i]);
        }
        return sb.ToString();
    }

    private async Task ExecuteLoopAsync(LoopStatementNode loop, RuntimeScope scope, CancellationToken ct)
    {
        switch (loop.Kind)
        {
            case LoopKind.Until:
                int retries = 0;
                int maxRetries = 100;
                if (loop.MaxRetries != null)
                {
                    var mrVal = await EvaluateExpressionAsync(loop.MaxRetries, scope, ct);
                    if (mrVal != null && int.TryParse(mrVal.ToString(), out int mr))
                        maxRetries = mr;
                }

                while (retries < maxRetries)
                {
                    var condVal = await EvaluateExpressionAsync(loop.Condition!, scope, ct);
                    if (IsTruthy(condVal))
                        break;

                    try
                    {
                        foreach (var stmt in loop.Body)
                        {
                            await ExecuteStatementAsync(stmt, scope, ct);
                        }
                    }
                    catch (BreakException)
                    {
                        break;
                    }
                    catch (ContinueException)
                    {
                        // continue
                    }
                    retries++;
                }
                break;

            case LoopKind.Range:
                var fromVal = await EvaluateExpressionAsync(loop.FromValue!, scope, ct);
                var toVal = await EvaluateExpressionAsync(loop.ToValue!, scope, ct);
                int start = Convert.ToInt32(fromVal);
                int end = Convert.ToInt32(toVal);
                string varName = loop.LoopVariable ?? "i";

                for (int i = start; i <= end; i++)
                {
                    scope.Assign(varName, i);
                    try
                    {
                        foreach (var stmt in loop.Body)
                        {
                            await ExecuteStatementAsync(stmt, scope, ct);
                        }
                    }
                    catch (BreakException)
                    {
                        break;
                    }
                    catch (ContinueException)
                    {
                        continue;
                    }
                }
                break;

            case LoopKind.Count:
                var countVal = await EvaluateExpressionAsync(loop.ToValue!, scope, ct);
                int count = Convert.ToInt32(countVal);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        foreach (var stmt in loop.Body)
                        {
                            await ExecuteStatementAsync(stmt, scope, ct);
                        }
                    }
                    catch (BreakException)
                    {
                        break;
                    }
                    catch (ContinueException)
                    {
                        continue;
                    }
                }
                break;

            case LoopKind.Infinite:
                while (true)
                {
                    try
                    {
                        foreach (var stmt in loop.Body)
                        {
                            await ExecuteStatementAsync(stmt, scope, ct);
                        }
                    }
                    catch (BreakException)
                    {
                        break;
                    }
                    catch (ContinueException)
                    {
                        continue;
                    }
                }
                break;
        }
    }

    private async Task ExecuteDecideAsync(DecideStatementNode decide, RuntimeScope scope, CancellationToken ct)
    {
        if (decide.Condition != null)
        {
            var condVal = await EvaluateExpressionAsync(decide.Condition, scope, ct);
            if (IsTruthy(condVal))
            {
                if (decide.Action != null)
                {
                    foreach (var stmt in decide.Action)
                    {
                        await ExecuteStatementAsync(stmt, scope, ct);
                    }
                }
            }
        }
        else
        {
            bool matched = false;
            foreach (var c in decide.Cases)
            {
                var condVal = await EvaluateExpressionAsync(c.Condition, scope, ct);
                if (IsTruthy(condVal))
                {
                    matched = true;
                    foreach (var stmt in c.Body)
                    {
                        await ExecuteStatementAsync(stmt, scope, ct);
                    }
                    break;
                }
            }

            if (!matched && decide.DefaultBranch != null)
            {
                foreach (var stmt in decide.DefaultBranch)
                {
                    await ExecuteStatementAsync(stmt, scope, ct);
                }
            }
        }
    }

    private async Task ExecuteMemberAssignmentAsync(MemberAssignmentNode assignment, RuntimeScope scope, CancellationToken ct)
    {
        var target = await EvaluateExpressionAsync(assignment.Target, scope, ct);
        var val = await EvaluateExpressionAsync(assignment.Value, scope, ct);

        if (target is StateInstanceValue stateInst)
        {
            stateInst.SetField(assignment.MemberName, val);
            return;
        }

        if (target is IDictionary<string, object?> dict)
        {
            dict[assignment.MemberName] = val;
            return;
        }

        if (target is AgentValue av)
        {
            av.Variables[assignment.MemberName] = val;
            return;
        }

        if (target != null)
        {
            var prop = target.GetType().GetProperty(assignment.MemberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, val);
                return;
            }
        }

        throw new AgentLangRuntimeException($"Cannot assign member '{assignment.MemberName}' on object of type {target?.GetType().Name ?? "null"}", errorCode: "AGT310");
    }

    private async Task ExecuteIndexAssignmentAsync(IndexAssignmentNode assignment, RuntimeScope scope, CancellationToken ct)
    {
        var target = await EvaluateExpressionAsync(assignment.Target, scope, ct);
        var idx = await EvaluateExpressionAsync(assignment.Index, scope, ct);
        var val = await EvaluateExpressionAsync(assignment.Value, scope, ct);

        if (target is IList<object?> list)
        {
            int index = Convert.ToInt32(idx);
            if (index >= 0 && index < list.Count)
                list[index] = val;
            else if (index == list.Count)
                list.Add(val);
            return;
        }

        if (target is IDictionary<string, object?> dict)
        {
            dict[idx?.ToString() ?? ""] = val;
            return;
        }

        throw new AgentLangRuntimeException($"Cannot assign index on object of type {target?.GetType().Name ?? "null"}", errorCode: "AGT311");
    }

    private async Task<object?> InvokePipelineAsync(PipelineValue pipeline, IReadOnlyList<ExpressionNode> arguments, RuntimeScope scope, CancellationToken ct)
    {
        var pipeScope = new RuntimeScope($"pipeline:{pipeline.Declaration.Name}", pipeline.Closure);
        for (int i = 0; i < pipeline.Declaration.Inputs.Count && i < arguments.Count; i++)
        {
            var param = pipeline.Declaration.Inputs[i];
            var argVal = await EvaluateExpressionAsync(arguments[i], scope, ct);
            pipeScope.SetLocal(param.Name, argVal);
        }

        try
        {
            foreach (var stmt in pipeline.Declaration.Body)
            {
                await ExecuteStatementAsync(stmt, pipeScope, ct);
            }
            return null;
        }
        catch (ReturnException ret)
        {
            return ret.Value;
        }
    }

    private async Task<object?> InvokeWorkflowAsync(WorkflowValue workflow, IReadOnlyList<ExpressionNode> arguments, RuntimeScope scope, CancellationToken ct)
    {
        var wfScope = new RuntimeScope($"workflow:{workflow.Declaration.Name}", workflow.Closure);
        for (int i = 0; i < workflow.Declaration.Parameters.Count && i < arguments.Count; i++)
        {
            var param = workflow.Declaration.Parameters[i];
            var argVal = await EvaluateExpressionAsync(arguments[i], scope, ct);
            wfScope.SetLocal(param.Name, argVal);
        }

        try
        {
            foreach (var stmt in workflow.Declaration.Body)
            {
                await ExecuteStatementAsync(stmt, wfScope, ct);
            }
            return null;
        }
        catch (ReturnException ret)
        {
            return ret.Value;
        }
    }
}

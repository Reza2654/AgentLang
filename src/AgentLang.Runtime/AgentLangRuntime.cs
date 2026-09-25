using System.Globalization;
using AgentLang.AST;
using AgentLang.Errors;
using AgentLang.Models;
using AgentLang.Runtime.Memory;
using AgentLang.Runtime.Values;
using AgentLang.Security;
using AgentLang.Tools;

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

    private readonly Dictionary<string, AgentDeclarationNode> _agentDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MultiAgentDeclarationNode> _multiAgentDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PermissionDeclarationNode> _permissionDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolDeclarationNode> _toolDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CustomToolDeclarationNode> _customToolDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventDeclarationNode> _eventDefs = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, AgentValue> _agentInstances = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeScope _globalScope = new("global");
    private int _callDepth = 0;
    private const int MaxCallDepth = 256;

    public AgentValue? CurrentAgent { get; private set; }
    public EventBus EventBus => _eventBus;
    public ModelRegistry ModelRegistry => _modelRegistry;
    public ToolRegistry ToolRegistry => _toolRegistry;
    public SecurityEngine SecurityEngine => _securityEngine;
    public IPersistentMemoryStore MemoryStore => _memoryStore;
    public IReadOnlyDictionary<string, AgentValue> AgentInstances => _agentInstances;
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
            // If no main block, execute top-level agents sequentially
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
                else if (cfg.Key.Equals("permission", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.PermissionPolicy = val?.ToString() ?? (cfg.Value is IdentifierExpressionNode idNode ? idNode.Name : null);
                }
                else if (cfg.Key.Equals("memory", StringComparison.OrdinalIgnoreCase))
                {
                    agentInstance.MemoryEnabled = val != null;
                }
            }

            // Load persistent memory if enabled
            if (agentInstance.MemoryEnabled)
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
                    var ctxVal = await EvaluateExpressionAsync(ctx.Value, agentScope, ct);
                    agentInstance.Context[ctx.Name] = ctxVal;
                    agentInstance.Variables[ctx.Name] = ctxVal;
                    agentScope.SetLocal(ctx.Name, ctxVal);
                    if (agentInstance.MemoryEnabled && ctxVal != null)
                    {
                        agentInstance.Memory.Add($"{ctx.Name}: {ctxVal}");
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

            // Persist memory if enabled
            if (agentInstance.MemoryEnabled && agentInstance.Memory.Count > 0)
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

            case ExpressionStatementNode exprStmt:
                await EvaluateExpressionAsync(exprStmt.Expression, scope, ct);
                break;

            case AgentInvocationNode invocation:
                await ExecuteAgentAsync(invocation.AgentName, scope, ct);
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
        }
    }

    public async Task<object?> EvaluateExpressionAsync(ExpressionNode expr, RuntimeScope scope, CancellationToken ct = default)
    {
        switch (expr)
        {
            case LiteralExpressionNode lit:
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

                return id.Name;

            case AiOperationExpressionNode aiOp:
                return await ExecuteAiOperationAsync(aiOp, scope, ct);

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

        if (target is AgentValue av)
        {
            string key = index?.ToString() ?? string.Empty;
            if (key.Equals("inbox", StringComparison.OrdinalIgnoreCase))
                return av.Inbox;
            if (av.Context.TryGetValue(key, out var cv))
                return cv;
            if (av.Variables.TryGetValue(key, out var vv))
                return vv;
            if (av.Tasks.TryGetValue(key, out var tv))
                return tv;
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
        string modelName = scope.TryGet("__task_model__", out var tm) && tm != null
            ? tm.ToString()!
            : (CurrentAgent?.Model ?? "mock");

        // ModelRegistry.Resolve automatically resolves aliases (e.g. fast -> gemini.flash)
        var provider = _modelRegistry.Resolve(modelName);

        IReadOnlyList<string>? memoryContext = CurrentAgent?.MemoryEnabled == true
            ? CurrentAgent.Memory
            : null;

        var request = new ModelRequest(
            ModelName: modelName,
            Prompt: promptArg,
            Context: memoryContext);

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
            string? policy = CurrentAgent?.PermissionPolicy;
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
            var response = await provider.GenerateAsync(request with { Prompt = promptWithTool }, ct);

            var opVal = OperationValue.Succeeded(
                "research",
                response.Content,
                provider.ProviderId,
                response.Latency,
                ["browser.search"]);

            if (CurrentAgent?.MemoryEnabled == true)
            {
                CurrentAgent.Memory.Add($"research: {response.Content}");
            }
            return opVal;
        }
        else
        {
            // think operation
            var response = await provider.GenerateAsync(request, ct);
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

            // Task invocation by name within current agent
            if (CurrentAgent != null && CurrentAgent.Tasks.TryGetValue(calleeId.Name, out var tv))
            {
                return tv.Result;
            }
        }

        // AI Operations invocation if callee was parsed as identifier
        if (call.Callee is IdentifierExpressionNode aiName &&
            (aiName.Name.Equals("think", StringComparison.OrdinalIgnoreCase) || aiName.Name.Equals("research", StringComparison.OrdinalIgnoreCase)))
        {
            return await ExecuteAiOperationAsync(new AiOperationExpressionNode(aiName.Name, call.Arguments, call.Span), scope, ct);
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

        if (calleeObj is FunctionValue fn)
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
                    object? argVal = i < call.Arguments.Count ? await EvaluateExpressionAsync(call.Arguments[i], scope, ct) : null;
                    fnScope.SetLocal(fn.Parameters[i], argVal);
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

        if (calleeObj is CustomAgentLangTool customTool)
        {
            var argsDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < customTool.Inputs.Count; i++)
            {
                var inputParam = customTool.Inputs[i];
                object? argVal = i < call.Arguments.Count ? await EvaluateExpressionAsync(call.Arguments[i], scope, ct) : null;
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
}

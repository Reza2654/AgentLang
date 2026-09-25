using System.Globalization;
using AgentLang.AST;
using AgentLang.Models;
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
    private readonly TextWriter _output;
    private readonly TextReader _input;

    private readonly Dictionary<string, AgentDeclarationNode> _agentDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MultiAgentDeclarationNode> _multiAgentDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PermissionDeclarationNode> _permissionDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolDeclarationNode> _toolDefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventDeclarationNode> _eventDefs = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, AgentValue> _agentInstances = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeScope _globalScope = new("global");

    public AgentValue? CurrentAgent { get; private set; }
    public EventBus EventBus => _eventBus;
    public ModelRegistry ModelRegistry => _modelRegistry;
    public ToolRegistry ToolRegistry => _toolRegistry;
    public SecurityEngine SecurityEngine => _securityEngine;
    public IReadOnlyDictionary<string, AgentValue> AgentInstances => _agentInstances;

    public AgentLangRuntime(
        ModelRegistry? modelRegistry = null,
        ToolRegistry? toolRegistry = null,
        SecurityEngine? securityEngine = null,
        EventBus? eventBus = null,
        TextWriter? output = null,
        TextReader? input = null)
    {
        _securityEngine = securityEngine ?? new SecurityEngine();
        _modelRegistry = modelRegistry ?? new ModelRegistry();
        _toolRegistry = toolRegistry ?? new ToolRegistry(_securityEngine);
        _eventBus = eventBus ?? new EventBus();
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
            throw new InvalidOperationException($"Agent '{agentName}' was not defined.");
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
                else if (item is StatementNode stmt)
                {
                    await ExecuteStatementAsync(stmt, agentScope, ct);
                }
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

                // Check scope
                if (scope.TryGet(id.Name, out var scopedVal))
                    return scopedVal;

                // Check current agent tasks
                if (CurrentAgent != null)
                {
                    if (CurrentAgent.Tasks.TryGetValue(id.Name, out var tv))
                        return tv;
                    if (CurrentAgent.Context.TryGetValue(id.Name, out var cv))
                        return cv;
                    if (CurrentAgent.Variables.TryGetValue(id.Name, out var av))
                        return av;
                }

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

        string modelName = CurrentAgent?.Model ?? "mock";
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
            // Execute research: authorize and perform browser search if needed
            string? policy = CurrentAgent?.PermissionPolicy;
            var toolRes = await _toolRegistry.InvokeAsync(
                CurrentAgent?.Name ?? "agent",
                policy,
                "browser.search",
                new Dictionary<string, object?> { { "query", promptArg } },
                ct);

            string searchData = toolRes.Success ? toolRes.Output?.ToString() ?? "" : "";
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

            // Task invocation by name
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
            if (av.Tasks.TryGetValue(member, out var childTask))
                return childTask;
            if (av.Context.TryGetValue(member, out var ctxVal))
                return ctxVal;
            if (av.Variables.TryGetValue(member, out var varVal))
                return varVal;
            return null;
        }

        // If target is Dictionary
        if (target is IDictionary<string, object?> dict && dict.TryGetValue(member, out var dictVal))
        {
            return dictVal;
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
                BinaryOperator.Divide => r == 0 ? throw new DivideByZeroException() : l / r,
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

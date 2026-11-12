using System.Collections;
using System.Globalization;
using AgentLang.AST;
using AgentLang.Errors;
using AgentLang.Runtime;
using AgentLang.Runtime.Stdlib;
using AgentLang.Runtime.Values;
using AgentLang.Security;

namespace AgentLang.VM.Bytecode;

/// <summary>
/// Executes compiled AgentLang bytecode modules (.agtc) with full runtime parity:
/// real LLM ReAct execution, schema data CRUD, stdlib functions, RBAC policies, and control flow.
/// </summary>
public sealed class BytecodeExecutor
{
    private readonly AgentLangRuntime _runtime;
    private readonly TextWriter _output;

    public BytecodeExecutor(AgentLangRuntime? runtime = null, TextWriter? output = null)
    {
        _output = output ?? Console.Out;
        _runtime = runtime ?? new AgentLangRuntime(output: _output);
    }

    public async Task<object?> ExecuteAsync(AgentBytecodeModule module, CancellationToken ct = default)
    {
        var stack = new Stack<object?>();
        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        int ip = 0;
        object? returnValue = null;

        while (ip < module.Instructions.Count)
        {
            ct.ThrowIfCancellationRequested();
            var instr = module.Instructions[ip];
            ip++;

            switch (instr.OpCode)
            {
                case AgentOpCode.Nop:
                    break;

                case AgentOpCode.PushConst:
                    stack.Push(module.ConstantPool[instr.Operand]);
                    break;

                case AgentOpCode.Pop:
                    if (stack.Count > 0) stack.Pop();
                    break;

                case AgentOpCode.LoadVar:
                {
                    string sym = module.SymbolTable[instr.Operand];
                    if (vars.TryGetValue(sym, out var val))
                        stack.Push(val);
                    else
                        stack.Push(_runtime.GlobalScope.Get(sym));
                    break;
                }

                case AgentOpCode.StoreVar:
                {
                    string sym = module.SymbolTable[instr.Operand];
                    var val = stack.Count > 0 ? stack.Pop() : null;
                    vars[sym] = val;
                    _runtime.GlobalScope.SetLocal(sym, val);
                    break;
                }

                case AgentOpCode.Negate:
                {
                    var operand = Pop(stack);
                    stack.Push(-ToDouble(operand));
                    break;
                }

                case AgentOpCode.Not:
                {
                    var operand = Pop(stack);
                    stack.Push(!IsTruthy(operand));
                    break;
                }

                case AgentOpCode.Add:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    if (left is string || right is string)
                        stack.Push($"{left}{right}");
                    else
                        stack.Push(ToDouble(left) + ToDouble(right));
                    break;
                }

                case AgentOpCode.Sub:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(ToDouble(left) - ToDouble(right));
                    break;
                }

                case AgentOpCode.Mul:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(ToDouble(left) * ToDouble(right));
                    break;
                }

                case AgentOpCode.Div:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    double denom = ToDouble(right);
                    if (denom == 0.0)
                        throw new AgentLangRuntimeException("Division by zero in bytecode execution.", errorCode: "VM-DIV-ZERO");
                    stack.Push(ToDouble(left) / denom);
                    break;
                }

                case AgentOpCode.Modulo:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(ToDouble(left) % ToDouble(right));
                    break;
                }

                case AgentOpCode.CompareEq:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(AreEqual(left, right));
                    break;
                }

                case AgentOpCode.CompareNeq:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(!AreEqual(left, right));
                    break;
                }

                case AgentOpCode.CompareLt:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(ToDouble(left) < ToDouble(right));
                    break;
                }

                case AgentOpCode.CompareLte:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(ToDouble(left) <= ToDouble(right));
                    break;
                }

                case AgentOpCode.CompareGt:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(ToDouble(left) > ToDouble(right));
                    break;
                }

                case AgentOpCode.CompareGte:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(ToDouble(left) >= ToDouble(right));
                    break;
                }

                case AgentOpCode.LogicalAnd:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(IsTruthy(left) && IsTruthy(right));
                    break;
                }

                case AgentOpCode.LogicalOr:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Push(IsTruthy(left) || IsTruthy(right));
                    break;
                }

                case AgentOpCode.Jump:
                    ip = instr.Operand;
                    break;

                case AgentOpCode.JumpIfTrue:
                    if (IsTruthy(Pop(stack))) ip = instr.Operand;
                    break;

                case AgentOpCode.JumpIfFalse:
                    if (!IsTruthy(Pop(stack))) ip = instr.Operand;
                    break;

                case AgentOpCode.Print:
                {
                    var val = Pop(stack);
                    _output.WriteLine(val?.ToString() ?? "null");
                    stack.Push(null);
                    break;
                }

                case AgentOpCode.GetMember:
                {
                    string member = module.SymbolTable[instr.Operand];
                    var target = Pop(stack);
                    stack.Push(AgentLangRuntime.ResolveMemberAccess(target, member));
                    break;
                }

                case AgentOpCode.MakeList:
                {
                    var list = new List<object?>(instr.Operand);
                    for (int i = 0; i < instr.Operand; i++) list.Add(Pop(stack));
                    list.Reverse();
                    stack.Push(list);
                    break;
                }

                case AgentOpCode.MakeMap:
                {
                    var pairs = new List<(string Key, object? Value)>(instr.Operand);
                    for (int i = 0; i < instr.Operand; i++)
                    {
                        var val = Pop(stack);
                        string key = Pop(stack)?.ToString() ?? "";
                        pairs.Add((key, val));
                    }
                    pairs.Reverse();
                    var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (k, v) in pairs) map[k] = v;
                    stack.Push(map);
                    break;
                }

                case AgentOpCode.DefineAgent:
                {
                    int cfgCount = instr.Operand;
                    var pairs = new List<(string Key, object? Value)>(cfgCount);
                    for (int i = 0; i < cfgCount; i++)
                    {
                        var val = Pop(stack);
                        string key = Pop(stack)?.ToString() ?? "";
                        pairs.Add((key, val));
                    }
                    pairs.Reverse();
                    string agentName = Pop(stack)?.ToString() ?? "UnnamedAgent";

                    var agent = BuildAgentValue(agentName, pairs);
                    _runtime.RegisterAgent(agent);
                    vars[agentName] = agent;
                    _runtime.GlobalScope.SetLocal(agentName, agent);
                    break;
                }

                case AgentOpCode.CallAgent:
                {
                    int argCount = instr.Operand;
                    var args = PopArgsReversed(stack, argCount);
                    string methodName = Pop(stack)?.ToString() ?? "solve";
                    string agentName = Pop(stack)?.ToString() ?? "";

                    var result = await ExecuteAgentCallAsync(agentName, methodName, args, ct);
                    stack.Push(result);
                    break;
                }

                case AgentOpCode.CallFunc:
                {
                    int argCount = instr.Operand;
                    var args = PopArgsReversed(stack, argCount);
                    string funcName = Pop(stack)?.ToString() ?? "";

                    var result = await ExecuteFunctionCallAsync(funcName, args, ct);
                    stack.Push(result);
                    break;
                }

                case AgentOpCode.CallTool:
                {
                    string toolName = module.SymbolTable[instr.Operand];
                    var arg = stack.Count > 0 ? stack.Pop() : null;
                    var tool = _runtime.ToolRegistry.GetTool(toolName);
                    if (tool != null)
                    {
                        var dict = new Dictionary<string, object?> { ["input"] = arg?.ToString() ?? "" };
                        var res = await _runtime.ToolRegistry.InvokeAsync(
                            _runtime.CurrentAgent?.Name ?? "bytecode",
                            _runtime.CurrentAgent?.PermissionPolicy,
                            toolName,
                            dict,
                            ct);
                        stack.Push(res.Output);
                    }
                    else
                    {
                        stack.Push(null);
                    }
                    break;
                }

                case AgentOpCode.EmitEvent:
                    break;

                case AgentOpCode.Return:
                    returnValue = stack.Count > 0 ? stack.Pop() : null;
                    return returnValue;

                case AgentOpCode.Halt:
                    return returnValue ?? (stack.Count > 0 ? stack.Peek() : null);
            }
        }

        return returnValue ?? (stack.Count > 0 ? stack.Peek() : null);
    }

    private AgentValue BuildAgentValue(string agentName, List<(string Key, object? Value)> config)
    {
        var agent = new AgentValue(agentName);
        foreach (var (key, val) in config)
        {
            if (key.Equals("model", StringComparison.OrdinalIgnoreCase))
                agent.Model = val?.ToString();
            else if (key.Equals("role", StringComparison.OrdinalIgnoreCase))
                agent.Role = val?.ToString();
            else if (key.Equals("instructions", StringComparison.OrdinalIgnoreCase) || key.Equals("instruction", StringComparison.OrdinalIgnoreCase))
                agent.Instructions = val?.ToString();
            else if (key.Equals("persona", StringComparison.OrdinalIgnoreCase) || key.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                agent.Persona = val?.ToString();
                agent.Instructions ??= val?.ToString();
            }
            else if (key.Equals("goal", StringComparison.OrdinalIgnoreCase))
                agent.Goal = val?.ToString();
            else if (key.Equals("max_steps", StringComparison.OrdinalIgnoreCase) || key.Equals("maxsteps", StringComparison.OrdinalIgnoreCase))
            {
                if (val != null && int.TryParse(val.ToString(), out int ms)) agent.MaxSteps = ms;
            }
            else if (key.Equals("temperature", StringComparison.OrdinalIgnoreCase))
            {
                if (val != null) agent.Temperature = Convert.ToDouble(val, CultureInfo.InvariantCulture);
            }
            else if (key.Equals("permission", StringComparison.OrdinalIgnoreCase))
                agent.PermissionPolicy = val?.ToString();
            else if (key.Equals("tools", StringComparison.OrdinalIgnoreCase) && val is IEnumerable<object?> tools)
            {
                foreach (var t in tools)
                    if (t != null) agent.BoundTools.Add(t.ToString()!);
            }
            else if ((key.Equals("fallback", StringComparison.OrdinalIgnoreCase) || key.Equals("fallbacks", StringComparison.OrdinalIgnoreCase)) &&
                     val is IEnumerable<object?> fallbacks)
            {
                foreach (var fb in fallbacks)
                    if (fb != null) agent.Fallbacks.Add(fb.ToString()!);
            }
        }
        return agent;
    }

    private async Task<AgentExecutionResult> ExecuteAgentCallAsync(
        string agentName, string methodName, List<object?> args, CancellationToken ct)
    {
        if (!_runtime.AgentInstances.TryGetValue(agentName, out var agent))
            agent = _runtime.GlobalScope.Get(agentName) as AgentValue;

        if (agent == null)
        {
            throw new AgentLangRuntimeException(
                $"Agent '{agentName}' is not defined in bytecode module.",
                errorCode: "VM-AGENT-404");
        }

        string goal = string.Join(" ", args.Where(a => a != null).Select(a => a is GoalValue g ? g.Description : a!.ToString()));
        var reactResult = await _runtime.ReActEngine.SolveAsync(agent, goal, _runtime.GlobalScope, ct);
        string ans = reactResult.FinalAnswer ?? reactResult.Error ?? $"{agent.Name} completed {methodName}: {goal}";
        return new AgentExecutionResult(ans, ans, reactResult.Success ? "DONE" : "FAILED")
        {
            Passed = reactResult.Success
        };
    }

    private async Task<object?> ExecuteFunctionCallAsync(string funcName, List<object?> args, CancellationToken ct)
    {
        switch (funcName)
        {
            case "__define_model":
                if (args.Count >= 2 && args[0] != null && args[1] != null)
                    _runtime.ModelRegistry.RegisterAlias(args[0]!.ToString()!, args[1]!.ToString()!);
                return null;

            case "__define_permission":
                if (args.Count >= 1 && args[0] != null)
                {
                    var policy = new PermissionPolicy(args[0]!.ToString()!);
                    for (int i = 1; i < args.Count; i++)
                    {
                        if (args[i] is IList pair && pair.Count >= 2)
                        {
                            var action = (PermissionAction)Convert.ToInt32(pair[0], CultureInfo.InvariantCulture);
                            policy.AddRule(action, pair[1]?.ToString() ?? "*");
                        }
                    }
                    _runtime.SecurityEngine.RegisterPolicy(policy);
                }
                return null;

            case "__define_schema":
                if (args.Count >= 2 && args[0] != null)
                {
                    string name = args[0]!.ToString()!;
                    string? desc = args[1]?.ToString();
                    var fields = new List<SchemaFieldNode>();
                    for (int i = 2; i < args.Count; i++)
                    {
                        if (args[i] is IList f && f.Count >= 8)
                        {
                            fields.Add(new SchemaFieldNode(
                                f[0]?.ToString() ?? "field",
                                f[1]?.ToString() ?? "string",
                                f[2] is bool ba && ba,
                                f[3] is bool br && br,
                                f[4] != null ? Convert.ToDouble(f[4], CultureInfo.InvariantCulture) : null,
                                f[5] != null ? Convert.ToDouble(f[5], CultureInfo.InvariantCulture) : null,
                                f[6]?.ToString(),
                                f[7]?.ToString(),
                                default));
                        }
                    }
                    var schema = new SchemaDeclarationNode(name, string.IsNullOrEmpty(desc) ? null : desc, fields, default);
                    _runtime.EntityStore.RegisterSchema(schema);
                    _runtime.GlobalScope.SetLocal(name, schema);
                }
                return null;

            case "__len":
                if (args.Count == 0 || args[0] == null) return 0L;
                if (args[0] is ICollection col) return (long)col.Count;
                if (args[0] is string s) return (long)s.Length;
                return 0L;

            case "__at":
                if (args.Count >= 2 && args[0] is IList list)
                {
                    int idx = Convert.ToInt32(args[1], CultureInfo.InvariantCulture);
                    return idx >= 0 && idx < list.Count ? list[idx] : null;
                }
                return null;
        }

        if (funcName.Equals("print", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine(string.Join(" ", args.Select(a => a?.ToString() ?? "null")));
            return null;
        }

        int dot = funcName.IndexOf('.');
        if (dot > 0)
        {
            string mod = funcName.Substring(0, dot);
            string member = funcName.Substring(dot + 1);

            if (mod.Equals("data", StringComparison.OrdinalIgnoreCase) ||
                mod.Equals("db", StringComparison.OrdinalIgnoreCase))
            {
                return await _runtime.InvokeDataModuleAsync(member, args, ct);
            }

            if (StandardLibrary.TryInvoke(mod, member, args, out var modRes))
                return modRes;

            var dotTool = _runtime.ToolRegistry.GetTool(mod) ?? _runtime.ToolRegistry.GetTool(funcName);
            if (dotTool != null)
            {
                var toolArgs = new Dictionary<string, object?>();
                for (int i = 0; i < args.Count; i++) toolArgs[$"arg{i}"] = args[i];
                if (args.Count > 0) toolArgs["input"] = args[0];
                var tres = await _runtime.ToolRegistry.InvokeAsync(
                    _runtime.CurrentAgent?.Name ?? "bytecode",
                    _runtime.CurrentAgent?.PermissionPolicy,
                    funcName,
                    toolArgs,
                    ct);
                return tres.Output;
            }
        }
        else
        {
            if (StandardLibrary.TryInvoke(funcName, null, args, out var stdRes))
                return stdRes;

            var tool = _runtime.ToolRegistry.GetTool(funcName);
            if (tool != null)
            {
                var toolArgs = new Dictionary<string, object?>();
                for (int i = 0; i < args.Count; i++) toolArgs[$"arg{i}"] = args[i];
                if (args.Count > 0) toolArgs["input"] = args[0];
                var tres = await _runtime.ToolRegistry.InvokeAsync(
                    _runtime.CurrentAgent?.Name ?? "bytecode",
                    _runtime.CurrentAgent?.PermissionPolicy,
                    funcName,
                    toolArgs,
                    ct);
                return tres.Output;
            }
        }

        return null;
    }

    private static List<object?> PopArgsReversed(Stack<object?> stack, int count)
    {
        var args = new List<object?>(count);
        for (int i = 0; i < count; i++) args.Add(Pop(stack));
        args.Reverse();
        return args;
    }

    private static object? Pop(Stack<object?> stack) => stack.Count > 0 ? stack.Pop() : null;

    private static double ToDouble(object? value) =>
        value switch
        {
            null => 0.0,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            bool b => b ? 1.0 : 0.0,
            _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0.0
        };

    private static bool AreEqual(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (left is string ls && right is string rs) return string.Equals(ls, rs, StringComparison.Ordinal);
        if (left is bool lb && right is bool rb) return lb == rb;
        if (IsNumeric(left) && IsNumeric(right)) return Math.Abs(ToDouble(left) - ToDouble(right)) < 1e-9;
        return Equals(left.ToString(), right.ToString());
    }

    private static bool IsNumeric(object v) => v is int or long or float or double or decimal;

    private static bool IsTruthy(object? value) =>
        value switch
        {
            null => false,
            bool b => b,
            int n => n != 0,
            long l => l != 0,
            double d => d != 0.0,
            string s => !string.IsNullOrEmpty(s),
            IList list => list.Count > 0,
            _ => true
        };
}

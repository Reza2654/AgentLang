using System.IO;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.Tools;

namespace AgentLang.VM.Bytecode;

public sealed class BytecodeExecutor
{
    private readonly AgentLangRuntime _runtime;
    private readonly TextWriter _output;

    public BytecodeExecutor(AgentLangRuntime? runtime = null, TextWriter? output = null)
    {
        _runtime = runtime ?? new AgentLangRuntime();
        _output = output ?? Console.Out;
    }

    public async Task<object?> ExecuteAsync(AgentBytecodeModule module, CancellationToken ct = default)
    {
        var stack = new Stack<object?>();
        int ip = 0;
        object? returnValue = null;

        while (ip < module.Instructions.Count && !ct.IsCancellationRequested)
        {
            var inst = module.Instructions[ip++];

            switch (inst.OpCode)
            {
                case AgentOpCode.Nop:
                    break;

                case AgentOpCode.PushConst:
                    var constant = module.ConstantPool[inst.Operand];
                    stack.Push(constant);
                    break;

                case AgentOpCode.Pop:
                    if (stack.Count > 0) stack.Pop();
                    break;

                case AgentOpCode.LoadVar:
                    string varName = module.SymbolTable[inst.Operand];
                    var val = _runtime.GlobalScope.Get(varName);
                    stack.Push(val);
                    break;

                case AgentOpCode.StoreVar:
                    string storeName = module.SymbolTable[inst.Operand];
                    var storeVal = stack.Count > 0 ? stack.Pop() : null;
                    _runtime.GlobalScope.SetLocal(storeName, storeVal);
                    break;

                case AgentOpCode.Add:
                    var rightAdd = stack.Pop();
                    var leftAdd = stack.Pop();
                    if (leftAdd is string || rightAdd is string)
                    {
                        stack.Push($"{leftAdd}{rightAdd}");
                    }
                    else
                    {
                        double l = Convert.ToDouble(leftAdd);
                        double r = Convert.ToDouble(rightAdd);
                        stack.Push(l + r);
                    }
                    break;

                case AgentOpCode.Sub:
                    var rightSub = Convert.ToDouble(stack.Pop());
                    var leftSub = Convert.ToDouble(stack.Pop());
                    stack.Push(leftSub - rightSub);
                    break;

                case AgentOpCode.Mul:
                    var rightMul = Convert.ToDouble(stack.Pop());
                    var leftMul = Convert.ToDouble(stack.Pop());
                    stack.Push(leftMul * rightMul);
                    break;

                case AgentOpCode.Div:
                    var rightDiv = Convert.ToDouble(stack.Pop());
                    var leftDiv = Convert.ToDouble(stack.Pop());
                    stack.Push(rightDiv != 0 ? leftDiv / rightDiv : 0.0);
                    break;

                case AgentOpCode.CompareEq:
                    var rightEq = stack.Pop();
                    var leftEq = stack.Pop();
                    stack.Push(Equals(leftEq, rightEq));
                    break;

                case AgentOpCode.CompareLt:
                    var rightLt = Convert.ToDouble(stack.Pop());
                    var leftLt = Convert.ToDouble(stack.Pop());
                    stack.Push(leftLt < rightLt);
                    break;

                case AgentOpCode.CompareGt:
                    var rightGt = Convert.ToDouble(stack.Pop());
                    var leftGt = Convert.ToDouble(stack.Pop());
                    stack.Push(leftGt > rightGt);
                    break;

                case AgentOpCode.Jump:
                    ip = inst.Operand;
                    break;

                case AgentOpCode.JumpIfTrue:
                    var condTrue = stack.Count > 0 ? stack.Pop() : false;
                    if (IsTruthy(condTrue))
                    {
                        ip = inst.Operand;
                    }
                    break;

                case AgentOpCode.JumpIfFalse:
                    var condFalse = stack.Count > 0 ? stack.Pop() : false;
                    if (!IsTruthy(condFalse))
                    {
                        ip = inst.Operand;
                    }
                    break;

                case AgentOpCode.Print:
                    int argCount = inst.Operand;
                    var printArgs = new List<object?>();
                    for (int i = 0; i < argCount; i++)
                    {
                        printArgs.Insert(0, stack.Pop());
                    }
                    _output.WriteLine(string.Join(" ", printArgs));
                    break;

                case AgentOpCode.CallAgent:
                    string methodName = module.SymbolTable[inst.Operand];
                    var goalObj = stack.Count > 0 ? stack.Pop() : "";
                    var agentObj = stack.Count > 0 ? stack.Pop() : null;

                    if (agentObj is AgentValue agentVal)
                    {
                        var result = await _runtime.ReActEngine.SolveAsync(agentVal, goalObj?.ToString() ?? "", _runtime.GlobalScope, ct);
                        stack.Push(result.FinalAnswer ?? "");
                    }
                    else if (agentObj is string agentNameStr && _runtime.AgentInstances.TryGetValue(agentNameStr, out var foundAgent))
                    {
                        var result = await _runtime.ReActEngine.SolveAsync(foundAgent, goalObj?.ToString() ?? "", _runtime.GlobalScope, ct);
                        stack.Push(result.FinalAnswer ?? "");
                    }
                    else
                    {
                        stack.Push($"[Agent Execution: {methodName}]");
                    }
                    break;

                case AgentOpCode.CallTool:
                    string toolName = module.SymbolTable[inst.Operand];
                    var toolResult = await _runtime.ToolRegistry.InvokeAsync(
                        agentName: "bytecode_executor",
                        policyName: null,
                        capability: toolName,
                        arguments: new Dictionary<string, object?> { ["expr"] = stack.Count > 0 ? stack.Pop() : "" },
                        ct: ct);
                    stack.Push(toolResult.Output);
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

    private static bool IsTruthy(object? value) =>
        value switch
        {
            null => false,
            bool b => b,
            int n => n != 0,
            double d => d != 0.0,
            string s => !string.IsNullOrEmpty(s),
            _ => true
        };
}

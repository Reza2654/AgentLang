using System.Globalization;

namespace AgentLang.VM.Bytecode;

/// <summary>
/// Lightweight compile-time peephole optimizer for AgentLang bytecode modules (.agtc).
/// Performs constant folding, dead jump elimination, and Nop instruction compaction.
/// </summary>
public static class BytecodeOptimizer
{
    public static int Optimize(AgentBytecodeModule module)
    {
        int optimizationsApplied = 0;
        bool changed = true;
        int passes = 0;

        while (changed && passes < 10)
        {
            changed = false;
            passes++;

            // Collect all current jump targets to prevent folding across control flow jump boundaries
            var jumpTargets = new HashSet<int>();
            foreach (var inst in module.Instructions)
            {
                if (inst.OpCode is AgentOpCode.Jump or AgentOpCode.JumpIfTrue or AgentOpCode.JumpIfFalse)
                {
                    jumpTargets.Add(inst.Operand);
                }
            }

            // Pass 1: Constant folding
            for (int i = 0; i < module.Instructions.Count - 2; i++)
            {
                if (jumpTargets.Contains(i + 1) || jumpTargets.Contains(i + 2))
                {
                    continue;
                }

                var inst1 = module.Instructions[i];
                var inst2 = module.Instructions[i + 1];
                var inst3 = module.Instructions[i + 2];

                if (inst1.OpCode == AgentOpCode.PushConst && inst2.OpCode == AgentOpCode.PushConst &&
                    inst1.Operand >= 0 && inst1.Operand < module.ConstantPool.Count &&
                    inst2.Operand >= 0 && inst2.Operand < module.ConstantPool.Count)
                {
                    object? val1 = module.ConstantPool[inst1.Operand];
                    object? val2 = module.ConstantPool[inst2.Operand];

                    if (TryFoldBinary(val1, val2, inst3.OpCode, out object? foldedResult))
                    {
                        int foldedConstIdx = module.AddConstant(foldedResult);
                        module.Instructions[i] = new BytecodeInstruction(AgentOpCode.PushConst, foldedConstIdx, inst1.Line);
                        module.Instructions[i + 1] = new BytecodeInstruction(AgentOpCode.Nop, 0, inst2.Line);
                        module.Instructions[i + 2] = new BytecodeInstruction(AgentOpCode.Nop, 0, inst3.Line);
                        changed = true;
                        optimizationsApplied++;
                    }
                }
            }

            // Pass 2: Unary constant folding
            for (int i = 0; i < module.Instructions.Count - 1; i++)
            {
                if (jumpTargets.Contains(i + 1))
                {
                    continue;
                }

                var inst1 = module.Instructions[i];
                var inst2 = module.Instructions[i + 1];

                if (inst1.OpCode == AgentOpCode.PushConst &&
                    inst1.Operand >= 0 && inst1.Operand < module.ConstantPool.Count)
                {
                    object? val = module.ConstantPool[inst1.Operand];
                    if (TryFoldUnary(val, inst2.OpCode, out object? foldedResult))
                    {
                        int foldedConstIdx = module.AddConstant(foldedResult);
                        module.Instructions[i] = new BytecodeInstruction(AgentOpCode.PushConst, foldedConstIdx, inst1.Line);
                        module.Instructions[i + 1] = new BytecodeInstruction(AgentOpCode.Nop, 0, inst2.Line);
                        changed = true;
                        optimizationsApplied++;
                    }
                }
            }
        }

        // Pass 3: Compact NOPs and adjust Jump targets
        CompactNops(module);

        return optimizationsApplied;
    }

    private static bool TryFoldBinary(object? v1, object? v2, AgentOpCode op, out object? result)
    {
        result = null;

        // String concatenation
        if (op == AgentOpCode.Add && (v1 is string || v2 is string))
        {
            result = $"{v1}{v2}";
            return true;
        }

        // Numeric operations
        if (IsNumeric(v1) && IsNumeric(v2))
        {
            double d1 = ToDouble(v1);
            double d2 = ToDouble(v2);

            switch (op)
            {
                case AgentOpCode.Add:
                    result = (v1 is long || v1 is int) && (v2 is long || v2 is int) ? (object)(ToLong(v1) + ToLong(v2)) : d1 + d2;
                    return true;

                case AgentOpCode.Sub:
                    result = (v1 is long || v1 is int) && (v2 is long || v2 is int) ? (object)(ToLong(v1) - ToLong(v2)) : d1 - d2;
                    return true;

                case AgentOpCode.Mul:
                    result = (v1 is long || v1 is int) && (v2 is long || v2 is int) ? (object)(ToLong(v1) * ToLong(v2)) : d1 * d2;
                    return true;

                case AgentOpCode.Div:
                    if (Math.Abs(d2) < 1e-12) return false; // Don't fold division by zero at compile time
                    result = d1 / d2;
                    return true;

                case AgentOpCode.CompareEq:
                    result = Math.Abs(d1 - d2) < 1e-9;
                    return true;

                case AgentOpCode.CompareNeq:
                    result = Math.Abs(d1 - d2) >= 1e-9;
                    return true;

                case AgentOpCode.CompareLt:
                    result = d1 < d2;
                    return true;

                case AgentOpCode.CompareLte:
                    result = d1 <= d2;
                    return true;

                case AgentOpCode.CompareGt:
                    result = d1 > d2;
                    return true;

                case AgentOpCode.CompareGte:
                    result = d1 >= d2;
                    return true;
            }
        }

        // Boolean operations
        if (v1 is bool b1 && v2 is bool b2)
        {
            switch (op)
            {
                case AgentOpCode.LogicalAnd:
                    result = b1 && b2;
                    return true;
                case AgentOpCode.LogicalOr:
                    result = b1 || b2;
                    return true;
                case AgentOpCode.CompareEq:
                    result = b1 == b2;
                    return true;
                case AgentOpCode.CompareNeq:
                    result = b1 != b2;
                    return true;
            }
        }

        return false;
    }

    private static bool TryFoldUnary(object? v, AgentOpCode op, out object? result)
    {
        result = null;
        if (op == AgentOpCode.Negate && IsNumeric(v))
        {
            if (v is long l) { result = -l; return true; }
            if (v is int n) { result = -n; return true; }
            result = -ToDouble(v);
            return true;
        }

        if (op == AgentOpCode.Not && v is bool b)
        {
            result = !b;
            return true;
        }

        return false;
    }

    private static void CompactNops(AgentBytecodeModule module)
    {
        bool hasNops = module.Instructions.Any(i => i.OpCode == AgentOpCode.Nop);
        if (!hasNops) return;

        // Map old instruction indices to new compacted indices
        int[] indexMap = new int[module.Instructions.Count + 1];
        int newIdx = 0;
        for (int i = 0; i < module.Instructions.Count; i++)
        {
            indexMap[i] = newIdx;
            if (module.Instructions[i].OpCode != AgentOpCode.Nop)
            {
                newIdx++;
            }
        }
        indexMap[module.Instructions.Count] = newIdx;

        // Compact list while remapping jump operands
        var compacted = new List<BytecodeInstruction>(newIdx);
        for (int i = 0; i < module.Instructions.Count; i++)
        {
            var inst = module.Instructions[i];
            if (inst.OpCode == AgentOpCode.Nop) continue;

            if (inst.OpCode is AgentOpCode.Jump or AgentOpCode.JumpIfTrue or AgentOpCode.JumpIfFalse)
            {
                int oldTarget = inst.Operand;
                int newTarget = oldTarget >= 0 && oldTarget < indexMap.Length ? indexMap[oldTarget] : oldTarget;
                compacted.Add(new BytecodeInstruction(inst.OpCode, newTarget, inst.Line));
            }
            else
            {
                compacted.Add(inst);
            }
        }

        module.Instructions.Clear();
        module.Instructions.AddRange(compacted);
    }

    private static bool IsNumeric(object? v) => v is int or long or float or double or decimal;

    private static double ToDouble(object? v) => Convert.ToDouble(v, CultureInfo.InvariantCulture);

    private static long ToLong(object? v) => Convert.ToInt64(v, CultureInfo.InvariantCulture);
}

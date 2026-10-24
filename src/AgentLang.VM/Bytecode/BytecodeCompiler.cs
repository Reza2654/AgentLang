using AgentLang.AST;

namespace AgentLang.VM.Bytecode;

public sealed class BytecodeCompiler
{
    private readonly AgentBytecodeModule _module = new();

    public AgentBytecodeModule Compile(ProgramNode program, string moduleName = "Main")
    {
        _module.ModuleName = moduleName;

        foreach (var decl in program.Declarations)
        {
            CompileDeclaration(decl);
        }

        if (program.Main != null)
        {
            foreach (var stmt in program.Main.Statements)
            {
                CompileStatement(stmt);
            }
        }

        Emit(AgentOpCode.Halt);
        return _module;
    }

    private void CompileDeclaration(DeclarationNode decl)
    {
        if (decl is AgentDeclarationNode agentDecl)
        {
            int symIndex = _module.AddSymbol(agentDecl.Name);
            _module.AddConstant(agentDecl.Name);
        }
        else if (decl is StatementNode stmt)
        {
            CompileStatement(stmt);
        }
    }

    private void CompileStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case VariableAssignmentNode assign:
                CompileExpression(assign.Value);
                int varSym = _module.AddSymbol(assign.VariableName);
                Emit(AgentOpCode.StoreVar, varSym, assign.Span.Start.Line);
                break;

            case ExpressionStatementNode exprStmt:
                CompileExpression(exprStmt.Expression);
                Emit(AgentOpCode.Pop, 0, exprStmt.Span.Start.Line);
                break;

            case BlockStatementNode block:
                foreach (var s in block.Statements)
                {
                    CompileStatement(s);
                }
                break;

            case IfStatementNode ifStmt:
                CompileExpression(ifStmt.Condition);
                int jumpIfFalseIndex = _module.Instructions.Count;
                Emit(AgentOpCode.JumpIfFalse, 0, ifStmt.Span.Start.Line);

                foreach (var s in ifStmt.ThenBranch)
                {
                    CompileStatement(s);
                }

                if (ifStmt.ElseBranch != null && ifStmt.ElseBranch.Count > 0)
                {
                    int jumpAfterElseIndex = _module.Instructions.Count;
                    Emit(AgentOpCode.Jump, 0, ifStmt.Span.Start.Line);

                    // Patch JumpIfFalse target
                    PatchJump(jumpIfFalseIndex, _module.Instructions.Count);

                    foreach (var s in ifStmt.ElseBranch)
                    {
                        CompileStatement(s);
                    }

                    // Patch Jump target
                    PatchJump(jumpAfterElseIndex, _module.Instructions.Count);
                }
                else
                {
                    PatchJump(jumpIfFalseIndex, _module.Instructions.Count);
                }
                break;

            case ReturnStatementNode retStmt:
                if (retStmt.Value != null)
                {
                    CompileExpression(retStmt.Value);
                }
                else
                {
                    int nullConst = _module.AddConstant(null);
                    Emit(AgentOpCode.PushConst, nullConst, retStmt.Span.Start.Line);
                }
                Emit(AgentOpCode.Return, 0, retStmt.Span.Start.Line);
                break;

            default:
                // Other statements handled by fallback or no-op
                break;
        }
    }

    private void CompileExpression(ExpressionNode expr)
    {
        switch (expr)
        {
            case LiteralExpressionNode lit:
                int constIndex = _module.AddConstant(lit.Value);
                Emit(AgentOpCode.PushConst, constIndex, lit.Span.Start.Line);
                break;

            case IdentifierExpressionNode id:
                int symIndex = _module.AddSymbol(id.Name);
                Emit(AgentOpCode.LoadVar, symIndex, id.Span.Start.Line);
                break;

            case BinaryExpressionNode bin:
                CompileExpression(bin.Left);
                CompileExpression(bin.Right);
                var op = bin.Operator switch
                {
                    BinaryOperator.Add => AgentOpCode.Add,
                    BinaryOperator.Subtract => AgentOpCode.Sub,
                    BinaryOperator.Multiply => AgentOpCode.Mul,
                    BinaryOperator.Divide => AgentOpCode.Div,
                    BinaryOperator.Equal => AgentOpCode.CompareEq,
                    BinaryOperator.LessThan => AgentOpCode.CompareLt,
                    BinaryOperator.GreaterThan => AgentOpCode.CompareGt,
                    _ => AgentOpCode.CompareEq
                };
                Emit(op, 0, bin.Span.Start.Line);
                break;

            case CallExpressionNode call:
                // Check if calling print
                if (call.Callee is IdentifierExpressionNode calleeId &&
                    calleeId.Name.Equals("print", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var arg in call.Arguments)
                    {
                        CompileExpression(arg);
                    }
                    Emit(AgentOpCode.Print, call.Arguments.Count, call.Span.Start.Line);
                }
                // Check if calling member expression on agent (e.g. Agent.solve(prompt))
                else if (call.Callee is MemberAccessExpressionNode memberAccess)
                {
                    CompileExpression(memberAccess.Target);
                    foreach (var arg in call.Arguments)
                    {
                        CompileExpression(arg);
                    }
                    int methodSym = _module.AddSymbol(memberAccess.MemberName);
                    Emit(AgentOpCode.CallAgent, methodSym, call.Span.Start.Line);
                }
                else
                {
                    // Generic tool or function call
                    foreach (var arg in call.Arguments)
                    {
                        CompileExpression(arg);
                    }
                    string toolName = (call.Callee as IdentifierExpressionNode)?.Name ?? "unknown";
                    int toolSym = _module.AddSymbol(toolName);
                    Emit(AgentOpCode.CallTool, toolSym, call.Span.Start.Line);
                }
                break;

            default:
                int defaultConst = _module.AddConstant(null);
                Emit(AgentOpCode.PushConst, defaultConst, expr.Span.Start.Line);
                break;
        }
    }

    private void Emit(AgentOpCode op, int operand = 0, int line = 1)
    {
        _module.Instructions.Add(new BytecodeInstruction(op, operand, line));
    }

    private void PatchJump(int instructionIndex, int targetInstructionIndex)
    {
        var existing = _module.Instructions[instructionIndex];
        _module.Instructions[instructionIndex] = new BytecodeInstruction(existing.OpCode, targetInstructionIndex, existing.Line);
    }
}

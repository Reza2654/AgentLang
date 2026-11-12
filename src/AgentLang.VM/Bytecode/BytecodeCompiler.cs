using AgentLang.AST;

namespace AgentLang.VM.Bytecode;

/// <summary>
/// Compiles an AgentLang AST into portable stack-based VM bytecode (.agtc),
/// including agents, schemas, permissions, loops, member access, stdlib & data calls.
/// </summary>
public sealed class BytecodeCompiler
{
    private readonly AgentBytecodeModule _module = new();
    private readonly HashSet<string> _declaredAgents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _builtInModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "json", "math", "time", "collections", "crypto", "rag", "sandbox", "env", "data", "db"
    };
    private int _tempCounter;

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
        int line = decl.Span.Start.Line;
        switch (decl)
        {
            case ModelDeclarationNode mdl:
                Emit(AgentOpCode.PushConst, _module.AddConstant("__define_model"), line);
                Emit(AgentOpCode.PushConst, _module.AddConstant(mdl.Alias), line);
                Emit(AgentOpCode.PushConst, _module.AddConstant(mdl.TargetModel), line);
                Emit(AgentOpCode.CallFunc, 2, line);
                Emit(AgentOpCode.Pop, 0, line);
                break;

            case PermissionDeclarationNode perm:
                Emit(AgentOpCode.PushConst, _module.AddConstant("__define_permission"), line);
                Emit(AgentOpCode.PushConst, _module.AddConstant(perm.Name), line);
                foreach (var rule in perm.Rules)
                {
                    Emit(AgentOpCode.PushConst, _module.AddConstant((int)rule.Action), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(rule.CapabilityPattern), line);
                    Emit(AgentOpCode.MakeList, 2, line);
                }
                Emit(AgentOpCode.CallFunc, 1 + perm.Rules.Count, line);
                Emit(AgentOpCode.Pop, 0, line);
                break;

            case SchemaDeclarationNode schema:
                Emit(AgentOpCode.PushConst, _module.AddConstant("__define_schema"), line);
                Emit(AgentOpCode.PushConst, _module.AddConstant(schema.Name), line);
                Emit(AgentOpCode.PushConst, _module.AddConstant(schema.Description ?? ""), line);
                foreach (var f in schema.Fields)
                {
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.Name), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.TypeName), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.IsArray), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.IsRequired), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.MinValue.HasValue ? f.MinValue.Value : null), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.MaxValue.HasValue ? f.MaxValue.Value : null), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.RegexPattern), line);
                    Emit(AgentOpCode.PushConst, _module.AddConstant(f.Description), line);
                    Emit(AgentOpCode.MakeList, 8, line);
                }
                Emit(AgentOpCode.CallFunc, 2 + schema.Fields.Count, line);
                Emit(AgentOpCode.Pop, 0, line);
                break;

            case GoalDeclarationNode goal:
                _module.AddSymbol(goal.Name);
                CompileExpression(goal.Value);
                Emit(AgentOpCode.StoreVar, _module.AddSymbol(goal.Name), line);
                break;

            case AgentDeclarationNode agent:
                CompileAgentDeclaration(agent);
                break;

            case MultiAgentDeclarationNode multi:
                _module.AddSymbol(multi.Name);
                foreach (var item in multi.Body)
                {
                    if (item is AgentDeclarationNode childAgent)
                    {
                        CompileAgentDeclaration(childAgent);
                    }
                }
                break;
        }
    }

    private void CompileAgentDeclaration(AgentDeclarationNode agent)
    {
        int line = agent.Span.Start.Line;
        _declaredAgents.Add(agent.Name);
        _module.AddSymbol(agent.Name);

        Emit(AgentOpCode.PushConst, _module.AddConstant(agent.Name), line);
        foreach (var cfg in agent.Config)
        {
            Emit(AgentOpCode.PushConst, _module.AddConstant(cfg.Key), line);
            CompileAgentConfigValue(cfg.Key, cfg.Value);
        }
        Emit(AgentOpCode.DefineAgent, agent.Config.Count, line);
    }

    private void CompileAgentConfigValue(string key, ExpressionNode valExpr)
    {
        int line = valExpr.Span.Start.Line;
        if ((key.Equals("permission", StringComparison.OrdinalIgnoreCase) ||
             key.Equals("model", StringComparison.OrdinalIgnoreCase) ||
             key.Equals("role", StringComparison.OrdinalIgnoreCase)) &&
            valExpr is IdentifierExpressionNode idNode)
        {
            Emit(AgentOpCode.PushConst, _module.AddConstant(idNode.Name), line);
            return;
        }

        if ((key.Equals("tools", StringComparison.OrdinalIgnoreCase) ||
             key.Equals("fallback", StringComparison.OrdinalIgnoreCase) ||
             key.Equals("fallbacks", StringComparison.OrdinalIgnoreCase)) &&
            valExpr is ListLiteralExpressionNode listNode)
        {
            foreach (var elem in listNode.Elements)
            {
                if (elem is IdentifierExpressionNode elemId)
                    Emit(AgentOpCode.PushConst, _module.AddConstant(elemId.Name), elem.Span.Start.Line);
                else if (elem is MemberAccessExpressionNode elemMember && elemMember.Target is IdentifierExpressionNode memTarget)
                    Emit(AgentOpCode.PushConst, _module.AddConstant($"{memTarget.Name}.{elemMember.MemberName}"), elem.Span.Start.Line);
                else
                    CompileExpression(elem);
            }
            Emit(AgentOpCode.MakeList, listNode.Elements.Count, line);
            return;
        }

        CompileExpression(valExpr);
    }

    private void CompileStatement(StatementNode stmt)
    {
        int line = stmt.Span.Start.Line;
        switch (stmt)
        {
            case VariableAssignmentNode assign:
                CompileExpression(assign.Value);
                Emit(AgentOpCode.StoreVar, _module.AddSymbol(assign.VariableName), line);
                break;

            case ExpressionStatementNode exprStmt:
                CompileExpression(exprStmt.Expression);
                Emit(AgentOpCode.Pop, 0, line);
                break;

            case BlockStatementNode block:
                foreach (var s in block.Statements)
                    CompileStatement(s);
                break;

            case IfStatementNode ifStmt:
            {
                CompileExpression(ifStmt.Condition);
                int jumpToElseIdx = _module.Instructions.Count;
                Emit(AgentOpCode.JumpIfFalse, 0, line);

                foreach (var thenStmt in ifStmt.ThenBranch)
                    CompileStatement(thenStmt);

                if (ifStmt.ElseBranch != null && ifStmt.ElseBranch.Count > 0)
                {
                    int jumpOverElseIdx = _module.Instructions.Count;
                    Emit(AgentOpCode.Jump, 0, line);

                    PatchJump(jumpToElseIdx, _module.Instructions.Count);
                    foreach (var elseStmt in ifStmt.ElseBranch)
                        CompileStatement(elseStmt);

                    PatchJump(jumpOverElseIdx, _module.Instructions.Count);
                }
                else
                {
                    PatchJump(jumpToElseIdx, _module.Instructions.Count);
                }
                break;
            }

            case WhileStatementNode whileStmt:
            {
                int loopStart = _module.Instructions.Count;
                CompileExpression(whileStmt.Condition);
                int exitJump = _module.Instructions.Count;
                Emit(AgentOpCode.JumpIfFalse, 0, line);

                foreach (var bodyStmt in whileStmt.Body)
                    CompileStatement(bodyStmt);

                Emit(AgentOpCode.Jump, loopStart, line);
                PatchJump(exitJump, _module.Instructions.Count);
                break;
            }

            case ForStatementNode forStmt:
            {
                int suffix = ++_tempCounter;
                int iterSym = _module.AddSymbol($"$__iter_{suffix}");
                int idxSym = _module.AddSymbol($"$__idx_{suffix}");
                int varSym = _module.AddSymbol(forStmt.VariableName);

                CompileExpression(forStmt.Iterable);
                Emit(AgentOpCode.StoreVar, iterSym, line);

                Emit(AgentOpCode.PushConst, _module.AddConstant(0), line);
                Emit(AgentOpCode.StoreVar, idxSym, line);

                int loopStart = _module.Instructions.Count;
                Emit(AgentOpCode.LoadVar, idxSym, line);
                Emit(AgentOpCode.PushConst, _module.AddConstant("__len"), line);
                Emit(AgentOpCode.LoadVar, iterSym, line);
                Emit(AgentOpCode.CallFunc, 1, line);
                Emit(AgentOpCode.CompareLt, 0, line);

                int exitJump = _module.Instructions.Count;
                Emit(AgentOpCode.JumpIfFalse, 0, line);

                Emit(AgentOpCode.PushConst, _module.AddConstant("__at"), line);
                Emit(AgentOpCode.LoadVar, iterSym, line);
                Emit(AgentOpCode.LoadVar, idxSym, line);
                Emit(AgentOpCode.CallFunc, 2, line);
                Emit(AgentOpCode.StoreVar, varSym, line);

                foreach (var bodyStmt in forStmt.Body)
                    CompileStatement(bodyStmt);

                Emit(AgentOpCode.LoadVar, idxSym, line);
                Emit(AgentOpCode.PushConst, _module.AddConstant(1), line);
                Emit(AgentOpCode.Add, 0, line);
                Emit(AgentOpCode.StoreVar, idxSym, line);
                Emit(AgentOpCode.Jump, loopStart, line);

                PatchJump(exitJump, _module.Instructions.Count);
                break;
            }

            case ReturnStatementNode retStmt:
                if (retStmt.Value != null)
                    CompileExpression(retStmt.Value);
                else
                    Emit(AgentOpCode.PushConst, _module.AddConstant(null), line);

                Emit(AgentOpCode.Return, 0, line);
                break;

            case DeclarationNode nestedDecl:
                CompileDeclaration(nestedDecl);
                break;
        }
    }

    private void CompileExpression(ExpressionNode expr)
    {
        int line = expr.Span.Start.Line;
        switch (expr)
        {
            case LiteralExpressionNode lit:
                Emit(AgentOpCode.PushConst, _module.AddConstant(lit.Value), line);
                break;

            case IdentifierExpressionNode id:
                Emit(AgentOpCode.LoadVar, _module.AddSymbol(id.Name), line);
                break;

            case UnaryExpressionNode unary:
                CompileExpression(unary.Operand);
                Emit(unary.Operator == UnaryOperator.Not ? AgentOpCode.Not : AgentOpCode.Negate, 0, line);
                break;

            case BinaryExpressionNode bin:
                CompileExpression(bin.Left);
                CompileExpression(bin.Right);
                Emit(bin.Operator switch
                {
                    BinaryOperator.Add => AgentOpCode.Add,
                    BinaryOperator.Subtract => AgentOpCode.Sub,
                    BinaryOperator.Multiply => AgentOpCode.Mul,
                    BinaryOperator.Divide => AgentOpCode.Div,
                    BinaryOperator.Modulo => AgentOpCode.Modulo,
                    BinaryOperator.Equal => AgentOpCode.CompareEq,
                    BinaryOperator.NotEqual => AgentOpCode.CompareNeq,
                    BinaryOperator.LessThan => AgentOpCode.CompareLt,
                    BinaryOperator.LessThanOrEqual => AgentOpCode.CompareLte,
                    BinaryOperator.GreaterThan => AgentOpCode.CompareGt,
                    BinaryOperator.GreaterThanOrEqual => AgentOpCode.CompareGte,
                    BinaryOperator.And => AgentOpCode.LogicalAnd,
                    BinaryOperator.Or => AgentOpCode.LogicalOr,
                    _ => AgentOpCode.Nop
                }, 0, line);
                break;

            case MemberAccessExpressionNode member:
                CompileExpression(member.Target);
                Emit(AgentOpCode.GetMember, _module.AddSymbol(member.MemberName), line);
                break;

            case ListLiteralExpressionNode list:
                foreach (var elem in list.Elements)
                    CompileExpression(elem);
                Emit(AgentOpCode.MakeList, list.Elements.Count, line);
                break;

            case MapLiteralExpressionNode map:
                foreach (var entry in map.Entries)
                {
                    Emit(AgentOpCode.PushConst, _module.AddConstant(entry.Key), line);
                    CompileExpression(entry.Value);
                }
                Emit(AgentOpCode.MakeMap, map.Entries.Count, line);
                break;

            case NamedArgumentExpressionNode named:
                CompileExpression(named.Value);
                break;

            case CallExpressionNode call:
                CompileCallExpression(call, line);
                break;

            default:
                Emit(AgentOpCode.PushConst, _module.AddConstant(null), line);
                break;
        }
    }

    private void CompileCallExpression(CallExpressionNode call, int line)
    {
        if (call.Callee is IdentifierExpressionNode id)
        {
            if (id.Name.Equals("print", StringComparison.OrdinalIgnoreCase) && call.Arguments.Count == 1)
            {
                CompileExpression(call.Arguments[0]);
                Emit(AgentOpCode.Print, 0, line);
                return;
            }

            Emit(AgentOpCode.PushConst, _module.AddConstant(id.Name), line);
            foreach (var arg in call.Arguments)
                CompileExpression(arg);
            Emit(AgentOpCode.CallFunc, call.Arguments.Count, line);
            return;
        }

        if (call.Callee is MemberAccessExpressionNode ma && ma.Target is IdentifierExpressionNode targetId)
        {
            bool isAgentInvoke =
                _declaredAgents.Contains(targetId.Name) ||
                (!_builtInModules.Contains(targetId.Name) &&
                 ma.MemberName.ToLowerInvariant() is "solve" or "run" or "achieve" or "execute" or "draft" or "revise" or "review");

            if (isAgentInvoke)
            {
                Emit(AgentOpCode.PushConst, _module.AddConstant(targetId.Name), line);
                Emit(AgentOpCode.PushConst, _module.AddConstant(ma.MemberName), line);
                foreach (var arg in call.Arguments)
                    CompileExpression(arg);
                Emit(AgentOpCode.CallAgent, call.Arguments.Count, line);
                return;
            }

            Emit(AgentOpCode.PushConst, _module.AddConstant($"{targetId.Name}.{ma.MemberName}"), line);
            foreach (var arg in call.Arguments)
                CompileExpression(arg);
            Emit(AgentOpCode.CallFunc, call.Arguments.Count, line);
            return;
        }

        Emit(AgentOpCode.PushConst, _module.AddConstant(null), line);
    }

    private void Emit(AgentOpCode op, int operand = 0, int line = 1)
    {
        _module.Instructions.Add(new BytecodeInstruction(op, operand, line));
    }

    private void PatchJump(int instructionIndex, int targetInstructionIndex)
    {
        var orig = _module.Instructions[instructionIndex];
        _module.Instructions[instructionIndex] = orig with { Operand = targetInstructionIndex };
    }
}

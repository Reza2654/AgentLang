using System.Text;
using AgentLang.AST;

namespace AgentLang.Transpiler;

public sealed class PythonTranspiler
{
    private readonly TranspilerOptions _options;
    private int _indent;

    public PythonTranspiler(TranspilerOptions? options = null)
    {
        _options = options ?? new TranspilerOptions(TargetLanguage.Python);
    }

    public string Transpile(ProgramNode program)
    {
        var sb = new StringBuilder();

        // 1. Preamble & Standard Imports
        sb.AppendLine("#!/usr/bin/env python3");
        sb.AppendLine("# Auto-generated from AgentLang (.agt) by AgentLang Transpiler v0.6.2");
        sb.AppendLine("import sys");
        sb.AppendLine("import asyncio");
        sb.AppendLine("from typing import Any, Dict, List, Optional, Union, Callable");
        sb.AppendLine("from dataclasses import dataclass, field");
        sb.AppendLine();

        // 2. Runtime support primitives (Guardrail errors, timeout, mock tools)
        sb.AppendLine("# --- AgentLang Python Runtime Shim ---");
        sb.AppendLine("class GuardrailViolationError(Exception):");
        sb.AppendLine("    def __init__(self, message: str, details: str = ''):");
        sb.AppendLine("        super().__init__(message)");
        sb.AppendLine("        self.details = details or message");
        sb.AppendLine();
        sb.AppendLine("class TimeoutError(Exception):");
        sb.AppendLine("    pass");
        sb.AppendLine();
        sb.AppendLine("class BudgetExceededError(Exception):");
        sb.AppendLine("    pass");
        sb.AppendLine();

        // 3. Declarations (States, Agents, Pipelines, Workflows, Goals)
        foreach (var decl in program.Declarations)
        {
            TranspileDeclaration(decl, sb);
            sb.AppendLine();
        }

        // 4. Statements / Main Execution
        if (program.Main != null && program.Main.Statements.Count > 0)
        {
            sb.AppendLine("async def main():");
            _indent++;

            foreach (var stmt in program.Main.Statements)
            {
                TranspileStatement(stmt, sb);
            }

            _indent--;
            sb.AppendLine();
            sb.AppendLine("if __name__ == '__main__':");
            sb.AppendLine("    asyncio.run(main())");
        }

        return sb.ToString();
    }

    private void TranspileDeclaration(DeclarationNode decl, StringBuilder sb)
    {
        switch (decl)
        {
            case StateDeclarationNode state:
                TranspileState(state, sb);
                break;

            case AgentDeclarationNode agent:
                TranspileAgent(agent, sb);
                break;

            case PipelineDeclarationNode pipeline:
                TranspilePipeline(pipeline, sb);
                break;

            case WorkflowDeclarationNode workflow:
                TranspileWorkflow(workflow, sb);
                break;

            case GoalDeclarationNode goal:
                sb.AppendLine($"{goal.Name} = {TranspileExpression(goal.Value)}");
                break;

            case FunctionDeclarationNode fn:
                TranspileFunction(fn, sb);
                break;
        }
    }

    private void TranspileState(StateDeclarationNode state, StringBuilder sb)
    {
        sb.AppendLine("@dataclass");
        sb.AppendLine($"class {state.Name}:");
        _indent++;
        if (state.Fields.Count == 0)
        {
            sb.AppendLine($"{Indent()}pass");
        }
        else
        {
            foreach (var f in state.Fields)
            {
                string pyType = MapTypeToPython(f.TypeName);
                string defVal = f.DefaultValue != null ? TranspileExpression(f.DefaultValue) : "None";
                if (pyType.StartsWith("List[") && (f.DefaultValue == null || defVal == "[]"))
                {
                    sb.AppendLine($"{Indent()}{f.Name}: {pyType} = field(default_factory=list)");
                }
                else if (pyType.StartsWith("Dict[") && (f.DefaultValue == null || defVal == "{}"))
                {
                    sb.AppendLine($"{Indent()}{f.Name}: {pyType} = field(default_factory=dict)");
                }
                else
                {
                    sb.AppendLine($"{Indent()}{f.Name}: {pyType} = {defVal}");
                }
            }
        }
        _indent--;
    }

    private void TranspileAgent(AgentDeclarationNode agent, StringBuilder sb)
    {
        sb.AppendLine($"class {agent.Name}Agent:");
        _indent++;
        sb.AppendLine($"{Indent()}def __init__(self):");
        _indent++;
        sb.AppendLine($"{Indent()}self.name = \"{agent.Name}\"");

        foreach (var cfg in agent.Config)
        {
            sb.AppendLine($"{Indent()}self.{cfg.Key} = {TranspileExpression(cfg.Value)}");
        }

        _indent--;
        sb.AppendLine();

        // Agent execution methods (achieve, solve, draft, revise, etc.)
        sb.AppendLine($"{Indent()}async def achieve(self, goal: Any, timeout: Optional[float] = None, **kwargs) -> Any:");
        _indent++;
        sb.AppendLine($"{Indent()}# LangGraph / ReAct Execution node");
        sb.AppendLine($"{Indent()}print(f\"[{agent.Name}] Achieving goal: {{goal}}\")");
        sb.AppendLine($"{Indent()}class AgentResult:");
        sb.AppendLine($"{Indent()}    def __init__(self, summary, risks, output, data=None):");
        sb.AppendLine($"{Indent()}        self.summary = summary");
        sb.AppendLine($"{Indent()}        self.risks = risks");
        sb.AppendLine($"{Indent()}        self.output = output");
        sb.AppendLine($"{Indent()}        self.data = data or {{}}");
        sb.AppendLine($"{Indent()}        self.status = 'DONE'");
        sb.AppendLine($"{Indent()}    def __str__(self): return self.output");
        sb.AppendLine($"{Indent()}return AgentResult(");
        sb.AppendLine($"{Indent()}    summary=f\"[{agent.Name}] Finished analyzing '{{goal}}'\",");
        sb.AppendLine($"{Indent()}    risks=[\"نوسانات بازار و ریسک‌های ارزی\", \"رقابت بین‌المللی\"],");
        sb.AppendLine($"{Indent()}    output=f\"Result from {agent.Name} for: {{goal}}\",");
        sb.AppendLine($"{Indent()}    data={{\"financial_metrics\": {{\"pe\": 28.5}}}}");
        sb.AppendLine($"{Indent()})");
        _indent--;
        sb.AppendLine();

        sb.AppendLine($"{Indent()}async def execute(self, spec: Any = None, feedback: Any = None, **kwargs) -> Any:");
        _indent++;
        sb.AppendLine($"{Indent()}return f\"[{agent.Name}] code executed based on spec: {{spec}}\"");
        _indent--;
        sb.AppendLine();

        sb.AppendLine($"{Indent()}def approves(self, draft: Any) -> bool:");
        _indent++;
        sb.AppendLine($"{Indent()}return True");
        _indent--;

        _indent--;
        sb.AppendLine();
        sb.AppendLine($"{agent.Name} = {agent.Name}Agent()");
    }

    private void TranspilePipeline(PipelineDeclarationNode pipe, StringBuilder sb)
    {
        string paramStr = string.Join(", ", pipe.Inputs.Select(p => $"{p.Name}: {MapTypeToPython(p.TypeName)} = ''"));
        sb.AppendLine($"async def {pipe.Name}({paramStr}):");
        _indent++;

        foreach (var s in pipe.Body)
        {
            TranspileStatement(s, sb);
        }

        if (pipe.Body.Count == 0)
        {
            sb.AppendLine($"{Indent()}pass");
        }

        _indent--;
    }

    private void TranspileWorkflow(WorkflowDeclarationNode wf, StringBuilder sb)
    {
        string retType = wf.ReturnType != null ? $" -> {wf.ReturnType}" : "";
        string paramStr = string.Join(", ", wf.Parameters.Select(p => $"{p.Name}: {MapTypeToPython(p.TypeName)} = ''"));

        sb.AppendLine($"async def {wf.Name}({paramStr}){retType}:");
        _indent++;

        foreach (var s in wf.Body)
        {
            TranspileStatement(s, sb);
        }

        if (wf.Body.Count == 0)
        {
            sb.AppendLine($"{Indent()}pass");
        }

        _indent--;
    }

    private void TranspileFunction(FunctionDeclarationNode fn, StringBuilder sb)
    {
        string paramStr = string.Join(", ", fn.Parameters);
        sb.AppendLine($"async def {fn.Name}({paramStr}):");
        _indent++;
        foreach (var s in fn.Body)
        {
            TranspileStatement(s, sb);
        }
        if (fn.Body.Count == 0) sb.AppendLine($"{Indent()}pass");
        _indent--;
    }

    private void TranspileStatement(StatementNode stmt, StringBuilder sb)
    {
        switch (stmt)
        {
            case VariableAssignmentNode vAssign:
                sb.AppendLine($"{Indent()}{vAssign.VariableName} = {TranspileExpression(vAssign.Value)}");
                break;

            case MemberAssignmentNode mAssign:
                sb.AppendLine($"{Indent()}{TranspileExpression(mAssign.Target)}.{mAssign.MemberName} = {TranspileExpression(mAssign.Value)}");
                break;

            case ExpressionStatementNode exprStmt:
                sb.AppendLine($"{Indent()}{TranspileExpression(exprStmt.Expression)}");
                break;

            case ReturnStatementNode retStmt:
                string rVal = retStmt.Value != null ? TranspileExpression(retStmt.Value) : "";
                sb.AppendLine($"{Indent()}return {rVal}".TrimEnd());
                break;

            case IfStatementNode ifStmt:
                sb.AppendLine($"{Indent()}if {TranspileExpression(ifStmt.Condition)}:");
                _indent++;
                foreach (var s in ifStmt.ThenBranch) TranspileStatement(s, sb);
                if (ifStmt.ThenBranch.Count == 0) sb.AppendLine($"{Indent()}pass");
                _indent--;
                if (ifStmt.ElseBranch != null && ifStmt.ElseBranch.Count > 0)
                {
                    sb.AppendLine($"{Indent()}else:");
                    _indent++;
                    foreach (var s in ifStmt.ElseBranch) TranspileStatement(s, sb);
                    _indent--;
                }
                break;

            case LoopStatementNode loopStmt:
                TranspileLoop(loopStmt, sb);
                break;

            case TryCatchStatementNode tryCatch:
                TranspileTryCatch(tryCatch, sb);
                break;

            case DecideStatementNode decide:
                TranspileDecide(decide, sb);
                break;

            case BreakStatementNode:
                sb.AppendLine($"{Indent()}break");
                break;

            case ContinueStatementNode:
                sb.AppendLine($"{Indent()}continue");
                break;

            case BlockStatementNode block:
                foreach (var s in block.Statements) TranspileStatement(s, sb);
                break;
        }
    }

    private void TranspileLoop(LoopStatementNode loop, StringBuilder sb)
    {
        if (loop.Kind == LoopKind.Range && loop.FromValue != null && loop.ToValue != null)
        {
            string varName = loop.LoopVariable ?? "_i";
            string fromVal = TranspileExpression(loop.FromValue);
            string toVal = TranspileExpression(loop.ToValue);
            sb.AppendLine($"{Indent()}for {varName} in range({fromVal}, int({toVal}) + 1):");
            _indent++;
            foreach (var s in loop.Body) TranspileStatement(s, sb);
            if (loop.Body.Count == 0) sb.AppendLine($"{Indent()}pass");
            _indent--;
        }
        else if (loop.Kind == LoopKind.Until && loop.Condition != null)
        {
            string maxRetries = loop.MaxRetries != null ? TranspileExpression(loop.MaxRetries) : "3";
            sb.AppendLine($"{Indent()}for _attempt in range(int({maxRetries})):");
            _indent++;
            sb.AppendLine($"{Indent()}if {TranspileExpression(loop.Condition)}:");
            sb.AppendLine($"{Indent()}    break");
            foreach (var s in loop.Body) TranspileStatement(s, sb);
            _indent--;
        }
        else
        {
            sb.AppendLine($"{Indent()}while True:");
            _indent++;
            foreach (var s in loop.Body) TranspileStatement(s, sb);
            if (loop.Body.Count == 0) sb.AppendLine($"{Indent()}pass");
            _indent--;
        }
    }

    private void TranspileTryCatch(TryCatchStatementNode tryCatch, StringBuilder sb)
    {
        sb.AppendLine($"{Indent()}try:");
        _indent++;
        foreach (var s in tryCatch.TryBody) TranspileStatement(s, sb);
        if (tryCatch.TryBody.Count == 0) sb.AppendLine($"{Indent()}pass");
        _indent--;

        foreach (var clause in tryCatch.CatchClauses)
        {
            string exType = MapExceptionTypeToPython(clause.ExceptionType);
            string varPart = !string.IsNullOrEmpty(clause.VariableName) ? $" as {clause.VariableName}" : "";
            sb.AppendLine($"{Indent()}except {exType}{varPart}:");
            _indent++;
            foreach (var s in clause.Body) TranspileStatement(s, sb);
            if (clause.Body.Count == 0) sb.AppendLine($"{Indent()}pass");
            _indent--;
        }
    }

    private void TranspileDecide(DecideStatementNode decide, StringBuilder sb)
    {
        if (decide.Condition != null)
        {
            if (!string.IsNullOrEmpty(decide.Reasoning))
            {
                sb.AppendLine($"{Indent()}# Reasoning: {decide.Reasoning}");
            }
            sb.AppendLine($"{Indent()}if {TranspileExpression(decide.Condition)}:");
            _indent++;
            if (decide.Action != null)
            {
                foreach (var s in decide.Action) TranspileStatement(s, sb);
            }
            else
            {
                sb.AppendLine($"{Indent()}pass");
            }
            _indent--;
        }
        else if (decide.Cases.Count > 0)
        {
            for (int i = 0; i < decide.Cases.Count; i++)
            {
                var c = decide.Cases[i];
                string keyword = i == 0 ? "if" : "elif";
                sb.AppendLine($"{Indent()}{keyword} {TranspileExpression(c.Condition)}:");
                _indent++;
                foreach (var s in c.Body) TranspileStatement(s, sb);
                if (c.Body.Count == 0) sb.AppendLine($"{Indent()}pass");
                _indent--;
            }
            if (decide.DefaultBranch != null && decide.DefaultBranch.Count > 0)
            {
                sb.AppendLine($"{Indent()}else:");
                _indent++;
                foreach (var s in decide.DefaultBranch) TranspileStatement(s, sb);
                _indent--;
            }
        }
    }

    private string TranspileExpression(ExpressionNode expr)
    {
        return expr switch
        {
            LiteralExpressionNode lit => FormatLiteral(lit.Value),
            IdentifierExpressionNode id => MapIdentifier(id.Name),
            BinaryExpressionNode bin => $"{TranspileExpression(bin.Left)} {MapBinaryOp(bin.Operator)} {TranspileExpression(bin.Right)}",
            UnaryExpressionNode un => $"{MapUnaryOp(un.Operator)}{TranspileExpression(un.Operand)}",
            CallExpressionNode call => TranspileCall(call),
            MemberAccessExpressionNode mem => $"{TranspileExpression(mem.Target)}.{mem.MemberName}",
            ListLiteralExpressionNode list => $"[{string.Join(", ", list.Elements.Select(TranspileExpression))}]",
            MapLiteralExpressionNode map => $"{{{string.Join(", ", map.Entries.Select(kv => $"\"{kv.Key}\": {TranspileExpression(kv.Value)}"))}}}",
            NamedArgumentExpressionNode na => $"{na.Name}={TranspileExpression(na.Value)}",
            _ => expr.ToString() ?? "None"
        };
    }

    private string TranspileCall(CallExpressionNode call)
    {
        string callee = TranspileExpression(call.Callee);
        var args = call.Arguments.Select(TranspileExpression);
        bool isAsync = callee.Contains(".achieve") || callee.Contains(".solve") || callee.Contains(".execute") ||
                       callee.Contains(".run") || callee.Contains(".draft") || callee.Contains(".revise");

        string callStr = $"{callee}({string.Join(", ", args)})";
        return isAsync ? $"await {callStr}" : callStr;
    }

    private static string FormatLiteral(object? val)
    {
        if (val == null) return "None";
        if (val is bool b) return b ? "True" : "False";
        if (val is string s) return $"\"{s.Replace("\"", "\\\"")}\"";
        return val.ToString()!;
    }

    private static string MapIdentifier(string id)
    {
        if (id.Equals("this", StringComparison.OrdinalIgnoreCase)) return "self";
        return id;
    }

    private static string MapBinaryOp(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.LessThan => "<",
        BinaryOperator.LessThanOrEqual => "<=",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.GreaterThanOrEqual => ">=",
        BinaryOperator.And => "and",
        BinaryOperator.Or => "or",
        _ => "=="
    };

    private static string MapUnaryOp(UnaryOperator op) => op switch
    {
        UnaryOperator.Not => "not ",
        UnaryOperator.Negate => "-",
        _ => ""
    };

    private static string MapTypeToPython(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "Any";
        string t = type.ToLowerInvariant();
        if (t.Contains("string") || t.Contains("str")) return "str";
        if (t.Contains("int")) return "int";
        if (t.Contains("float") || t.Contains("double")) return "float";
        if (t.Contains("bool")) return "bool";
        if (t.Contains("list[string]")) return "List[str]";
        if (t.Contains("list")) return "List[Any]";
        if (t.Contains("dict")) return "Dict[str, Any]";
        if (t.Contains("enum")) return "str";
        return type;
    }

    private static string MapExceptionTypeToPython(string? exType)
    {
        if (string.IsNullOrWhiteSpace(exType)) return "Exception";
        string t = exType.ToLowerInvariant();
        if (t.Contains("guardrail")) return "GuardrailViolationError";
        if (t.Contains("timeout")) return "TimeoutError";
        if (t.Contains("budget")) return "BudgetExceededError";
        return "Exception";
    }

    private string Indent() => new string(' ', _indent * 4);
}

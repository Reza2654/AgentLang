using System.Text;
using AgentLang.AST;

namespace AgentLang.Transpiler;

public sealed class TypeScriptTranspiler
{
    private readonly TranspilerOptions _options;
    private int _indent;

    public TypeScriptTranspiler(TranspilerOptions? options = null)
    {
        _options = options ?? new TranspilerOptions(TargetLanguage.TypeScript);
    }

    public string Transpile(ProgramNode program)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// Auto-generated from AgentLang (.agt) by AgentLang Transpiler v0.6.2");
        sb.AppendLine("// Compatible with Node.js, Bun, and Vercel AI SDK (@ai-sdk)");
        sb.AppendLine();

        // Runtime errors & helpers
        sb.AppendLine("export class GuardrailViolationError extends Error {");
        sb.AppendLine("    public readonly details: string;");
        sb.AppendLine("    constructor(message: string, details?: string) {");
        sb.AppendLine("        super(message);");
        sb.AppendLine("        this.name = 'GuardrailViolationError';");
        sb.AppendLine("        this.details = details || message;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("export class TimeoutError extends Error {");
        sb.AppendLine("    constructor(message: string = 'Agent execution timed out') {");
        sb.AppendLine("        super(message);");
        sb.AppendLine("        this.name = 'TimeoutError';");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("export class BudgetExceededError extends Error {");
        sb.AppendLine("    constructor(message: string = 'Budget exceeded') {");
        sb.AppendLine("        super(message);");
        sb.AppendLine("        this.name = 'BudgetExceededError';");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function print(...args: any[]) { console.log(...args); }");
        sb.AppendLine();

        // Declarations
        foreach (var decl in program.Declarations)
        {
            TranspileDeclaration(decl, sb);
            sb.AppendLine();
        }

        // Statements / Main
        if (program.Main != null && program.Main.Statements.Count > 0)
        {
            sb.AppendLine("export async function main() {");
            _indent++;

            foreach (var stmt in program.Main.Statements)
            {
                TranspileStatement(stmt, sb);
            }

            _indent--;
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("if (require.main === module) {");
            sb.AppendLine("    main().catch(console.error);");
            sb.AppendLine("}");
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
                sb.AppendLine($"export const {goal.Name} = {TranspileExpression(goal.Value)};");
                break;

            case FunctionDeclarationNode fn:
                TranspileFunction(fn, sb);
                break;
        }
    }

    private void TranspileState(StateDeclarationNode state, StringBuilder sb)
    {
        sb.AppendLine($"export interface {state.Name} {{");
        _indent++;
        foreach (var f in state.Fields)
        {
            string tsType = MapTypeToTypeScript(f.TypeName);
            sb.AppendLine($"{Indent()}{f.Name}: {tsType};");
        }
        _indent--;
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"export function create{state.Name}(): {state.Name} {{");
        _indent++;
        sb.AppendLine($"{Indent()}return {{");
        _indent++;
        foreach (var f in state.Fields)
        {
            string defVal = f.DefaultValue != null ? TranspileExpression(f.DefaultValue) : "undefined";
            if (f.TypeName?.Contains("list", StringComparison.OrdinalIgnoreCase) == true && (f.DefaultValue == null || defVal == "undefined"))
                defVal = "[]";
            sb.AppendLine($"{Indent()}{f.Name}: {defVal},");
        }
        _indent--;
        sb.AppendLine($"{Indent()}}};");
        _indent--;
        sb.AppendLine("}");
    }

    private void TranspileAgent(AgentDeclarationNode agent, StringBuilder sb)
    {
        sb.AppendLine($"export class {agent.Name}Agent {{");
        _indent++;
        sb.AppendLine($"{Indent()}public readonly name = \"{agent.Name}\";");

        foreach (var cfg in agent.Config)
        {
            sb.AppendLine($"{Indent()}public {cfg.Key}: any = {TranspileExpression(cfg.Value)};");
        }

        sb.AppendLine();
        sb.AppendLine(Indent() + "async achieve(goal: any, options?: { timeout?: number }): Promise<any> {");
        _indent++;
        sb.AppendLine(Indent() + "console.log(`[" + agent.Name + "] Achieving goal: ${goal}`);");
        sb.AppendLine(Indent() + "return {");
        sb.AppendLine(Indent() + "    summary: `[" + agent.Name + "] Completed task: ${goal}`,");
        sb.AppendLine(Indent() + "    risks: ['Market volatility', 'Global regulatory risks'],");
        sb.AppendLine(Indent() + "    output: `Result from " + agent.Name + " for: ${goal}`,");
        sb.AppendLine(Indent() + "    status: 'DONE'");
        sb.AppendLine(Indent() + "};");
        _indent--;
        sb.AppendLine(Indent() + "}");
        sb.AppendLine();

        sb.AppendLine(Indent() + "async execute(args?: any): Promise<any> {");
        _indent++;
        sb.AppendLine(Indent() + "return `[" + agent.Name + "] executed: ${JSON.stringify(args)}`;");
        _indent--;
        sb.AppendLine(Indent() + "}");
        sb.AppendLine();

        sb.AppendLine(Indent() + "approves(draft: any): boolean {");
        _indent++;
        sb.AppendLine(Indent() + "return true;");
        _indent--;
        sb.AppendLine(Indent() + "}");

        _indent--;
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"export const {agent.Name} = new {agent.Name}Agent();");
    }

    private void TranspilePipeline(PipelineDeclarationNode pipe, StringBuilder sb)
    {
        string paramStr = string.Join(", ", pipe.Inputs.Select(p => $"{p.Name}: {MapTypeToTypeScript(p.TypeName)}"));
        sb.AppendLine($"export async function {pipe.Name}({paramStr}): Promise<any> {{");
        _indent++;

        foreach (var s in pipe.Body)
        {
            TranspileStatement(s, sb);
        }

        _indent--;
        sb.AppendLine("}");
    }

    private void TranspileWorkflow(WorkflowDeclarationNode wf, StringBuilder sb)
    {
        string retType = wf.ReturnType != null ? $": Promise<{wf.ReturnType}>" : ": Promise<any>";
        string paramStr = string.Join(", ", wf.Parameters.Select(p => $"{p.Name}: {MapTypeToTypeScript(p.TypeName)}"));

        sb.AppendLine($"export async function {wf.Name}({paramStr}){retType} {{");
        _indent++;

        foreach (var s in wf.Body)
        {
            TranspileStatement(s, sb);
        }

        _indent--;
        sb.AppendLine("}");
    }

    private void TranspileFunction(FunctionDeclarationNode fn, StringBuilder sb)
    {
        string paramStr = string.Join(", ", fn.Parameters.Select(p => $"{p}: any"));
        sb.AppendLine($"export async function {fn.Name}({paramStr}): Promise<any> {{");
        _indent++;
        foreach (var s in fn.Body)
        {
            TranspileStatement(s, sb);
        }
        _indent--;
        sb.AppendLine("}");
    }

    private void TranspileStatement(StatementNode stmt, StringBuilder sb)
    {
        switch (stmt)
        {
            case VariableAssignmentNode vAssign:
                sb.AppendLine($"{Indent()}{vAssign.VariableName} = {TranspileExpression(vAssign.Value)};");
                break;

            case MemberAssignmentNode mAssign:
                sb.AppendLine($"{Indent()}{TranspileExpression(mAssign.Target)}.{mAssign.MemberName} = {TranspileExpression(mAssign.Value)};");
                break;

            case IndexAssignmentNode idxAssign:
                sb.AppendLine($"{Indent()}{TranspileExpression(idxAssign.Target)}[{TranspileExpression(idxAssign.Index)}] = {TranspileExpression(idxAssign.Value)};");
                break;

            case ExpressionStatementNode exprStmt:
                sb.AppendLine($"{Indent()}{TranspileExpression(exprStmt.Expression)};");
                break;

            case ReturnStatementNode retStmt:
                string rVal = retStmt.Value != null ? TranspileExpression(retStmt.Value) : "";
                sb.AppendLine($"{Indent()}return {rVal};".TrimEnd());
                break;

            case IfStatementNode ifStmt:
                sb.AppendLine($"{Indent()}if ({TranspileExpression(ifStmt.Condition)}) {{");
                _indent++;
                foreach (var s in ifStmt.ThenBranch) TranspileStatement(s, sb);
                _indent--;
                if (ifStmt.ElseBranch != null && ifStmt.ElseBranch.Count > 0)
                {
                    sb.AppendLine($"{Indent()}}} else {{");
                    _indent++;
                    foreach (var s in ifStmt.ElseBranch) TranspileStatement(s, sb);
                    _indent--;
                }
                sb.AppendLine($"{Indent()}}}");
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
                sb.AppendLine($"{Indent()}break;");
                break;

            case ContinueStatementNode:
                sb.AppendLine($"{Indent()}continue;");
                break;

            case BlockStatementNode block:
                sb.AppendLine($"{Indent()}{{");
                _indent++;
                foreach (var s in block.Statements) TranspileStatement(s, sb);
                _indent--;
                sb.AppendLine($"{Indent()}}}");
                break;
        }
    }

    private void TranspileLoop(LoopStatementNode loop, StringBuilder sb)
    {
        if (loop.Kind == LoopKind.Range && loop.FromValue != null && loop.ToValue != null)
        {
            string varName = loop.LoopVariable ?? "i";
            string fromVal = TranspileExpression(loop.FromValue);
            string toVal = TranspileExpression(loop.ToValue);
            sb.AppendLine($"{Indent()}for (let {varName} = {fromVal}; {varName} <= {toVal}; {varName}++) {{");
            _indent++;
            foreach (var s in loop.Body) TranspileStatement(s, sb);
            _indent--;
            sb.AppendLine($"{Indent()}}}");
        }
        else if (loop.Kind == LoopKind.Until && loop.Condition != null)
        {
            string maxRetries = loop.MaxRetries != null ? TranspileExpression(loop.MaxRetries) : "3";
            sb.AppendLine($"{Indent()}for (let _attempt = 0; _attempt < {maxRetries}; _attempt++) {{");
            _indent++;
            sb.AppendLine($"{Indent()}if ({TranspileExpression(loop.Condition)}) break;");
            foreach (var s in loop.Body) TranspileStatement(s, sb);
            _indent--;
            sb.AppendLine($"{Indent()}}}");
        }
        else
        {
            sb.AppendLine($"{Indent()}while (true) {{");
            _indent++;
            foreach (var s in loop.Body) TranspileStatement(s, sb);
            _indent--;
            sb.AppendLine($"{Indent()}}}");
        }
    }

    private void TranspileTryCatch(TryCatchStatementNode tryCatch, StringBuilder sb)
    {
        sb.AppendLine($"{Indent()}try {{");
        _indent++;
        foreach (var s in tryCatch.TryBody) TranspileStatement(s, sb);
        _indent--;

        string catchVar = tryCatch.CatchClauses.FirstOrDefault()?.VariableName ?? "err";
        sb.AppendLine($"{Indent()}}} catch ({catchVar}: any) {{");
        _indent++;

        if (tryCatch.CatchClauses.Count > 1)
        {
            for (int i = 0; i < tryCatch.CatchClauses.Count; i++)
            {
                var clause = tryCatch.CatchClauses[i];
                string kw = i == 0 ? "if" : "else if";
                sb.AppendLine($"{Indent()}{kw} ({catchVar} instanceof {clause.ExceptionType} || {catchVar}?.name === '{clause.ExceptionType}') {{");
                _indent++;
                foreach (var s in clause.Body) TranspileStatement(s, sb);
                _indent--;
                sb.AppendLine($"{Indent()}}}");
            }
        }
        else if (tryCatch.CatchClauses.Count == 1)
        {
            foreach (var s in tryCatch.CatchClauses[0].Body) TranspileStatement(s, sb);
        }

        _indent--;
        sb.AppendLine($"{Indent()}}}");
    }

    private void TranspileDecide(DecideStatementNode decide, StringBuilder sb)
    {
        if (decide.Condition != null)
        {
            if (!string.IsNullOrEmpty(decide.Reasoning))
            {
                sb.AppendLine($"{Indent()}// Reasoning: {decide.Reasoning}");
            }
            sb.AppendLine($"{Indent()}if ({TranspileExpression(decide.Condition)}) {{");
            _indent++;
            if (decide.Action != null)
            {
                foreach (var s in decide.Action) TranspileStatement(s, sb);
            }
            _indent--;
            sb.AppendLine($"{Indent()}}}");
        }
        else if (decide.Cases.Count > 0)
        {
            for (int i = 0; i < decide.Cases.Count; i++)
            {
                var c = decide.Cases[i];
                string keyword = i == 0 ? "if" : "} else if";
                sb.AppendLine($"{Indent()}{keyword} ({TranspileExpression(c.Condition)}) {{");
                _indent++;
                foreach (var s in c.Body) TranspileStatement(s, sb);
                _indent--;
            }
            if (decide.DefaultBranch != null && decide.DefaultBranch.Count > 0)
            {
                sb.AppendLine($"{Indent()}}} else {{");
                _indent++;
                foreach (var s in decide.DefaultBranch) TranspileStatement(s, sb);
                _indent--;
            }
            sb.AppendLine($"{Indent()}}}");
        }
    }

    private string TranspileExpression(ExpressionNode expr)
    {
        return expr switch
        {
            LiteralExpressionNode lit => FormatLiteral(lit.Value),
            IdentifierExpressionNode id => MapIdentifier(id.Name),
            BinaryExpressionNode bin => $"({TranspileExpression(bin.Left)} {MapBinaryOp(bin.Operator)} {TranspileExpression(bin.Right)})",
            UnaryExpressionNode un => $"{MapUnaryOp(un.Operator)}{TranspileExpression(un.Operand)}",
            CallExpressionNode call => TranspileCall(call),
            MemberAccessExpressionNode mem => $"{TranspileExpression(mem.Target)}.{mem.MemberName}",
            ListLiteralExpressionNode list => $"[{string.Join(", ", list.Elements.Select(TranspileExpression))}]",
            MapLiteralExpressionNode map => $"{{{string.Join(", ", map.Entries.Select(kv => $"\"{kv.Key}\": {TranspileExpression(kv.Value)}"))}}}",
            NamedArgumentExpressionNode na => $"{na.Name}: {TranspileExpression(na.Value)}",
            IndexAccessExpressionNode idx => $"{TranspileExpression(idx.Target)}[{TranspileExpression(idx.Index)}]",
            _ => expr.ToString() ?? "null"
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
        if (val == null) return "null";
        if (val is bool b) return b ? "true" : "false";
        if (val is string s) return $"\"{s.Replace("\"", "\\\"")}\"";
        return val.ToString()!;
    }

    private static string MapIdentifier(string id)
    {
        if (id.Equals("this", StringComparison.OrdinalIgnoreCase)) return "this";
        return id;
    }

    private static string MapBinaryOp(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Equal => "===",
        BinaryOperator.NotEqual => "!==",
        BinaryOperator.LessThan => "<",
        BinaryOperator.LessThanOrEqual => "<=",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.GreaterThanOrEqual => ">=",
        BinaryOperator.And => "&&",
        BinaryOperator.Or => "||",
        _ => "==="
    };

    private static string MapUnaryOp(UnaryOperator op) => op switch
    {
        UnaryOperator.Not => "!",
        UnaryOperator.Negate => "-",
        _ => ""
    };

    private static string MapTypeToTypeScript(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "any";
        string t = type.ToLowerInvariant();
        if (t.Contains("string") || t.Contains("str")) return "string";
        if (t.Contains("int") || t.Contains("float") || t.Contains("double")) return "number";
        if (t.Contains("bool")) return "boolean";
        if (t.Contains("list[string]")) return "string[]";
        if (t.Contains("list")) return "any[]";
        if (t.Contains("dict")) return "Record<string, any>";
        if (t.Contains("enum")) return "string";
        return type;
    }

    private string Indent() => new string(' ', _indent * 4);
}

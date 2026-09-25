namespace AgentLang.AST;

public abstract class AstVisitor : IAstVisitor
{
    public virtual void Visit(ProgramNode node)
    {
        foreach (var decl in node.Declarations)
            decl.Accept(this);
        node.Main?.Accept(this);
    }

    public virtual void Visit(MainBlockNode node)
    {
        foreach (var stmt in node.Statements)
            stmt.Accept(this);
    }

    public virtual void Visit(AgentDeclarationNode node)
    {
        foreach (var cfg in node.Config)
            cfg.Accept(this);
        foreach (var item in node.Body)
            item.Accept(this);
    }

    public virtual void Visit(AgentConfigItemNode node) => node.Value.Accept(this);

    public virtual void Visit(MultiAgentDeclarationNode node)
    {
        foreach (var item in node.Body)
            item.Accept(this);
    }

    public virtual void Visit(TaskDeclarationNode node)
    {
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(ContextDeclarationNode node) => node.Value.Accept(this);

    public virtual void Visit(PermissionDeclarationNode node)
    {
        foreach (var rule in node.Rules)
            rule.Accept(this);
    }

    public virtual void Visit(PermissionRuleNode node) { }

    public virtual void Visit(ToolDeclarationNode node)
    {
        foreach (var member in node.Members)
            member.Accept(this);
    }

    public virtual void Visit(EventDeclarationNode node)
    {
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(BlockStatementNode node)
    {
        foreach (var stmt in node.Statements)
            stmt.Accept(this);
    }

    public virtual void Visit(ExpressionStatementNode node) => node.Expression.Accept(this);

    public virtual void Visit(VariableAssignmentNode node) => node.Value.Accept(this);

    public virtual void Visit(IfStatementNode node)
    {
        node.Condition.Accept(this);
        foreach (var stmt in node.ThenBranch)
            stmt.Accept(this);
        if (node.ElseBranch != null)
        {
            foreach (var stmt in node.ElseBranch)
                stmt.Accept(this);
        }
    }

    public virtual void Visit(WhileStatementNode node)
    {
        node.Condition.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(RepeatStatementNode node)
    {
        node.Count.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(ForStatementNode node)
    {
        node.Iterable.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(ReturnStatementNode node) => node.Value?.Accept(this);

    public virtual void Visit(TryCatchStatementNode node)
    {
        foreach (var stmt in node.TryBody)
            stmt.Accept(this);
        foreach (var stmt in node.CatchBody)
            stmt.Accept(this);
    }

    public virtual void Visit(RetryStatementNode node)
    {
        node.Count.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(ParallelBlockNode node)
    {
        foreach (var item in node.Body)
            item.Accept(this);
    }

    public virtual void Visit(AgentInvocationNode node) { }

    public virtual void Visit(LiteralExpressionNode node) { }
    public virtual void Visit(IdentifierExpressionNode node) { }

    public virtual void Visit(BinaryExpressionNode node)
    {
        node.Left.Accept(this);
        node.Right.Accept(this);
    }

    public virtual void Visit(UnaryExpressionNode node) => node.Operand.Accept(this);

    public virtual void Visit(CallExpressionNode node)
    {
        node.Callee.Accept(this);
        foreach (var arg in node.Arguments)
            arg.Accept(this);
    }

    public virtual void Visit(MemberAccessExpressionNode node) => node.Target.Accept(this);

    public virtual void Visit(ListLiteralExpressionNode node)
    {
        foreach (var el in node.Elements)
            el.Accept(this);
    }

    public virtual void Visit(MapLiteralExpressionNode node)
    {
        foreach (var kvp in node.Entries)
            kvp.Value.Accept(this);
    }

    public virtual void Visit(AiOperationExpressionNode node)
    {
        foreach (var arg in node.Arguments)
            arg.Accept(this);
    }
}

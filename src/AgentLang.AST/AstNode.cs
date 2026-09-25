namespace AgentLang.AST;

public abstract class AstNode
{
    public SourceSpan Span { get; set; }

    protected AstNode(SourceSpan span)
    {
        Span = span;
    }

    public abstract void Accept(IAstVisitor visitor);
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

public interface IAstVisitor
{
    void Visit(ProgramNode node);
    void Visit(MainBlockNode node);
    void Visit(AgentDeclarationNode node);
    void Visit(AgentConfigItemNode node);
    void Visit(MultiAgentDeclarationNode node);
    void Visit(TaskDeclarationNode node);
    void Visit(ContextDeclarationNode node);
    void Visit(PermissionDeclarationNode node);
    void Visit(PermissionRuleNode node);
    void Visit(ToolDeclarationNode node);
    void Visit(EventDeclarationNode node);

    void Visit(BlockStatementNode node);
    void Visit(ExpressionStatementNode node);
    void Visit(VariableAssignmentNode node);
    void Visit(IfStatementNode node);
    void Visit(WhileStatementNode node);
    void Visit(RepeatStatementNode node);
    void Visit(ForStatementNode node);
    void Visit(ReturnStatementNode node);
    void Visit(TryCatchStatementNode node);
    void Visit(RetryStatementNode node);
    void Visit(ParallelBlockNode node);
    void Visit(AgentInvocationNode node);

    void Visit(LiteralExpressionNode node);
    void Visit(IdentifierExpressionNode node);
    void Visit(BinaryExpressionNode node);
    void Visit(UnaryExpressionNode node);
    void Visit(CallExpressionNode node);
    void Visit(MemberAccessExpressionNode node);
    void Visit(ListLiteralExpressionNode node);
    void Visit(MapLiteralExpressionNode node);
    void Visit(AiOperationExpressionNode node);
}

public interface IAstVisitor<T>
{
    T Visit(ProgramNode node);
    T Visit(MainBlockNode node);
    T Visit(AgentDeclarationNode node);
    T Visit(AgentConfigItemNode node);
    T Visit(MultiAgentDeclarationNode node);
    T Visit(TaskDeclarationNode node);
    T Visit(ContextDeclarationNode node);
    T Visit(PermissionDeclarationNode node);
    T Visit(PermissionRuleNode node);
    T Visit(ToolDeclarationNode node);
    T Visit(EventDeclarationNode node);

    T Visit(BlockStatementNode node);
    T Visit(ExpressionStatementNode node);
    T Visit(VariableAssignmentNode node);
    T Visit(IfStatementNode node);
    T Visit(WhileStatementNode node);
    T Visit(RepeatStatementNode node);
    T Visit(ForStatementNode node);
    T Visit(ReturnStatementNode node);
    T Visit(TryCatchStatementNode node);
    T Visit(RetryStatementNode node);
    T Visit(ParallelBlockNode node);
    T Visit(AgentInvocationNode node);

    T Visit(LiteralExpressionNode node);
    T Visit(IdentifierExpressionNode node);
    T Visit(BinaryExpressionNode node);
    T Visit(UnaryExpressionNode node);
    T Visit(CallExpressionNode node);
    T Visit(MemberAccessExpressionNode node);
    T Visit(ListLiteralExpressionNode node);
    T Visit(MapLiteralExpressionNode node);
    T Visit(AiOperationExpressionNode node);
}

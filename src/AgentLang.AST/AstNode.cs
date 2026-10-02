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
    void Visit(FunctionDeclarationNode node);
    void Visit(ModelDeclarationNode node);
    void Visit(CustomToolDeclarationNode node);
    void Visit(ToolParameterNode node);
    void Visit(ImportApiDeclarationNode node);
    void Visit(SwarmDeclarationNode node);
    void Visit(DatasetDeclarationNode node);
    void Visit(DatasetPairNode node);
    void Visit(DatasetPreferenceNode node);
    void Visit(TrainDeclarationNode node);
    void Visit(TrainValidationNode node);
    void Visit(ValidationTestCaseNode node);
    void Visit(McpDeclarationNode node);
    void Visit(CustomApiDeclarationNode node);
    void Visit(ApiMethodDeclarationNode node);
    void Visit(GoalDeclarationNode node);
    void Visit(PipelineDeclarationNode node);
    void Visit(StateDeclarationNode node);
    void Visit(StateFieldNode node);
    void Visit(WorkflowDeclarationNode node);
    void Visit(GuardrailsDeclarationNode node);
    void Visit(GuardrailRuleNode node);
    void Visit(OnEventDeclarationNode node);

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
    void Visit(SendMessageStatementNode node);
    void Visit(BroadcastStatementNode node);
    void Visit(WaitStatementNode node);
    void Visit(UntilStatementNode node);
    void Visit(LearnStatementNode node);
    void Visit(DecideStatementNode node);
    void Visit(DecideCaseNode node);
    void Visit(LoopStatementNode node);
    void Visit(BudgetStatementNode node);
    void Visit(BreakStatementNode node);
    void Visit(ContinueStatementNode node);
    void Visit(MemberAssignmentNode node);
    void Visit(IndexAssignmentNode node);

    void Visit(LiteralExpressionNode node);
    void Visit(IdentifierExpressionNode node);
    void Visit(BinaryExpressionNode node);
    void Visit(UnaryExpressionNode node);
    void Visit(CallExpressionNode node);
    void Visit(MemberAccessExpressionNode node);
    void Visit(ListLiteralExpressionNode node);
    void Visit(MapLiteralExpressionNode node);
    void Visit(AiOperationExpressionNode node);
    void Visit(IndexAccessExpressionNode node);
    void Visit(DelegateExpressionNode node);
    void Visit(PlanExpressionNode node);
    void Visit(NamedArgumentExpressionNode node);
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
    T Visit(FunctionDeclarationNode node);
    T Visit(ModelDeclarationNode node);
    T Visit(CustomToolDeclarationNode node);
    T Visit(ToolParameterNode node);
    T Visit(ImportApiDeclarationNode node);
    T Visit(SwarmDeclarationNode node);
    T Visit(DatasetDeclarationNode node);
    T Visit(DatasetPairNode node);
    T Visit(DatasetPreferenceNode node);
    T Visit(TrainDeclarationNode node);
    T Visit(TrainValidationNode node);
    T Visit(ValidationTestCaseNode node);
    T Visit(McpDeclarationNode node);
    T Visit(CustomApiDeclarationNode node);
    T Visit(ApiMethodDeclarationNode node);
    T Visit(GoalDeclarationNode node);
    T Visit(PipelineDeclarationNode node);
    T Visit(StateDeclarationNode node);
    T Visit(StateFieldNode node);
    T Visit(WorkflowDeclarationNode node);
    T Visit(GuardrailsDeclarationNode node);
    T Visit(GuardrailRuleNode node);
    T Visit(OnEventDeclarationNode node);

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
    T Visit(SendMessageStatementNode node);
    T Visit(BroadcastStatementNode node);
    T Visit(WaitStatementNode node);
    T Visit(UntilStatementNode node);
    T Visit(LearnStatementNode node);
    T Visit(DecideStatementNode node);
    T Visit(DecideCaseNode node);
    T Visit(LoopStatementNode node);
    T Visit(BudgetStatementNode node);
    T Visit(BreakStatementNode node);
    T Visit(ContinueStatementNode node);
    T Visit(MemberAssignmentNode node);
    T Visit(IndexAssignmentNode node);

    T Visit(LiteralExpressionNode node);
    T Visit(IdentifierExpressionNode node);
    T Visit(BinaryExpressionNode node);
    T Visit(UnaryExpressionNode node);
    T Visit(CallExpressionNode node);
    T Visit(MemberAccessExpressionNode node);
    T Visit(ListLiteralExpressionNode node);
    T Visit(MapLiteralExpressionNode node);
    T Visit(AiOperationExpressionNode node);
    T Visit(IndexAccessExpressionNode node);
    T Visit(DelegateExpressionNode node);
    T Visit(PlanExpressionNode node);
    T Visit(NamedArgumentExpressionNode node);
}

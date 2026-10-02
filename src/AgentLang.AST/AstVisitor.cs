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

    public virtual void Visit(FunctionDeclarationNode node)
    {
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(ModelDeclarationNode node) { }

    public virtual void Visit(CustomToolDeclarationNode node)
    {
        foreach (var param in node.Inputs)
            param.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(ToolParameterNode node) { }
    public virtual void Visit(ImportApiDeclarationNode node)
    {
        node.ApiKey.Accept(this);
        foreach (var opt in node.Options.Values)
            opt.Accept(this);
    }

    public virtual void Visit(SwarmDeclarationNode node)
    {
        foreach (var item in node.Body)
            item.Accept(this);
    }

    public virtual void Visit(DatasetDeclarationNode node)
    {
        foreach (var item in node.Items)
            item.Accept(this);
    }

    public virtual void Visit(DatasetPairNode node)
    {
        node.Input.Accept(this);
        node.Output.Accept(this);
    }

    public virtual void Visit(DatasetPreferenceNode node)
    {
        node.Prompt.Accept(this);
        node.Chosen.Accept(this);
        node.Rejected.Accept(this);
    }

    public virtual void Visit(TrainDeclarationNode node)
    {
        node.BaseModel?.Accept(this);
        node.DatasetRef?.Accept(this);
        node.Epochs?.Accept(this);
        node.LearningRate?.Accept(this);
        node.Validation?.Accept(this);
    }

    public virtual void Visit(TrainValidationNode node)
    {
        foreach (var tc in node.TestCases)
            tc.Accept(this);
        node.MinAccuracy?.Accept(this);
    }

    public virtual void Visit(ValidationTestCaseNode node)
    {
        node.Prompt.Accept(this);
        node.Expected.Accept(this);
    }

    public virtual void Visit(McpDeclarationNode node)
    {
        node.CommandOrPath.Accept(this);
        foreach (var envVal in node.Env.Values)
            envVal.Accept(this);
    }

    public virtual void Visit(CustomApiDeclarationNode node)
    {
        node.Endpoint.Accept(this);
        node.ApiType?.Accept(this);
        node.DefaultModel?.Accept(this);
        foreach (var header in node.Headers.Values)
            header.Accept(this);
        foreach (var method in node.Methods)
            method.Accept(this);
    }

    public virtual void Visit(ApiMethodDeclarationNode node)
    {
        foreach (var p in node.Parameters)
            p.Accept(this);
        node.PathExpression?.Accept(this);
    }

    public virtual void Visit(GoalDeclarationNode node) => node.Value.Accept(this);

    public virtual void Visit(PipelineDeclarationNode node)
    {
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(StateDeclarationNode node)
    {
        foreach (var field in node.Fields)
            field.Accept(this);
    }

    public virtual void Visit(StateFieldNode node) => node.DefaultValue?.Accept(this);

    public virtual void Visit(WorkflowDeclarationNode node)
    {
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(GuardrailsDeclarationNode node)
    {
        foreach (var rule in node.Rules)
            rule.Accept(this);
    }

    public virtual void Visit(GuardrailRuleNode node) => node.Value?.Accept(this);

    public virtual void Visit(OnEventDeclarationNode node)
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
    public virtual void Visit(SendMessageStatementNode node)
    {
        node.Message.Accept(this);
        node.Tag?.Accept(this);
    }

    public virtual void Visit(BroadcastStatementNode node)
    {
        node.Message.Accept(this);
        node.Tag?.Accept(this);
    }

    public virtual void Visit(WaitStatementNode node) => node.TargetOrDuration.Accept(this);

    public virtual void Visit(UntilStatementNode node)
    {
        node.Condition.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(LearnStatementNode node)
    {
        node.DatasetRef.Accept(this);
        node.InputOrPrompt.Accept(this);
        node.OutputOrChosen.Accept(this);
        node.Rejected?.Accept(this);
    }

    public virtual void Visit(DecideStatementNode node)
    {
        node.Condition?.Accept(this);
        if (node.Action != null)
        {
            foreach (var stmt in node.Action)
                stmt.Accept(this);
        }
        foreach (var c in node.Cases)
            c.Accept(this);
        if (node.DefaultBranch != null)
        {
            foreach (var stmt in node.DefaultBranch)
                stmt.Accept(this);
        }
    }

    public virtual void Visit(DecideCaseNode node)
    {
        node.Condition.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(LoopStatementNode node)
    {
        node.Condition?.Accept(this);
        node.FromValue?.Accept(this);
        node.ToValue?.Accept(this);
        node.MaxRetries?.Accept(this);
        foreach (var stmt in node.Body)
            stmt.Accept(this);
    }

    public virtual void Visit(BudgetStatementNode node)
    {
        node.Value.Accept(this);
    }

    public virtual void Visit(BreakStatementNode node) { }
    public virtual void Visit(ContinueStatementNode node) { }

    public virtual void Visit(MemberAssignmentNode node)
    {
        node.Target.Accept(this);
        node.Value.Accept(this);
    }

    public virtual void Visit(IndexAssignmentNode node)
    {
        node.Target.Accept(this);
        node.Index.Accept(this);
        node.Value.Accept(this);
    }

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

    public virtual void Visit(IndexAccessExpressionNode node)
    {
        node.Target.Accept(this);
        node.Index.Accept(this);
    }

    public virtual void Visit(DelegateExpressionNode node) => node.Message.Accept(this);
    public virtual void Visit(PlanExpressionNode node) => node.Prompt.Accept(this);
    public virtual void Visit(NamedArgumentExpressionNode node) => node.Value.Accept(this);
}

namespace AgentLang.AST;

public abstract class StatementNode(SourceSpan span) : AstNode(span)
{
}

public sealed class BlockStatementNode(IReadOnlyList<StatementNode> statements, SourceSpan span)
    : StatementNode(span)
{
    public IReadOnlyList<StatementNode> Statements { get; } = statements;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ExpressionStatementNode(ExpressionNode expression, SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Expression { get; } = expression;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class VariableAssignmentNode(string variableName, ExpressionNode value, SourceSpan span)
    : StatementNode(span)
{
    public string VariableName { get; } = variableName;
    public ExpressionNode Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class IfStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> thenBranch,
    IReadOnlyList<StatementNode>? elseBranch,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Condition { get; } = condition;
    public IReadOnlyList<StatementNode> ThenBranch { get; } = thenBranch;
    public IReadOnlyList<StatementNode>? ElseBranch { get; } = elseBranch;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class WhileStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Condition { get; } = condition;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class RepeatStatementNode(
    ExpressionNode count,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Count { get; } = count;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ForStatementNode(
    string variableName,
    ExpressionNode iterable,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public string VariableName { get; } = variableName;
    public ExpressionNode Iterable { get; } = iterable;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ReturnStatementNode(ExpressionNode? value, SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode? Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class TryCatchStatementNode(
    IReadOnlyList<StatementNode> tryBody,
    string? catchVariable,
    IReadOnlyList<StatementNode> catchBody,
    SourceSpan span)
    : StatementNode(span)
{
    public IReadOnlyList<StatementNode> TryBody { get; } = tryBody;
    public string? CatchVariable { get; } = catchVariable;
    public IReadOnlyList<StatementNode> CatchBody { get; } = catchBody;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class RetryStatementNode(
    ExpressionNode count,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Count { get; } = count;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ParallelBlockNode(
    IReadOnlyList<AstNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public IReadOnlyList<AstNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class AgentInvocationNode(string agentName, SourceSpan span)
    : StatementNode(span)
{
    public string AgentName { get; } = agentName;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class SendMessageStatementNode(
    ExpressionNode message,
    string targetAgent,
    ExpressionNode? tag,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Message { get; } = message;
    public string TargetAgent { get; } = targetAgent;
    public ExpressionNode? Tag { get; } = tag;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class BroadcastStatementNode(
    ExpressionNode message,
    ExpressionNode? tag,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Message { get; } = message;
    public ExpressionNode? Tag { get; } = tag;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class WaitStatementNode(
    ExpressionNode targetOrDuration,
    bool isAwait,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode TargetOrDuration { get; } = targetOrDuration;
    public bool IsAwait { get; } = isAwait;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class UntilStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Condition { get; } = condition;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class LearnStatementNode(
    ExpressionNode datasetRef,
    ExpressionNode inputOrPrompt,
    ExpressionNode outputOrChosen,
    ExpressionNode? rejected,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode DatasetRef { get; } = datasetRef;
    public ExpressionNode InputOrPrompt { get; } = inputOrPrompt;
    public ExpressionNode OutputOrChosen { get; } = outputOrChosen;
    public ExpressionNode? Rejected { get; } = rejected;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}


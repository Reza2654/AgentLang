namespace AgentLang.AST;

public abstract class ExpressionNode(SourceSpan span) : AstNode(span)
{
}

public sealed class LiteralExpressionNode(object? value, SourceSpan span) : ExpressionNode(span)
{
    public object? Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class IdentifierExpressionNode(string name, SourceSpan span) : ExpressionNode(span)
{
    public string Name { get; } = name;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    And,
    Or
}

public sealed class BinaryExpressionNode(ExpressionNode left, BinaryOperator op, ExpressionNode right, SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Left { get; } = left;
    public BinaryOperator Operator { get; } = op;
    public ExpressionNode Right { get; } = right;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public enum UnaryOperator
{
    Not,
    Negate
}

public sealed class UnaryExpressionNode(UnaryOperator op, ExpressionNode operand, SourceSpan span)
    : ExpressionNode(span)
{
    public UnaryOperator Operator { get; } = op;
    public ExpressionNode Operand { get; } = operand;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class CallExpressionNode(ExpressionNode callee, IReadOnlyList<ExpressionNode> arguments, SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Callee { get; } = callee;
    public IReadOnlyList<ExpressionNode> Arguments { get; } = arguments;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class MemberAccessExpressionNode(ExpressionNode target, string memberName, SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public string MemberName { get; } = memberName;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ListLiteralExpressionNode(IReadOnlyList<ExpressionNode> elements, SourceSpan span)
    : ExpressionNode(span)
{
    public IReadOnlyList<ExpressionNode> Elements { get; } = elements;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class MapLiteralExpressionNode(IReadOnlyList<KeyValuePair<string, ExpressionNode>> entries, SourceSpan span)
    : ExpressionNode(span)
{
    public IReadOnlyList<KeyValuePair<string, ExpressionNode>> Entries { get; } = entries;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class AiOperationExpressionNode(string operationName, IReadOnlyList<ExpressionNode> arguments, SourceSpan span)
    : ExpressionNode(span)
{
    public string OperationName { get; } = operationName; // e.g. "think", "research"
    public IReadOnlyList<ExpressionNode> Arguments { get; } = arguments;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class IndexAccessExpressionNode(
    ExpressionNode target,
    ExpressionNode index,
    SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public ExpressionNode Index { get; } = index;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class DelegateExpressionNode(
    ExpressionNode message,
    string targetAgent,
    SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Message { get; } = message;
    public string TargetAgent { get; } = targetAgent;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class PlanExpressionNode(
    ExpressionNode prompt,
    SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Prompt { get; } = prompt;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class NamedArgumentExpressionNode(
    string name,
    ExpressionNode value,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Name { get; } = name;
    public ExpressionNode Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

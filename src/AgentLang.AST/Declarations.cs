namespace AgentLang.AST;

public abstract class DeclarationNode(SourceSpan span) : StatementNode(span)
{
}

public sealed class AgentConfigItemNode(string key, ExpressionNode value, SourceSpan span)
    : AstNode(span)
{
    public string Key { get; } = key;
    public ExpressionNode Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class AgentDeclarationNode(
    string name,
    IReadOnlyList<AgentConfigItemNode> config,
    IReadOnlyList<AstNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<AgentConfigItemNode> Config { get; } = config;
    public IReadOnlyList<AstNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class MultiAgentDeclarationNode(
    string name,
    IReadOnlyList<AstNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<AstNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class TaskDeclarationNode : AstNode
{
    public string Name { get; }
    public IReadOnlyList<AgentConfigItemNode> Config { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public TaskDeclarationNode(
        string name,
        IReadOnlyList<AgentConfigItemNode> config,
        IReadOnlyList<StatementNode> body,
        SourceSpan span)
        : base(span)
    {
        Name = name;
        Config = config;
        Body = body;
    }

    public TaskDeclarationNode(string name, IReadOnlyList<StatementNode> body, SourceSpan span)
        : this(name, [], body, span)
    {
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ContextDeclarationNode(
    string name,
    ExpressionNode value,
    SourceSpan span)
    : AstNode(span)
{
    public string Name { get; } = name;
    public ExpressionNode Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public enum PermissionAction
{
    Allow,
    Ask,
    Cannot
}

public sealed class PermissionRuleNode(
    PermissionAction action,
    string capabilityPattern,
    SourceSpan span)
    : AstNode(span)
{
    public PermissionAction Action { get; } = action;
    public string CapabilityPattern { get; } = capabilityPattern;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class PermissionDeclarationNode(
    string name,
    IReadOnlyList<PermissionRuleNode> rules,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<PermissionRuleNode> Rules { get; } = rules;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ToolDeclarationNode(
    string name,
    IReadOnlyList<AstNode> members,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<AstNode> Members { get; } = members;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ToolParameterNode(string name, string typeName, SourceSpan span) : AstNode(span)
{
    public string Name { get; } = name;
    public string TypeName { get; } = typeName;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class CustomToolDeclarationNode(
    string name,
    string? description,
    IReadOnlyList<ToolParameterNode> inputs,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public string? Description { get; } = description;
    public IReadOnlyList<ToolParameterNode> Inputs { get; } = inputs;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class FunctionDeclarationNode(
    string name,
    IReadOnlyList<string> parameters,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> Parameters { get; } = parameters;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ModelDeclarationNode(
    string alias,
    string targetModel,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Alias { get; } = alias;
    public string TargetModel { get; } = targetModel;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ImportApiDeclarationNode(
    string provider,
    ExpressionNode apiKey,
    IReadOnlyDictionary<string, ExpressionNode>? options,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Provider { get; } = provider;
    public ExpressionNode ApiKey { get; } = apiKey;
    public IReadOnlyDictionary<string, ExpressionNode> Options { get; } = options ?? new Dictionary<string, ExpressionNode>();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class EventDeclarationNode(
    string target,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Target { get; } = target;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class MainBlockNode(
    IReadOnlyList<StatementNode> statements,
    SourceSpan span)
    : AstNode(span)
{
    public IReadOnlyList<StatementNode> Statements { get; } = statements;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ProgramNode(
    IReadOnlyList<DeclarationNode> declarations,
    MainBlockNode? main,
    SourceSpan span)
    : AstNode(span)
{
    public IReadOnlyList<DeclarationNode> Declarations { get; } = declarations;
    public MainBlockNode? Main { get; } = main;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

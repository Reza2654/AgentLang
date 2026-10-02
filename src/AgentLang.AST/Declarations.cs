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

public sealed class SwarmDeclarationNode(
    string name,
    string? coordinator,
    IReadOnlyList<string> agents,
    string? strategy,
    IReadOnlyList<AstNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public string? Coordinator { get; } = coordinator;
    public IReadOnlyList<string> Agents { get; } = agents;
    public string? Strategy { get; } = strategy;
    public IReadOnlyList<AstNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public abstract class DatasetItemNode(SourceSpan span) : AstNode(span)
{
}

public sealed class DatasetPairNode(
    ExpressionNode input,
    ExpressionNode output,
    SourceSpan span)
    : DatasetItemNode(span)
{
    public ExpressionNode Input { get; } = input;
    public ExpressionNode Output { get; } = output;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class DatasetPreferenceNode(
    ExpressionNode prompt,
    ExpressionNode chosen,
    ExpressionNode rejected,
    SourceSpan span)
    : DatasetItemNode(span)
{
    public ExpressionNode Prompt { get; } = prompt;
    public ExpressionNode Chosen { get; } = chosen;
    public ExpressionNode Rejected { get; } = rejected;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class DatasetDeclarationNode(
    string name,
    string? mode,
    IReadOnlyList<DatasetItemNode> items,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public string? Mode { get; } = mode; // "qa", "preference", "vision", etc.
    public IReadOnlyList<DatasetItemNode> Items { get; } = items;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ValidationTestCaseNode(
    ExpressionNode prompt,
    ExpressionNode expected,
    SourceSpan span)
    : AstNode(span)
{
    public ExpressionNode Prompt { get; } = prompt;
    public ExpressionNode Expected { get; } = expected;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class TrainValidationNode(
    IReadOnlyList<ValidationTestCaseNode> testCases,
    ExpressionNode? minAccuracy,
    SourceSpan span)
    : AstNode(span)
{
    public IReadOnlyList<ValidationTestCaseNode> TestCases { get; } = testCases;
    public ExpressionNode? MinAccuracy { get; } = minAccuracy;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class TrainDeclarationNode(
    string modelName,
    ExpressionNode? baseModel,
    ExpressionNode? datasetRef,
    ExpressionNode? epochs,
    ExpressionNode? learningRate,
    TrainValidationNode? validation,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string ModelName { get; } = modelName;
    public ExpressionNode? BaseModel { get; } = baseModel;
    public ExpressionNode? DatasetRef { get; } = datasetRef;
    public ExpressionNode? Epochs { get; } = epochs;
    public ExpressionNode? LearningRate { get; } = learningRate;
    public TrainValidationNode? Validation { get; } = validation;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class McpDeclarationNode(
    string serverName,
    ExpressionNode commandOrPath,
    IReadOnlyDictionary<string, ExpressionNode>? env,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string ServerName { get; } = serverName;
    public ExpressionNode CommandOrPath { get; } = commandOrPath;
    public IReadOnlyDictionary<string, ExpressionNode> Env { get; } = env ?? new Dictionary<string, ExpressionNode>();

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class ApiMethodDeclarationNode(
    string httpMethod,
    string name,
    IReadOnlyList<ToolParameterNode> parameters,
    ExpressionNode? pathExpression,
    SourceSpan span)
    : AstNode(span)
{
    public string HttpMethod { get; } = httpMethod;
    public string Name { get; } = name;
    public IReadOnlyList<ToolParameterNode> Parameters { get; } = parameters;
    public ExpressionNode? PathExpression { get; } = pathExpression;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class CustomApiDeclarationNode(
    string apiName,
    ExpressionNode endpoint,
    ExpressionNode? apiType,
    ExpressionNode? defaultModel,
    IReadOnlyDictionary<string, ExpressionNode>? headers,
    IReadOnlyList<ApiMethodDeclarationNode> methods,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string ApiName { get; } = apiName;
    public ExpressionNode Endpoint { get; } = endpoint;
    public ExpressionNode? ApiType { get; } = apiType;
    public ExpressionNode? DefaultModel { get; } = defaultModel;
    public IReadOnlyDictionary<string, ExpressionNode> Headers { get; } = headers ?? new Dictionary<string, ExpressionNode>();
    public IReadOnlyList<ApiMethodDeclarationNode> Methods { get; } = methods;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class GoalDeclarationNode(
    string name,
    ExpressionNode value,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public ExpressionNode Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class PipelineDeclarationNode(
    string name,
    IReadOnlyList<ToolParameterNode> inputs,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<ToolParameterNode> Inputs { get; } = inputs;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class StateFieldNode(
    string name,
    string? typeName,
    ExpressionNode? defaultValue,
    SourceSpan span)
    : AstNode(span)
{
    public string Name { get; } = name;
    public string? TypeName { get; } = typeName;
    public ExpressionNode? DefaultValue { get; } = defaultValue;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class StateDeclarationNode(
    string name,
    IReadOnlyList<StateFieldNode> fields,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<StateFieldNode> Fields { get; } = fields;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class WorkflowDeclarationNode(
    string name,
    IReadOnlyList<ToolParameterNode> parameters,
    string? returnType,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<ToolParameterNode> Parameters { get; } = parameters;
    public string? ReturnType { get; } = returnType;
    public IReadOnlyList<StatementNode> Body { get; } = body;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class GuardrailRuleNode(
    string kind,
    ExpressionNode value,
    SourceSpan span)
    : AstNode(span)
{
    public string Kind { get; } = kind;
    public ExpressionNode Value { get; } = value;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class GuardrailsDeclarationNode(
    IReadOnlyList<GuardrailRuleNode> rules,
    SourceSpan span)
    : DeclarationNode(span)
{
    public IReadOnlyList<GuardrailRuleNode> Rules { get; } = rules;

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

public sealed class OnEventDeclarationNode(
    string eventType,
    string parameterName,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : DeclarationNode(span)
{
    public string EventType { get; } = eventType;
    public string ParameterName { get; } = parameterName;
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

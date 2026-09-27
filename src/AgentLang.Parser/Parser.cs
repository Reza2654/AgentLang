using AgentLang.AST;
using AgentLang.Lexer;

namespace AgentLang.Parser;

public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly SourceText _source;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    public Parser(SourceText source, DiagnosticBag? diagnostics = null)
    {
        _source = source;
        _diagnostics = diagnostics ?? new DiagnosticBag();
        var lexer = new Lexer.Lexer(source, _diagnostics);
        _tokens = lexer.TokenizeAll();
    }

    public Parser(IReadOnlyList<Token> tokens, SourceText source, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _source = source;
        _diagnostics = diagnostics;
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private Token Current => Peek(0);
    private Token Lookahead => Peek(1);

    private Token Peek(int offset)
    {
        int index = _position + offset;
        return index >= _tokens.Count ? _tokens[^1] : _tokens[index];
    }

    private Token Match(TokenType expectedType, string? customMessage = null)
    {
        if (Current.Type == expectedType)
            return NextToken();

        string msg = customMessage ?? $"Expected token '{expectedType}', but found '{Current.Type}' ({Current.Text})";
        _diagnostics.ReportError("AL1001", msg, Current.Span);
        return new Token(expectedType, string.Empty, null, Current.Span);
    }

    private bool Check(TokenType type) => Current.Type == type;

    private bool MatchOptional(TokenType type, out Token token)
    {
        if (Check(type))
        {
            token = NextToken();
            return true;
        }
        token = default!;
        return false;
    }

    private static bool IsContextualIdentifier(TokenType type) =>
        type is TokenType.Identifier or TokenType.Research or TokenType.Think or
                TokenType.Model or TokenType.Memory or TokenType.Tools or
                TokenType.Context or TokenType.Task or TokenType.Agent or
                TokenType.Permission or TokenType.Tool or TokenType.Event or
                TokenType.Main or TokenType.Function or TokenType.Send or
                TokenType.To or TokenType.Description or TokenType.Execute or
                TokenType.Type or TokenType.ImportApi or TokenType.Swarm or
                TokenType.Broadcast or TokenType.Delegate or TokenType.Wait or
                TokenType.Await or TokenType.Plan or TokenType.Until or
                TokenType.Dataset or TokenType.Train or TokenType.Validate or
                TokenType.Mcp or TokenType.Api or TokenType.Learn or
                TokenType.Preference or TokenType.Pair or TokenType.Image or
                TokenType.Vision;

    private static bool CanStartUnary(TokenType type) =>
        type is TokenType.Exclamation or TokenType.Not or TokenType.Minus or
                TokenType.NumberLiteral or TokenType.StringLiteral or TokenType.BooleanLiteral or
                TokenType.NullLiteral or TokenType.OpenParen or TokenType.OpenBracket or
                TokenType.OpenBrace or TokenType.Think or TokenType.Research or
                TokenType.Plan or TokenType.Delegate or TokenType.Print or
                TokenType.Input or TokenType.Type or TokenType.Image or TokenType.Vision ||
                IsContextualIdentifier(type);

    private string ParseIdentifierName(string? customError = null)
    {
        if (IsContextualIdentifier(Current.Type))
        {
            return NextToken().Text;
        }

        return Match(TokenType.Identifier, customError).Text;
    }

    private Token NextToken()
    {
        var current = Current;
        if (_position < _tokens.Count - 1)
            _position++;
        return current;
    }

    public ProgramNode ParseProgram()
    {
        var startSpan = Current.Span;
        var declarations = new List<DeclarationNode>();
        MainBlockNode? main = null;

        while (!Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Main))
            {
                if (main != null)
                {
                    _diagnostics.ReportError("AL1002", "Duplicate 'main' block. Only one 'main' block is allowed per program.", Current.Span);
                }
                main = ParseMainBlock();
            }
            else if (Check(TokenType.Agent))
            {
                declarations.Add(ParseAgentDeclaration());
            }
            else if (Check(TokenType.MultiAgent))
            {
                declarations.Add(ParseMultiAgentDeclaration());
            }
            else if (Check(TokenType.Permission))
            {
                declarations.Add(ParsePermissionDeclaration());
            }
            else if (Check(TokenType.Tool))
            {
                declarations.Add(ParseToolDeclaration());
            }
            else if (Check(TokenType.Event))
            {
                declarations.Add(ParseEventDeclaration());
            }
            else if (Check(TokenType.Function))
            {
                declarations.Add(ParseFunctionDeclaration());
            }
            else if (Check(TokenType.Model) && Lookahead.Type != TokenType.OpenBrace)
            {
                declarations.Add(ParseModelDeclaration());
            }
            else if (Check(TokenType.Dependencies))
            {
                declarations.Add(ParseDependenciesDeclaration());
            }
            else if (Check(TokenType.ImportApi))
            {
                declarations.Add(ParseImportApiDeclaration());
            }
            else if (Check(TokenType.Swarm))
            {
                declarations.Add(ParseSwarmDeclaration());
            }
            else if (Check(TokenType.Dataset))
            {
                declarations.Add(ParseDatasetDeclaration());
            }
            else if (Check(TokenType.Train))
            {
                declarations.Add(ParseTrainDeclaration());
            }
            else if (Check(TokenType.Mcp))
            {
                declarations.Add(ParseMcpDeclaration());
            }
            else if (Check(TokenType.Api))
            {
                declarations.Add(ParseCustomApiDeclaration());
            }
            else
            {
                _diagnostics.ReportError("AL1003", $"Unexpected top-level token '{Current.Text}'", Current.Span);
                SynchronizeTopLevel();
            }
        }

        var endSpan = Current.Span;
        return new ProgramNode(declarations, main, new SourceSpan(startSpan.Start, endSpan.End, _source.FilePath));
    }

    private void SynchronizeTopLevel()
    {
        NextToken();
        while (!Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Main) || Check(TokenType.Agent) || Check(TokenType.MultiAgent) ||
                Check(TokenType.Permission) || Check(TokenType.Tool) || Check(TokenType.Event) ||
                Check(TokenType.Function) || Check(TokenType.Model) || Check(TokenType.ImportApi) ||
                Check(TokenType.Swarm))
            {
                return;
            }
            NextToken();
        }
    }

    private MainBlockNode ParseMainBlock()
    {
        var mainToken = Match(TokenType.Main);
        Match(TokenType.OpenBrace);

        var statements = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new MainBlockNode(statements, new SourceSpan(mainToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private AgentDeclarationNode ParseAgentDeclaration()
    {
        var agentToken = Match(TokenType.Agent);
        string name = ParseIdentifierName("Expected agent name identifier");

        var config = new List<AgentConfigItemNode>();
        if (Check(TokenType.OpenParen))
        {
            Match(TokenType.OpenParen);
            while (!Check(TokenType.CloseParen) && !Check(TokenType.EndOfFile))
            {
                var cfgStart = Current.Span;
                string key = Current.Text;
                NextToken(); // consume key identifier or keyword (e.g. model, memory, permission, tools, persona, temperature, fallback, goal)
                if (Check(TokenType.Equals) || Check(TokenType.Colon))
                    NextToken();
                else
                    Match(TokenType.Equals);
                var valExpr = ParseExpression();
                config.Add(new AgentConfigItemNode(key, valExpr, new SourceSpan(cfgStart.Start, valExpr.Span.End, _source.FilePath)));

                if (Check(TokenType.Comma))
                    Match(TokenType.Comma);
                else
                    break;
            }
            Match(TokenType.CloseParen);
        }

        Match(TokenType.OpenBrace);
        var body = new List<AstNode>();

        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Context))
            {
                body.Add(ParseContextDeclaration());
            }
            else if (Check(TokenType.Task))
            {
                body.Add(ParseTaskDeclaration());
            }
            else if (Check(TokenType.Function))
            {
                body.Add(ParseFunctionDeclaration());
            }
            else if (IsAgentConfigKey(Current.Text) && (Lookahead.Type == TokenType.Colon || Lookahead.Type == TokenType.Equals))
            {
                var cfgStart = Current.Span;
                string key = Current.Text;
                NextToken(); // consume key
                NextToken(); // consume : or =
                var valExpr = ParseExpression();
                MatchOptional(TokenType.Semicolon, out _);
                MatchOptional(TokenType.Comma, out _);
                config.Add(new AgentConfigItemNode(key, valExpr, new SourceSpan(cfgStart.Start, valExpr.Span.End, _source.FilePath)));
            }
            else
            {
                body.Add(ParseStatement());
            }
        }

        var closeBrace = Match(TokenType.CloseBrace);
        return new AgentDeclarationNode(name, config, body, new SourceSpan(agentToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private static bool IsAgentConfigKey(string key) =>
        key.Equals("role", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("instructions", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("instruction", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("system", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("model", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("tools", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("max_steps", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("maxsteps", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("memory", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("permission", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("temperature", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("goal", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("persona", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("fallback", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("fallbacks", StringComparison.OrdinalIgnoreCase);

    private MultiAgentDeclarationNode ParseMultiAgentDeclaration()
    {
        var multiToken = Match(TokenType.MultiAgent);
        string name = ParseIdentifierName("Expected multiagent name identifier");
        Match(TokenType.OpenBrace);

        var body = new List<AstNode>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Agent))
            {
                body.Add(ParseAgentDeclaration());
            }
            else if (Check(TokenType.Parallel))
            {
                body.Add(ParseParallelBlock());
            }
            else
            {
                body.Add(ParseStatement());
            }
        }

        var closeBrace = Match(TokenType.CloseBrace);
        return new MultiAgentDeclarationNode(name, body, new SourceSpan(multiToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private ParallelBlockNode ParseParallelBlock()
    {
        var parToken = Match(TokenType.Parallel);
        Match(TokenType.OpenBrace);

        var body = new List<AstNode>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Agent))
            {
                body.Add(ParseAgentDeclaration());
            }
            else
            {
                body.Add(ParseStatement());
            }
        }

        var closeBrace = Match(TokenType.CloseBrace);
        return new ParallelBlockNode(body, new SourceSpan(parToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private ContextDeclarationNode ParseContextDeclaration()
    {
        var ctxToken = Match(TokenType.Context);
        string name = ParseIdentifierName("Expected context name identifier");
        Match(TokenType.Equals);
        var expr = ParseExpression();
        MatchOptional(TokenType.Semicolon, out _);

        return new ContextDeclarationNode(name, expr, new SourceSpan(ctxToken.Span.Start, expr.Span.End, _source.FilePath));
    }

    private TaskDeclarationNode ParseTaskDeclaration()
    {
        var taskToken = Match(TokenType.Task);
        string name = ParseIdentifierName("Expected task name identifier");

        var config = new List<AgentConfigItemNode>();
        if (Check(TokenType.OpenParen))
        {
            Match(TokenType.OpenParen);
            while (!Check(TokenType.CloseParen) && !Check(TokenType.EndOfFile))
            {
                var cfgStart = Current.Span;
                string key = Current.Text;
                NextToken();
                Match(TokenType.Equals);
                var valExpr = ParseExpression();
                config.Add(new AgentConfigItemNode(key, valExpr, new SourceSpan(cfgStart.Start, valExpr.Span.End, _source.FilePath)));

                if (Check(TokenType.Comma))
                    Match(TokenType.Comma);
                else
                    break;
            }
            Match(TokenType.CloseParen);
        }

        Match(TokenType.OpenBrace);
        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new TaskDeclarationNode(name, config, body, new SourceSpan(taskToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private FunctionDeclarationNode ParseFunctionDeclaration()
    {
        var fnToken = Match(TokenType.Function);
        string name = ParseIdentifierName("Expected function name identifier");
        Match(TokenType.OpenParen);

        var parameters = new List<string>();
        while (!Check(TokenType.CloseParen) && !Check(TokenType.EndOfFile))
        {
            parameters.Add(ParseIdentifierName("Expected parameter name"));
            if (Check(TokenType.Comma))
                Match(TokenType.Comma);
            else
                break;
        }
        Match(TokenType.CloseParen);

        Match(TokenType.OpenBrace);
        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new FunctionDeclarationNode(name, parameters, body, new SourceSpan(fnToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private ModelDeclarationNode ParseModelDeclaration()
    {
        var modelToken = Match(TokenType.Model);
        string alias = ParseIdentifierName("Expected model alias identifier");
        Match(TokenType.Equals);
        string target = ReadCapabilityPath();
        MatchOptional(TokenType.Semicolon, out _);

        return new ModelDeclarationNode(alias, target, new SourceSpan(modelToken.Span.Start, Current.Span.End, _source.FilePath));
    }

    private PermissionDeclarationNode ParsePermissionDeclaration()
    {
        var permToken = Match(TokenType.Permission);
        string name = ParseIdentifierName("Expected permission name identifier");
        Match(TokenType.OpenBrace);

        var rules = new List<PermissionRuleNode>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            var ruleStart = Current.Span;
            PermissionAction action;
            if (Check(TokenType.Allow))
            {
                NextToken();
                action = PermissionAction.Allow;
            }
            else if (Check(TokenType.Ask))
            {
                NextToken();
                action = PermissionAction.Ask;
            }
            else if (Check(TokenType.Cannot))
            {
                NextToken();
                action = PermissionAction.Cannot;
            }
            else
            {
                _diagnostics.ReportError("AL1004", $"Expected 'allow', 'ask', or 'cannot' in permission definition, found '{Current.Text}'", Current.Span);
                NextToken();
                continue;
            }

            // Read dotted capability path e.g. browser.search or filesystem.write or terminal or *
            string path = ReadCapabilityPath();
            MatchOptional(TokenType.Semicolon, out _);

            rules.Add(new PermissionRuleNode(action, path, new SourceSpan(ruleStart.Start, Current.Span.End, _source.FilePath)));
        }

        var closeBrace = Match(TokenType.CloseBrace);
        return new PermissionDeclarationNode(name, rules, new SourceSpan(permToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private string ReadCapabilityPath()
    {
        var parts = new List<string>();
        while (IsContextualIdentifier(Current.Type) || Check(TokenType.Asterisk))
        {
            parts.Add(Current.Text);
            NextToken();
            if (Check(TokenType.Dot))
            {
                NextToken();
            }
            else
            {
                break;
            }
        }
        return string.Join(".", parts);
    }

    private DeclarationNode ParseToolDeclaration()
    {
        var toolToken = Match(TokenType.Tool);
        string toolName = ParseIdentifierName("Expected tool name identifier");
        Match(TokenType.OpenBrace);

        if (Check(TokenType.Description) || Check(TokenType.Input) || Check(TokenType.Execute))
        {
            string? description = null;
            var inputs = new List<ToolParameterNode>();
            List<StatementNode> executeBody = [];

            while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
            {
                if (Check(TokenType.Description))
                {
                    Match(TokenType.Description);
                    Match(TokenType.Equals);
                    var descExpr = ParseExpression();
                    if (descExpr is LiteralExpressionNode lit && lit.Value != null)
                    {
                        description = lit.Value.ToString();
                    }
                    MatchOptional(TokenType.Semicolon, out _);
                }
                else if (Check(TokenType.Input))
                {
                    var inputToken = Match(TokenType.Input);
                    string paramName = ParseIdentifierName("Expected input parameter name");
                    Match(TokenType.Colon);
                    string typeName = ParseIdentifierName("Expected input parameter type");
                    MatchOptional(TokenType.Semicolon, out _);
                    inputs.Add(new ToolParameterNode(paramName, typeName, new SourceSpan(inputToken.Span.Start, Current.Span.End, _source.FilePath)));
                }
                else if (Check(TokenType.Execute))
                {
                    Match(TokenType.Execute);
                    Match(TokenType.OpenBrace);
                    executeBody = ParseStatementListUntil(TokenType.CloseBrace);
                    Match(TokenType.CloseBrace);
                }
                else
                {
                    _diagnostics.ReportError("AL1006", $"Unexpected token '{Current.Text}' in custom tool declaration", Current.Span);
                    NextToken();
                }
            }

            var closeBrace = Match(TokenType.CloseBrace);
            return new CustomToolDeclarationNode(toolName, description, inputs, executeBody, new SourceSpan(toolToken.Span.Start, closeBrace.Span.End, _source.FilePath));
        }
        else
        {
            var members = new List<AstNode>();
            while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
            {
                if (Check(TokenType.Task))
                    members.Add(ParseTaskDeclaration());
                else if (Check(TokenType.Context))
                    members.Add(ParseContextDeclaration());
                else
                    members.Add(ParseStatement());
            }

            var closeBrace = Match(TokenType.CloseBrace);
            return new ToolDeclarationNode(toolName, members, new SourceSpan(toolToken.Span.Start, closeBrace.Span.End, _source.FilePath));
        }
    }

    private EventDeclarationNode ParseEventDeclaration()
    {
        var eventToken = Match(TokenType.Event);
        // Target can be dotted identifier, e.g. research.finished
        string target = ReadCapabilityPath();
        Match(TokenType.OpenBrace);

        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new EventDeclarationNode(target, body, new SourceSpan(eventToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private ToolDeclarationNode ParseDependenciesDeclaration()
    {
        var depToken = Match(TokenType.Dependencies);
        Match(TokenType.OpenBrace);
        var members = new List<AstNode>();
        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            members.Add(ParseStatement());
        }
        var closeBrace = Match(TokenType.CloseBrace);
        return new ToolDeclarationNode("dependencies", members, new SourceSpan(depToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private ImportApiDeclarationNode ParseImportApiDeclaration()
    {
        var importToken = Match(TokenType.ImportApi);
        string provider = ParseIdentifierName("Expected provider name after 'importapi' (e.g. 'gemini', 'tavily', 'openai')");

        ExpressionNode apiKeyExpr;
        var options = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase);

        if (MatchOptional(TokenType.OpenParen, out _))
        {
            while (!Check(TokenType.CloseParen) && !Check(TokenType.EndOfFile))
            {
                string optKey = ParseIdentifierName();
                Match(TokenType.Equals);
                var optVal = ParseExpression();
                options[optKey] = optVal;

                if (!MatchOptional(TokenType.Comma, out _))
                    break;
            }
            Match(TokenType.CloseParen);

            if (options.TryGetValue("key", out var kExpr) || options.TryGetValue("apiKey", out kExpr))
            {
                apiKeyExpr = kExpr;
            }
            else
            {
                apiKeyExpr = new LiteralExpressionNode("", new SourceSpan(importToken.Span.Start, importToken.Span.End, _source.FilePath));
            }
        }
        else
        {
            Match(TokenType.Equals);
            apiKeyExpr = ParseExpression();

            while (MatchOptional(TokenType.Comma, out _))
            {
                string optKey = ParseIdentifierName();
                Match(TokenType.Equals);
                var optVal = ParseExpression();
                options[optKey] = optVal;
            }
        }

        MatchOptional(TokenType.Semicolon, out _);
        var endSpan = options.Count > 0 ? options.Values.Last().Span.End : apiKeyExpr.Span.End;
        return new ImportApiDeclarationNode(provider, apiKeyExpr, options, new SourceSpan(importToken.Span.Start, endSpan, _source.FilePath));
    }

    private SwarmDeclarationNode ParseSwarmDeclaration()
    {
        var swarmToken = Match(TokenType.Swarm);
        string name = ParseIdentifierName("Expected swarm name identifier");
        Match(TokenType.OpenBrace);

        string? coordinator = null;
        string? strategy = null;
        var agentNames = new List<string>();
        var body = new List<AstNode>();

        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Agent))
            {
                if (Lookahead.Type == TokenType.Identifier && Peek(2).Type != TokenType.OpenBrace && Peek(2).Type != TokenType.OpenParen)
                {
                    NextToken(); // consume 'agent'
                    agentNames.Add(ParseIdentifierName());
                    MatchOptional(TokenType.Semicolon, out _);
                }
                else
                {
                    var agentDecl = ParseAgentDeclaration();
                    body.Add(agentDecl);
                    agentNames.Add(agentDecl.Name);
                }
            }
            else if (Current.Text.Equals("coordinator", StringComparison.OrdinalIgnoreCase) && (Lookahead.Type == TokenType.Colon || Lookahead.Type == TokenType.Equals))
            {
                NextToken(); // coordinator
                NextToken(); // : or =
                if (Check(TokenType.StringLiteral))
                {
                    coordinator = Current.Value?.ToString() ?? Current.Text;
                    NextToken();
                }
                else
                {
                    coordinator = ParseIdentifierName();
                }
                MatchOptional(TokenType.Semicolon, out _);
            }
            else if (Current.Text.Equals("strategy", StringComparison.OrdinalIgnoreCase) && (Lookahead.Type == TokenType.Colon || Lookahead.Type == TokenType.Equals))
            {
                NextToken(); // strategy
                NextToken(); // : or =
                var expr = ParseExpression();
                if (expr is LiteralExpressionNode lit)
                    strategy = lit.Value?.ToString();
                else if (expr is IdentifierExpressionNode idNode)
                    strategy = idNode.Name;
                else
                    strategy = expr.ToString();
                MatchOptional(TokenType.Semicolon, out _);
            }
            else if (Current.Text.Equals("agents", StringComparison.OrdinalIgnoreCase) && (Lookahead.Type == TokenType.Colon || Lookahead.Type == TokenType.Equals))
            {
                NextToken(); // agents
                NextToken(); // : or =
                Match(TokenType.OpenBracket);
                while (!Check(TokenType.CloseBracket) && !Check(TokenType.EndOfFile))
                {
                    agentNames.Add(ParseIdentifierName());
                    if (Check(TokenType.Comma)) NextToken();
                    else break;
                }
                Match(TokenType.CloseBracket);
                MatchOptional(TokenType.Semicolon, out _);
            }
            else
            {
                body.Add(ParseStatement());
            }
        }

        var closeBrace = Match(TokenType.CloseBrace);
        return new SwarmDeclarationNode(name, coordinator, agentNames, strategy, body, new SourceSpan(swarmToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private DatasetDeclarationNode ParseDatasetDeclaration()
    {
        var startToken = Match(TokenType.Dataset);
        string name = ParseIdentifierName("Expected dataset name identifier");
        Match(TokenType.OpenBrace, "Expected '{' after dataset name");

        string? mode = null;
        var items = new List<DatasetItemNode>();

        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Identifier) && Current.Text.Equals("mode", StringComparison.OrdinalIgnoreCase))
            {
                NextToken();
                if (MatchOptional(TokenType.Colon, out _) || MatchOptional(TokenType.Equals, out _))
                {
                    var modeExpr = ParseExpression();
                    mode = modeExpr is LiteralExpressionNode lit ? lit.Value?.ToString() : (modeExpr as IdentifierExpressionNode)?.Name;
                    MatchOptional(TokenType.Semicolon, out _);
                    MatchOptional(TokenType.Comma, out _);
                    continue;
                }
            }

            if (Check(TokenType.Pair) || (Check(TokenType.Identifier) && Current.Text.Equals("pair", StringComparison.OrdinalIgnoreCase)))
            {
                var pSpan = NextToken().Span;
                Match(TokenType.OpenParen, "Expected '(' after pair");
                ExpressionNode inputExpr;
                ExpressionNode outputExpr;

                if (Check(TokenType.Input) || (Check(TokenType.Identifier) && Current.Text.Equals("input", StringComparison.OrdinalIgnoreCase)))
                {
                    NextToken();
                    if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                    inputExpr = ParseExpression();
                    Match(TokenType.Comma);
                    if (Check(TokenType.Identifier) && Current.Text.Equals("output", StringComparison.OrdinalIgnoreCase))
                    {
                        NextToken();
                        if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                    }
                    outputExpr = ParseExpression();
                }
                else
                {
                    inputExpr = ParseExpression();
                    Match(TokenType.Comma);
                    outputExpr = ParseExpression();
                }
                var closeP = Match(TokenType.CloseParen);
                MatchOptional(TokenType.Semicolon, out _);
                MatchOptional(TokenType.Comma, out _);
                items.Add(new DatasetPairNode(inputExpr, outputExpr, new SourceSpan(pSpan.Start, closeP.Span.End, _source.FilePath)));
                continue;
            }

            if (Check(TokenType.Preference) || (Check(TokenType.Identifier) && Current.Text.Equals("preference", StringComparison.OrdinalIgnoreCase)))
            {
                var pSpan = NextToken().Span;
                Match(TokenType.OpenParen, "Expected '(' after preference");
                ExpressionNode promptExpr;
                ExpressionNode chosenExpr;
                ExpressionNode rejectedExpr;

                if (Check(TokenType.Identifier) && Current.Text.Equals("prompt", StringComparison.OrdinalIgnoreCase))
                {
                    NextToken();
                    if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                    promptExpr = ParseExpression();
                    Match(TokenType.Comma);
                    if (Check(TokenType.Identifier) && Current.Text.Equals("chosen", StringComparison.OrdinalIgnoreCase))
                    {
                        NextToken();
                        if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                    }
                    chosenExpr = ParseExpression();
                    Match(TokenType.Comma);
                    if (Check(TokenType.Identifier) && Current.Text.Equals("rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        NextToken();
                        if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                    }
                    rejectedExpr = ParseExpression();
                }
                else
                {
                    promptExpr = ParseExpression();
                    Match(TokenType.Comma);
                    chosenExpr = ParseExpression();
                    Match(TokenType.Comma);
                    rejectedExpr = ParseExpression();
                }
                var closeP = Match(TokenType.CloseParen);
                MatchOptional(TokenType.Semicolon, out _);
                MatchOptional(TokenType.Comma, out _);
                items.Add(new DatasetPreferenceNode(promptExpr, chosenExpr, rejectedExpr, new SourceSpan(pSpan.Start, closeP.Span.End, _source.FilePath)));
                continue;
            }

            _diagnostics.ReportError("AL1004", $"Unexpected token '{Current.Text}' in dataset declaration", Current.Span);
            NextToken();
        }

        var endBrace = Match(TokenType.CloseBrace);
        MatchOptional(TokenType.Semicolon, out _);
        return new DatasetDeclarationNode(name, mode, items, new SourceSpan(startToken.Span.Start, endBrace.Span.End, _source.FilePath));
    }

    private TrainDeclarationNode ParseTrainDeclaration()
    {
        var startToken = Match(TokenType.Train);
        if (Check(TokenType.Model))
        {
            NextToken();
        }
        string modelName = ParseIdentifierName("Expected trained model name identifier");
        Match(TokenType.OpenBrace, "Expected '{' after train declaration");

        ExpressionNode? baseModel = null;
        ExpressionNode? datasetRef = null;
        ExpressionNode? epochs = null;
        ExpressionNode? learningRate = null;
        TrainValidationNode? validation = null;

        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Validate) || (Check(TokenType.Identifier) && Current.Text.Equals("validate", StringComparison.OrdinalIgnoreCase)))
            {
                validation = ParseTrainValidation();
                continue;
            }

            string key = ParseIdentifierName("Expected configuration key in train block (e.g. 'base', 'data', 'epochs', 'learning_rate', 'validate')");

            if (MatchOptional(TokenType.Colon, out _) || MatchOptional(TokenType.Equals, out _))
            {
                var valExpr = ParseExpression();
                MatchOptional(TokenType.Semicolon, out _);
                MatchOptional(TokenType.Comma, out _);

                if (key.Equals("base", StringComparison.OrdinalIgnoreCase) || key.Equals("baseModel", StringComparison.OrdinalIgnoreCase))
                    baseModel = valExpr;
                else if (key.Equals("data", StringComparison.OrdinalIgnoreCase) || key.Equals("dataset", StringComparison.OrdinalIgnoreCase))
                    datasetRef = valExpr;
                else if (key.Equals("epochs", StringComparison.OrdinalIgnoreCase))
                    epochs = valExpr;
                else if (key.Equals("learning_rate", StringComparison.OrdinalIgnoreCase) || key.Equals("learningRate", StringComparison.OrdinalIgnoreCase) || key.Equals("lr", StringComparison.OrdinalIgnoreCase))
                    learningRate = valExpr;
            }
            else
            {
                _diagnostics.ReportError("AL1004", $"Unexpected token '{Current.Text}' in train declaration", Current.Span);
                NextToken();
            }
        }

        var endBrace = Match(TokenType.CloseBrace);
        MatchOptional(TokenType.Semicolon, out _);
        return new TrainDeclarationNode(modelName, baseModel, datasetRef, epochs, learningRate, validation, new SourceSpan(startToken.Span.Start, endBrace.Span.End, _source.FilePath));
    }

    private TrainValidationNode ParseTrainValidation()
    {
        var startSpan = Current.Span;
        if (Check(TokenType.Validate) || (Check(TokenType.Identifier) && Current.Text.Equals("validate", StringComparison.OrdinalIgnoreCase)))
        {
            NextToken();
        }
        Match(TokenType.OpenBrace, "Expected '{' for validate block");

        var testCases = new List<ValidationTestCaseNode>();
        ExpressionNode? minAccuracy = null;

        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Identifier) && (Current.Text.Equals("test", StringComparison.OrdinalIgnoreCase) || Current.Text.Equals("pair", StringComparison.OrdinalIgnoreCase)))
            {
                var tcSpan = NextToken().Span;
                Match(TokenType.OpenParen);
                ExpressionNode promptExpr;
                ExpressionNode expectedExpr;

                if (Check(TokenType.Identifier) && Current.Text.Equals("prompt", StringComparison.OrdinalIgnoreCase))
                {
                    NextToken();
                    if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                    promptExpr = ParseExpression();
                    Match(TokenType.Comma);
                    if (Check(TokenType.Identifier) && (Current.Text.Equals("expected", StringComparison.OrdinalIgnoreCase) || Current.Text.Equals("output", StringComparison.OrdinalIgnoreCase)))
                    {
                        NextToken();
                        if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                    }
                    expectedExpr = ParseExpression();
                }
                else
                {
                    promptExpr = ParseExpression();
                    Match(TokenType.Comma);
                    expectedExpr = ParseExpression();
                }
                var closeP = Match(TokenType.CloseParen);
                MatchOptional(TokenType.Semicolon, out _);
                MatchOptional(TokenType.Comma, out _);
                testCases.Add(new ValidationTestCaseNode(promptExpr, expectedExpr, new SourceSpan(tcSpan.Start, closeP.Span.End, _source.FilePath)));
                continue;
            }

            if (Check(TokenType.Identifier) && (Current.Text.Equals("min_accuracy", StringComparison.OrdinalIgnoreCase) || Current.Text.Equals("minAccuracy", StringComparison.OrdinalIgnoreCase) || Current.Text.Equals("accuracy", StringComparison.OrdinalIgnoreCase)))
            {
                NextToken();
                if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                var accExpr = ParseExpression();
                if (MatchOptional(TokenType.Percent, out _))
                {
                    if (accExpr is LiteralExpressionNode lit && lit.Value != null)
                    {
                        if (double.TryParse(lit.Value.ToString(), out double d))
                            accExpr = new LiteralExpressionNode(d / 100.0, accExpr.Span);
                    }
                }
                minAccuracy = accExpr;
                MatchOptional(TokenType.Semicolon, out _);
                MatchOptional(TokenType.Comma, out _);
                continue;
            }

            _diagnostics.ReportError("AL1005", $"Unexpected token '{Current.Text}' in validate block", Current.Span);
            NextToken();
        }

        var endBrace = Match(TokenType.CloseBrace);
        MatchOptional(TokenType.Semicolon, out _);
        return new TrainValidationNode(testCases, minAccuracy, new SourceSpan(startSpan.Start, endBrace.Span.End, _source.FilePath));
    }

    private McpDeclarationNode ParseMcpDeclaration()
    {
        var startToken = Match(TokenType.Mcp);
        string serverName = ParseIdentifierName("Expected MCP server name identifier");
        ExpressionNode commandOrPath;
        var env = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase);

        if (MatchOptional(TokenType.Equals, out _))
        {
            commandOrPath = ParseExpression();
            if (Check(TokenType.OpenBrace))
            {
                Match(TokenType.OpenBrace);
                while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
                {
                    string key = ParseIdentifierName();
                    if (key.Equals("env", StringComparison.OrdinalIgnoreCase))
                    {
                        MatchOptional(TokenType.Colon, out _);
                        Match(TokenType.OpenBrace);
                        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
                        {
                            var eKey = Current.Type == TokenType.StringLiteral ? Match(TokenType.StringLiteral).Value?.ToString() ?? "" : ParseIdentifierName();
                            MatchOptional(TokenType.Colon, out _);
                            var eVal = ParseExpression();
                            env[eKey] = eVal;
                            MatchOptional(TokenType.Comma, out _);
                            MatchOptional(TokenType.Semicolon, out _);
                        }
                        Match(TokenType.CloseBrace);
                    }
                    MatchOptional(TokenType.Semicolon, out _);
                    MatchOptional(TokenType.Comma, out _);
                }
                Match(TokenType.CloseBrace);
            }
        }
        else
        {
            Match(TokenType.OpenBrace);
            commandOrPath = new LiteralExpressionNode("", startToken.Span);
            while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
            {
                string key = ParseIdentifierName();
                if (key.Equals("command", StringComparison.OrdinalIgnoreCase) || key.Equals("cmd", StringComparison.OrdinalIgnoreCase) || key.Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    MatchOptional(TokenType.Colon, out _);
                    commandOrPath = ParseExpression();
                }
                else if (key.Equals("env", StringComparison.OrdinalIgnoreCase))
                {
                    MatchOptional(TokenType.Colon, out _);
                    Match(TokenType.OpenBrace);
                    while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
                    {
                        var eKey = Current.Type == TokenType.StringLiteral ? Match(TokenType.StringLiteral).Value?.ToString() ?? "" : ParseIdentifierName();
                        MatchOptional(TokenType.Colon, out _);
                        var eVal = ParseExpression();
                        env[eKey] = eVal;
                        MatchOptional(TokenType.Comma, out _);
                        MatchOptional(TokenType.Semicolon, out _);
                    }
                    Match(TokenType.CloseBrace);
                }
                MatchOptional(TokenType.Semicolon, out _);
                MatchOptional(TokenType.Comma, out _);
            }
            Match(TokenType.CloseBrace);
        }

        MatchOptional(TokenType.Semicolon, out _);
        return new McpDeclarationNode(serverName, commandOrPath, env, new SourceSpan(startToken.Span.Start, Current.Span.End, _source.FilePath));
    }

    private CustomApiDeclarationNode ParseCustomApiDeclaration()
    {
        var startToken = Match(TokenType.Api);
        string apiName = ParseIdentifierName("Expected API name identifier");
        Match(TokenType.OpenBrace, "Expected '{' after API name");

        ExpressionNode? endpoint = null;
        ExpressionNode? apiType = null;
        ExpressionNode? defaultModel = null;
        var headers = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase);
        var methods = new List<ApiMethodDeclarationNode>();

        while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Identifier) && (Current.Text.Equals("get", StringComparison.OrdinalIgnoreCase) ||
                                                Current.Text.Equals("post", StringComparison.OrdinalIgnoreCase) ||
                                                Current.Text.Equals("put", StringComparison.OrdinalIgnoreCase) ||
                                                Current.Text.Equals("delete", StringComparison.OrdinalIgnoreCase)))
            {
                var httpMethod = NextToken().Text.ToUpperInvariant();
                string methodName = ParseIdentifierName("Expected API method name");
                Match(TokenType.OpenParen);
                var parameters = new List<ToolParameterNode>();
                while (!Check(TokenType.CloseParen) && !Check(TokenType.EndOfFile))
                {
                    var pStart = Current.Span;
                    string pName = ParseIdentifierName();
                    Match(TokenType.Colon);
                    string pType = ParseIdentifierName();
                    parameters.Add(new ToolParameterNode(pName, pType, new SourceSpan(pStart.Start, Current.Span.End, _source.FilePath)));
                    if (!MatchOptional(TokenType.Comma, out _)) break;
                }
                Match(TokenType.CloseParen);

                Match(TokenType.OpenBrace);
                ExpressionNode? pathExpr = null;
                while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
                {
                    string mKey = ParseIdentifierName();
                    if (mKey.Equals("path", StringComparison.OrdinalIgnoreCase) || mKey.Equals("url", StringComparison.OrdinalIgnoreCase))
                    {
                        MatchOptional(TokenType.Colon, out _);
                        pathExpr = ParseExpression();
                    }
                    MatchOptional(TokenType.Semicolon, out _);
                    MatchOptional(TokenType.Comma, out _);
                }
                var mEnd = Match(TokenType.CloseBrace);
                MatchOptional(TokenType.Semicolon, out _);
                methods.Add(new ApiMethodDeclarationNode(httpMethod, methodName, parameters, pathExpr, new SourceSpan(startToken.Span.Start, mEnd.Span.End, _source.FilePath)));
                continue;
            }

            string key = ParseIdentifierName();
            if (key.Equals("endpoint", StringComparison.OrdinalIgnoreCase) || key.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                MatchOptional(TokenType.Colon, out _);
                endpoint = ParseExpression();
            }
            else if (key.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                MatchOptional(TokenType.Colon, out _);
                apiType = ParseExpression();
            }
            else if (key.Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                MatchOptional(TokenType.Colon, out _);
                defaultModel = ParseExpression();
            }
            else if (key.Equals("headers", StringComparison.OrdinalIgnoreCase))
            {
                MatchOptional(TokenType.Colon, out _);
                Match(TokenType.OpenBrace);
                while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
                {
                    var hKey = Current.Type == TokenType.StringLiteral ? Match(TokenType.StringLiteral).Value?.ToString() ?? "" : ParseIdentifierName();
                    MatchOptional(TokenType.Colon, out _);
                    var hVal = ParseExpression();
                    headers[hKey] = hVal;
                    MatchOptional(TokenType.Comma, out _);
                    MatchOptional(TokenType.Semicolon, out _);
                }
                Match(TokenType.CloseBrace);
            }

            MatchOptional(TokenType.Semicolon, out _);
            MatchOptional(TokenType.Comma, out _);
        }

        var endBrace = Match(TokenType.CloseBrace);
        MatchOptional(TokenType.Semicolon, out _);
        endpoint ??= new LiteralExpressionNode("http://localhost:11434", startToken.Span);
        return new CustomApiDeclarationNode(apiName, endpoint, apiType, defaultModel, headers, methods, new SourceSpan(startToken.Span.Start, endBrace.Span.End, _source.FilePath));
    }

    private LearnStatementNode ParseLearnStatement()
    {
        var startToken = Match(TokenType.Learn);
        if (Check(TokenType.Identifier) && (Current.Text.Equals("into", StringComparison.OrdinalIgnoreCase) || Current.Text.Equals("to", StringComparison.OrdinalIgnoreCase) || Current.Text.Equals("in", StringComparison.OrdinalIgnoreCase)))
        {
            NextToken();
        }
        else if (Check(TokenType.To) || Check(TokenType.In))
        {
            NextToken();
        }

        var datasetRef = ParsePrimary();
        Match(TokenType.OpenParen, "Expected '(' after dataset reference in learn statement");

        ExpressionNode inputOrPrompt;
        ExpressionNode outputOrChosen;
        ExpressionNode? rejected = null;

        if (Check(TokenType.Input) || (Check(TokenType.Identifier) && Current.Text.Equals("input", StringComparison.OrdinalIgnoreCase)))
        {
            NextToken();
            if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
            inputOrPrompt = ParseExpression();
            Match(TokenType.Comma);
            if (Check(TokenType.Identifier) && Current.Text.Equals("output", StringComparison.OrdinalIgnoreCase))
            {
                NextToken();
                if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
            }
            outputOrChosen = ParseExpression();
        }
        else if (Check(TokenType.Identifier) && Current.Text.Equals("prompt", StringComparison.OrdinalIgnoreCase))
        {
            NextToken();
            if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
            inputOrPrompt = ParseExpression();
            Match(TokenType.Comma);
            if (Check(TokenType.Identifier) && Current.Text.Equals("chosen", StringComparison.OrdinalIgnoreCase))
            {
                NextToken();
                if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
            }
            outputOrChosen = ParseExpression();
            if (MatchOptional(TokenType.Comma, out _))
            {
                if (Check(TokenType.Identifier) && Current.Text.Equals("rejected", StringComparison.OrdinalIgnoreCase))
                {
                    NextToken();
                    if (!MatchOptional(TokenType.Colon, out _)) Match(TokenType.Equals);
                }
                rejected = ParseExpression();
            }
        }
        else
        {
            inputOrPrompt = ParseExpression();
            Match(TokenType.Comma);
            outputOrChosen = ParseExpression();
            if (MatchOptional(TokenType.Comma, out _))
            {
                rejected = ParseExpression();
            }
        }

        var endParen = Match(TokenType.CloseParen);
        MatchOptional(TokenType.Semicolon, out _);
        return new LearnStatementNode(datasetRef, inputOrPrompt, outputOrChosen, rejected, new SourceSpan(startToken.Span.Start, endParen.Span.End, _source.FilePath));
    }

    private BroadcastStatementNode ParseBroadcastStatement()
    {
        var bToken = Match(TokenType.Broadcast);
        var msgExpr = ParseExpression();
        ExpressionNode? tag = null;
        if (Current.Text.Equals("with", StringComparison.OrdinalIgnoreCase))
        {
            NextToken();
            tag = ParseExpression();
        }
        MatchOptional(TokenType.Semicolon, out _);
        var end = tag?.Span.End ?? msgExpr.Span.End;
        return new BroadcastStatementNode(msgExpr, tag, new SourceSpan(bToken.Span.Start, end, _source.FilePath));
    }

    private WaitStatementNode ParseWaitStatement()
    {
        bool isAwait = Check(TokenType.Await);
        var token = NextToken(); // wait or await
        var targetOrDuration = ParseExpression();
        MatchOptional(TokenType.Semicolon, out _);
        return new WaitStatementNode(targetOrDuration, isAwait, new SourceSpan(token.Span.Start, targetOrDuration.Span.End, _source.FilePath));
    }

    private UntilStatementNode ParseUntilStatement()
    {
        var untilToken = Match(TokenType.Until);
        var condition = ParseExpression();
        Match(TokenType.OpenBrace);
        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);
        return new UntilStatementNode(condition, body, new SourceSpan(untilToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private List<StatementNode> ParseStatementListUntil(TokenType endToken)
    {
        var statements = new List<StatementNode>();
        while (!Check(endToken) && !Check(TokenType.EndOfFile))
        {
            statements.Add(ParseStatement());
        }
        return statements;
    }

    private StatementNode ParseStatement()
    {
        if (Check(TokenType.If))
            return ParseIfStatement();
        if (Check(TokenType.While))
            return ParseWhileStatement();
        if (Check(TokenType.Until))
            return ParseUntilStatement();
        if (Check(TokenType.Repeat))
            return ParseRepeatStatement();
        if (Check(TokenType.For))
            return ParseForStatement();
        if (Check(TokenType.Return))
            return ParseReturnStatement();
        if (Check(TokenType.Try))
            return ParseTryCatchStatement();
        if (Check(TokenType.Retry))
            return ParseRetryStatement();
        if (Check(TokenType.Parallel))
            return ParseParallelBlock();
        if (Check(TokenType.Function))
            return ParseFunctionDeclaration();
        if (Check(TokenType.Send))
            return ParseSendMessageStatement();
        if (Check(TokenType.Broadcast))
            return ParseBroadcastStatement();
        if (Check(TokenType.Wait) || Check(TokenType.Await))
            return ParseWaitStatement();
        if (Check(TokenType.ImportApi))
            return ParseImportApiDeclaration();
        if (Check(TokenType.Learn))
            return ParseLearnStatement();
        if (Check(TokenType.Dataset))
            return ParseDatasetDeclaration();
        if (Check(TokenType.Train))
            return ParseTrainDeclaration();
        if (Check(TokenType.Mcp))
            return ParseMcpDeclaration();
        if (Check(TokenType.Api))
            return ParseCustomApiDeclaration();

        // Agent / Swarm / MultiAgent invocation: "agent Researcher", "swarm ResearchSwarm"
        if ((Check(TokenType.Agent) || Check(TokenType.Swarm) || Check(TokenType.MultiAgent)) && IsContextualIdentifier(Lookahead.Type))
        {
            var agToken = NextToken();
            string agName = ParseIdentifierName();
            MatchOptional(TokenType.Semicolon, out _);
            return new AgentInvocationNode(agName, new SourceSpan(agToken.Span.Start, Current.Span.End, _source.FilePath));
        }

        // Variable assignment: identifier = expr
        if (IsContextualIdentifier(Current.Type) && Lookahead.Type == TokenType.Equals)
        {
            var idToken = NextToken();
            Match(TokenType.Equals);
            var valExpr = ParseExpression();
            MatchOptional(TokenType.Semicolon, out _);
            return new VariableAssignmentNode(idToken.Text, valExpr, new SourceSpan(idToken.Span.Start, valExpr.Span.End, _source.FilePath));
        }

        // Expression statement
        var expr = ParseExpression();
        MatchOptional(TokenType.Semicolon, out _);
        return new ExpressionStatementNode(expr, expr.Span);
    }

    private IfStatementNode ParseIfStatement()
    {
        var ifToken = Match(TokenType.If);
        var condition = ParseExpression();
        Match(TokenType.OpenBrace);
        var thenBranch = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        List<StatementNode>? elseBranch = null;
        if (Check(TokenType.Else))
        {
            Match(TokenType.Else);
            if (Check(TokenType.If))
            {
                elseBranch = [ParseIfStatement()];
            }
            else
            {
                Match(TokenType.OpenBrace);
                elseBranch = ParseStatementListUntil(TokenType.CloseBrace);
                Match(TokenType.CloseBrace);
            }
        }

        return new IfStatementNode(condition, thenBranch, elseBranch, new SourceSpan(ifToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private WhileStatementNode ParseWhileStatement()
    {
        var whileToken = Match(TokenType.While);
        var condition = ParseExpression();
        Match(TokenType.OpenBrace);
        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new WhileStatementNode(condition, body, new SourceSpan(whileToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private RepeatStatementNode ParseRepeatStatement()
    {
        var repToken = Match(TokenType.Repeat);
        var count = ParseExpression();
        Match(TokenType.OpenBrace);
        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new RepeatStatementNode(count, body, new SourceSpan(repToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private ForStatementNode ParseForStatement()
    {
        var forToken = Match(TokenType.For);
        var idToken = Match(TokenType.Identifier);
        Match(TokenType.In);
        var iterable = ParseExpression();
        Match(TokenType.OpenBrace);
        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new ForStatementNode(idToken.Text, iterable, body, new SourceSpan(forToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        var retToken = Match(TokenType.Return);
        ExpressionNode? val = null;
        if (!Check(TokenType.CloseBrace) && !Check(TokenType.Semicolon) && !Check(TokenType.EndOfFile))
        {
            val = ParseExpression();
        }
        MatchOptional(TokenType.Semicolon, out _);
        return new ReturnStatementNode(val, new SourceSpan(retToken.Span.Start, (val?.Span.End ?? retToken.Span.End), _source.FilePath));
    }

    private TryCatchStatementNode ParseTryCatchStatement()
    {
        var tryToken = Match(TokenType.Try);
        Match(TokenType.OpenBrace);
        var tryBody = ParseStatementListUntil(TokenType.CloseBrace);
        Match(TokenType.CloseBrace);

        Match(TokenType.Catch);
        string? catchVar = null;
        if (Check(TokenType.Identifier))
        {
            catchVar = NextToken().Text;
        }
        Match(TokenType.OpenBrace);
        var catchBody = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new TryCatchStatementNode(tryBody, catchVar, catchBody, new SourceSpan(tryToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private RetryStatementNode ParseRetryStatement()
    {
        var retryToken = Match(TokenType.Retry);
        var count = ParseExpression();
        Match(TokenType.OpenBrace);
        var body = ParseStatementListUntil(TokenType.CloseBrace);
        var closeBrace = Match(TokenType.CloseBrace);

        return new RetryStatementNode(count, body, new SourceSpan(retryToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

    private SendMessageStatementNode ParseSendMessageStatement()
    {
        var sendToken = Match(TokenType.Send);
        var messageExpr = ParseExpression();
        Match(TokenType.To);
        string targetAgent = ParseIdentifierName("Expected target agent name");
        ExpressionNode? tag = null;
        if (Check(TokenType.Identifier) && Current.Text == "tag")
        {
            NextToken();
            tag = ParseExpression();
        }
        MatchOptional(TokenType.Semicolon, out var semiToken);
        var endSpan = semiToken?.Span.End ?? tag?.Span.End ?? Current.Span.End;
        return new SendMessageStatementNode(messageExpr, targetAgent, tag, new SourceSpan(sendToken.Span.Start, endSpan, _source.FilePath));
    }

    // Expression parsing with operator precedence (Pratt parsing)
    public ExpressionNode ParseExpression() => ParseLogicalOr();

    private ExpressionNode ParseLogicalOr()
    {
        var left = ParseLogicalAnd();
        while (Check(TokenType.Or))
        {
            var opToken = NextToken();
            var right = ParseLogicalAnd();
            left = new BinaryExpressionNode(left, BinaryOperator.Or, right, new SourceSpan(left.Span.Start, right.Span.End, _source.FilePath));
        }
        return left;
    }

    private ExpressionNode ParseLogicalAnd()
    {
        var left = ParseEquality();
        while (Check(TokenType.And))
        {
            var opToken = NextToken();
            var right = ParseEquality();
            left = new BinaryExpressionNode(left, BinaryOperator.And, right, new SourceSpan(left.Span.Start, right.Span.End, _source.FilePath));
        }
        return left;
    }

    private ExpressionNode ParseEquality()
    {
        var left = ParseComparison();
        while (Check(TokenType.EqualsEquals) || Check(TokenType.ExclamationEquals))
        {
            var opToken = NextToken();
            var op = opToken.Type == TokenType.EqualsEquals ? BinaryOperator.Equal : BinaryOperator.NotEqual;
            var right = ParseComparison();
            left = new BinaryExpressionNode(left, op, right, new SourceSpan(left.Span.Start, right.Span.End, _source.FilePath));
        }
        return left;
    }

    private ExpressionNode ParseComparison()
    {
        var left = ParseAdditive();
        while (Check(TokenType.LessThan) || Check(TokenType.LessThanEquals) ||
               Check(TokenType.GreaterThan) || Check(TokenType.GreaterThanEquals))
        {
            var opToken = NextToken();
            var op = opToken.Type switch
            {
                TokenType.LessThan => BinaryOperator.LessThan,
                TokenType.LessThanEquals => BinaryOperator.LessThanOrEqual,
                TokenType.GreaterThan => BinaryOperator.GreaterThan,
                TokenType.GreaterThanEquals => BinaryOperator.GreaterThanOrEqual,
                _ => throw new InvalidOperationException()
            };
            var right = ParseAdditive();
            left = new BinaryExpressionNode(left, op, right, new SourceSpan(left.Span.Start, right.Span.End, _source.FilePath));
        }
        return left;
    }

    private ExpressionNode ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var opToken = NextToken();
            var op = opToken.Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            var right = ParseMultiplicative();
            left = new BinaryExpressionNode(left, op, right, new SourceSpan(left.Span.Start, right.Span.End, _source.FilePath));
        }
        return left;
    }

    private ExpressionNode ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Check(TokenType.Asterisk) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            var opToken = NextToken();
            if (opToken.Type == TokenType.Percent && !CanStartUnary(Current.Type))
            {
                // Postfix percentage: e.g. 80% -> 0.8
                if (left is LiteralExpressionNode lit && lit.Value != null && double.TryParse(lit.Value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numVal))
                {
                    left = new LiteralExpressionNode(numVal / 100.0, new SourceSpan(left.Span.Start, opToken.Span.End, _source.FilePath));
                }
                else
                {
                    left = new BinaryExpressionNode(left, BinaryOperator.Divide, new LiteralExpressionNode(100.0, opToken.Span), new SourceSpan(left.Span.Start, opToken.Span.End, _source.FilePath));
                }
                continue;
            }

            var op = opToken.Type switch
            {
                TokenType.Asterisk => BinaryOperator.Multiply,
                TokenType.Slash => BinaryOperator.Divide,
                TokenType.Percent => BinaryOperator.Modulo,
                _ => throw new InvalidOperationException()
            };
            var right = ParseUnary();
            left = new BinaryExpressionNode(left, op, right, new SourceSpan(left.Span.Start, right.Span.End, _source.FilePath));
        }
        return left;
    }

    private ExpressionNode ParseUnary()
    {
        if (Check(TokenType.Exclamation) || Check(TokenType.Not))
        {
            var opToken = NextToken();
            var operand = ParseUnary();
            return new UnaryExpressionNode(UnaryOperator.Not, operand, new SourceSpan(opToken.Span.Start, operand.Span.End, _source.FilePath));
        }
        if (Check(TokenType.Minus))
        {
            var opToken = NextToken();
            var operand = ParseUnary();
            return new UnaryExpressionNode(UnaryOperator.Negate, operand, new SourceSpan(opToken.Span.Start, operand.Span.End, _source.FilePath));
        }
        return ParsePrimaryWithPostfix();
    }

    private ExpressionNode ParsePrimaryWithPostfix()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (Check(TokenType.OpenParen))
            {
                // Call expression: expr(args)
                Match(TokenType.OpenParen);
                var args = new List<ExpressionNode>();
                while (!Check(TokenType.CloseParen) && !Check(TokenType.EndOfFile))
                {
                    if (IsContextualIdentifier(Current.Type) && (Lookahead.Type == TokenType.Colon || Lookahead.Type == TokenType.Equals))
                    {
                        var argNameTok = NextToken();
                        NextToken(); // consume : or =
                        var valExpr = ParseExpression();
                        args.Add(new NamedArgumentExpressionNode(argNameTok.Text, valExpr, new SourceSpan(argNameTok.Span.Start, valExpr.Span.End, _source.FilePath)));
                    }
                    else
                    {
                        args.Add(ParseExpression());
                    }

                    if (Check(TokenType.Comma))
                        Match(TokenType.Comma);
                    else
                        break;
                }
                var closeParen = Match(TokenType.CloseParen);
                expr = new CallExpressionNode(expr, args, new SourceSpan(expr.Span.Start, closeParen.Span.End, _source.FilePath));
            }
            else if (Check(TokenType.Dot))
            {
                // Member access: expr.member
                Match(TokenType.Dot);
                // The member could be an identifier or keyword (e.g. .result, .search, .write)
                string member = Current.Text;
                var memberSpan = Current.Span;
                NextToken();
                expr = new MemberAccessExpressionNode(expr, member, new SourceSpan(expr.Span.Start, memberSpan.End, _source.FilePath));
            }
            else if (Check(TokenType.OpenBracket))
            {
                // Index access: expr[index]
                Match(TokenType.OpenBracket);
                var indexExpr = ParseExpression();
                var closeBracket = Match(TokenType.CloseBracket);
                expr = new IndexAccessExpressionNode(expr, indexExpr, new SourceSpan(expr.Span.Start, closeBracket.Span.End, _source.FilePath));
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private ExpressionNode ParsePrimary()
    {
        var current = Current;

        // AI Operations: think(...) and research(...)
        if ((Check(TokenType.Think) || Check(TokenType.Research)) && Lookahead.Type == TokenType.OpenParen)
        {
            var opToken = NextToken();
            Match(TokenType.OpenParen);
            var args = new List<ExpressionNode>();
            while (!Check(TokenType.CloseParen) && !Check(TokenType.EndOfFile))
            {
                if (IsContextualIdentifier(Current.Type) && (Lookahead.Type == TokenType.Colon || Lookahead.Type == TokenType.Equals))
                {
                    var argNameTok = NextToken();
                    NextToken(); // consume : or =
                    var valExpr = ParseExpression();
                    args.Add(new NamedArgumentExpressionNode(argNameTok.Text, valExpr, new SourceSpan(argNameTok.Span.Start, valExpr.Span.End, _source.FilePath)));
                }
                else
                {
                    args.Add(ParseExpression());
                }

                if (Check(TokenType.Comma))
                    Match(TokenType.Comma);
                else
                    break;
            }
            var closeParen = Match(TokenType.CloseParen);
            return new AiOperationExpressionNode(opToken.Text, args, new SourceSpan(opToken.Span.Start, closeParen.Span.End, _source.FilePath));
        }

        // Planning Operation: plan(...) or plan "..."
        if (Check(TokenType.Plan))
        {
            var planToken = NextToken();
            ExpressionNode promptExpr;
            SourceSpan endSpan;
            if (Check(TokenType.OpenParen))
            {
                Match(TokenType.OpenParen);
                promptExpr = ParseExpression();
                var cp = Match(TokenType.CloseParen);
                endSpan = cp.Span;
            }
            else
            {
                promptExpr = ParseExpression();
                endSpan = promptExpr.Span;
            }
            return new PlanExpressionNode(promptExpr, new SourceSpan(planToken.Span.Start, endSpan.End, _source.FilePath));
        }

        // Delegation Operation: delegate "task" to AgentName[.TaskName]
        if (Check(TokenType.Delegate))
        {
            var delToken = NextToken();
            var msgExpr = ParseExpression();
            Match(TokenType.To, "Expected 'to' after delegated message (e.g. delegate 'task' to AgentName)");
            string target = ParseIdentifierName("Expected target agent name after 'to'");
            if (Check(TokenType.Dot))
            {
                NextToken(); // dot
                string taskName = ParseIdentifierName("Expected task name after '.'");
                target = $"{target}.{taskName}";
            }
            return new DelegateExpressionNode(msgExpr, target, new SourceSpan(delToken.Span.Start, Current.Span.End, _source.FilePath));
        }

        // Builtins: print(...), input(...), type(...)
        if (Check(TokenType.Print) || Check(TokenType.Input) || Check(TokenType.Type))
        {
            var fnToken = NextToken();
            return new IdentifierExpressionNode(fnToken.Text, fnToken.Span);
        }

        if (Check(TokenType.NumberLiteral))
        {
            NextToken();
            return new LiteralExpressionNode(current.Value, current.Span);
        }

        if (Check(TokenType.StringLiteral))
        {
            NextToken();
            return new LiteralExpressionNode(current.Value, current.Span);
        }

        if (Check(TokenType.BooleanLiteral))
        {
            NextToken();
            return new LiteralExpressionNode(current.Value, current.Span);
        }

        if (Check(TokenType.NullLiteral))
        {
            NextToken();
            return new LiteralExpressionNode(null, current.Span);
        }

        if (IsContextualIdentifier(Current.Type))
        {
            var token = NextToken();
            return new IdentifierExpressionNode(token.Text, token.Span);
        }

        // Parenthesized expression
        if (Check(TokenType.OpenParen))
        {
            Match(TokenType.OpenParen);
            var inner = ParseExpression();
            Match(TokenType.CloseParen);
            return inner;
        }

        // List literal: [a, b, c]
        if (Check(TokenType.OpenBracket))
        {
            var openBracket = Match(TokenType.OpenBracket);
            var elements = new List<ExpressionNode>();
            while (!Check(TokenType.CloseBracket) && !Check(TokenType.EndOfFile))
            {
                elements.Add(ParseExpression());
                if (Check(TokenType.Comma))
                    Match(TokenType.Comma);
                else
                    break;
            }
            var closeBracket = Match(TokenType.CloseBracket);
            return new ListLiteralExpressionNode(elements, new SourceSpan(openBracket.Span.Start, closeBracket.Span.End, _source.FilePath));
        }

        // Map literal: { key: value, ... }
        if (Check(TokenType.OpenBrace))
        {
            var openBrace = Match(TokenType.OpenBrace);
            var entries = new List<KeyValuePair<string, ExpressionNode>>();
            while (!Check(TokenType.CloseBrace) && !Check(TokenType.EndOfFile))
            {
                string key;
                if (Check(TokenType.StringLiteral))
                {
                    key = Current.Value?.ToString() ?? Current.Text;
                    NextToken();
                }
                else if (IsContextualIdentifier(Current.Type))
                {
                    key = Current.Text;
                    NextToken();
                }
                else
                {
                    _diagnostics.ReportError("AL1007", $"Expected map key string or identifier, got '{Current.Text}'", Current.Span);
                    break;
                }

                Match(TokenType.Colon);
                var val = ParseExpression();
                entries.Add(new KeyValuePair<string, ExpressionNode>(key, val));

                if (Check(TokenType.Comma))
                    Match(TokenType.Comma);
                else
                    break;
            }
            var closeBrace = Match(TokenType.CloseBrace);
            return new MapLiteralExpressionNode(entries, new SourceSpan(openBrace.Span.Start, closeBrace.Span.End, _source.FilePath));
        }

        _diagnostics.ReportError("AL1005", $"Unexpected expression token '{current.Text}'", current.Span);
        NextToken();
        return new LiteralExpressionNode(null, current.Span);
    }
}

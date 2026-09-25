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
                TokenType.Type or TokenType.ImportApi;

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
                Check(TokenType.Function) || Check(TokenType.Model) || Check(TokenType.ImportApi))
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
                NextToken(); // consume key identifier or keyword (e.g. model, memory, permission, tools)
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
            else
            {
                body.Add(ParseStatement());
            }
        }

        var closeBrace = Match(TokenType.CloseBrace);
        return new AgentDeclarationNode(name, config, body, new SourceSpan(agentToken.Span.Start, closeBrace.Span.End, _source.FilePath));
    }

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
        if (Check(TokenType.ImportApi))
            return ParseImportApiDeclaration();

        // Agent invocation: "agent Researcher"
        if (Check(TokenType.Agent) && IsContextualIdentifier(Lookahead.Type))
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
                    args.Add(ParseExpression());
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
                args.Add(ParseExpression());
                if (Check(TokenType.Comma))
                    Match(TokenType.Comma);
                else
                    break;
            }
            var closeParen = Match(TokenType.CloseParen);
            return new AiOperationExpressionNode(opToken.Text, args, new SourceSpan(opToken.Span.Start, closeParen.Span.End, _source.FilePath));
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

using AgentLang.AST;

namespace AgentLang.Semantic;

public sealed class SemanticAnalyzer : AstVisitor
{
    private readonly DiagnosticBag _diagnostics;
    private Scope _currentScope;
    private readonly Scope _globalScope;

    private static readonly HashSet<string> BuiltInTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "browser", "filesystem", "terminal", "http", "calculator"
    };

    private static readonly HashSet<string> BuiltInModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "GPT", "Claude", "Gemini", "Mock", "Test"
    };

    private static readonly HashSet<string> BuiltInFunctions = new(StringComparer.Ordinal)
    {
        "print", "input", "type", "remember", "recall", "forget", "confirm"
    };

    private static readonly HashSet<string> BuiltInConstants = new(StringComparer.OrdinalIgnoreCase)
    {
        "short_term", "long_term", "sequential", "parallel"
    };

    private static readonly HashSet<string> BuiltInAiOperations = new(StringComparer.Ordinal)
    {
        "think", "research", "plan"
    };

    public Scope GlobalScope => _globalScope;
    public DiagnosticBag Diagnostics => _diagnostics;

    public SemanticAnalyzer(DiagnosticBag? diagnostics = null)
    {
        _diagnostics = diagnostics ?? new DiagnosticBag();
        _globalScope = new Scope("global");
        _currentScope = _globalScope;

        // Register built-in tools, models and functions in global scope
        foreach (var tool in BuiltInTools)
        {
            _globalScope.TryDeclare(new Symbol(tool, SymbolKind.Tool, SourceSpan.None));
        }

        foreach (var model in BuiltInModels)
        {
            _globalScope.TryDeclare(new Symbol(model, SymbolKind.Variable, SourceSpan.None, "model"));
        }

        foreach (var fn in BuiltInFunctions)
        {
            _globalScope.TryDeclare(new Symbol(fn, SymbolKind.Variable, SourceSpan.None));
        }

        foreach (var c in BuiltInConstants)
        {
            _globalScope.TryDeclare(new Symbol(c, SymbolKind.Variable, SourceSpan.None));
        }
    }

    public void Analyze(ProgramNode program)
    {
        // First pass: Declare top-level symbols (agents, multiagents, permissions, tools, events)
        foreach (var decl in program.Declarations)
        {
            DeclareTopLevel(decl);
        }

        // Second pass: Validate bodies, references, expressions
        program.Accept(this);
    }

    private void DeclareTopLevel(DeclarationNode decl)
    {
        switch (decl)
        {
            case PermissionDeclarationNode perm:
                if (!_globalScope.TryDeclare(new Symbol(perm.Name, SymbolKind.Permission, perm.Span, perm)))
                {
                    _diagnostics.ReportError("AL2001", $"Duplicate permission declaration '{perm.Name}'", perm.Span);
                }
                break;

            case ToolDeclarationNode tool:
                if (!_globalScope.TryDeclare(new Symbol(tool.Name, SymbolKind.Tool, tool.Span, tool)))
                {
                    _diagnostics.ReportError("AL2002", $"Duplicate tool declaration '{tool.Name}'", tool.Span);
                }
                break;

            case CustomToolDeclarationNode ctool:
                if (!_globalScope.TryDeclare(new Symbol(ctool.Name, SymbolKind.Tool, ctool.Span, ctool)))
                {
                    _diagnostics.ReportError("AL2002", $"Duplicate tool declaration '{ctool.Name}'", ctool.Span);
                }
                break;

            case FunctionDeclarationNode fn:
                if (!_globalScope.TryDeclare(new Symbol(fn.Name, SymbolKind.Function, fn.Span, fn)))
                {
                    _diagnostics.ReportError("AL2011", $"Duplicate function declaration '{fn.Name}'", fn.Span);
                }
                break;

            case ModelDeclarationNode mdl:
                if (!_globalScope.TryDeclare(new Symbol(mdl.Alias, SymbolKind.ModelAlias, mdl.Span, mdl)))
                {
                    _diagnostics.ReportError("AL2012", $"Duplicate model declaration '{mdl.Alias}'", mdl.Span);
                }
                break;

            case AgentDeclarationNode agent:
                if (!_globalScope.TryDeclare(new Symbol(agent.Name, SymbolKind.Agent, agent.Span, agent)))
                {
                    _diagnostics.ReportError("AL2003", $"Duplicate agent declaration '{agent.Name}'", agent.Span);
                }
                break;

            case MultiAgentDeclarationNode multi:
                if (!_globalScope.TryDeclare(new Symbol(multi.Name, SymbolKind.MultiAgent, multi.Span, multi)))
                {
                    _diagnostics.ReportError("AL2004", $"Duplicate multiagent declaration '{multi.Name}'", multi.Span);
                }
                break;

            case EventDeclarationNode evt:
                _globalScope.TryDeclare(new Symbol($"event:{evt.Target}", SymbolKind.Event, evt.Span, evt));
                break;

            case SwarmDeclarationNode swarm:
                if (!_globalScope.TryDeclare(new Symbol(swarm.Name, SymbolKind.MultiAgent, swarm.Span, swarm)))
                {
                    _diagnostics.ReportError("AL2004", $"Duplicate swarm declaration '{swarm.Name}'", swarm.Span);
                }
                break;

            case ImportApiDeclarationNode:
                // importapi is processed during evaluation/execution
                break;
        }
    }

    public override void Visit(SwarmDeclarationNode node)
    {
        var swarmScope = new Scope($"swarm:{node.Name}", _currentScope);
        var prevScope = _currentScope;
        _currentScope = swarmScope;
        foreach (var item in node.Body)
        {
            item.Accept(this);
        }
        _currentScope = prevScope;
    }

    public override void Visit(AgentDeclarationNode node)
    {
        var agentScope = new Scope($"agent:{node.Name}", _currentScope);
        var prevScope = _currentScope;
        _currentScope = agentScope;

        // Check configs
        foreach (var cfg in node.Config)
        {
            cfg.Accept(this);
            if (cfg.Key.Equals("permission", StringComparison.OrdinalIgnoreCase))
            {
                if (cfg.Value is IdentifierExpressionNode idNode)
                {
                    var permSym = _globalScope.Lookup(idNode.Name);
                    if (permSym == null || permSym.Kind != SymbolKind.Permission)
                    {
                        string? suggestion = FindClosestMatch(idNode.Name, _globalScope.AllSymbols().Where(s => s.Kind == SymbolKind.Permission).Select(s => s.Name));
                        _diagnostics.ReportError("AL2005", $"Unknown permission '{idNode.Name}'", idNode.Span, suggestion != null ? $"did you mean '{suggestion}'?" : null);
                    }
                }
            }
            else if (cfg.Key.Equals("tools", StringComparison.OrdinalIgnoreCase))
            {
                if (cfg.Value is ListLiteralExpressionNode listNode)
                {
                    foreach (var elem in listNode.Elements)
                    {
                        if (elem is IdentifierExpressionNode toolId)
                        {
                            var toolSym = _globalScope.Lookup(toolId.Name);
                            if (toolSym == null || toolSym.Kind != SymbolKind.Tool)
                            {
                                string? suggestion = FindClosestMatch(toolId.Name, BuiltInTools.Concat(_globalScope.AllSymbols().Where(s => s.Kind == SymbolKind.Tool).Select(s => s.Name)));
                                _diagnostics.ReportError("AL2006", $"Unknown tool '{toolId.Name}'", toolId.Span, suggestion != null ? $"did you mean '{suggestion}'?" : null);
                            }
                        }
                    }
                }
            }
        }

        // Register tasks and context in agent scope first
        foreach (var item in node.Body)
        {
            if (item is ContextDeclarationNode ctx)
            {
                if (!_currentScope.TryDeclare(new Symbol(ctx.Name, SymbolKind.Context, ctx.Span, ctx)))
                {
                    _diagnostics.ReportError("AL2007", $"Duplicate context variable '{ctx.Name}' in agent '{node.Name}'", ctx.Span);
                }
            }
            else if (item is TaskDeclarationNode task)
            {
                if (!_currentScope.TryDeclare(new Symbol(task.Name, SymbolKind.Task, task.Span, task)))
                {
                    _diagnostics.ReportError("AL2008", $"Duplicate task '{task.Name}' in agent '{node.Name}'", task.Span);
                }
            }
            else if (item is FunctionDeclarationNode fn)
            {
                if (!_currentScope.TryDeclare(new Symbol(fn.Name, SymbolKind.Function, fn.Span, fn)))
                {
                    _diagnostics.ReportError("AL2011", $"Duplicate function '{fn.Name}' in agent '{node.Name}'", fn.Span);
                }
            }
        }

        // Now visit all body items
        foreach (var item in node.Body)
        {
            item.Accept(this);
        }

        _currentScope = prevScope;
    }

    public override void Visit(MultiAgentDeclarationNode node)
    {
        var multiScope = new Scope($"multiagent:{node.Name}", _currentScope);
        var prevScope = _currentScope;
        _currentScope = multiScope;

        // Collect child agents
        foreach (var item in node.Body)
        {
            if (item is AgentDeclarationNode agent)
            {
                _currentScope.TryDeclare(new Symbol(agent.Name, SymbolKind.Agent, agent.Span, agent));
                _globalScope.TryDeclare(new Symbol(agent.Name, SymbolKind.Agent, agent.Span, agent));
            }
            else if (item is ParallelBlockNode par)
            {
                foreach (var child in par.Body)
                {
                    if (child is AgentDeclarationNode parAgent)
                    {
                        _currentScope.TryDeclare(new Symbol(parAgent.Name, SymbolKind.Agent, parAgent.Span, parAgent));
                        _globalScope.TryDeclare(new Symbol(parAgent.Name, SymbolKind.Agent, parAgent.Span, parAgent));
                    }
                }
            }
        }

        foreach (var item in node.Body)
        {
            item.Accept(this);
        }

        _currentScope = prevScope;
    }

    public override void Visit(TaskDeclarationNode node)
    {
        foreach (var cfg in node.Config)
        {
            cfg.Accept(this);
        }

        var taskScope = new Scope($"task:{node.Name}", _currentScope);
        var prevScope = _currentScope;
        _currentScope = taskScope;

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        _currentScope = prevScope;
    }

    public override void Visit(FunctionDeclarationNode node)
    {
        _currentScope.TryDeclare(new Symbol(node.Name, SymbolKind.Function, node.Span, node));

        var fnScope = new Scope($"fn:{node.Name}", _currentScope);
        var prevScope = _currentScope;
        _currentScope = fnScope;

        var seenParams = new HashSet<string>(StringComparer.Ordinal);
        foreach (var param in node.Parameters)
        {
            if (!seenParams.Add(param))
            {
                _diagnostics.ReportError("AL2013", $"Duplicate parameter name '{param}' in function '{node.Name}'", node.Span);
            }
            _currentScope.TryDeclare(new Symbol(param, SymbolKind.Variable, node.Span));
        }

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        _currentScope = prevScope;
    }

    public override void Visit(ModelDeclarationNode node)
    {
        _globalScope.TryDeclare(new Symbol(node.Alias, SymbolKind.ModelAlias, node.Span, node));
    }

    public override void Visit(CustomToolDeclarationNode node)
    {
        _globalScope.TryDeclare(new Symbol(node.Name, SymbolKind.Tool, node.Span, node));

        var toolScope = new Scope($"tool:{node.Name}", _currentScope);
        var prevScope = _currentScope;
        _currentScope = toolScope;

        var seenParams = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in node.Inputs)
        {
            if (!seenParams.Add(input.Name))
            {
                _diagnostics.ReportError("AL2014", $"Duplicate input parameter '{input.Name}' in tool '{node.Name}'", input.Span);
            }
            input.Accept(this);
            _currentScope.TryDeclare(new Symbol(input.Name, SymbolKind.Variable, input.Span));
        }

        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }

        _currentScope = prevScope;
    }

    public override void Visit(ToolParameterNode node)
    {
    }

    public override void Visit(SendMessageStatementNode node)
    {
        node.Message.Accept(this);
        node.Tag?.Accept(this);

        var agentSym = _globalScope.Lookup(node.TargetAgent) ?? _currentScope.Lookup(node.TargetAgent);
        if (agentSym == null || (agentSym.Kind != SymbolKind.Agent && agentSym.Kind != SymbolKind.Variable))
        {
            string? suggestion = FindClosestMatch(node.TargetAgent, _globalScope.AllSymbols().Where(s => s.Kind == SymbolKind.Agent).Select(s => s.Name));
            _diagnostics.ReportError("AL2015", $"Cannot send message: unknown agent '{node.TargetAgent}'", node.Span, suggestion != null ? $"did you mean '{suggestion}'?" : null);
        }
    }

    public override void Visit(ForStatementNode node)
    {
        node.Iterable.Accept(this);
        var forScope = new Scope("for", _currentScope);
        forScope.TryDeclare(new Symbol(node.VariableName, SymbolKind.Variable, node.Span));

        var prevScope = _currentScope;
        _currentScope = forScope;
        foreach (var stmt in node.Body)
        {
            stmt.Accept(this);
        }
        _currentScope = prevScope;
    }

    public override void Visit(IndexAccessExpressionNode node)
    {
        node.Target.Accept(this);
        node.Index.Accept(this);
    }

    public override void Visit(MapLiteralExpressionNode node)
    {
        foreach (var entry in node.Entries)
        {
            entry.Value.Accept(this);
        }
    }

    public override void Visit(AgentInvocationNode node)
    {
        var sym = _currentScope.Lookup(node.AgentName);
        if (sym == null || (sym.Kind != SymbolKind.Agent && sym.Kind != SymbolKind.MultiAgent))
        {
            string? suggestion = FindClosestMatch(node.AgentName, _currentScope.AllSymbols().Where(s => s.Kind == SymbolKind.Agent || s.Kind == SymbolKind.MultiAgent).Select(s => s.Name));
            _diagnostics.ReportError("AL2009", $"Unknown agent '{node.AgentName}'", node.Span, suggestion != null ? $"did you mean '{suggestion}'?" : null);
        }
    }

    public override void Visit(VariableAssignmentNode node)
    {
        node.Value.Accept(this);
        // Declare or update variable in current scope
        var existing = _currentScope.Lookup(node.VariableName);
        if (existing == null)
        {
            _currentScope.TryDeclare(new Symbol(node.VariableName, SymbolKind.Variable, node.Span));
        }
    }

    public override void Visit(IdentifierExpressionNode node)
    {
        var sym = _currentScope.Lookup(node.Name);
        if (sym == null)
        {
            // Check if it's a known model or function or operation
            if (BuiltInFunctions.Contains(node.Name) || BuiltInTools.Contains(node.Name) || BuiltInModels.Contains(node.Name))
                return;

            string? suggestion = FindClosestMatch(node.Name, _currentScope.AllSymbols().Select(s => s.Name));
            _diagnostics.ReportWarning("AL2010", $"Reference to unresolved symbol '{node.Name}'", node.Span, suggestion != null ? $"did you mean '{suggestion}'?" : null);
        }
    }

    public override void Visit(MemberAccessExpressionNode node)
    {
        node.Target.Accept(this);

        // Result access: e.g. research.result, answer.result
        if (node.MemberName.Equals("result", StringComparison.OrdinalIgnoreCase))
        {
            // Valid on task, operation, agent, variable
            return;
        }
    }

    public override void Visit(TryCatchStatementNode node)
    {
        var tryScope = new Scope("try", _currentScope);
        var prevScope = _currentScope;

        _currentScope = tryScope;
        foreach (var stmt in node.TryBody)
            stmt.Accept(this);

        var catchScope = new Scope("catch", prevScope);
        if (node.CatchVariable != null)
        {
            catchScope.TryDeclare(new Symbol(node.CatchVariable, SymbolKind.Variable, node.Span));
        }

        _currentScope = catchScope;
        foreach (var stmt in node.CatchBody)
            stmt.Accept(this);

        _currentScope = prevScope;
    }

    private static string? FindClosestMatch(string target, IEnumerable<string> candidates)
    {
        string? closest = null;
        int minDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            int dist = LevenshteinDistance(target.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (dist <= 2 && dist < minDistance)
            {
                minDistance = dist;
                closest = candidate;
            }
        }

        return closest;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}

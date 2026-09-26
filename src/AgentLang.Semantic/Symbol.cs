using AgentLang.AST;

namespace AgentLang.Semantic;

public enum SymbolKind
{
    Agent,
    MultiAgent,
    Task,
    Context,
    Permission,
    Tool,
    Event,
    Variable,
    Operation,
    Function,
    ModelAlias,
    CustomTool,
    Dataset,
    TrainedModel,
    McpServer,
    CustomApi
}

public sealed record Symbol(
    string Name,
    SymbolKind Kind,
    SourceSpan Span,
    object? Metadata = null)
{
    public override string ToString() => $"{Kind} '{Name}' at {Span}";
}

public sealed class Scope
{
    private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);

    public Scope? Parent { get; }
    public string Name { get; }

    public Scope(string name, Scope? parent = null)
    {
        Name = name;
        Parent = parent;
    }

    public bool TryDeclare(Symbol symbol)
    {
        if (_symbols.ContainsKey(symbol.Name))
            return false;
        _symbols[symbol.Name] = symbol;
        return true;
    }

    public void DeclareOrAssign(Symbol symbol)
    {
        _symbols[symbol.Name] = symbol;
    }

    public Symbol? Lookup(string name)
    {
        if (_symbols.TryGetValue(name, out var symbol))
            return symbol;
        return Parent?.Lookup(name);
    }

    public Symbol? LookupLocal(string name) =>
        _symbols.TryGetValue(name, out var symbol) ? symbol : null;

    public IEnumerable<Symbol> AllSymbols()
    {
        foreach (var sym in _symbols.Values)
            yield return sym;
        if (Parent != null)
        {
            foreach (var sym in Parent.AllSymbols())
                yield return sym;
        }
    }
}

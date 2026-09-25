namespace AgentLang.AST;

public readonly record struct SourceLocation(int Line, int Column, int Offset)
{
    public static SourceLocation None => new(0, 0, 0);

    public override string ToString() => $"{Line}:{Column}";
}

public readonly record struct SourceSpan(SourceLocation Start, SourceLocation End, string? FilePath = null)
{
    public static SourceSpan None => new(SourceLocation.None, SourceLocation.None, null);

    public int Length => Math.Max(0, End.Offset - Start.Offset);

    public override string ToString() => $"{FilePath ?? "<source>"}:{Start.Line}:{Start.Column}";
}

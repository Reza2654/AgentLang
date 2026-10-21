namespace AgentLang.Transpiler;

public enum TargetLanguage
{
    Python,
    TypeScript
}

public sealed class TranspilerOptions
{
    public TargetLanguage Target { get; set; } = TargetLanguage.Python;
    public string Framework { get; set; } = "LangGraph";
    public bool IncludeRuntimePreamble { get; set; } = true;
    public string IndentString { get; set; } = "    ";

    public TranspilerOptions(TargetLanguage target = TargetLanguage.Python, string framework = "LangGraph")
    {
        Target = target;
        Framework = framework;
    }
}

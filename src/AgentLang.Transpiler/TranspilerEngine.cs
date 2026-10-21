using AgentLang.AST;

namespace AgentLang.Transpiler;

public sealed class TranspilerEngine
{
    public static string Transpile(ProgramNode program, TranspilerOptions options)
    {
        return options.Target switch
        {
            TargetLanguage.Python => new PythonTranspiler(options).Transpile(program),
            TargetLanguage.TypeScript => new TypeScriptTranspiler(options).Transpile(program),
            _ => throw new NotSupportedException($"Target language '{options.Target}' is not supported.")
        };
    }

    public static string TranspileToPython(ProgramNode program, string framework = "LangGraph")
    {
        var options = new TranspilerOptions(TargetLanguage.Python, framework);
        return new PythonTranspiler(options).Transpile(program);
    }

    public static string TranspileToTypeScript(ProgramNode program)
    {
        var options = new TranspilerOptions(TargetLanguage.TypeScript, "VercelAISDK");
        return new TypeScriptTranspiler(options).Transpile(program);
    }
}

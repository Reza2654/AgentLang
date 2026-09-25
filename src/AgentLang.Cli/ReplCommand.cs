using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Security;
using AgentLang.Semantic;

namespace AgentLang.Cli;

public static class ReplCommand
{
    public static async Task<int> RunAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"AgentLang REPL — v{Program.Version}");
        Console.WriteLine("Interactive AgentLang Shell (.NET 10 / C# 14)");
        Console.WriteLine("Type ':help' for commands, ':exit' or Ctrl+C to quit.\n");
        Console.ResetColor();

        var sec = new SecurityEngine(new AutoApprovalProvider(true));
        var runtime = new AgentLangRuntime(securityEngine: sec);
        var sem = new SemanticAnalyzer();

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("agentlang> ");
            Console.ResetColor();

            string? line = Console.ReadLine();
            if (line == null) break;

            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith(':'))
            {
                if (line is ":exit" or ":quit" or ":q")
                {
                    Console.WriteLine("Bye!");
                    break;
                }
                if (line is ":help" or ":h")
                {
                    PrintHelp();
                    continue;
                }
                if (line is ":clear" or ":cls")
                {
                    Console.Clear();
                    continue;
                }
                if (line is ":agents")
                {
                    PrintAgents(runtime);
                    continue;
                }
                Console.WriteLine($"Unknown command: {line}. Type :help for commands.");
                continue;
            }

            await ExecuteReplLineAsync(line, runtime, sem);
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("REPL Commands:");
        Console.WriteLine("  :help, :h      Show this help message");
        Console.WriteLine("  :agents        List active agents and inbox status");
        Console.WriteLine("  :clear, :cls   Clear the screen");
        Console.WriteLine("  :exit, :q      Exit the REPL\n");
        Console.WriteLine("Examples:");
        Console.WriteLine("  x = 10 + 20");
        Console.WriteLine("  print(type(x))");
        Console.WriteLine("  function add(a, b) { return a + b }");
        Console.WriteLine("  add(5, 7)");
    }

    private static void PrintAgents(AgentLangRuntime runtime)
    {
        Console.WriteLine("Active Agents:");
        if (runtime.AgentInstances.Count == 0)
        {
            Console.WriteLine("  (No active agents)");
            return;
        }
        foreach (var (name, ag) in runtime.AgentInstances)
        {
            Console.WriteLine($"  {name} (model: {ag.Model ?? "default"}, memory: {ag.Memory.Count} items, inbox: {ag.Inbox.Count} messages)");
        }
    }

    private static async Task ExecuteReplLineAsync(string input, AgentLangRuntime runtime, SemanticAnalyzer sem)
    {
        try
        {
            bool isExprOnly = !input.Contains('=') &&
                              !input.StartsWith("function", StringComparison.OrdinalIgnoreCase) &&
                              !input.StartsWith("agent", StringComparison.OrdinalIgnoreCase) &&
                              !input.StartsWith("tool", StringComparison.OrdinalIgnoreCase) &&
                              !input.StartsWith("model", StringComparison.OrdinalIgnoreCase) &&
                              !input.StartsWith("send", StringComparison.OrdinalIgnoreCase) &&
                              !input.StartsWith("print", StringComparison.OrdinalIgnoreCase);

            if (isExprOnly)
            {
                var exprSource = new SourceText(input, "repl");
                var exprParser = new Parser.Parser(exprSource);
                var exprNode = exprParser.ParseExpression();
                if (!exprParser.Diagnostics.HasErrors)
                {
                    var result = await runtime.EvaluateExpressionAsync(exprNode, runtime.GlobalScope);
                    if (result != null)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(result);
                        Console.ResetColor();
                    }
                    return;
                }
            }

            string wrappedCode = input.StartsWith("agent", StringComparison.OrdinalIgnoreCase) ||
                                 input.StartsWith("tool", StringComparison.OrdinalIgnoreCase) ||
                                 input.StartsWith("model", StringComparison.OrdinalIgnoreCase) ||
                                 input.StartsWith("permission", StringComparison.OrdinalIgnoreCase) ||
                                 input.StartsWith("function", StringComparison.OrdinalIgnoreCase)
                ? input
                : $"main {{ {input} }}";

            var src = new SourceText(wrappedCode, "repl");
            var parser = new Parser.Parser(src);
            var prog = parser.ParseProgram();

            if (parser.Diagnostics.HasErrors)
            {
                foreach (var diag in parser.Diagnostics.Errors)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Syntax Error [{diag.Code}]: {diag.Message}");
                    Console.ResetColor();
                }
                return;
            }

            await runtime.ExecuteProgramAsync(prog);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Runtime Error: {ex.Message}");
            Console.ResetColor();
        }
    }
}

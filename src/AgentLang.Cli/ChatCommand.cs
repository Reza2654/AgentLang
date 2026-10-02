using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using AgentLang.Runtime.Values;

namespace AgentLang.Cli;

public static class ChatCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent chat <file.agt> [AgentName]");
            Console.ResetColor();
            return 1;
        }

        string filePath = args[1];
        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File not found: '{filePath}'");
            Console.ResetColor();
            return 1;
        }

        string sourceCode = await File.ReadAllTextAsync(filePath);
        var sourceText = new SourceText(sourceCode, filePath);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        if (parser.Diagnostics.HasErrors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Syntax errors in agent file:");
            foreach (var diag in parser.Diagnostics)
            {
                Console.WriteLine($"  {diag}");
            }
            Console.ResetColor();
            return 1;
        }

        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);
        if (semantic.Diagnostics.HasErrors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Semantic errors in agent file:");
            foreach (var diag in semantic.Diagnostics)
            {
                Console.WriteLine($"  {diag}");
            }
            Console.ResetColor();
            return 1;
        }

        // Find agent declarations
        var agentDecls = program.Declarations.OfType<AgentDeclarationNode>().ToList();
        if (agentDecls.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: No 'agent' declarations found in '{filePath}'.");
            Console.ResetColor();
            return 1;
        }

        string targetAgentName = args.Length > 2 ? args[2] : agentDecls[0].Name;
        var targetDecl = agentDecls.FirstOrDefault(a => a.Name.Equals(targetAgentName, StringComparison.OrdinalIgnoreCase));
        if (targetDecl == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Agent '{targetAgentName}' not found. Available agents: {string.Join(", ", agentDecls.Select(a => a.Name))}");
            Console.ResetColor();
            return 1;
        }

        var runtime = new AgentLangRuntime();
        // Catalog declarations (tools, permissions, models, etc.)
        await runtime.ExecuteProgramAsync(new ProgramNode(program.Declarations, null, program.Span));

        var agentInstance = await runtime.ExecuteAgentAsync(targetDecl.Name, runtime.GlobalScope);

        // Display Header Banner
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  🤖 AgentLang v{Program.Version} — Interactive Autonomous Agent Chat         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  • Agent:        {agentInstance.Name}");
        Console.WriteLine($"  • Role:         {agentInstance.Role ?? agentInstance.Persona ?? "(General Assistant)"}");
        Console.WriteLine($"  • Model:        {agentInstance.Model ?? "mock"} (Autonomous ReAct Engine Active)");
        Console.WriteLine($"  • Tools:        {(agentInstance.BoundTools.Count > 0 ? string.Join(", ", agentInstance.BoundTools) : "all tools")}");
        Console.WriteLine($"  • Session ID:   {agentInstance.SessionId}");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Commands: /reset (clear session), /history (view messages), /tools (list tools), /exit");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────\n");
        Console.ResetColor();

        // Subscribe to step events for live visual feedback
        runtime.EventBus.Subscribe($"{agentInstance.Name}.step", payload =>
        {
            if (payload != null)
            {
                var stepProp = payload.GetType().GetProperty("Step")?.GetValue(payload);
                var thoughtProp = payload.GetType().GetProperty("Thought")?.GetValue(payload);
                var toolProp = payload.GetType().GetProperty("Tool")?.GetValue(payload);
                var obsProp = payload.GetType().GetProperty("Observation")?.GetValue(payload);

                if (thoughtProp != null && !string.IsNullOrWhiteSpace(thoughtProp.ToString()))
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"  🤔 [Step {stepProp}] تفکر: {thoughtProp}");
                    Console.ResetColor();
                }
                if (toolProp != null && !string.IsNullOrWhiteSpace(toolProp.ToString()))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"  🔧 [Step {stepProp}] فراخوانی ابزار: {toolProp}");
                    Console.ResetColor();
                }
                if (obsProp != null && !string.IsNullOrWhiteSpace(obsProp.ToString()))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    string obsStr = obsProp.ToString()!;
                    if (obsStr.Length > 150) obsStr = obsStr.Substring(0, 150) + "...";
                    Console.WriteLine($"  👁️ [Step {stepProp}] نتیجه ابزار: {obsStr}");
                    Console.ResetColor();
                }
            }
            return Task.CompletedTask;
        });

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("User > ");
            Console.ResetColor();

            string? line = Console.ReadLine();
            if (line == null) break;

            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("👋 خروج از گفتگوی زنده با ربات.");
                Console.ResetColor();
                break;
            }

            if (trimmed.Equals("/reset", StringComparison.OrdinalIgnoreCase))
            {
                agentInstance.ResetSession();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("🔄 سشن ریست شد و تاریخچه مکالمه با موفقیت پاک گردید.");
                Console.ResetColor();
                continue;
            }

            if (trimmed.Equals("/history", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n--- تاریخچه پیام‌های این جلسه ---");
                foreach (var msg in agentInstance.History)
                {
                    Console.WriteLine($"[{msg.Timestamp:HH:mm:ss}] {msg.Role}: {msg.Content}");
                }
                Console.WriteLine("---------------------------------\n");
                Console.ResetColor();
                continue;
            }

            if (trimmed.Equals("/tools", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n--- ابزارهای متصل به ربات ---");
                foreach (var tool in runtime.ToolRegistry.AllTools)
                {
                    Console.WriteLine($"  • {tool.Name}: ({string.Join(", ", tool.SupportedCapabilities)})");
                }
                Console.WriteLine("------------------------------\n");
                Console.ResetColor();
                continue;
            }

            // Normal chat message
            try
            {
                string reply = await runtime.ReActEngine.ChatAsync(agentInstance, trimmed, runtime.GlobalScope);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write($"\n🤖 {agentInstance.Name} > ");
                Console.ResetColor();
                Console.WriteLine(reply + "\n");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ خطا در پردازش ربات: {ex.Message}");
                Console.ResetColor();
            }
        }

        return 0;
    }
}

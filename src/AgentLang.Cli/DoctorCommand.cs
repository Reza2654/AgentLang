using AgentLang.AST;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Semantic;
using AgentLang.Security;
using AgentLang.Tools;

namespace AgentLang.Cli;

public static class DoctorCommand
{
    public static async Task<int> RunAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"AgentLang v{Program.Version} Doctor — System & Environment Diagnostics");
        Console.WriteLine("====================================================\n");
        Console.ResetColor();

        int warnings = 0;
        int errors = 0;

        // 1. .NET Runtime Check
        PrintStatus(true, ".NET Runtime", $".NET {Environment.Version} on {Environment.OSVersion.VersionString}");

        // 2. Compiler Pipeline Check
        bool compilerOk = false;
        try
        {
            var src = new SourceText("main { print(\"Doctor check\") }");
            var parser = new Parser.Parser(src);
            var prog = parser.ParseProgram();
            var sem = new SemanticAnalyzer();
            sem.Analyze(prog);
            compilerOk = !parser.Diagnostics.HasErrors && !sem.Diagnostics.HasErrors;
        }
        catch { }

        PrintStatus(compilerOk, "Compiler & Parser", "Lexer, Parser, AST, and Semantic Engine operational");
        if (!compilerOk) errors++;

        // 3. Security Engine Check
        bool securityOk = false;
        try
        {
            var pol = new PermissionPolicy("DocCheck");
            pol.AddRule(PermissionAction.Allow, "test.read");
            var sec = new SecurityEngine(new AutoApprovalProvider(true));
            sec.RegisterPolicy(pol);
            await sec.AuthorizeAsync("DoctorAgent", "DocCheck", "test.read", "probe");
            securityOk = true;
        }
        catch { }

        PrintStatus(securityOk, "Security Engine", "Zero-Trust capability authorization and sandbox active");
        if (!securityOk) errors++;

        // 4. Tool System Check
        var toolRegistry = new ToolRegistry();
        bool toolsOk = toolRegistry.GetTool("filesystem") != null &&
                       toolRegistry.GetTool("browser") != null &&
                       toolRegistry.GetTool("terminal") != null &&
                       toolRegistry.GetTool("http") != null &&
                       toolRegistry.GetTool("calculator") != null &&
                       toolRegistry.GetTool("image") != null &&
                       toolRegistry.GetTool("vision") != null;

        PrintStatus(toolsOk, "Tool System", "filesystem, browser, terminal, http, calculator, image, vision registered");
        if (!toolsOk) errors++;

        // 5. AI Training & MCP Protocol Engine Check
        var trainingEngine = new AiTrainingEngine();
        var ds = trainingEngine.GetOrCreateDataset("DoctorQA", "qa");
        ds.AddPair("DoctorPrompt", "DoctorAnswer");
        PrintStatus(ds.Entries.Count == 1, "AI Training Engine", "Q&A, Preference, Vision dataset & tuning engine active");

        var mcpClient = new AgentLang.Tools.MCP.McpClient("DoctorMock", "mock");
        await mcpClient.InitializeAsync();
        PrintStatus(mcpClient.IsInitialized && mcpClient.DiscoveredTools.Count > 0, "MCP Protocol Engine", "JSON-RPC 2.0 MCP Client & Tool Adapter active");

        // 6. Model Providers Check
        var models = new ModelRegistry();
        var mock = models.Resolve("mock");
        var mockRes = await mock.GenerateAsync(new ModelRequest("mock", "ping"));
        PrintStatus(mockRes.Success, "Mock Model Provider", "Deterministic offline AI engine available");

        string? openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiKey))
            PrintStatus(true, "OpenAI API Provider", "OPENAI_API_KEY is configured");
        else
        {
            PrintInfo("OpenAI API Provider", "OPENAI_API_KEY not set (optional - mock provider active)");
            warnings++;
        }

        string? geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(geminiKey))
            PrintStatus(true, "Google Gemini Provider", "GEMINI_API_KEY is configured");
        else
        {
            PrintInfo("Google Gemini Provider", "GEMINI_API_KEY not set (optional - mock provider active)");
            warnings++;
        }

        string? anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(anthropicKey))
            PrintStatus(true, "Anthropic Claude Provider", "ANTHROPIC_API_KEY is configured");
        else
        {
            PrintInfo("Anthropic Claude Provider", "ANTHROPIC_API_KEY not set (optional - mock provider active)");
            warnings++;
        }

        // 6. Search API Providers Check
        string? tavilyKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
        string? serperKey = Environment.GetEnvironmentVariable("SERPER_API_KEY");
        string? braveKey = Environment.GetEnvironmentVariable("BRAVE_API_KEY");
        string? generalSearchKey = Environment.GetEnvironmentVariable("SEARCH_API_KEY");

        bool hasSearchKey = !string.IsNullOrWhiteSpace(tavilyKey) ||
                            !string.IsNullOrWhiteSpace(serperKey) ||
                            !string.IsNullOrWhiteSpace(braveKey) ||
                            !string.IsNullOrWhiteSpace(generalSearchKey);

        if (hasSearchKey)
        {
            string configuredProvider = !string.IsNullOrWhiteSpace(tavilyKey) ? "Tavily (TAVILY_API_KEY)" :
                                        !string.IsNullOrWhiteSpace(serperKey) ? "Serper (SERPER_API_KEY)" :
                                        !string.IsNullOrWhiteSpace(braveKey) ? "Brave (BRAVE_API_KEY)" :
                                        "Generic (SEARCH_API_KEY)";
            PrintStatus(true, "Web Search API", $"Active provider: {configuredProvider}");
        }
        else
        {
            PrintInfo("Web Search API", "No Search API key set (Set TAVILY_API_KEY, SERPER_API_KEY, BRAVE_API_KEY, or SEARCH_API_KEY for live search)");
            warnings++;
        }

        Console.WriteLine();
        if (errors == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Doctor found no blocking issues ({warnings} informational notices). Environment is ready!");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Doctor found {errors} blocking error(s) and {warnings} warning(s).");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintStatus(bool ok, string component, string details)
    {
        if (ok)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[✓] ");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[✗] ");
        }
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{component,-26} ");
        Console.ResetColor();
        Console.WriteLine(details);
    }

    private static void PrintInfo(string component, string details)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("[!] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{component,-26} ");
        Console.ResetColor();
        Console.WriteLine(details);
    }
}

using AgentLang.AST;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Security;
using AgentLang.Semantic;
using AgentLang.Tools;
using Xunit;

namespace AgentLang.IntegrationTests;

public class ExamplesIntegrationTests
{
    private static async Task<(string Output, AgentLangRuntime Runtime)> RunScriptAsync(string relativePath)
    {
        // Locate file relative to repo root
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string fullPath = Path.Combine(repoRoot, relativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Script not found at: {fullPath}");
        }

        string source = await File.ReadAllTextAsync(fullPath);
        var sourceText = new SourceText(source, fullPath);

        // 1. Parse
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors, $"Parser error in {relativePath}: {string.Join(", ", parser.Diagnostics.Diagnostics)}");

        // 2. Semantic Analysis
        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);
        Assert.False(semantic.Diagnostics.HasErrors, $"Semantic error in {relativePath}: {string.Join(", ", semantic.Diagnostics.Diagnostics)}");

        // 3. Runtime
        var outputWriter = new StringWriter();
        var approver = new AutoApprovalProvider(true);
        var secEngine = new SecurityEngine(approver);
        var searchRegistry = new Tools.Search.SearchProviderRegistry(allowMockFallback: true);
        var toolRegistry = new ToolRegistry(secEngine, searchRegistry);
        var modelRegistry = new ModelRegistry();

        var runtime = new AgentLangRuntime(
            modelRegistry: modelRegistry,
            toolRegistry: toolRegistry,
            securityEngine: secEngine,
            output: outputWriter);

        await runtime.ExecuteProgramAsync(program);
        return (outputWriter.ToString(), runtime);
    }

    [Fact]
    public async Task RunsHelloAgentExample()
    {
        var (output, _) = await RunScriptAsync("examples/hello-agent/main.agent");
        Assert.Contains("Welcome to AgentLang", output);
        Assert.Contains("Execution complete", output);
    }

    [Fact]
    public async Task RunsSimpleResearcherExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/simple-researcher/main.agent");
        Assert.Contains("Initiating simple researcher workflow", output);
        Assert.True(runtime.AgentInstances.ContainsKey("Researcher"));
    }

    [Fact]
    public async Task RunsPermissionsExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/permissions/main.agent");
        Assert.Contains("Zero-trust permissions verified successfully", output);
        Assert.True(runtime.AgentInstances.ContainsKey("SecureAgent"));
    }

    [Fact]
    public async Task RunsToolsExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/tools/main.agent");
        Assert.Contains("Testing Tool integration", output);
        Assert.True(runtime.AgentInstances.ContainsKey("ToolUser"));
    }

    [Fact]
    public async Task RunsMemoryExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/memory/main.agent");
        Assert.Contains("Testing Agent Memory Retention", output);
        Assert.True(runtime.AgentInstances["MemoryAgent"].MemoryEnabled);
        Assert.False(runtime.AgentInstances["StatelessAgent"].MemoryEnabled);
    }

    [Fact]
    public async Task RunsMultiAgentExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/multiagent/main.agent");
        Assert.Contains("Launching MultiAgent Research & Review Team", output);
        Assert.True(runtime.AgentInstances.ContainsKey("Researcher"));
        Assert.True(runtime.AgentInstances.ContainsKey("Reviewer"));
    }

    [Fact]
    public async Task RunsParallelExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/parallel/main.agent");
        Assert.Contains("Starting Parallel MultiAgent Execution", output);
        Assert.True(runtime.AgentInstances.ContainsKey("TrendsSpecialist"));
        Assert.True(runtime.AgentInstances.ContainsKey("RiskSpecialist"));
        Assert.True(runtime.AgentInstances.ContainsKey("ExecutiveSynthesizer"));
    }

    [Fact]
    public async Task RunsEventsExample()
    {
        var (output, _) = await RunScriptAsync("examples/events/main.agent");
        Assert.Contains("[EVENT RECEIVED] research.finished triggered!", output);
        Assert.Contains("Event pipeline completed", output);
    }

    [Fact]
    public async Task RunsRetryExample()
    {
        var (output, _) = await RunScriptAsync("examples/retry/main.agent");
        Assert.Contains("Testing resilient execution with retry logic", output);
        Assert.Contains("Connecting to service", output);
    }

    [Fact]
    public async Task RunsHumanApprovalExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/human-approval/main.agent");
        Assert.Contains("Executing sensitive operation with human approval guardrails", output);
        Assert.True(runtime.AgentInstances.ContainsKey("AdminAgent"));
    }

    [Fact]
    public async Task RunsFunctionsExample()
    {
        var (output, _) = await RunScriptAsync("examples/functions/main.agent");
        Assert.Contains("Hello, Alice [Lead Researcher]!", output);
        Assert.Contains("Calculated total score: 100", output);
        Assert.Contains("Type of score: number", output);
        Assert.Contains("Factorial of 5: 120", output);
    }

    [Fact]
    public async Task RunsCustomToolsExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/custom-tools/main.agent");
        Assert.Contains("Tool Output: Positive (score: 95)", output);
        Assert.True(runtime.AgentInstances.ContainsKey("ReviewAgent"));
    }

    [Fact]
    public async Task RunsAgentCommunicationExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/agent-communication/main.agent");
        Assert.Contains("Total messages in inbox: 2", output);
        Assert.Contains("Mission Alpha: Scan all network nodes", output);
        Assert.True(runtime.AgentInstances.ContainsKey("Worker"));
        Assert.Equal(2, runtime.AgentInstances["Worker"].Inbox.Count);
    }

    [Fact]
    public async Task RunsModelOverridesExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/model-overrides/main.agent");
        Assert.Contains("Quick overview of Autonomous Agent Protocols", output);
        Assert.Contains("Deep architectural breakdown of Autonomous Agent Protocols", output);
        Assert.True(runtime.ModelRegistry.Aliases.ContainsKey("fast"));
    }

    [Fact]
    public async Task RunsPersistentMemoryExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/persistent-memory/main.agent");
        Assert.Contains("Persistent memory snapshot verified successfully", output);
        Assert.True(runtime.AgentInstances.ContainsKey("Historian"));
    }

    [Fact]
    public async Task RunsStructuredErrorsExample()
    {
        var (output, _) = await RunScriptAsync("examples/structured-errors/main.agent");
        Assert.Contains("[CAUGHT ERROR]: [AGT303] Division by zero", output);
        Assert.Contains("[CAUGHT BOUNDS ERROR]: [AGT302]", output);
        Assert.Contains("Status:   completed", output);
    }

    [Fact]
    public async Task RunsSearchApiExample()
    {
        var (output, runtime) = await RunScriptAsync("examples/search-api/main.agent");
        Assert.Contains("=== SearchAgent Results ===", output);
        Assert.Contains("Web search completed successfully", output);
        Assert.True(runtime.AgentInstances.ContainsKey("SearchAgent"));
    }
}

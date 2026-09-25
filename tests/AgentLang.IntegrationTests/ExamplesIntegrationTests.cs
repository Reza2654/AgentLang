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
        var toolRegistry = new ToolRegistry(secEngine);
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
}

using System.Text.Json;
using AgentLang.AST;
using AgentLang.Lexer;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.Tools;
using AgentLang.VM;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Eval;
using AgentLang.VM.Memory;
using AgentLang.VM.Resilience;
using Xunit;

namespace AgentLang.Tests;

public class V066Tests
{
    private static ProgramNode ParseCode(string source)
    {
        var sourceText = new SourceText(source, "<test>");
        var lexer = new Lexer.Lexer(sourceText);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser.Parser(tokens, sourceText, new DiagnosticBag());
        return parser.ParseProgram();
    }

    [Fact]
    public async Task Bytecode_Compilation_Serialization_And_Execution()
    {
        string code = "x = 20\ny = 22\nsum = x + y";
        var program = ParseCode(code);

        var compiler = new BytecodeCompiler();
        var module = compiler.Compile(program, "MathModule");

        Assert.Equal("MathModule", module.ModuleName);
        Assert.NotEmpty(module.Instructions);
        Assert.Contains("x", module.SymbolTable);
        Assert.Contains("y", module.SymbolTable);
        Assert.Contains("sum", module.SymbolTable);

        // Binary round-trip serialization (.agtc)
        byte[] bytes = module.ToBytes();
        Assert.NotEmpty(bytes);

        var loadedModule = AgentBytecodeModule.FromBytes(bytes);
        Assert.Equal(module.Instructions.Count, loadedModule.Instructions.Count);

        // Execute in VM
        var vm = new AgentVirtualMachine();
        await vm.ExecuteBytecodeAsync(loadedModule);

        var sumVal = vm.Runtime.GlobalScope.Get("sum");
        Assert.NotNull(sumVal);
        Assert.Equal(42.0, Convert.ToDouble(sumVal));
    }

    [Fact]
    public async Task Bytecode_IfElse_Branching_Execution()
    {
        string code = @"
score = 85
if (score > 80) {
    status = ""passed""
} else {
    status = ""failed""
}
";
        var program = ParseCode(code);
        var compiler = new BytecodeCompiler();
        var module = compiler.Compile(program, "BranchModule");

        var vm = new AgentVirtualMachine();
        await vm.ExecuteBytecodeAsync(module);

        var statusVal = vm.Runtime.GlobalScope.Get("status");
        Assert.Equal("passed", statusVal?.ToString());
    }

    [Fact]
    public void HybridVectorStore_Combines_BM25_And_DenseVectors_With_RRF()
    {
        var store = new HybridVectorStore();

        store.Add("Authentication service handles JWT token verification and user login.", "doc-auth");
        store.Add("Billing service processes credit card charges, refunds, and subscriptions.", "doc-billing");
        store.Add("Database migration script for PostgreSQL schema v2 upgrade.", "doc-db");

        // Query targeting billing and refunds
        var results = store.Search("credit card refund and billing issue", topK: 3);

        Assert.NotEmpty(results);
        var topResult = results[0];

        Assert.Equal("doc-billing", topResult.Document.Id);
        Assert.True(topResult.FusedScore > 0);
        Assert.True(topResult.DenseScore > 0);
        Assert.True(topResult.Bm25Score > 0);
    }

    [Fact]
    public async Task SemanticPromptCache_Hits_Similar_Queries_And_Tracks_Metrics()
    {
        var cache = new SemanticPromptCache { DefaultThreshold = 0.80 };

        int invocationCount = 0;
        Func<Task<string>> mockModel = () =>
        {
            invocationCount++;
            return Task.FromResult($"Model Response #{invocationCount}");
        };

        // 1. Initial request (Miss)
        var resp1 = await cache.GetOrComputeAsync("Explain quantum computing in simple terms", mockModel);
        Assert.Equal("Model Response #1", resp1);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(0, cache.Hits);
        Assert.Equal(1, invocationCount);

        // 2. Semantically similar request (Hit)
        var resp2 = await cache.GetOrComputeAsync("explain quantum computing in simple words", mockModel);
        Assert.Equal("Model Response #1", resp2); // Cached response
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, invocationCount); // Model was NOT invoked again!
        Assert.True(cache.EstimatedTokensSaved > 0);
        Assert.Equal(0.5, cache.HitRate);
    }

    [Fact]
    public async Task AutoRemediator_Executes_Fallback_Tools_On_Failure()
    {
        var remediator = new AutoRemediator();
        var policy = new SelfHealingPolicy
        {
            MaxAttempts = 3
        };
        policy.ToolFallbacks["primary_search"] = new List<string> { "fallback_search" };

        var callHistory = new List<string>();

        Func<string, IReadOnlyDictionary<string, object?>, Task<ToolResult>> mockInvoker = (tool, args) =>
        {
            callHistory.Add(tool);
            if (tool == "primary_search")
            {
                return Task.FromResult(ToolResult.Fail("Rate limit 429 on primary search API."));
            }
            return Task.FromResult(ToolResult.Ok("Fallback search results"));
        };

        var result = await remediator.ExecuteWithRemediationAsync(
            mockInvoker,
            "primary_search",
            new Dictionary<string, object?> { ["q"] = "agentlang" },
            policy);

        Assert.True(result.Success);
        Assert.Equal("Fallback search results", result.Output);
        Assert.Equal(2, result.Attempts);
        Assert.Contains("primary_search", callHistory);
        Assert.Contains("fallback_search", callHistory);
        Assert.Equal(RemediationStrategy.FallbackTool, result.RemediationTrail[1].Strategy);
    }

    [Fact]
    public async Task AutoRemediator_ReflectorRepair_Repairs_Malformed_Arguments()
    {
        var remediator = new AutoRemediator();
        var policy = new SelfHealingPolicy
        {
            MaxAttempts = 3,
            ArgumentRepairer = (tool, error, args) =>
            {
                var repaired = new Dictionary<string, object?>(args);
                if (error.Contains("Missing query parameter"))
                {
                    repaired["query"] = "auto_repaired_term";
                }
                return repaired;
            }
        };

        Func<string, IReadOnlyDictionary<string, object?>, Task<ToolResult>> mockInvoker = (tool, args) =>
        {
            if (!args.ContainsKey("query"))
            {
                return Task.FromResult(ToolResult.Fail("Invalid request: Missing query parameter."));
            }
            return Task.FromResult(ToolResult.Ok($"Success with query '{args["query"]}'"));
        };

        var result = await remediator.ExecuteWithRemediationAsync(
            mockInvoker,
            "database_lookup",
            new Dictionary<string, object?> { ["table"] = "users" }, // Missing "query"
            policy);

        Assert.True(result.Success);
        Assert.Contains("auto_repaired_term", result.Output?.ToString());
        Assert.Equal(2, result.Attempts);
        Assert.Equal(RemediationStrategy.ReflectorRepair, result.RemediationTrail[1].Strategy);
    }

    [Fact]
    public void OpenApiToolGenerator_Synthesizes_Dynamic_Tools_From_Spec()
    {
        string openApiJson = @"
{
  ""openapi"": ""3.0.0"",
  ""info"": { ""title"": ""Petstore API"", ""version"": ""1.0.0"" },
  ""servers"": [ { ""url"": ""https://api.petstore.io/v1"" } ],
  ""paths"": {
    ""/pets"": {
      ""get"": {
        ""operationId"": ""listPets"",
        ""parameters"": [ { ""name"": ""limit"", ""in"": ""query"" } ]
      },
      ""post"": {
        ""operationId"": ""createPet"",
        ""parameters"": [ { ""name"": ""name"", ""in"": ""body"" } ]
      }
    }
  }
}
";
        var registry = new ToolRegistry();
        var tools = OpenApiToolGenerator.RegisterOpenApiIntoRegistry(registry, openApiJson, "petstore");

        Assert.NotEmpty(tools);
        var petstoreTool = registry.GetTool("petstore");
        Assert.NotNull(petstoreTool);

        Assert.Contains("petstore.listPets", petstoreTool.SupportedCapabilities);
        Assert.Contains("petstore.createPet", petstoreTool.SupportedCapabilities);
    }

    [Fact]
    public async Task AgentBenchmarkRunner_Evaluates_Agent_Against_Dataset()
    {
        var runtime = new AgentLangRuntime();
        var agent = new AgentValue("TestAuditor")
        {
            Role = "Security Auditor",
            Instructions = "Identify security flaws and provide remediations"
        };
        runtime.RegisterAgent(agent);

        var dataset = new BenchmarkDataset
        {
            Name = "SecuritySanitySuite",
            Cases = new List<BenchmarkCase>
            {
                new()
                {
                    Id = "sec-01",
                    Input = "Analyze SQL injection risk in unsanitized queries",
                    ExpectedOutput = "risk"
                },
                new()
                {
                    Id = "sec-02",
                    Input = "Review password storage best practices",
                    ExpectedOutput = "storage"
                }
            }
        };

        var runner = new AgentBenchmarkRunner();
        var scorecard = await runner.RunBenchmarkAsync(agent, dataset, runtime, similarityThreshold: 0.50);

        Assert.Equal("TestAuditor", scorecard.AgentName);
        Assert.Equal(2, scorecard.TotalCases);
        Assert.Equal(2, scorecard.PassedCases);
        Assert.Equal(1.0, scorecard.PassRate);
        Assert.NotEmpty(scorecard.Results);

        string markdownReport = scorecard.ToMarkdownReport();
        Assert.Contains("# Benchmark Scorecard: TestAuditor", markdownReport);
        Assert.Contains("SecuritySanitySuite", markdownReport);
        Assert.Contains("PASS", markdownReport);
    }
}

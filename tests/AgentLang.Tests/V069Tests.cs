using System.Diagnostics;
using AgentLang.AST;
using AgentLang.Lexer;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using AgentLang.VM;
using AgentLang.VM.Schemas;
using Xunit;

namespace AgentLang.Tests;

public class V069Tests
{
    // =========================================================================
    // 1. NATIVE SCHEMA AST & PARSER TESTS
    // =========================================================================

    [Fact]
    public void Parser_SchemaDeclaration_ParsesFieldsAndConstraints()
    {
        string source = """
        schema SecurityReport {
            report_id: int(min: 1, max: 999999),
            severity: string(regex: "^(low|medium|high|critical)$"),
            score: float(min: 0.0, max: 10.0),
            verified: bool,
            cves: [string]
        }
        """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        Assert.False(parser.Diagnostics.HasErrors);
        Assert.Single(program.Declarations);
        var schemaDecl = Assert.IsType<SchemaDeclarationNode>(program.Declarations[0]);
        Assert.Equal("SecurityReport", schemaDecl.Name);
        Assert.Equal(5, schemaDecl.Fields.Count);

        // Field 0: report_id: int(min: 1, max: 999999)
        var f0 = schemaDecl.Fields[0];
        Assert.Equal("report_id", f0.Name);
        Assert.Equal("int", f0.TypeName);
        Assert.False(f0.IsArray);
        Assert.True(f0.IsRequired);
        Assert.Equal(1.0, f0.MinValue);
        Assert.Equal(999999.0, f0.MaxValue);

        // Field 1: severity: string(regex: "^(low|medium|high|critical)$")
        var f1 = schemaDecl.Fields[1];
        Assert.Equal("severity", f1.Name);
        Assert.Equal("string", f1.TypeName);
        Assert.Equal("^(low|medium|high|critical)$", f1.RegexPattern);

        // Field 2: score: float(min: 0.0, max: 10.0)
        var f2 = schemaDecl.Fields[2];
        Assert.Equal("score", f2.Name);
        Assert.Equal("float", f2.TypeName);
        Assert.Equal(0.0, f2.MinValue);
        Assert.Equal(10.0, f2.MaxValue);

        // Field 3: verified: bool
        var f3 = schemaDecl.Fields[3];
        Assert.Equal("verified", f3.Name);
        Assert.Equal("bool", f3.TypeName);

        // Field 4: cves: [string]
        var f4 = schemaDecl.Fields[4];
        Assert.Equal("cves", f4.Name);
        Assert.Equal("string", f4.TypeName);
        Assert.True(f4.IsArray);
    }

    [Fact]
    public void Parser_MultipleSchemas_ParsesAllDeclarations()
    {
        string source = """
        schema Header {
            timestamp: string,
            version: int
        }

        schema Payload {
            data: [string],
            valid: bool
        }
        """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        Assert.Equal(2, program.Declarations.Count);
        Assert.Equal("Header", ((SchemaDeclarationNode)program.Declarations[0]).Name);
        Assert.Equal("Payload", ((SchemaDeclarationNode)program.Declarations[1]).Name);
    }

    [Fact]
    public void SemanticAnalyzer_DuplicateSchemaFields_ReportsDiagnostic()
    {
        string source = """
        schema UserProfile {
            email: string,
            email: string
        }
        """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        Assert.True(analyzer.Diagnostics.HasErrors);
        var err = analyzer.Diagnostics.Diagnostics.First(d => d.Id == "AL0038");
        Assert.Contains("Duplicate field 'email'", err.Message);
    }

    // =========================================================================
    // 2. SCHEMA AST TO STRUCTUREDSCHEMA VALIDATION TESTS
    // =========================================================================

    [Fact]
    public void SchemaAstExtensions_ToStructuredSchema_ValidatesCorrectJson()
    {
        string source = """
        schema Product {
            title: string,
            price: float(min: 0.01),
            stock: int(min: 0),
            in_stock: bool,
            tags: [string]
        }
        """;

        var sourceText = new SourceText(source);
        var program = new Parser.Parser(sourceText).ParseProgram();
        var schemaDecl = (SchemaDeclarationNode)program.Declarations[0];

        var structured = schemaDecl.ToStructuredSchema();
        Assert.Equal("Product", structured.Name);
        Assert.Equal(5, structured.Fields.Count);

        string validJson = """
        {
            "title": "Quantum Sensor",
            "price": 299.99,
            "stock": 15,
            "in_stock": true,
            "tags": ["hardware", "iot", "sensor"]
        }
        """;

        var result = structured.Validate(validJson);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SchemaAstExtensions_ToStructuredSchema_CatchesInvalidValues()
    {
        string source = """
        schema Metric {
            name: string,
            value: int(min: 10, max: 100)
        }
        """;

        var sourceText = new SourceText(source);
        var program = new Parser.Parser(sourceText).ParseProgram();
        var schemaDecl = (SchemaDeclarationNode)program.Declarations[0];

        var structured = schemaDecl.ToStructuredSchema();

        // value is below min (5 < 10)
        string invalidJson = """
        {
            "name": "CPU_Usage",
            "value": 5
        }
        """;

        var result = structured.Validate(invalidJson);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("below minimum"));
    }

    // =========================================================================
    // 3. OLLAMA LOCAL PROVIDER TESTS
    // =========================================================================

    [Fact]
    public void OllamaModelProvider_Initialization_SetsDefaults()
    {
        var provider = new OllamaModelProvider("http://localhost:11434");
        Assert.Equal("ollama", provider.ProviderId);
        Assert.Equal("http://localhost:11434", provider.BaseUrl);
        Assert.True(provider.CanHandle("llama3.2"));
        Assert.True(provider.CanHandle("mistral:7b"));
        Assert.True(provider.CanHandle("deepseek-coder"));
        Assert.True(provider.CanHandle("ollama/my-custom-model"));
        Assert.False(provider.CanHandle("gpt-4o"));
    }

    [Fact]
    public void ModelRegistry_ResolvesOllamaProvider()
    {
        var registry = new ModelRegistry();
        var provider = registry.GetProvider("ollama");
        Assert.NotNull(provider);
        Assert.Equal("ollama", provider.ProviderId);

        var resolved = registry.Resolve("llama3.2");
        Assert.NotNull(resolved);
        Assert.Equal("ollama", resolved.ProviderId);
    }

    // =========================================================================
    // 4. SMART LLM GATEWAY ROUTER & CIRCUIT BREAKER TESTS
    // =========================================================================

    private class MockTestProvider : IModelProvider
    {
        public string ProviderId { get; }
        private readonly string _supportedPrefix;
        public bool ShouldFail { get; set; }
        public int CallCount { get; private set; }

        public MockTestProvider(string providerId, string supportedPrefix, bool shouldFail = false)
        {
            ProviderId = providerId;
            _supportedPrefix = supportedPrefix;
            ShouldFail = shouldFail;
        }

        public bool CanHandle(string modelName) =>
            modelName.StartsWith(_supportedPrefix, StringComparison.OrdinalIgnoreCase);

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (ShouldFail)
            {
                return Task.FromResult(ModelResponse.Failed($"Simulated failure on {ProviderId}", request.ModelName));
            }

            return Task.FromResult(new ModelResponse(
                Content: $"Response from {ProviderId} for prompt '{request.Prompt}'",
                Model: request.ModelName,
                TokensUsed: 42,
                Latency: TimeSpan.FromMilliseconds(50),
                Success: true
            ));
        }
    }

    [Fact]
    public async Task LlmGateway_CascadingFallback_SucceedsOnSecondary()
    {
        var registry = new ModelRegistry();
        var primary = new MockTestProvider("primary_prov", "primary-model", shouldFail: true);
        var secondary = new MockTestProvider("backup_prov", "backup-model", shouldFail: false);

        registry.RegisterProvider(primary);
        registry.RegisterProvider(secondary);

        var router = new LlmGatewayRouter(registry, fallbackChain: ["primary-model", "backup-model"]);

        var request = new ModelRequest(
            ModelName: "primary-model",
            Prompt: "Explain agent loops"
        );

        var response = await router.GenerateAsync(request);

        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
        Assert.True(response.Success);
        Assert.Contains("Response from backup_prov", response.Content);

        // Verify health metrics
        var primaryHealth = router.GetOrCreateHealth("primary_prov");
        var secondaryHealth = router.GetOrCreateHealth("backup_prov");

        Assert.Equal(1, primaryHealth.ConsecutiveFailures);
        Assert.Equal(1, secondaryHealth.TotalRequests);
        Assert.Equal(0, secondaryHealth.ConsecutiveFailures);
    }

    [Fact]
    public async Task LlmGateway_CircuitBreaker_TripsOpenAfterThreshold()
    {
        var registry = new ModelRegistry();
        var faulty = new MockTestProvider("faulty_prov", "faulty-model", shouldFail: true);
        var backup = new MockTestProvider("backup_prov", "backup-model", shouldFail: false);

        registry.RegisterProvider(faulty);
        registry.RegisterProvider(backup);

        var router = new LlmGatewayRouter(registry, fallbackChain: ["faulty-model", "backup-model"])
        {
            MaxConsecutiveFailures = 3,
            CooldownPeriod = TimeSpan.FromSeconds(30)
        };

        var request = new ModelRequest("faulty-model", "Test prompt");

        // 3 consecutive failures trip circuit to Open
        for (int i = 0; i < 3; i++)
        {
            await router.GenerateAsync(request);
        }

        var faultyHealth = router.GetOrCreateHealth("faulty_prov");
        Assert.Equal(CircuitState.Open, faultyHealth.State);
        Assert.Equal(3, faultyHealth.ConsecutiveFailures);

        // On 4th call, faulty provider should be bypassed immediately without executing
        int callCountBefore = faulty.CallCount;
        var resp = await router.GenerateAsync(request);
        Assert.Equal(callCountBefore, faulty.CallCount); // Did NOT increment because circuit is Open!
        Assert.True(resp.Success);
        Assert.Contains("Response from backup_prov", resp.Content);
    }

    [Fact]
    public async Task LlmGateway_ReportsFailureWhenAllProvidersFail()
    {
        var registry = new ModelRegistry();
        registry.RegisterProvider(new MockTestProvider("p1", "fail1", shouldFail: true));
        registry.RegisterProvider(new MockTestProvider("p2", "fail2", shouldFail: true));

        var router = new LlmGatewayRouter(registry, fallbackChain: ["fail1", "fail2"]);

        var request = new ModelRequest("fail1", "All will fail");
        var response = await router.GenerateAsync(request);

        Assert.False(response.Success);
        Assert.Contains("All providers in gateway fallback chain failed", response.ErrorMessage);
    }

    // =========================================================================
    // 5. RUNTIME & VM INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public void Vm_Gateway_InitializedAndAccessible()
    {
        var vm = new AgentVirtualMachine();
        Assert.NotNull(vm.Gateway);
        Assert.Equal("gateway", vm.Gateway.ProviderId);
    }

    [Fact]
    public async Task Runtime_RegistersSchemaDeclarationInScope()
    {
        var runtime = new AgentLangRuntime();
        string source = """
        schema AnalysisReport {
            query: string,
            findings_count: int(min: 0)
        }
        """;

        var sourceText = new SourceText(source);
        var program = new Parser.Parser(sourceText).ParseProgram();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.SchemaDefs.ContainsKey("AnalysisReport"));
        var schema = runtime.SchemaDefs["AnalysisReport"];
        Assert.Equal("AnalysisReport", schema.Name);
        Assert.Equal(2, schema.Fields.Count);
    }
}

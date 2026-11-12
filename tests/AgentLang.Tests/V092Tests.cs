using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Errors;
using AgentLang.Lexer;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Configuration;
using AgentLang.Runtime.Data;
using AgentLang.Security;
using AgentLang.VM;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Server;
using Xunit;

namespace AgentLang.Tests;

public class V092Tests
{
    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // =========================================================================
    // 1. SCHEMA-BACKED PERSISTENT ENTITY STORE + DISK PERSISTENCE
    // =========================================================================

    [Fact]
    public async Task SchemaEntityStore_Crud_Validation_And_DiskPersistence()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"al_v092_data_{Guid.NewGuid():N}");
        try
        {
            string code = """
            schema Customer {
                name: string,
                score: int(min: 0, max: 100),
                code: string(regex: "^CUST-[0-9]{3}$")
            }

            main {
                inserted = data.insert(Customer, { name: "Acme Corp", score: 92, code: "CUST-101" })
                inserted_id = inserted["id"]
                fetched = data.get(Customer, inserted_id)
                updated = data.update(Customer, inserted_id, { score: 98 })
                listed = data.list(Customer, { name: "Acme Corp" })
                total = data.count(Customer)
            }
            """;

            var program = new Parser.Parser(new SourceText(code)).ParseProgram();
            var runtime = new AgentLangRuntime(entityStorageDirectory: tempDir);
            await runtime.ExecuteProgramAsync(program);

            var totalVal = runtime.GlobalScope.Get("total");
            Assert.Equal(1L, Convert.ToInt64(totalVal));

            var updated = runtime.GlobalScope.Get("updated") as IDictionary<string, object?>;
            Assert.NotNull(updated);
            Assert.Equal(98L, Convert.ToInt64(updated!["score"]));

            var listed = runtime.GlobalScope.Get("listed") as IList<object?>;
            Assert.NotNull(listed);
            Assert.Single(listed!);

            // Validation rejects out-of-range integer & regex mismatch
            var badEx = Assert.Throws<AgentLangRuntimeException>(() =>
                runtime.EntityStore.Insert("Customer", new Dictionary<string, object?>
                {
                    ["name"] = "Invalid Co",
                    ["score"] = 250,
                    ["code"] = "BAD"
                }));
            Assert.Equal("AL-DATA-001", badEx.ErrorCode);

            // Reload into a brand-new runtime pointing at same directory
            var programReload = new Parser.Parser(new SourceText("""
            schema Customer {
                name: string,
                score: int(min: 0, max: 100),
                code: string(regex: "^CUST-[0-9]{3}$")
            }
            """)).ParseProgram();

            var reloadedRuntime = new AgentLangRuntime(entityStorageDirectory: tempDir);
            await reloadedRuntime.ExecuteProgramAsync(programReload);
            Assert.Equal(1, reloadedRuntime.EntityStore.Count("Customer"));
            var records = reloadedRuntime.EntityStore.List("Customer");
            Assert.Equal("Acme Corp", records[0]["name"]?.ToString());
            Assert.Equal(98L, Convert.ToInt64(records[0]["score"]));

            // Delete removes record from memory and disk
            string id = records[0]["id"]!.ToString()!;
            Assert.True(reloadedRuntime.EntityStore.Delete("Customer", id));
            Assert.Equal(0, reloadedRuntime.EntityStore.Count("Customer"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // =========================================================================
    // 2. DATA RBAC ENFORCEMENT VIA PERMISSION POLICIES
    // =========================================================================

    [Fact]
    public async Task SecurityEngine_Enforces_Data_Rbac_When_Agent_Has_Policy()
    {
        string code = """
        permission ReadOnlyData {
            allow data.AuditLog.read
            cannot data.AuditLog.write
            cannot data.AuditLog.delete
        }

        schema AuditLog {
            event_name: string
        }

        agent Auditor {
            model: "mock",
            permission: ReadOnlyData
        }
        """;

        var program = new Parser.Parser(new SourceText(code)).ParseProgram();
        var runtime = new AgentLangRuntime();
        await runtime.ExecuteProgramAsync(program);

        // Authorized top-level seed insert succeeds
        var created = await runtime.InvokeDataModuleAsync("insert", new object?[]
        {
            "AuditLog",
            new Dictionary<string, object?> { ["event_name"] = "boot" }
        }) as Dictionary<string, object?>;
        Assert.NotNull(created);

        // Simulate active agent enforcing ReadOnlyData policy directly via runtime SecurityEngine check
        await runtime.SecurityEngine.AuthorizeAsync("Auditor", "ReadOnlyData", "data.AuditLog.read", "list");
        await Assert.ThrowsAsync<SecurityException>(() =>
            runtime.SecurityEngine.AuthorizeAsync("Auditor", "ReadOnlyData", "data.AuditLog.write", "insert"));
        await Assert.ThrowsAsync<SecurityException>(() =>
            runtime.SecurityEngine.AuthorizeAsync("Auditor", "ReadOnlyData", "data.AuditLog.delete", "delete"));
    }

    // =========================================================================
    // 3. AUTO-GENERATED ENTITY REST API + OPENAPI 3.0 SPEC OVER HTTP
    // =========================================================================

    [Fact]
    public async Task AgentHttpServer_Serves_AutoGenerated_EntityCrud_And_OpenApiSpec()
    {
        int port = GetFreeTcpPort();
        var vm = new AgentVirtualMachine();
        using var server = new AgentHttpServer(port, vm);

        await server.LoadScriptAsync("""
        schema Order {
            customer: string,
            amount: float(min: 0.01)
        }
        """);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cts.Token);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/") };

            // 1. OpenAPI 3.0 Spec includes Order paths and component schema
            var openApiRes = await client.GetAsync("openapi.json", cts.Token);
            Assert.Equal(HttpStatusCode.OK, openApiRes.StatusCode);
            string openApiJson = await openApiRes.Content.ReadAsStringAsync(cts.Token);
            using (var openApiDoc = JsonDocument.Parse(openApiJson))
            {
                Assert.Equal("3.0.3", openApiDoc.RootElement.GetProperty("openapi").GetString());
                Assert.True(openApiDoc.RootElement.GetProperty("paths").TryGetProperty("/api/v1/entities/Order", out _));
                Assert.True(openApiDoc.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("Order", out _));
            }

            // 2. POST valid entity -> 201 Created
            var postOk = await client.PostAsync(
                "api/v1/entities/Order",
                new StringContent("""{"customer":"Alice","amount":49.95}""", Encoding.UTF8, "application/json"),
                cts.Token);
            Assert.Equal(HttpStatusCode.Created, postOk.StatusCode);
            string createdBody = await postOk.Content.ReadAsStringAsync(cts.Token);
            string orderId;
            using (var createdDoc = JsonDocument.Parse(createdBody))
            {
                orderId = createdDoc.RootElement.GetProperty("id").GetString()!;
                Assert.Equal("Alice", createdDoc.RootElement.GetProperty("customer").GetString());
            }

            // 3. POST invalid entity constraint -> 400 Bad Request
            var postBad = await client.PostAsync(
                "api/v1/entities/Order",
                new StringContent("""{"customer":"Bob","amount":-5}""", Encoding.UTF8, "application/json"),
                cts.Token);
            Assert.Equal(HttpStatusCode.BadRequest, postBad.StatusCode);

            // 4. GET collection & single item
            var getOne = await client.GetAsync($"api/v1/entities/Order/{orderId}", cts.Token);
            Assert.Equal(HttpStatusCode.OK, getOne.StatusCode);

            // 5. PUT update
            var putRes = await client.PutAsync(
                $"api/v1/entities/Order/{orderId}",
                new StringContent("""{"amount":79.50}""", Encoding.UTF8, "application/json"),
                cts.Token);
            Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
            string updatedJson = await putRes.Content.ReadAsStringAsync(cts.Token);
            using (var updatedDoc = JsonDocument.Parse(updatedJson))
            {
                Assert.Equal(79.5, updatedDoc.RootElement.GetProperty("amount").GetDouble(), 2);
            }

            // 6. DELETE + verify 404 after delete
            var delRes = await client.DeleteAsync($"api/v1/entities/Order/{orderId}", cts.Token);
            Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
            var getMissing = await client.GetAsync($"api/v1/entities/Order/{orderId}", cts.Token);
            Assert.Equal(HttpStatusCode.NotFound, getMissing.StatusCode);
        }
        finally
        {
            server.Stop();
        }
    }

    // =========================================================================
    // 4. REAL .AGTC BYTECODE EXECUTION PARITY (AGENTS, MEMBER ACCESS, LOOPS, DATA)
    // =========================================================================

    private sealed class StubLlmProvider : IModelProvider
    {
        public string ProviderId => "stub-llm";
        public bool CanHandle(string modelName) =>
            modelName.Equals("stub-llm", StringComparison.OrdinalIgnoreCase);

        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default)
        {
            string reply = $"Final Answer: [REAL-BYTECODE-LLM] Analyzed '{request.Prompt.Split('\n').LastOrDefault()}'";
            return Task.FromResult(new ModelResponse(reply, request.ModelName, 42, TimeSpan.FromMilliseconds(5), true));
        }

        public async IAsyncEnumerable<string> StreamAsync(
            ModelRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return $"Final Answer: [REAL-BYTECODE-LLM]";
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Bytecode_RealAgentExecution_MemberAccess_Loops_And_DataCrud_Roundtrip()
    {
        string code = """
        schema SummaryRecord {
            title: string,
            score: int(min: 0, max: 100)
        }

        agent Analyst {
            model: "stub-llm",
            role: "Senior Reviewer",
            instructions: "Analyze concisely"
        }

        main {
            counter = 0
            acc = 0
            while (counter < 4) {
                counter = counter + 1
                acc = acc + counter
            }

            response = Analyst.solve("Summarize quarterly metrics")
            summary_text = response.output
            upper_summary = text.to_upper(summary_text)

            rec = data.insert(SummaryRecord, { title: upper_summary, score: acc })
            total_records = data.count(SummaryRecord)
        }
        """;

        var program = new Parser.Parser(new SourceText(code)).ParseProgram();
        var compiler = new BytecodeCompiler();
        var module = compiler.Compile(program, "ParityModule");

        // Serialize -> deserialize .agtc binary format
        byte[] agtcBytes = module.ToBytes();
        var loaded = AgentBytecodeModule.FromBytes(agtcBytes);

        // Configure runtime with custom IModelProvider verifying live LLM invocation from bytecode
        var registry = new ModelRegistry(allowMockFallback: false);
        registry.RegisterProvider(new StubLlmProvider(), makeDefault: true);

        var output = new StringWriter();
        var runtime = new AgentLangRuntime(modelRegistry: registry, output: output);
        var vm = new AgentVirtualMachine(runtime: runtime);

        await vm.ExecuteBytecodeAsync(loaded);

        // Verify while loop result
        Assert.Equal(10.0, Convert.ToDouble(vm.Runtime.GlobalScope.Get("acc")));

        // Verify REAL agent execution (NOT placeholder) and response.output member access
        string? summaryText = vm.Runtime.GlobalScope.Get("summary_text")?.ToString();
        Assert.NotNull(summaryText);
        Assert.DoesNotContain("[Agent Execution:", summaryText!);
        Assert.Contains("[REAL-BYTECODE-LLM]", summaryText!);

        // Verify stdlib call & data store persistence inside bytecode VM
        string? upperSummary = vm.Runtime.GlobalScope.Get("upper_summary")?.ToString();
        Assert.Equal(summaryText!.ToUpperInvariant(), upperSummary);
        Assert.Equal(1L, Convert.ToInt64(vm.Runtime.GlobalScope.Get("total_records")));
    }

    // =========================================================================
    // 5. TRIM-SAFE PROJECT CONFIG & PROJECT MANAGER JSON ROUNDTRIP
    // =========================================================================

    [Fact]
    public void ProjectConfig_And_ProjectManager_TrimSafeJson_Roundtrip()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"al_v092_proj_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempRoot);

            // ProjectConfig Save/Load without reflection
            var cfg = ProjectConfig.CreateDefault("trim-safe-app");
            cfg.BudgetLimitUsd = 12.5;
            cfg.Env["APP_ENV"] = "production";
            cfg.Dependencies["std/http"] = "^1.0.0";

            string cfgPath = Path.Combine(tempRoot, "agent.config.json");
            File.WriteAllText(cfgPath, cfg.ToJson());

            var loadedCfg = ProjectConfig.LoadFromFile(cfgPath);
            Assert.Equal("trim-safe-app", loadedCfg.Name);
            Assert.Equal(12.5, loadedCfg.BudgetLimitUsd);
            Assert.Equal("production", loadedCfg.Env["APP_ENV"]);
            Assert.Equal("^1.0.0", loadedCfg.Dependencies["std/http"]);

            // ProjectManager scaffold/manifest/lockfile roundtrip
            ProjectManager.ScaffoldProject("sample-pkg", tempRoot);
            string projDir = Path.Combine(tempRoot, "sample-pkg");
            var manifest = ProjectManager.LoadManifest(projDir);
            Assert.Equal("sample-pkg", manifest.Name);
            manifest.Dependencies["agent-tools"] = "^0.9.2";
            ProjectManager.SaveManifest(projDir, manifest);

            var reloadedManifest = ProjectManager.LoadManifest(projDir);
            Assert.Equal("^0.9.2", reloadedManifest.Dependencies["agent-tools"]);
            Assert.True(File.Exists(Path.Combine(projDir, ProjectManager.LockFileName)));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}

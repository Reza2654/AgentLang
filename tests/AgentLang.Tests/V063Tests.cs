using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Errors;
using AgentLang.Lexer;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.VM;
using Xunit;

namespace AgentLang.Tests;

public class V063Tests
{
    private static ProgramNode Parse(string code)
    {
        var sourceText = new SourceText(code);
        var lexer = new Lexer.Lexer(sourceText);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser.Parser(tokens, sourceText, new DiagnosticBag());
        return parser.ParseProgram();
    }

    [Fact]
    public async Task VM_CycleLimit_Exceeded_Throws_VmCycleLimitExceededException()
    {
        var options = new VmOptions
        {
            MaxInstructions = 15
        };
        var vm = new AgentVirtualMachine(options);

        string code = """
            count = 0
            loop 100 {
                count = count + 1
            }
            """;

        var ex = await Assert.ThrowsAsync<VmCycleLimitExceededException>(async () =>
        {
            await vm.ExecuteAsync(code);
        });

        Assert.Equal("VM101", ex.ErrorCode);
        Assert.Equal(15, ex.InstructionLimit);
        Assert.True(ex.CyclesExecuted > 15);
        Assert.Equal("Halted:CycleLimitExceeded", vm.Status);
    }

    [Fact]
    public async Task VM_Sandbox_Capability_Gating_Blocks_Unauthorized_Tool()
    {
        var options = new VmOptions
        {
            EnableSandboxing = true,
            AllowedCapabilities = ["memory.*"] // Only memory allowed, no browser/network
        };
        var vm = new AgentVirtualMachine(options);

        string code = """
            agent TestAgent {
                task t {
                    browser.search("unauthorized search")
                }
            }
            main {
                agent TestAgent
            }
            """;

        var ex = await Assert.ThrowsAsync<VmSandboxViolationException>(async () =>
        {
            await vm.ExecuteAsync(code);
        });

        Assert.Equal("VM102", ex.ErrorCode);
        Assert.Equal("browser.search", ex.Capability);
        Assert.Equal("Halted:SandboxViolation", vm.Status);
    }

    [Fact]
    public async Task VM_Snapshot_Create_And_Restore_State()
    {
        var vm = new AgentVirtualMachine();

        string code1 = """
            agent MemoryAgent {
                remember("initial memory fact 1")
                remember("initial memory fact 2")
            }
            main {
                agent MemoryAgent
                counter = 42
                status = "checkpoint_ready"
            }
            """;

        await vm.ExecuteAsync(code1);

        // Capture snapshot
        var snapshot = vm.CreateSnapshot("checkpoint_alpha");
        Assert.Equal("checkpoint_alpha", snapshot.Label);
        Assert.Equal("Completed", snapshot.ExecutionStatus);
        Assert.True(Convert.ToInt32(snapshot.GlobalVariables["counter"]) == 42);
        Assert.Equal("checkpoint_ready", snapshot.GlobalVariables["status"]?.ToString());
        Assert.True(snapshot.AgentMemories.ContainsKey("MemoryAgent"));
        Assert.Equal(2, snapshot.AgentMemories["MemoryAgent"].Count);

        // Serialize snapshot to JSON and back to test serialization
        string json = snapshot.ToJson();
        var deserializedSnapshot = VmSnapshot.FromJson(json);
        Assert.Equal(snapshot.Id, deserializedSnapshot.Id);
        Assert.Equal("checkpoint_alpha", deserializedSnapshot.Label);

        // Mutate VM state
        string mutateCode = """
            main {
                counter = 999
                status = "corrupted"
            }
            """;
        await vm.ExecuteAsync(mutateCode);
        Assert.True(Convert.ToInt32(vm.Runtime.GlobalScope.Get("counter")) == 999);

        // Restore snapshot
        vm.RestoreSnapshot(snapshot);
        Assert.True(Convert.ToInt32(vm.Runtime.GlobalScope.Get("counter")) == 42);
        Assert.Equal("checkpoint_ready", vm.Runtime.GlobalScope.Get("status")?.ToString());
        Assert.Equal("Completed", vm.Status);
    }

    [Fact]
    public async Task Stdlib_Crypto_Functions_Work_Correctly()
    {
        var runtime = new AgentLangRuntime();
        string code = """
            import "@stdlib/crypto";

            main {
                h_sha = sha256("hello world")
                h_md5 = md5("hello world")
                u = uuid()
                tok = random_token(16)
            }
            """;

        var program = Parse(code);
        await runtime.ExecuteProgramAsync(program);

        string? sha = runtime.GlobalScope.Get("h_sha")?.ToString();
        string? md5 = runtime.GlobalScope.Get("h_md5")?.ToString();
        string? uuid = runtime.GlobalScope.Get("u")?.ToString();
        string? tok = runtime.GlobalScope.Get("tok")?.ToString();

        // sha256("hello world") = b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9
        Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", sha);
        // md5("hello world") = 5eb63bbbe01eeed093cb22bb8f5acdc3
        Assert.Equal("5eb63bbbe01eeed093cb22bb8f5acdc3", md5);
        Assert.NotNull(uuid);
        Assert.True(Guid.TryParse(uuid, out _));
        Assert.NotNull(tok);
        Assert.Equal(16, tok.Length);
    }

    [Fact]
    public async Task Stdlib_Text_Functions_Work_Correctly()
    {
        var runtime = new AgentLangRuntime();
        string code = """
            import { split, join, trim, matches, replace } from "@stdlib/text";

            main {
                parts = split("ai,agent,autonomous", ",")
                joined = join(parts, " | ")
                cleaned = trim("   AgentLang   ")
                is_match = matches("test@agentlang.org", "^[a-z0-9._%+-]+@[a-z0-9.-]+\\.[a-z]{2,}$")
                replaced = replace("hello javascript", "javascript", "AgentLang")
            }
            """;

        var program = Parse(code);
        await runtime.ExecuteProgramAsync(program);

        var parts = runtime.GlobalScope.Get("parts") as System.Collections.IList;
        Assert.NotNull(parts);
        Assert.Equal(3, parts.Count);
        Assert.Equal("ai", parts[0]?.ToString());
        Assert.Equal("agent", parts[1]?.ToString());
        Assert.Equal("autonomous", parts[2]?.ToString());

        Assert.Equal("ai | agent | autonomous", runtime.GlobalScope.Get("joined")?.ToString());
        Assert.Equal("AgentLang", runtime.GlobalScope.Get("cleaned")?.ToString());
        Assert.Equal(true, runtime.GlobalScope.Get("is_match"));
        Assert.Equal("hello AgentLang", runtime.GlobalScope.Get("replaced")?.ToString());
    }

    [Fact]
    public async Task Stdlib_RAG_Functions_Work_Correctly()
    {
        var runtime = new AgentLangRuntime();
        string code = """
            import "@stdlib/rag";

            main {
                text = "Autonomous AI agents execute complex multi-step reasoning workflows with tool access and memory."
                chunks = chunk_text(text, 30, 5)
                sim_high = similarity("autonomous artificial intelligence agent", "autonomous artificial intelligence system")
                sim_low = similarity("autonomous artificial intelligence", "baking chocolate chip cookies")
            }
            """;

        var program = Parse(code);
        await runtime.ExecuteProgramAsync(program);

        var chunks = runtime.GlobalScope.Get("chunks") as System.Collections.IList;
        Assert.NotNull(chunks);
        Assert.True(chunks.Count >= 2);

        double simHigh = Convert.ToDouble(runtime.GlobalScope.Get("sim_high"));
        double simLow = Convert.ToDouble(runtime.GlobalScope.Get("sim_low"));

        Assert.True(simHigh > 0.4);
        Assert.True(simLow < 0.2);
    }

    [Fact]
    public async Task Modular_MultiFile_Import_Loads_Functions_And_Variables()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AgentLangTest_Modular_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string helperFile = Path.Combine(tempDir, "math_utils.agt");
            string helperCode = """
                fn add(a, b) {
                    return a + b
                }

                fn multiply(a, b) {
                    return a * b
                }

                pi_val = 314
                """;
            await File.WriteAllTextAsync(helperFile, helperCode);

            string mainFile = Path.Combine(tempDir, "main.agt");
            string mainCode = """
                import { add, multiply, pi_val } from "./math_utils.agt";

                main {
                    sum = add(10, 25)
                    product = multiply(6, 7)
                    val = pi_val
                }
                """;
            await File.WriteAllTextAsync(mainFile, mainCode);

            var runtime = new AgentLangRuntime();
            runtime.CurrentSourceFile = mainFile;
            var prog = Parse(mainCode);
            await runtime.ExecuteProgramAsync(prog);

            Assert.Equal(35, Convert.ToInt32(runtime.GlobalScope.Get("sum")));
            Assert.Equal(42, Convert.ToInt32(runtime.GlobalScope.Get("product")));
            Assert.Equal(314, Convert.ToInt32(runtime.GlobalScope.Get("val")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Modular_Circular_Import_Is_Detected_And_Prevented()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AgentLangTest_Circular_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string fileA = Path.Combine(tempDir, "moduleA.agt");
            string fileB = Path.Combine(tempDir, "moduleB.agt");

            string codeA = """
                import "./moduleB.agt";
                valA = "A"
                """;
            string codeB = """
                import "./moduleA.agt";
                valB = "B"
                """;

            await File.WriteAllTextAsync(fileA, codeA);
            await File.WriteAllTextAsync(fileB, codeB);

            var runtime = new AgentLangRuntime();
            runtime.CurrentSourceFile = fileA;
            var prog = Parse(codeA);

            var ex = await Assert.ThrowsAsync<AgentLangRuntimeException>(async () =>
            {
                await runtime.ExecuteProgramAsync(prog);
            });

            Assert.Equal("MOD002", ex.ErrorCode);
            Assert.Contains("Circular import detected", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProjectManager_Pack_And_Unpack_Creates_Valid_Package()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AgentLangTest_Pack_" + Guid.NewGuid().ToString("N"));
        string unpackDir = Path.Combine(Path.GetTempPath(), "AgentLangTest_Unpack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            ProjectManager.ScaffoldProject("DemoProject", tempDir);
            string projectDir = Path.Combine(tempDir, "DemoProject");

            // Pack project
            string packagePath = ProjectManager.PackProject(projectDir);
            Assert.True(File.Exists(packagePath));
            Assert.EndsWith(".agtpkg", packagePath);

            // Unpack package
            ProjectManager.UnpackPackage(packagePath, unpackDir);
            Assert.True(File.Exists(Path.Combine(unpackDir, "agent.json")));
            Assert.True(File.Exists(Path.Combine(unpackDir, "agent.lock")));
            Assert.True(File.Exists(Path.Combine(unpackDir, "main.agt")));
            Assert.True(Directory.Exists(Path.Combine(unpackDir, "agents")));

            var manifest = ProjectManager.LoadManifest(unpackDir);
            Assert.Equal("DemoProject", manifest.Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            if (Directory.Exists(unpackDir))
                Directory.Delete(unpackDir, true);
        }
    }
}

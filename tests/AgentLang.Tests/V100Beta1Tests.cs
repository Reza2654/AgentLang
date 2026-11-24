using System.IO;
using System.Text.Json;
using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Parser;
using AgentLang.VM;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Formatting;
using AgentLang.VM.Lsp;
using Xunit;

namespace AgentLang.Tests;

public class V100Beta1Tests
{
    private static async Task<string?> ReadResponseAsync(MemoryStream streamOut)
    {
        streamOut.Position = 0;
        using var clientTransport = new LspTransport(streamOut, Stream.Null, leaveOpen: true);
        return await clientTransport.ReadMessageAsync();
    }

    [Fact]
    public void BytecodeDisassembler_FormatsConstantsSymbolsAndInstructionsCorrectly()
    {
        // 1. Arrange a program with declarations and main logic
        string source = """
            agent Analyst {
                model: "gpt-4o-mini"
                role: "Senior Code Reviewer"
            }

            main {
                val = 42 + 8
                print(val)
            }
            """;

        var sourceText = new SourceText(source, "test_disasm.agt");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var compiler = new BytecodeCompiler();
        var module = compiler.Compile(program, "TestDisasm");

        // 2. Act: disassemble module
        string disassembly = module.Disassemble();

        // 3. Assert
        Assert.Contains("AGENTLANG BYTECODE DISASSEMBLY (.agtc v1)", disassembly);
        Assert.Contains("Module: TestDisasm", disassembly);
        Assert.Contains("CONSTANT POOL", disassembly);
        Assert.Contains("SYMBOL TABLE", disassembly);
        Assert.Contains("INSTRUCTION STREAM", disassembly);
        Assert.Contains("PushConst", disassembly);
        Assert.Contains("Halt", disassembly);
        Assert.Contains("Analyst", disassembly);
    }

    [Fact]
    public void BytecodeDisassembler_RoundtripFromFile_MaintainsIntegrity()
    {
        var module = new AgentBytecodeModule { ModuleName = "RoundtripTest" };
        int c0 = module.AddConstant("hello world");
        int c1 = module.AddConstant(1337L);
        int s0 = module.AddSymbol("result");

        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.PushConst, c0, Line: 1));
        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.PushConst, c1, Line: 2));
        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.StoreVar, s0, Line: 3));
        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.Halt, 0, Line: 4));

        string tempFile = Path.Combine(Path.GetTempPath(), $"agent_rt_{Guid.NewGuid():N}.agtc");
        try
        {
            module.SaveToFile(tempFile);
            Assert.True(File.Exists(tempFile));

            var reloaded = AgentBytecodeModule.LoadFromFile(tempFile);
            Assert.Equal("RoundtripTest", reloaded.ModuleName);
            Assert.Equal(2, reloaded.ConstantPool.Count);
            Assert.Single(reloaded.SymbolTable);
            Assert.Equal(4, reloaded.Instructions.Count);

            string disasm = reloaded.Disassemble();
            Assert.Contains("RoundtripTest", disasm);
            Assert.Contains("hello world", disasm);
            Assert.Contains("1337", disasm);
            Assert.Contains("result", disasm);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task BytecodeExecutor_ExecutionMetrics_TracksOpcodesAndPeakStackDepth()
    {
        string source = """
            main {
                x = 10
                y = 20
                sum = x + y * 2
                print(sum)
            }
            """;

        var sourceText = new SourceText(source, "test_metrics.agt");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var compiler = new BytecodeCompiler();
        var module = compiler.Compile(program, "MetricsTest");

        var vm = new AgentVirtualMachine();
        var metrics = new BytecodeExecutionMetrics();

        var result = await vm.ExecuteBytecodeAsync(module, metrics);

        Assert.True(metrics.InstructionsExecuted > 0, "InstructionsExecuted should be positive.");
        Assert.True(metrics.PeakStackDepth >= 1, "PeakStackDepth should be at least 1.");
        Assert.True(metrics.OpCodeCounts.ContainsKey(AgentOpCode.PushConst));
        Assert.True(metrics.OpCodeCounts.ContainsKey(AgentOpCode.StoreVar));
        Assert.True(metrics.OpCodeCounts.ContainsKey(AgentOpCode.LoadVar));
        Assert.True(metrics.OpCodeCounts.ContainsKey(AgentOpCode.Halt));
    }

    [Fact]
    public void AgentCodeFormatter_NormalizesIndentationAndSpacing()
    {
        string unformatted = """
        agent Reviewer{
        model:"gpt-4o"
        role:"Security Expert"
        }
        main{
        x=10,y=20
        if(x<y){
        print("x is smaller")
        }
        }
        """;

        string formatted = AgentCodeFormatter.Format(unformatted);

        // Check indentation of inner lines
        Assert.Contains("    model: \"gpt-4o\"", formatted);
        Assert.Contains("    role: \"Security Expert\"", formatted);
        Assert.Contains("        print(\"x is smaller\")", formatted);
        // Check clean closing brace
        Assert.Contains("}", formatted);
        // Check newline at end
        Assert.EndsWith(Environment.NewLine, formatted);
    }

    [Fact]
    public async Task LspServer_FormattingHandler_ReturnsEditsUsingSharedFormatter()
    {
        using var streamIn = new MemoryStream();
        using var streamOut = new MemoryStream();
        using var transport = new LspTransport(streamIn, streamOut, leaveOpen: true);
        var server = new AgentLspServer(transport);

        string docUri = "file:///workspace/format_test.agt";
        string unformatted = "schema BadIndent {\nfieldA: string,\n    fieldB: int\n}";
        server.Documents.UpdateDocument(docUri, unformatted);

        var formatReq = new
        {
            jsonrpc = "2.0",
            id = 202,
            method = "textDocument/formatting",
            @params = new
            {
                textDocument = new { uri = docUri },
                options = new { tabSize = 4, insertSpaces = true }
            }
        };

        await server.HandleMessageAsync(JsonSerializer.Serialize(formatReq));

        string? respJson = await ReadResponseAsync(streamOut);
        Assert.NotNull(respJson);

        using var doc = JsonDocument.Parse(respJson);
        var edits = doc.RootElement.GetProperty("result");
        Assert.True(edits.GetArrayLength() > 0);

        string newText = edits[0].GetProperty("newText").GetString() ?? "";
        Assert.Contains("    fieldA: string,", newText);
        Assert.Contains("    fieldB: int", newText);
        Assert.EndsWith("}", newText.TrimEnd());
    }

    [Fact]
    public void Program_Version_Matches_V100BetaSeries()
    {
        Assert.StartsWith("1.0.0-beta", Program.Version);
    }
}

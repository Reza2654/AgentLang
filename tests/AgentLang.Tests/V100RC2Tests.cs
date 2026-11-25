using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Errors;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Stdlib;
using AgentLang.VM;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Lsp;
using AgentLang.VM.Server;
using Xunit;

namespace AgentLang.Tests;

public class V100RC2Tests
{
    [Fact]
    public async Task Cli_Run_WithDlpFlag_RedactsSensitiveTokensFromOutput()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent_dlp_{Guid.NewGuid():N}.agt");
        string outFile = Path.Combine(Path.GetTempPath(), $"agent_dlp_nested_{Guid.NewGuid():N}", "out.txt");
        string fakeKey = "sk-proj1234567890abcdefghijklmnop";

        await File.WriteAllTextAsync(tempFile, $"main {{\n    print(\"Secret: {fakeKey}\")\n}}");

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            int code = await Program.Main(["run", tempFile, "--dlp", "-o", outFile]);
            Assert.Equal(0, code);
        }
        finally
        {
            Console.SetOut(originalOut);
            try { File.Delete(tempFile); } catch { }
        }

        try
        {
            string stdout = sw.ToString();
            Assert.DoesNotContain(fakeKey, stdout);
            Assert.Contains("[REDACTED:", stdout);

            Assert.True(File.Exists(outFile));
            string saved = await File.ReadAllTextAsync(outFile);
            Assert.DoesNotContain(fakeKey, saved);
            Assert.Contains("[REDACTED:", saved);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(outFile)!, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ModuloByZero_ThrowsStructuredRuntimeException_InAstAndBytecodeVm()
    {
        // 1. AST Runtime
        var runtime = new AgentLangRuntime();
        var parser = new Parser.Parser(new SourceText("main { x = 10 % 0 }"));
        var program = parser.ParseProgram();
        await Assert.ThrowsAsync<AgentLangRuntimeException>(() => runtime.ExecuteProgramAsync(program));

        // 2. Bytecode VM
        var compiler = new BytecodeCompiler();
        var module = compiler.Compile(program, "mod_zero");
        var vm = new AgentVirtualMachine();
        await Assert.ThrowsAsync<AgentLangRuntimeException>(() => vm.ExecuteBytecodeAsync(module));
    }

    [Fact]
    public async Task AstInterpreter_IsTruthy_TreatsZeroLongAndEmptyListAsFalsy_MatchingBytecodeVm()
    {
        var astOut = new StringWriter();
        var astRuntime = new AgentLangRuntime(output: astOut);
        astRuntime.GlobalScope.SetLocal("zeroLong", 0L);
        var code = """
            main {
                zero = json.parse("0")
                empty = collections.slice([1, 2], 5, 1)
                if zero {
                    print("ZERO_TRUTHY")
                }
                if zeroLong {
                    print("ZEROLONG_TRUTHY")
                }
                if empty {
                    print("EMPTY_TRUTHY")
                }
                print("DONE")
            }
            """;
        var program = new Parser.Parser(new SourceText(code)).ParseProgram();
        await astRuntime.ExecuteProgramAsync(program);

        Assert.DoesNotContain("ZERO_TRUTHY", astOut.ToString());
        Assert.DoesNotContain("ZEROLONG_TRUTHY", astOut.ToString());
        Assert.DoesNotContain("EMPTY_TRUTHY", astOut.ToString());
        Assert.Contains("DONE", astOut.ToString());
    }

    [Fact]
    public void StandardLibrary_EdgeCases_HandleEmptyReplaceInvalidRegexBadJsonAndRoundDecimals()
    {
        // 1. text.replace with empty target string should not crash with ArgumentException
        Assert.True(StandardLibrary.TryInvoke("text", "replace", ["hello", "", "x"], out var replaced));
        Assert.Equal("hello", replaced);

        // 2. text.matches with malformed regex throws structured AgentLangRuntimeException
        Assert.Throws<AgentLangRuntimeException>(() =>
            StandardLibrary.TryInvoke("text", "matches", ["abc", "[unclosed"], out _));

        // 3. json.parse with malformed JSON throws structured AgentLangRuntimeException
        Assert.Throws<AgentLangRuntimeException>(() =>
            StandardLibrary.TryInvoke("json", "parse", ["{bad json"], out _));

        // 4. math.round with out-of-range decimals does not crash with ArgumentOutOfRangeException
        Assert.True(StandardLibrary.TryInvoke("math", "round", [3.14159, -2], out var roundedNeg));
        Assert.Equal(3.0, Convert.ToDouble(roundedNeg));
        Assert.True(StandardLibrary.TryInvoke("math", "round", [3.14159, 99], out var roundedHigh));
        Assert.Equal(3.14159, Convert.ToDouble(roundedHigh));

        // 5. json.stringify handles NaN/Infinity and nested non-generic dictionaries without crashing
        var ht = new System.Collections.Hashtable { ["nan"] = double.NaN, ["ok"] = 42 };
        Assert.True(StandardLibrary.TryInvoke("json", "stringify", [ht], out var jsonStr));
        Assert.Contains("\"ok\":42", jsonStr?.ToString());
    }

    [Fact]
    public async Task BytecodeVm_EnforcesMaxInstructions_AndRejectsCorruptBytecodeCleanly()
    {
        // 1. Bytecode infinite loop hits VmCycleLimitExceededException when MaxInstructions is configured
        var program = new Parser.Parser(new SourceText("main { x = 0\n while true { x = x + 1 } }")).ParseProgram();
        var module = new BytecodeCompiler().Compile(program, "inf_loop");
        var vm = new AgentVirtualMachine(new VmOptions { MaxInstructions = 25 });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<VmCycleLimitExceededException>(() => vm.ExecuteBytecodeAsync(module, cts.Token));

        // 2. Corrupt Jump operand throws structured AgentLangRuntimeException instead of IndexOutOfRange
        var badJumpModule = new AgentBytecodeModule { ModuleName = "bad_jump" };
        badJumpModule.Instructions.Add(new BytecodeInstruction(AgentOpCode.Jump, -5, 1));
        await Assert.ThrowsAsync<AgentLangRuntimeException>(() => new AgentVirtualMachine().ExecuteBytecodeAsync(badJumpModule));

        // 3. Corrupt opcode byte throws structured AgentLangRuntimeException instead of silent no-op
        var badOpModule = new AgentBytecodeModule { ModuleName = "bad_op" };
        badOpModule.Instructions.Add(new BytecodeInstruction((AgentOpCode)250, 0, 1));
        await Assert.ThrowsAsync<AgentLangRuntimeException>(() => new AgentVirtualMachine().ExecuteBytecodeAsync(badOpModule));

        // 4. Negative counts in .agtc binary throw InvalidDataException cleanly
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(AgentBytecodeModule.Magic);
            bw.Write(AgentBytecodeModule.FormatVersion);
            bw.Write("Corrupt");
            bw.Write(-10); // negative constant pool count
        }
        Assert.Throws<InvalidDataException>(() => AgentBytecodeModule.FromBytes(ms.ToArray()));
    }

    [Fact]
    public async Task HttpServer_InvalidJsonReturns400_AndStreamSyntaxErrorEmitsSseError()
    {
        int port;
        using (var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        var vm = new AgentVirtualMachine();
        using var server = new AgentHttpServer(port, vm);
        await server.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
        try
        {
            // 1. Malformed JSON on /api/v1/run -> 400 BadRequest (not 500!)
            var runBad = await client.PostAsync(
                "/api/v1/run",
                new StringContent("{not valid json", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, runBad.StatusCode);

            // 2. Malformed JSON on /api/v1/resume -> 400 BadRequest (not 500!)
            var resumeBad = await client.PostAsync(
                "/api/v1/resume",
                new StringContent("{not valid json", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, resumeBad.StatusCode);

            // 3. Syntax error on /api/v1/stream -> emits SSE "error" event (not finish: completed!)
            var streamBad = await client.PostAsync(
                "/api/v1/stream",
                new StringContent("""{"code":"main { = 123 }"}""", Encoding.UTF8, "application/json"));
            string sseText = await streamBad.Content.ReadAsStringAsync();
            Assert.Contains("\"type\":\"error\"", sseText.Replace(" ", ""));
            Assert.DoesNotContain("\"status\":\"completed\"", sseText.Replace(" ", ""));
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task LspServer_MalformedJsonAndMissingParams_RecoversAndContinuesServing()
    {
        var inputStream = new MemoryStream();
        var outputStream = new MemoryStream();

        static void WriteLspFrame(Stream s, string payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            s.Write(header);
            s.Write(body);
        }

        // Frame 1: Completely broken JSON -> server must survive!
        WriteLspFrame(inputStream, "{broken json");
        // Frame 2: Request missing required 'position.line' fields -> returns JSON-RPC error instead of crashing
        WriteLspFrame(inputStream, """{"jsonrpc":"2.0","id":7,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///a.agt"},"position":{}}}""");
        // Frame 3: Valid initialize request AFTER failures -> proves server recovered and stayed alive!
        WriteLspFrame(inputStream, """{"jsonrpc":"2.0","id":8,"method":"initialize"}""");
        // Frame 4: Exit
        WriteLspFrame(inputStream, """{"jsonrpc":"2.0","method":"exit"}""");
        inputStream.Position = 0;

        using var transport = new LspTransport(inputStream, outputStream, leaveOpen: true);
        var server = new AgentLspServer(transport);
        await server.RunAsync();

        string outputText = Encoding.UTF8.GetString(outputStream.ToArray());
        Assert.Contains("\"id\":8", outputText);
        Assert.Contains("agentlang-lsp", outputText);
        Assert.Contains("\"version\":\"1.0.0-rc.2\"", outputText);
    }

    [Fact]
    public void VersionAlignment_AcrossAllComponents_Is100Rc2()
    {
        Assert.Equal("1.0.0-rc.2", Program.Version);
        Assert.Equal("1.0.0-rc.2", AgentHttpServer.ServerVersion);
    }
}

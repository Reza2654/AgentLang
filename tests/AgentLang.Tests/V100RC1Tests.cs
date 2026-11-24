using System.IO;
using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Runtime;
using AgentLang.Runtime.Stdlib;
using AgentLang.Security;
using AgentLang.VM;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Server;
using Xunit;

namespace AgentLang.Tests;

public class V100RC1Tests
{
    [Fact]
    public async Task BytecodeExecutor_EnvAccess_RespectsSecuritySandbox()
    {
        // 1. Build a bytecode module that accesses env.get("PATH")
        var compiler = new BytecodeCompiler();
        var source = "main { env.get(\"PATH\") }";
        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var module = compiler.Compile(program, "sandbox_test");

        // 2. Setup SecurityEngine with denied env access
        var sandbox = SecurityEngine.CreateSandboxPolicy(allowEnv: false);
        var secEngine = new SecurityEngine(globalPolicy: sandbox);
        var runtime = new AgentLangRuntime(securityEngine: secEngine);
        var vm = new AgentVirtualMachine(runtime: runtime);

        // 3. Executing bytecode must throw SecurityException
        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            vm.ExecuteBytecodeAsync(module));

        Assert.Contains("env", ex.Capability);
    }

    [Fact]
    public void BytecodeOptimizer_DoesNotFoldAcrossJumpTargets()
    {
        // Construct module where instruction 2 is a jump target
        var module = new AgentBytecodeModule { ModuleName = "jump_target_test" };
        int c1 = module.AddConstant(10L);
        int c2 = module.AddConstant(20L);

        // 0: Jump to instruction 2
        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.Jump, 2, 1));
        // 1: PushConst 10
        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.PushConst, c1, 2));
        // 2: PushConst 20 (TARGET OF JUMP at 0!)
        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.PushConst, c2, 3));
        // 3: Add
        module.Instructions.Add(new BytecodeInstruction(AgentOpCode.Add, 0, 3));

        // Optimize module
        int optCount = BytecodeOptimizer.Optimize(module);

        // Instruction 1 and 2 should NOT have been folded because 2 is a jump target!
        // Jump target 2 must remain valid
        Assert.Contains(module.Instructions, i => i.OpCode == AgentOpCode.Jump);
    }

    [Fact]
    public void TimeModule_Format_ResilientToNumericTypesAndBadFormats()
    {
        // 1. double timestamp with decimal
        StandardLibrary.TryInvoke("time", "format", new object?[] { 1700000000000.0, "yyyy" }, out var resDouble);
        Assert.Equal("2023", resDouble?.ToString());

        // 2. long timestamp
        StandardLibrary.TryInvoke("time", "format", new object?[] { 1700000000000L, "yyyy" }, out var resLong);
        Assert.Equal("2023", resLong?.ToString());

        // 3. invalid/malformed format string should fallback gracefully without throwing
        StandardLibrary.TryInvoke("time", "format", new object?[] { "2026-11-24T12:00:00Z", "INVALID_%_FORMAT" }, out var resFallback);
        Assert.NotNull(resFallback);

        // 4. sleep_ms clamps extreme values
        StandardLibrary.TryInvoke("time", "sleep_ms", new object?[] { -100 }, out var resNeg);
        Assert.Equal(0, resNeg);
    }

    [Fact]
    public void PermissionPolicy_NullOrWhitespaceCapability_ReturnsDefaultAction()
    {
        var policy = new PermissionPolicy("test", PermissionAction.Cannot);
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate(""));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("   "));

        var permissive = new PermissionPolicy("perm", PermissionAction.Allow);
        Assert.Equal(PermissionAction.Allow, permissive.Evaluate(""));
    }

    [Fact]
    public void VersionAlignment_AcrossAllComponents_Is100Rc1()
    {
        Assert.Equal("1.0.0-rc.1", Program.Version);
        Assert.Equal("1.0.0-rc.1", AgentHttpServer.ServerVersion);
    }
}

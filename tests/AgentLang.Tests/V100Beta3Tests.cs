using System.IO;
using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Runtime.Stdlib;
using AgentLang.Security;
using AgentLang.VM.Server;
using Xunit;

namespace AgentLang.Tests;

public class V100Beta3Tests
{
    [Fact]
    public void SecurityEngine_SandboxPolicy_AllowAndDeny_EvaluatesCorrectly()
    {
        // 1. Permissive sandbox with explicit deny on net
        var policy = SecurityEngine.CreateSandboxPolicy(
            strict: false,
            allowAll: false,
            allowNet: false,
            allowFs: true,
            allowEnv: false,
            allowTerminal: false);

        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("http.get"));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("http.post"));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("browser.search"));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("env.read"));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("terminal.run"));

        // Filesystem explicitly allowed
        Assert.Equal(PermissionAction.Allow, policy.Evaluate("filesystem.read"));
        Assert.Equal(PermissionAction.Allow, policy.Evaluate("filesystem.write"));

        // Fallback in permissive mode allows other safe actions
        Assert.Equal(PermissionAction.Allow, policy.Evaluate("custom.action"));
    }

    [Fact]
    public void SecurityEngine_StrictSandbox_BlocksUnpermittedCapabilities()
    {
        // Strict sandbox with only filesystem allowed
        var policy = SecurityEngine.CreateSandboxPolicy(
            strict: true,
            allowAll: false,
            allowFs: true);

        Assert.Equal(PermissionAction.Allow, policy.Evaluate("filesystem.read"));
        Assert.Equal(PermissionAction.Allow, policy.Evaluate("filesystem.exists"));
        Assert.Equal(PermissionAction.Allow, policy.Evaluate("calculator.eval")); // Safe built-in

        // Unpermitted network, env, and terminal blocked by default Cannot
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("http.get"));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("env.read"));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("terminal.run"));
        Assert.Equal(PermissionAction.Cannot, policy.Evaluate("arbitrary.capability"));
    }

    [Fact]
    public async Task SecurityEngine_GlobalPolicy_ThrowsOnForbiddenOperation()
    {
        var sandbox = SecurityEngine.CreateSandboxPolicy(allowNet: false);
        var secEngine = new SecurityEngine(globalPolicy: sandbox);

        // Filesystem allowed
        await secEngine.AuthorizeAsync("testAgent", null, "filesystem.read", "path=/tmp/test");

        // Net forbidden by sandbox
        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            secEngine.AuthorizeAsync("testAgent", null, "http.get", "url=https://example.com"));

        Assert.Equal("http.get", ex.Capability);
        Assert.Contains("sandbox policy forbade", ex.Message);
    }

    [Fact]
    public void StdLib_TimeModule_FormatAndSleepAndIso_ExecuteCorrectly()
    {
        // 1. time.format
        StandardLibrary.TryInvoke("time", "format", new object?[] { "2026-11-24T12:00:00Z", "yyyy-MM-dd" }, out var fmtRes);
        Assert.Equal("2026-11-24", fmtRes?.ToString());

        // 2. time.format timestamp ms
        StandardLibrary.TryInvoke("time", "format", new object?[] { 1700000000000L, "yyyy" }, out var fmtYear);
        Assert.Equal("2023", fmtYear?.ToString());

        // 3. time.iso
        StandardLibrary.TryInvoke("time", "iso", Array.Empty<object?>(), out var isoRes);
        Assert.NotNull(isoRes);
        Assert.True(DateTime.TryParse(isoRes?.ToString(), out _));

        // 4. time.sleep_ms
        StandardLibrary.TryInvoke("time", "sleep_ms", new object?[] { 5 }, out var sleepRes);
        Assert.Equal(5, sleepRes);

        // 5. Global aliases
        StandardLibrary.TryInvoke("time_format", null, new object?[] { "2026-01-15T00:00:00Z", "yyyy/MM" }, out var gFmt);
        Assert.Equal("2026/01", gFmt?.ToString());

        StandardLibrary.TryInvoke("time_iso", null, Array.Empty<object?>(), out var gIso);
        Assert.NotNull(gIso);

        StandardLibrary.TryInvoke("sleep_ms", null, new object?[] { 2 }, out var gSleep);
        Assert.Equal(2, gSleep);
    }

    [Fact]
    public void StdLib_MathModule_PowAndSqrt_ExecuteCorrectly()
    {
        // 1. math.pow
        StandardLibrary.TryInvoke("math", "pow", new object?[] { 2, 3 }, out var powRes);
        Assert.Equal(8.0, Convert.ToDouble(powRes));

        // 2. math.sqrt
        StandardLibrary.TryInvoke("math", "sqrt", new object?[] { 25 }, out var sqrtRes);
        Assert.Equal(5.0, Convert.ToDouble(sqrtRes));

        // 3. Global aliases
        StandardLibrary.TryInvoke("pow", null, new object?[] { 3, 2 }, out var gPow);
        Assert.Equal(9.0, Convert.ToDouble(gPow));

        StandardLibrary.TryInvoke("sqrt", null, new object?[] { 49 }, out var gSqrt);
        Assert.Equal(7.0, Convert.ToDouble(gSqrt));
    }

    [Fact]
    public void VersionAlignment_AcrossAllComponents_Is100Beta3()
    {
        Assert.Equal("1.0.0-beta.3", Program.Version);
        Assert.Equal("1.0.0-beta.3", AgentHttpServer.ServerVersion);
    }
}

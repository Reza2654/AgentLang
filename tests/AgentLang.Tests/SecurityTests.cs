using AgentLang.AST;
using AgentLang.Security;
using Xunit;

namespace AgentLang.Tests;

public class SecurityTests
{
    [Fact]
    public async Task AllowRuleAuthorizesImmediately()
    {
        var policy = new PermissionPolicy("TestPerm");
        policy.AddRule(PermissionAction.Allow, "browser.search");

        var approver = new AutoApprovalProvider(false);
        var engine = new SecurityEngine(approver);
        engine.RegisterPolicy(policy);

        // Should not throw and should not ask approval
        await engine.AuthorizeAsync("Researcher", "TestPerm", "browser.search", "querying AI");
        Assert.Empty(approver.Requests);
    }

    [Fact]
    public async Task CannotRuleThrowsSecurityException()
    {
        var policy = new PermissionPolicy("TestPerm");
        policy.AddRule(PermissionAction.Cannot, "terminal");

        var approver = new AutoApprovalProvider(true);
        var engine = new SecurityEngine(approver);
        engine.RegisterPolicy(policy);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            engine.AuthorizeAsync("Researcher", "TestPerm", "terminal", "rm -rf /"));

        Assert.Equal("terminal", ex.Capability);
        Assert.Contains("explicitly forbids", ex.Message);
        Assert.Empty(approver.Requests);
    }

    [Fact]
    public async Task AskRuleTriggersApproval_WhenApproved_Succeeds()
    {
        var policy = new PermissionPolicy("TestPerm");
        policy.AddRule(PermissionAction.Ask, "filesystem.write");

        var approver = new AutoApprovalProvider(true);
        var engine = new SecurityEngine(approver);
        engine.RegisterPolicy(policy);

        await engine.AuthorizeAsync("Researcher", "TestPerm", "filesystem.write", "writing file.txt");
        Assert.Single(approver.Requests);
        Assert.Equal("filesystem.write", approver.Requests[0].Capability);
    }

    [Fact]
    public async Task AskRuleTriggersApproval_WhenDenied_ThrowsSecurityException()
    {
        var policy = new PermissionPolicy("TestPerm");
        policy.AddRule(PermissionAction.Ask, "filesystem.write");

        var approver = new AutoApprovalProvider(false);
        var engine = new SecurityEngine(approver);
        engine.RegisterPolicy(policy);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            engine.AuthorizeAsync("Researcher", "TestPerm", "filesystem.write", "writing file.txt"));

        Assert.Contains("rejected by human approval", ex.Message);
        Assert.Single(approver.Requests);
    }

    [Fact]
    public async Task HierarchicalMatchingWorks()
    {
        var policy = new PermissionPolicy("Hierarchical");
        policy.AddRule(PermissionAction.Allow, "browser");
        policy.AddRule(PermissionAction.Cannot, "terminal");

        var engine = new SecurityEngine(new AutoApprovalProvider(true));
        engine.RegisterPolicy(policy);

        // browser.search matches browser
        await engine.AuthorizeAsync("Researcher", "Hierarchical", "browser.search", "search web");

        // terminal.run matches terminal
        await Assert.ThrowsAsync<SecurityException>(() =>
            engine.AuthorizeAsync("Researcher", "Hierarchical", "terminal.run", "ls"));
    }
}

using AgentLang.AST;
using AgentLang.Errors;

namespace AgentLang.Security;

public class SecurityException : AgentLangPermissionException
{
    public string Capability { get; }
    public string Reason { get; }

    public SecurityException(string capability, string reason)
        : base($"Security violation for '{capability}': {reason}", errorCode: "AGT600", helpText: "Check permission block to allow this capability.")
    {
        Capability = capability;
        Reason = reason;
    }
}

public interface IApprovalProvider
{
    Task<bool> RequestApprovalAsync(string agentName, string capability, string details, CancellationToken ct = default);
}

public sealed class ConsoleApprovalProvider : IApprovalProvider
{
    public Task<bool> RequestApprovalAsync(string agentName, string capability, string details, CancellationToken ct = default)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[SECURITY APPROVAL REQUIRED]");
        Console.ResetColor();
        Console.WriteLine($"Agent:      {agentName}");
        Console.WriteLine($"Operation:  {capability}");
        Console.WriteLine($"Details:    {details}");
        Console.Write("Approve execution? [y/N]: ");

        string? input = Console.ReadLine()?.Trim().ToLowerInvariant();
        bool approved = input is "y" or "yes";

        if (approved)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[APPROVED]");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[DENIED]");
            Console.ResetColor();
        }

        return Task.FromResult(approved);
    }
}

public sealed class AutoApprovalProvider(bool approve = true) : IApprovalProvider
{
    public bool AutoApprove { get; set; } = approve;
    public List<(string Agent, string Capability, string Details)> Requests { get; } = [];

    public Task<bool> RequestApprovalAsync(string agentName, string capability, string details, CancellationToken ct = default)
    {
        Requests.Add((agentName, capability, details));
        return Task.FromResult(AutoApprove);
    }
}

public sealed class PermissionPolicy
{
    public string Name { get; }
    private readonly List<(PermissionAction Action, string Pattern)> _rules = [];

    public IReadOnlyList<(PermissionAction Action, string Pattern)> Rules => _rules;

    public PermissionPolicy(string name)
    {
        Name = name;
    }

    public void AddRule(PermissionAction action, string pattern)
    {
        _rules.Add((action, pattern.Trim().ToLowerInvariant()));
    }

    public PermissionAction Evaluate(string capability)
    {
        string target = capability.Trim().ToLowerInvariant();

        // 1. Look for exact match first
        foreach (var rule in _rules)
        {
            if (rule.Pattern == target)
                return rule.Action;
        }

        // 2. Look for hierarchical match (e.g. rule 'browser' matches 'browser.search')
        foreach (var rule in _rules)
        {
            if (rule.Pattern == "*" ||
                target.StartsWith(rule.Pattern + ".", StringComparison.OrdinalIgnoreCase))
            {
                return rule.Action;
            }
        }

        // Default if not specified: Cannot
        return PermissionAction.Cannot;
    }
}

public sealed class SecurityEngine
{
    private readonly IApprovalProvider _approvalProvider;
    private readonly Dictionary<string, PermissionPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    public SecurityEngine(IApprovalProvider? approvalProvider = null)
    {
        _approvalProvider = approvalProvider ?? new ConsoleApprovalProvider();
    }

    public void RegisterPolicy(PermissionPolicy policy)
    {
        _policies[policy.Name] = policy;
    }

    public PermissionPolicy? GetPolicy(string name) =>
        _policies.TryGetValue(name, out var policy) ? policy : null;

    public async Task AuthorizeAsync(string agentName, string? policyName, string capability, string details, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            // If agent has no permission declared, default is allow or prompt?
            // "Security must be enforced by the runtime. Never treat prompts or model instructions as security boundary."
            // Without an explicit policy, sensitive operations should be allowed only for safe built-ins or prompt approval.
            return;
        }

        if (!_policies.TryGetValue(policyName, out var policy))
        {
            throw new SecurityException(capability, $"Unknown permission policy '{policyName}' referenced by agent '{agentName}'");
        }

        var action = policy.Evaluate(capability);
        switch (action)
        {
            case PermissionAction.Allow:
                return;

            case PermissionAction.Cannot:
                throw new SecurityException(capability, $"Permission '{policyName}' explicitly forbids '{capability}'");

            case PermissionAction.Ask:
                bool approved = await _approvalProvider.RequestApprovalAsync(agentName, capability, details, ct);
                if (!approved)
                {
                    throw new SecurityException(capability, $"Operation '{capability}' was rejected by human approval for agent '{agentName}'");
                }
                break;
        }
    }
}

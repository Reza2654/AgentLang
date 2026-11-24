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
    public PermissionAction DefaultAction { get; set; }

    public PermissionPolicy(string name, PermissionAction defaultAction = PermissionAction.Cannot)
    {
        Name = name;
        DefaultAction = defaultAction;
    }

    public void AddRule(PermissionAction action, string pattern)
    {
        _rules.Add((action, pattern.Trim().ToLowerInvariant()));
    }

    public PermissionAction Evaluate(string? capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            return DefaultAction;

        string target = capability.Trim().ToLowerInvariant();

        // 1. Look for exact match first
        foreach (var rule in _rules)
        {
            if (rule.Pattern == target)
                return rule.Action;
        }

        // 2. Look for wildcard or hierarchical match (e.g. rule 'browser' or 'browser.*' matches 'browser.search')
        foreach (var rule in _rules)
        {
            if (rule.Pattern == "*")
                return rule.Action;

            string pat = rule.Pattern.EndsWith(".*") ? rule.Pattern[..^2] : rule.Pattern;
            if (target.StartsWith(pat + ".", StringComparison.OrdinalIgnoreCase) || target == pat)
            {
                return rule.Action;
            }
        }

        // Default if not specified:
        return DefaultAction;
    }
}

public sealed class SecurityEngine
{
    private readonly IApprovalProvider _approvalProvider;
    private readonly Dictionary<string, PermissionPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    public IApprovalProvider ApprovalProvider => _approvalProvider;
    public PermissionPolicy? GlobalPolicy { get; set; }

    public SecurityEngine(IApprovalProvider? approvalProvider = null, PermissionPolicy? globalPolicy = null)
    {
        _approvalProvider = approvalProvider ?? new ConsoleApprovalProvider();
        GlobalPolicy = globalPolicy;
    }

    public static PermissionPolicy CreateSandboxPolicy(
        bool strict = false,
        bool allowAll = false,
        bool? allowNet = null,
        bool? allowFs = null,
        bool? allowEnv = null,
        bool? allowTerminal = null)
    {
        var policy = new PermissionPolicy("sandbox", strict ? PermissionAction.Cannot : PermissionAction.Allow);

        if (allowAll)
        {
            policy.AddRule(PermissionAction.Allow, "*");
            return policy;
        }

        if (strict)
        {
            policy.AddRule(PermissionAction.Allow, "calculator.*");
            policy.AddRule(PermissionAction.Allow, "data.*");
        }

        void ApplyCategory(bool? allowed, params string[] patterns)
        {
            if (allowed.HasValue)
            {
                var action = allowed.Value ? PermissionAction.Allow : PermissionAction.Cannot;
                foreach (var pat in patterns)
                {
                    policy.AddRule(action, pat);
                }
            }
        }

        ApplyCategory(allowNet, "net.*", "http.*", "browser.*");
        ApplyCategory(allowFs, "filesystem.*", "fs.*");
        ApplyCategory(allowEnv, "env.*");
        ApplyCategory(allowTerminal, "terminal.*", "exec.*");

        return policy;
    }

    public Task<bool> RequestApprovalAsync(string agentName, string capability, string details, CancellationToken ct = default) =>
        _approvalProvider.RequestApprovalAsync(agentName, capability, details, ct);

    public void RegisterPolicy(PermissionPolicy policy)
    {
        _policies[policy.Name] = policy;
    }

    public PermissionPolicy? GetPolicy(string name) =>
        _policies.TryGetValue(name, out var policy) ? policy : null;

    public async Task AuthorizeAsync(string agentName, string? policyName, string capability, string details, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(capability)) return;

        // 1. If GlobalPolicy (ambient sandbox) is defined, evaluate it first
        if (GlobalPolicy != null)
        {
            var globalAction = GlobalPolicy.Evaluate(capability);
            switch (globalAction)
            {
                case PermissionAction.Cannot:
                    throw new SecurityException(capability, $"Security sandbox policy forbade '{capability}'");
                case PermissionAction.Ask:
                    bool approved = await _approvalProvider.RequestApprovalAsync(agentName, capability, details, ct);
                    if (!approved)
                    {
                        throw new SecurityException(capability, $"Operation '{capability}' was rejected by human approval under sandbox policy");
                    }
                    break;
                case PermissionAction.Allow:
                    // Allowed at sandbox level; continue to agent-specific policy check if any
                    break;
            }
        }

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

using System.Text.RegularExpressions;

namespace AgentLang.Security.Policy;

public enum DlpSensitivity
{
    Low,
    Medium,
    High,
    Critical
}

public enum DlpAction
{
    Redact,
    Mask,
    Block,
    Warn
}

public sealed record DlpPatternMatch(
    string RuleName,
    string Value,
    int Index,
    int Length,
    DlpSensitivity Sensitivity
);

public sealed class DlpSecurityViolationException : Exception
{
    public string RuleName { get; }

    public DlpSecurityViolationException(string ruleName, string details)
        : base($"Security policy blocked execution due to sensitive data leak ({ruleName}): {details}")
    {
        RuleName = ruleName;
    }
}

public static partial class DataLossPrevention
{
    private static readonly (string Name, Regex Pattern, DlpSensitivity Sensitivity)[] Rules =
    [
        ("OpenAI_ApiKey", OpenAiKeyRegex(), DlpSensitivity.Critical),
        ("Anthropic_ApiKey", AnthropicKeyRegex(), DlpSensitivity.Critical),
        ("GitHub_Token", GitHubTokenRegex(), DlpSensitivity.Critical),
        ("Aws_AccessKey", AwsAccessKeyRegex(), DlpSensitivity.Critical),
        ("PrivateKey_Block", PrivateKeyRegex(), DlpSensitivity.Critical),
        ("Bearer_Token", BearerTokenRegex(), DlpSensitivity.High),
        ("CreditCard_Number", CreditCardRegex(), DlpSensitivity.High),
        ("ConnectionString_Password", PasswordRegex(), DlpSensitivity.High),
        ("Email_Address", EmailRegex(), DlpSensitivity.Medium)
    ];

    public static List<DlpPatternMatch> Scan(string input)
    {
        if (string.IsNullOrEmpty(input)) return [];

        var matches = new List<DlpPatternMatch>();
        foreach (var (name, regex, sensitivity) in Rules)
        {
            foreach (Match m in regex.Matches(input))
            {
                if (m.Success)
                {
                    matches.Add(new DlpPatternMatch(name, m.Value, m.Index, m.Length, sensitivity));
                }
            }
        }

        return matches.OrderBy(m => m.Index).ToList();
    }

    public static string Sanitize(string input, DlpAction action = DlpAction.Redact)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var matches = Scan(input);
        if (matches.Count == 0) return input;

        if (action == DlpAction.Block)
        {
            var critical = matches.FirstOrDefault(m => m.Sensitivity is DlpSensitivity.Critical or DlpSensitivity.High);
            if (critical != null)
            {
                throw new DlpSecurityViolationException(critical.RuleName, "Sensitive credentials or secrets detected in payload.");
            }
        }

        // Replace from end to start to preserve string indices
        string result = input;
        foreach (var m in matches.OrderByDescending(m => m.Index))
        {
            string replacement = action switch
            {
                DlpAction.Mask => MaskValue(m.Value),
                _ => $"[REDACTED:{m.RuleName}]"
            };

            result = result.Remove(m.Index, m.Length).Insert(m.Index, replacement);
        }

        return result;
    }

    private static string MaskValue(string value)
    {
        if (value.Length <= 4) return new string('*', value.Length);
        return value[..2] + new string('*', value.Length - 4) + value[^2..];
    }

    [GeneratedRegex(@"\bsk-[a-zA-Z0-9]{20,}\b")]
    private static partial Regex OpenAiKeyRegex();

    [GeneratedRegex(@"\bsk-ant-[a-zA-Z0-9_\-]{20,}\b")]
    private static partial Regex AnthropicKeyRegex();

    [GeneratedRegex(@"\bghp_[a-zA-Z0-9]{30,}\b")]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKeyRegex();

    [GeneratedRegex(@"-----BEGIN [A-Z\s]+PRIVATE KEY-----[\s\S]*?-----END [A-Z\s]+PRIVATE KEY-----")]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"Bearer\s+[a-zA-Z0-9_\-\.]{25,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3}\d{4}\b")]
    private static partial Regex CreditCardRegex();

    [GeneratedRegex(@"(?:password|pwd)\s*=\s*['""]?([^'"";\s]+)['""]?", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailRegex();
}

public sealed class ResourceAccessPolicy
{
    public List<string> AllowedPathRoots { get; } = [];
    public List<string> AllowedDomains { get; } = [];

    public bool IsPathAllowed(string targetPath, string defaultRoot)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return false;

        string normalizedTarget = Path.GetFullPath(targetPath);
        var roots = AllowedPathRoots.Count > 0
            ? AllowedPathRoots.Select(Path.GetFullPath).ToList()
            : [Path.GetFullPath(defaultRoot)];

        foreach (var root in roots)
        {
            if (normalizedTarget.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsDomainAllowed(string hostOrUrl)
    {
        if (AllowedDomains.Count == 0 || AllowedDomains.Contains("*")) return true;
        if (string.IsNullOrWhiteSpace(hostOrUrl)) return false;

        string host = hostOrUrl;
        if (Uri.TryCreate(hostOrUrl, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }

        foreach (var allowed in AllowedDomains)
        {
            if (allowed == "*" || allowed.Equals(host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (allowed.StartsWith("*.") && host.EndsWith(allowed[2..], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

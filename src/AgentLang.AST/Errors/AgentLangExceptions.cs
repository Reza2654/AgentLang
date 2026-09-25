using AgentLang.AST;

namespace AgentLang.Errors;

public class AgentLangException : Exception
{
    public string ErrorCode { get; }
    public SourceSpan Span { get; }
    public string? HelpText { get; }

    public AgentLangException(
        string errorCode,
        string message,
        SourceSpan span = default,
        string? helpText = null,
        Exception? innerException = null)
        : base($"[{errorCode}] {message}", innerException)
    {
        ErrorCode = errorCode;
        Span = span;
        HelpText = helpText;
    }
}

public class AgentLangSyntaxException : AgentLangException
{
    public AgentLangSyntaxException(string message, SourceSpan span = default, string errorCode = "AGT100", string? helpText = null)
        : base(errorCode, message, span, helpText) { }
}

public class AgentLangSemanticException : AgentLangException
{
    public AgentLangSemanticException(string message, SourceSpan span = default, string errorCode = "AGT200", string? helpText = null)
        : base(errorCode, message, span, helpText) { }
}

public class AgentLangRuntimeException : AgentLangException
{
    public AgentLangRuntimeException(string message, SourceSpan span = default, string errorCode = "AGT300", string? helpText = null, Exception? innerException = null)
        : base(errorCode, message, span, helpText, innerException) { }
}

public class AgentLangModelException : AgentLangException
{
    public AgentLangModelException(string message, SourceSpan span = default, string errorCode = "AGT400", string? helpText = null, Exception? innerException = null)
        : base(errorCode, message, span, helpText, innerException) { }
}

public class AgentLangToolException : AgentLangException
{
    public AgentLangToolException(string message, SourceSpan span = default, string errorCode = "AGT500", string? helpText = null, Exception? innerException = null)
        : base(errorCode, message, span, helpText, innerException) { }
}

public class AgentLangPermissionException : AgentLangException
{
    public AgentLangPermissionException(string message, SourceSpan span = default, string errorCode = "AGT600", string? helpText = null)
        : base(errorCode, message, span, helpText) { }
}

public class AgentLangApprovalException : AgentLangPermissionException
{
    public AgentLangApprovalException(string message, SourceSpan span = default, string errorCode = "AGT601", string? helpText = null)
        : base(message, span, errorCode, helpText) { }
}

public class AgentLangNetworkException : AgentLangException
{
    public AgentLangNetworkException(string message, SourceSpan span = default, string errorCode = "AGT700", string? helpText = null, Exception? innerException = null)
        : base(errorCode, message, span, helpText, innerException) { }
}

public class AgentLangTimeoutException : AgentLangException
{
    public AgentLangTimeoutException(string message, SourceSpan span = default, string errorCode = "AGT701", string? helpText = null)
        : base(errorCode, message, span, helpText) { }
}

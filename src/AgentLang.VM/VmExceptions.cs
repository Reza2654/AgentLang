namespace AgentLang.VM;

public class AgentVmException : Exception
{
    public string ErrorCode { get; }

    public AgentVmException(string message, string errorCode = "VM001", Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}

public sealed class VmCycleLimitExceededException : AgentVmException
{
    public long InstructionLimit { get; }
    public long CyclesExecuted { get; }

    public VmCycleLimitExceededException(long limit, long executed)
        : base($"Agent VM instruction step limit exceeded: {executed} cycles executed (limit was {limit}).", "VM101")
    {
        InstructionLimit = limit;
        CyclesExecuted = executed;
    }
}

public sealed class VmSandboxViolationException : AgentVmException
{
    public string Capability { get; }

    public VmSandboxViolationException(string capability)
        : base($"Agent VM sandbox security violation: unauthorized capability '{capability}' requested.", "VM102")
    {
        Capability = capability;
    }
}

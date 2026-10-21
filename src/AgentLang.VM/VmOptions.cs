namespace AgentLang.VM;

public sealed class VmOptions
{
    public long MaxInstructions { get; set; } = 50_000;
    public int MaxCallDepth { get; set; } = 64;
    public bool EnableSandboxing { get; set; } = true;
    public TimeSpan? ExecutionTimeout { get; set; }
    public List<string> AllowedCapabilities { get; set; } = ["*"];

    public VmOptions() { }

    public VmOptions(long maxInstructions, bool enableSandboxing = true)
    {
        MaxInstructions = maxInstructions;
        EnableSandboxing = enableSandboxing;
    }
}

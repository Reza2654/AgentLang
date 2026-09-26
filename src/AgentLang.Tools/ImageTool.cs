namespace AgentLang.Tools;

public sealed class ImageTool : ITool
{
    public string Name => "image";

    public IReadOnlyList<string> SupportedCapabilities { get; } =
    [
        "image",
        "image.generate",
        "image.edit"
    ];

    public Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string prompt = "";
        if (arguments.TryGetValue("prompt", out var pVal)) prompt = pVal?.ToString() ?? "";
        else if (arguments.TryGetValue("input", out var iVal)) prompt = iVal?.ToString() ?? "";
        else if (arguments.TryGetValue("text", out var tVal)) prompt = tVal?.ToString() ?? "";
        else if (arguments.Count > 0) prompt = arguments.Values.FirstOrDefault()?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Task.FromResult(ToolResult.Fail("Prompt is required for image operation"));
        }

        string result = $"[Image: Generated '{prompt}' (1024x1024)]";
        return Task.FromResult(ToolResult.Ok(result));
    }
}

public sealed class VisionTool : ITool
{
    public string Name => "vision";

    public IReadOnlyList<string> SupportedCapabilities { get; } =
    [
        "vision",
        "vision.analyze",
        "vision.describe"
    ];

    public Task<ToolResult> ExecuteAsync(string capability, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        string image = arguments.TryGetValue("image", out var img) ? img?.ToString() ?? "" : "";
        if (string.IsNullOrEmpty(image) && arguments.TryGetValue("path", out var p)) image = p?.ToString() ?? "";

        string prompt = arguments.TryGetValue("prompt", out var pr) ? pr?.ToString() ?? "" : "Describe this image";
        if (string.IsNullOrEmpty(prompt) && arguments.TryGetValue("question", out var q)) prompt = q?.ToString() ?? "Describe this image";

        string analysis = $"[Vision Analysis of '{image}']: {prompt} -> Detected visual elements and objects in image context.";
        return Task.FromResult(ToolResult.Ok(analysis));
    }
}

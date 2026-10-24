using System.Text.Json;
using System.Threading.Channels;

namespace AgentLang.VM.Streaming;

public enum AgentStreamEventType
{
    Token,
    Thought,
    ToolCallStart,
    ToolCallResult,
    NodeTransition,
    StatusChange,
    Error,
    Complete
}

public sealed record AgentStreamEvent(
    AgentStreamEventType Type,
    string Content,
    string? NodeOrAgent = null,
    Dictionary<string, object?>? Metadata = null
)
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public string ToSseString()
    {
        var payload = new
        {
            type = Type.ToString(),
            content = Content,
            nodeOrAgent = NodeOrAgent,
            metadata = Metadata ?? [],
            timestamp = Timestamp.ToString("o")
        };

        var json = JsonSerializer.Serialize(payload);
        return $"event: {Type}\ndata: {json}\n\n";
    }
}

public interface IAgentStreamListener
{
    Task OnEventAsync(AgentStreamEvent streamEvent, CancellationToken ct = default);
}

public sealed class AgentStreamChannel
{
    private readonly Channel<AgentStreamEvent> _channel;

    public AgentStreamChannel(int capacity = 1000)
    {
        _channel = Channel.CreateBounded<AgentStreamEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false
        });
    }

    public ChannelWriter<AgentStreamEvent> Writer => _channel.Writer;
    public ChannelReader<AgentStreamEvent> Reader => _channel.Reader;

    public async ValueTask EmitAsync(AgentStreamEvent ev, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(ev, ct);
    }

    public async ValueTask EmitTokenAsync(string token, string? nodeOrAgent = null, CancellationToken ct = default)
    {
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.Token, token, nodeOrAgent), ct);
    }

    public async ValueTask EmitThoughtAsync(string thought, string? nodeOrAgent = null, CancellationToken ct = default)
    {
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.Thought, thought, nodeOrAgent), ct);
    }

    public async ValueTask EmitToolCallAsync(string toolName, string args, string? nodeOrAgent = null, CancellationToken ct = default)
    {
        var meta = new Dictionary<string, object?> { ["tool"] = toolName, ["arguments"] = args };
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.ToolCallStart, $"Calling tool '{toolName}' with {args}", nodeOrAgent, meta), ct);
    }

    public async ValueTask EmitToolResultAsync(string toolName, string result, string? nodeOrAgent = null, CancellationToken ct = default)
    {
        var meta = new Dictionary<string, object?> { ["tool"] = toolName, ["result"] = result };
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.ToolCallResult, result, nodeOrAgent, meta), ct);
    }

    public async ValueTask EmitNodeTransitionAsync(string fromNode, string toNode, CancellationToken ct = default)
    {
        var meta = new Dictionary<string, object?> { ["from"] = fromNode, ["to"] = toNode };
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.NodeTransition, $"Transitioning: {fromNode} -> {toNode}", fromNode, meta), ct);
    }

    public async ValueTask EmitStatusChangeAsync(string status, string? nodeOrAgent = null, CancellationToken ct = default)
    {
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.StatusChange, status, nodeOrAgent), ct);
    }

    public async ValueTask EmitErrorAsync(string error, string? nodeOrAgent = null, CancellationToken ct = default)
    {
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.Error, error, nodeOrAgent), ct);
    }

    public async ValueTask EmitCompleteAsync(string finalOutput, long tokens = 0, double cost = 0.0, string? nodeOrAgent = null, CancellationToken ct = default)
    {
        var meta = new Dictionary<string, object?> { ["tokens"] = tokens, ["cost"] = cost };
        await EmitAsync(new AgentStreamEvent(AgentStreamEventType.Complete, finalOutput, nodeOrAgent, meta), ct);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    public IAsyncEnumerable<AgentStreamEvent> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}

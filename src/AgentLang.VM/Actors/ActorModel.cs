using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentLang.VM.Actors;

public enum SupervisionStrategy
{
    Resume,
    Restart,
    Stop
}

public sealed class ActorMessage
{
    public string MessageId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string? Sender { get; }
    public string Recipient { get; }
    public object? Payload { get; }
    public string? Tag { get; }
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public TaskCompletionSource<object?>? ResponseTcs { get; }

    public ActorMessage(string recipient, object? payload, string? sender = null, string? tag = null, TaskCompletionSource<object?>? responseTcs = null)
    {
        Recipient = recipient;
        Payload = payload;
        Sender = sender;
        Tag = tag;
        ResponseTcs = responseTcs;
    }

    public override string ToString() =>
        $"[Msg:{MessageId} {Sender ?? "anon"} -> {Recipient} ({Tag ?? "general"})]: {Payload}";
}

public sealed class ActorMailbox
{
    private readonly Channel<ActorMessage> _channel = Channel.CreateUnbounded<ActorMessage>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    public int Count => _channel.Reader.Count;

    public ValueTask EnqueueAsync(ActorMessage message, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(message, ct);

    public ValueTask<ActorMessage> DequeueAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAsync(ct);

    public bool TryComplete() => _channel.Writer.TryComplete();
}

public sealed class AgentActor
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public string Name { get; }
    public ActorMailbox Mailbox { get; } = new();
    public SupervisionStrategy Strategy { get; set; }
    public int MaxRestarts { get; set; }
    public int RestartCount { get; private set; }
    public string Status { get; private set; } = "Created";
    public Exception? LastFault { get; private set; }
    public Func<AgentActor, ActorMessage, Task<object?>> Handler { get; set; }
    public AgentActorSystem System { get; }

    public AgentActor(
        string name,
        Func<AgentActor, ActorMessage, Task<object?>> handler,
        AgentActorSystem system,
        SupervisionStrategy strategy = SupervisionStrategy.Restart,
        int maxRestarts = 3)
    {
        Name = name;
        Handler = handler;
        System = system;
        Strategy = strategy;
        MaxRestarts = maxRestarts;
    }

    public void Start()
    {
        if (Status is "Active" or "Idle") return;
        Status = "Active";
        _loopTask = Task.Run(RunLoopAsync);
    }

    private async Task RunLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                Status = "Idle";
                var message = await Mailbox.DequeueAsync(_cts.Token);
                Status = "Active";

                object? result = null;
                try
                {
                    result = await Handler(this, message);
                    message.ResponseTcs?.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    LastFault = ex;
                    await System.HandleActorFaultAsync(this, ex);
                    message.ResponseTcs?.TrySetException(ex);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastFault = ex;
                await System.HandleActorFaultAsync(this, ex);
            }
        }

        Status = "Stopped";
    }

    public void ApplyRestart()
    {
        RestartCount++;
        Status = "Active";
    }

    public void ApplyResume()
    {
        Status = "Active";
    }

    public async Task StopAsync()
    {
        Status = "Stopping";
        _cts.Cancel();
        Mailbox.TryComplete();
        if (_loopTask != null)
        {
            try { await _loopTask; } catch { }
        }
        Status = "Stopped";
    }

    public async Task<object?> AskAsync(object? payload, string? tag = null, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var msg = new ActorMessage(Name, payload, tag: tag, responseTcs: tcs);
        await Mailbox.EnqueueAsync(msg);

        if (timeout.HasValue)
        {
            using var cts = new CancellationTokenSource(timeout.Value);
            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }

        return await tcs.Task;
    }

    public async ValueTask TellAsync(object? payload, string? sender = null, string? tag = null)
    {
        var msg = new ActorMessage(Name, payload, sender: sender, tag: tag);
        await Mailbox.EnqueueAsync(msg);
    }
}

public sealed class AgentActorSystem
{
    private readonly ConcurrentDictionary<string, AgentActor> _actors = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, AgentActor> Actors => _actors;

    public AgentActor Spawn(
        string name,
        Func<AgentActor, ActorMessage, Task<object?>> handler,
        SupervisionStrategy strategy = SupervisionStrategy.Restart,
        int maxRestarts = 3)
    {
        var actor = new AgentActor(name, handler, this, strategy, maxRestarts);
        _actors[name] = actor;
        actor.Start();
        return actor;
    }

    public AgentActor? GetActor(string name) =>
        _actors.TryGetValue(name, out var actor) ? actor : null;

    public async Task<object?> SendAsync(string recipient, object? payload, string? tag = null, TimeSpan? timeout = null)
    {
        if (!_actors.TryGetValue(recipient, out var actor))
            throw new KeyNotFoundException($"Actor '{recipient}' not found in the actor system.");

        return await actor.AskAsync(payload, tag, timeout);
    }

    public async Task BroadcastAsync(object? payload, string? tag = null)
    {
        var tasks = new List<ValueTask>();
        foreach (var actor in _actors.Values)
        {
            tasks.Add(actor.TellAsync(payload, sender: "System", tag: tag));
        }
        foreach (var t in tasks)
        {
            await t;
        }
    }

    public async Task HandleActorFaultAsync(AgentActor actor, Exception fault)
    {
        if (actor.Strategy == SupervisionStrategy.Resume)
        {
            actor.ApplyResume();
        }
        else if (actor.Strategy == SupervisionStrategy.Restart)
        {
            if (actor.RestartCount < actor.MaxRestarts)
            {
                actor.ApplyRestart();
            }
            else
            {
                await actor.StopAsync();
            }
        }
        else // Stop
        {
            await actor.StopAsync();
        }
    }

    public async Task ShutdownAsync()
    {
        foreach (var actor in _actors.Values)
        {
            await actor.StopAsync();
        }
        _actors.Clear();
    }
}

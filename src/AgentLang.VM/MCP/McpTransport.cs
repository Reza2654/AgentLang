using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace AgentLang.VM.MCP;

public interface IMcpTransport : IAsyncDisposable, IDisposable
{
    Task SendMessageAsync(string json, CancellationToken ct = default);
    Task<string?> ReadMessageAsync(CancellationToken ct = default);
}

public sealed class StreamMcpTransport : IMcpTransport
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public StreamMcpTransport(Stream inStream, Stream outStream, bool leaveOpen = false)
    {
        _reader = new StreamReader(inStream);
        _writer = new StreamWriter(outStream) { AutoFlush = true };
        _leaveOpen = leaveOpen;
    }

    public async Task SendMessageAsync(string json, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), ct);
            await _writer.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<string?> ReadMessageAsync(CancellationToken ct = default)
    {
        return await _reader.ReadLineAsync(ct);
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _reader.Dispose();
            _writer.Dispose();
        }
        _writeLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            _reader.Dispose();
            await _writer.DisposeAsync();
        }
        _writeLock.Dispose();
    }
}

public sealed class InMemoryMcpTransport : IMcpTransport
{
    private readonly Channel<string> _inbox;
    private readonly Channel<string> _outbox;
    private bool _isDisposed;

    private InMemoryMcpTransport(Channel<string> inbox, Channel<string> outbox)
    {
        _inbox = inbox;
        _outbox = outbox;
    }

    public static (InMemoryMcpTransport ClientTransport, InMemoryMcpTransport ServerTransport) CreatePair()
    {
        var channelA = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var channelB = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var clientTransport = new InMemoryMcpTransport(channelB, channelA);
        var serverTransport = new InMemoryMcpTransport(channelA, channelB);

        return (clientTransport, serverTransport);
    }

    public async Task SendMessageAsync(string json, CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(InMemoryMcpTransport));
        await _outbox.Writer.WriteAsync(json, ct);
    }

    public async Task<string?> ReadMessageAsync(CancellationToken ct = default)
    {
        if (_isDisposed) return null;
        try
        {
            return await _inbox.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _outbox.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

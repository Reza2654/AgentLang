using System.Text;

namespace AgentLang.VM.Lsp;

public sealed class LspTransport : IDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public LspTransport(Stream input, Stream output, bool leaveOpen = false)
    {
        _input = input;
        _output = output;
        _leaveOpen = leaveOpen;
    }

    public async Task<string?> ReadMessageAsync(CancellationToken ct = default)
    {
        int contentLength = 0;
        var headerBuffer = new StringBuilder();

        while (true)
        {
            string? line = await ReadLineAsync(_input, ct);
            if (line == null) return null; // End of stream

            if (line.Length == 0) // Empty line separates headers from body
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                string lenStr = line.Substring("Content-Length:".Length).Trim();
                if (int.TryParse(lenStr, out int len))
                {
                    contentLength = len;
                }
            }
        }

        if (contentLength <= 0) return null;

        byte[] bodyBuffer = new byte[contentLength];
        int totalRead = 0;
        while (totalRead < contentLength)
        {
            int read = await _input.ReadAsync(bodyBuffer.AsMemory(totalRead, contentLength - totalRead), ct);
            if (read == 0) return null;
            totalRead += read;
        }

        return Encoding.UTF8.GetString(bodyBuffer);
    }

    public async Task SendMessageAsync(string jsonPayload, CancellationToken ct = default)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonPayload);
        string header = $"Content-Length: {bodyBytes.Length}\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _output.WriteAsync(headerBytes, ct);
            await _output.WriteAsync(bodyBytes, ct);
            await _output.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var lineBytes = new List<byte>();
        byte[] singleByte = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(singleByte.AsMemory(0, 1), ct);
            if (read == 0)
            {
                if (lineBytes.Count == 0) return null;
                break;
            }

            byte b = singleByte[0];
            if (b == (byte)'\n')
            {
                // Strip optional trailing \r
                if (lineBytes.Count > 0 && lineBytes[^1] == (byte)'\r')
                {
                    lineBytes.RemoveAt(lineBytes.Count - 1);
                }
                break;
            }
            lineBytes.Add(b);
        }

        return Encoding.ASCII.GetString(lineBytes.ToArray());
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _input.Dispose();
            _output.Dispose();
        }
        _writeLock.Dispose();
    }
}

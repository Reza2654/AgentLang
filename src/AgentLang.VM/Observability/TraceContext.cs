using System.Security.Cryptography;

namespace AgentLang.VM.Observability;

public sealed record TraceContext(
    string TraceId,
    string SpanId,
    string? ParentSpanId = null,
    string TraceFlags = "01")
{
    public static string GenerateTraceId()
    {
        byte[] bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string GenerateSpanId()
    {
        byte[] bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static TraceContext CreateNew() =>
        new(GenerateTraceId(), GenerateSpanId());

    public TraceContext CreateChild() =>
        new(TraceId, GenerateSpanId(), SpanId, TraceFlags);

    public string ToTraceParent() =>
        $"00-{TraceId}-{SpanId}-{TraceFlags}";

    public static TraceContext? TryParse(string? traceparent)
    {
        if (string.IsNullOrWhiteSpace(traceparent)) return null;

        var parts = traceparent.Split('-');
        if (parts.Length != 4) return null;
        if (parts[0] != "00") return null; // W3C version 00
        if (parts[1].Length != 32) return null; // 16-byte hex TraceId
        if (parts[2].Length != 16) return null; // 8-byte hex SpanId

        return new TraceContext(parts[1], parts[2], null, parts[3]);
    }
}

using System.IO.Compression;
using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Eda.Enums;

namespace OriginalCircuit.Altium.Serialization.Binary;

// Inflates zlib data without letting the data decide how much is allocated.
internal static class Inflater
{
    // The inflated bytes, or null when they come to more than limit (nothing past limit is held).
    // Throws InvalidDataException for data that is not zlib.
    public static byte[]? Inflate(byte[] compressed, int limit)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var buffer = new byte[(int)Math.Clamp(compressed.Length * 4L, 256, limit)];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                if (buffer.Length >= limit)
                    return zlib.ReadByte() < 0 ? buffer : null;
                Array.Resize(ref buffer, (int)Math.Min(limit, buffer.Length * 2L));
            }
            var read = zlib.Read(buffer, length, buffer.Length - length);
            if (read == 0)
                break;
            length += read;
        }
        if (length != buffer.Length)
            Array.Resize(ref buffer, length);
        return buffer;
    }
}

// What one document's embedded payloads may still inflate to (AltiumReaderOptions).
internal sealed class PayloadBudget(AltiumReaderOptions options, List<AltiumDiagnostic>? diagnostics)
{
    private long _used;

    public bool ReadsPayloads => options.ReadEmbeddedPayloads;

    // The inflated payload, or null when payloads are not read or it passes a limit (with a warning).
    // Throws InvalidDataException for data that is not zlib.
    public byte[]? Inflate(byte[] compressed, string what, string streamName)
    {
        if (!options.ReadEmbeddedPayloads)
            return null;
        var remaining = options.MaxInflatedTotalBytes - _used;
        if (remaining <= 0)
        {
            Warn($"{what} was left out: the document's embedded payloads already come to " +
                 $"{options.MaxInflatedTotalBytes:N0} bytes (AltiumReaderOptions.MaxInflatedTotalBytes).", streamName);
            return null;
        }
        var limit = (int)Math.Min(options.MaxInflatedPayloadBytes, remaining);
        var data = Inflater.Inflate(compressed, limit);
        if (data is null)
        {
            Warn(limit == options.MaxInflatedPayloadBytes
                ? $"{what} was left out: it inflates to more than {limit:N0} bytes (AltiumReaderOptions.MaxInflatedPayloadBytes)."
                : $"{what} was left out: it would take the document's embedded payloads past " +
                  $"{options.MaxInflatedTotalBytes:N0} bytes (AltiumReaderOptions.MaxInflatedTotalBytes).", streamName);
            return null;
        }
        _used += data.Length;
        return data;
    }

    private void Warn(string message, string streamName) =>
        diagnostics?.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning, message, streamName));
}

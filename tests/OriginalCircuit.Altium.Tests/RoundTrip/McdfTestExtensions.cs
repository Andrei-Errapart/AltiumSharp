using OpenMcdf;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Small helpers that adapt the OpenMcdf 3.x API to the read patterns the round-trip tests use.
/// These tests read the compound files directly with OpenMcdf (independently of the library's own
/// <c>CompoundFileAccessor</c>) so they verify real on-disk stream fidelity.
/// </summary>
internal static class McdfTestExtensions
{
    /// <summary>
    /// Reads the entire content of an open <see cref="CfbStream"/> into a byte array.
    /// </summary>
    public static byte[] GetData(this CfbStream stream)
    {
        var length = checked((int)stream.Length);
        var buffer = new byte[length];
        stream.Position = 0;
        stream.ReadExactly(buffer, 0, length);
        return buffer;
    }

    /// <summary>
    /// Opens a child stream by name, reads it fully, and disposes it.
    /// </summary>
    public static byte[] ReadStreamData(this Storage storage, string name)
    {
        using var stream = storage.OpenStream(name);
        return stream.GetData();
    }

    /// <summary>
    /// Tries to read a child stream by name. Returns false if the stream is absent.
    /// </summary>
    public static bool TryReadStreamData(this Storage storage, string name, out byte[] data)
    {
        if (storage.TryOpenStream(name, out var stream))
        {
            using (stream)
            {
                data = stream.GetData();
            }
            return true;
        }

        data = [];
        return false;
    }

    /// <summary>
    /// Recursively collects every stream in <paramref name="storage"/> keyed by its '/'-joined path.
    /// </summary>
    public static void CollectStreams(Storage storage, string path, Dictionary<string, byte[]> result)
    {
        // Materialize the entry list before opening any child (the underlying enumerator must not be
        // live while streams/storages are opened).
        foreach (var entry in storage.EnumerateEntries().ToList())
        {
            var fullPath = string.IsNullOrEmpty(path) ? entry.Name : $"{path}/{entry.Name}";
            if (entry.Type == EntryType.Stream)
            {
                result[fullPath] = storage.ReadStreamData(entry.Name);
            }
            else
            {
                CollectStreams(storage.OpenStorage(entry.Name), fullPath, result);
            }
        }
    }
}

using OpenMcdf;

namespace OriginalCircuit.Altium.Serialization.Compound;

/// <summary>
/// Library-neutral handle to a single stream inside a compound (OLE structured storage) file.
/// </summary>
/// <remarks>
/// This deliberately hides the underlying OpenMcdf <see cref="CfbStream"/> from the rest of the
/// library. It is a lightweight handle bound to an owning <see cref="Storage"/> and an entry name;
/// it does NOT keep a stream open. Every <see cref="GetData"/>/<see cref="SetData"/> call opens (or
/// creates) the backing stream and disposes it before returning.
///
/// This atomic open-read-dispose / create-write-dispose pattern is required by OpenMcdf 3.x: with a
/// non-transacted root storage, only one <see cref="CfbStream"/> may be in flight at a time and it
/// must be disposed before the next entry is created or opened, otherwise its content is silently
/// dropped. Keeping streams un-held makes the reader/writer call sites immune to that hazard.
/// </remarks>
internal sealed class CompoundStream
{
    private readonly Storage _owner;

    internal CompoundStream(Storage owner, string name)
    {
        _owner = owner;
        Name = name;
    }

    /// <summary>
    /// Gets the entry name of this stream within its owning storage.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Reads the entire stream content into a byte array.
    /// </summary>
    public byte[] GetData()
    {
        using var stream = _owner.OpenStream(Name);
        var length = checked((int)stream.Length);
        var buffer = new byte[length];
        stream.Position = 0;
        stream.ReadExactly(buffer, 0, length);
        return buffer;
    }

    /// <summary>
    /// Creates (or replaces) the stream with the given content.
    /// </summary>
    public void SetData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = _owner.CreateStream(Name);
        if (data.Length > 0)
        {
            stream.Write(data, 0, data.Length);
        }
    }
}

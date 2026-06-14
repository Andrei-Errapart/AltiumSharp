using OpenMcdf;

namespace OriginalCircuit.Altium.Serialization.Compound;

/// <summary>
/// Library-neutral handle to a storage (directory) inside a compound (OLE structured storage) file.
/// </summary>
/// <remarks>
/// Wraps an OpenMcdf <see cref="Storage"/> so the readers and writers never reference OpenMcdf types
/// directly. A storage handle is cheap and may be freely created; it owns no unmanaged resources.
/// Child streams are accessed through <see cref="CompoundStream"/>, which opens/creates them
/// atomically (see remarks there).
/// </remarks>
internal sealed class CompoundStorage
{
    private readonly Storage _storage;

    internal CompoundStorage(Storage storage)
    {
        _storage = storage;
    }

    /// <summary>Gets the entry name of this storage.</summary>
    public string Name => _storage.EntryInfo.Name;

    /// <summary>
    /// Tries to get a child stream by name.
    /// </summary>
    public bool TryGetStream(string name, out CompoundStream stream)
    {
        if (_storage.TryGetEntryInfo(name, out var info) && info.Type == EntryType.Stream)
        {
            stream = new CompoundStream(_storage, name);
            return true;
        }

        stream = null!;
        return false;
    }

    /// <summary>
    /// Gets a child stream by name, or <see langword="null"/> if it does not exist.
    /// </summary>
    public CompoundStream? TryGetStream(string name)
        => TryGetStream(name, out var stream) ? stream : null;

    /// <summary>
    /// Gets a child stream by name; throws if it does not exist.
    /// </summary>
    public CompoundStream GetStream(string name)
        => TryGetStream(name, out var stream)
            ? stream
            : throw new KeyNotFoundException($"Stream '{name}' not found.");

    /// <summary>
    /// Tries to get a child storage by name.
    /// </summary>
    public bool TryGetStorage(string name, out CompoundStorage storage)
    {
        if (_storage.TryOpenStorage(name, out var child))
        {
            storage = new CompoundStorage(child);
            return true;
        }

        storage = null!;
        return false;
    }

    /// <summary>
    /// Gets a child storage by name, or <see langword="null"/> if it does not exist.
    /// </summary>
    public CompoundStorage? TryGetStorage(string name)
        => TryGetStorage(name, out var storage) ? storage : null;

    /// <summary>
    /// Gets a child storage by name; throws if it does not exist.
    /// </summary>
    public CompoundStorage GetStorage(string name)
        => new(_storage.OpenStorage(name));

    /// <summary>
    /// Creates a new child stream. The stream content is written via <see cref="CompoundStream.SetData"/>.
    /// </summary>
    public CompoundStream AddStream(string name) => new(_storage, name);

    /// <summary>
    /// Creates a new child storage.
    /// </summary>
    public CompoundStorage AddStorage(string name) => new(_storage.CreateStorage(name));

    /// <summary>
    /// Enumerates the immediate child entries (streams and storages) of this storage.
    /// </summary>
    /// <remarks>
    /// The result is materialized so callers may open child streams/storages while iterating
    /// (the underlying OpenMcdf enumerator must not be live while other entries are opened).
    /// </remarks>
    public IReadOnlyList<CompoundEntry> EnumerateEntries()
    {
        var entries = new List<CompoundEntry>();
        foreach (var info in _storage.EnumerateEntries())
        {
            entries.Add(new CompoundEntry(this, info.Name, info.Type == EntryType.Storage));
        }
        return entries;
    }

    /// <summary>
    /// Enumerates the immediate child streams of this storage.
    /// </summary>
    public IEnumerable<CompoundStream> EnumerateStreams()
    {
        foreach (var entry in EnumerateEntries())
        {
            if (entry.IsStream)
            {
                yield return entry.AsStream();
            }
        }
    }

    /// <summary>
    /// Enumerates the immediate child storages of this storage.
    /// </summary>
    public IEnumerable<CompoundStorage> EnumerateStorages()
    {
        foreach (var entry in EnumerateEntries())
        {
            if (entry.IsStorage)
            {
                yield return entry.AsStorage();
            }
        }
    }
}

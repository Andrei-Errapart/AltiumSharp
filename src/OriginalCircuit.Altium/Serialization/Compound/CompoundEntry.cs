namespace OriginalCircuit.Altium.Serialization.Compound;

/// <summary>
/// Library-neutral description of a single child entry (stream or storage) of a
/// <see cref="CompoundStorage"/>, returned while enumerating its children.
/// </summary>
internal readonly struct CompoundEntry
{
    private readonly CompoundStorage _parent;

    internal CompoundEntry(CompoundStorage parent, string name, bool isStorage)
    {
        _parent = parent;
        Name = name;
        IsStorage = isStorage;
    }

    /// <summary>Gets the entry name.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether this entry is a storage (directory).</summary>
    public bool IsStorage { get; }

    /// <summary>Gets a value indicating whether this entry is a stream.</summary>
    public bool IsStream => !IsStorage;

    /// <summary>Opens this entry as a child storage.</summary>
    public CompoundStorage AsStorage() => _parent.GetStorage(Name);

    /// <summary>Opens this entry as a stream.</summary>
    public CompoundStream AsStream() => _parent.GetStream(Name);
}

namespace OriginalCircuit.Altium.Serialization;

/// <summary>
/// Constants for the PCB binary file format shared between readers and writers.
/// </summary>
internal static class PcbBinaryConstants
{
    /// <summary>Bit 2: Unlocked flag (inverted — 0 means locked).</summary>
    internal const ushort FlagUnlocked = 0x04;

    /// <summary>Bit 3: set on every primitive Altium saves (observed invariant across the corpus).</summary>
    internal const ushort FlagSaved = 0x08;

    /// <summary>Bit 5: Tenting top.</summary>
    internal const ushort FlagTentingTop = 0x20;

    /// <summary>Bit 6: Tenting bottom.</summary>
    internal const ushort FlagTentingBottom = 0x40;

    /// <summary>Bit 9: Keepout region.</summary>
    internal const ushort FlagKeepout = 0x200;

    /// <summary>
    /// Mask of the flag bits the model represents as typed properties. Bits outside this mask are
    /// not modelled (e.g. selection / display state) and must be preserved verbatim from the source.
    /// </summary>
    internal const ushort ModeledFlagsMask = FlagUnlocked | FlagSaved | FlagTentingTop | FlagTentingBottom | FlagKeepout;

    /// <summary>
    /// Decodes primitive flags into individual boolean properties.
    /// </summary>
    internal static void DecodeFlags(ushort flags, out bool isLocked, out bool isTentingTop,
        out bool isTentingBottom, out bool isKeepout)
    {
        isLocked = (flags & FlagUnlocked) == 0;
        isTentingTop = (flags & FlagTentingTop) != 0;
        isTentingBottom = (flags & FlagTentingBottom) != 0;
        isKeepout = (flags & FlagKeepout) != 0;
    }

    /// <summary>
    /// Combines freshly-encoded modelled flag bits with the unmodelled bits captured from the source
    /// record (<paramref name="rawFlags"/>), so edits to the typed properties take effect while bits
    /// the model does not represent round-trip verbatim. When <paramref name="rawFlags"/> is null
    /// (a primitive built from scratch) the encoded value is used unchanged.
    /// </summary>
    internal static ushort MergeFlags(ushort? rawFlags, ushort encoded)
        => rawFlags is { } raw
            ? (ushort)((encoded & ModeledFlagsMask) | (raw & ~ModeledFlagsMask))
            : encoded;
}

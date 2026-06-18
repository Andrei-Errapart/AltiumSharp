namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// One entry of a <c>PrimitiveGuids</c> table — the editor's per-primitive object-GUID cache. Each
/// record binds a primitive (identified by its type tag and per-type ordinal) to a stable GUID.
/// Modeled as a typed record (24 bytes on disk: <c>[u32 typeId][u32 index][16-byte GUID]</c>) rather
/// than an opaque blob. See docs/decompile/identity-streams.md.
/// </summary>
public sealed class PcbPrimitiveGuid
{
    /// <summary>Primitive type tag (Pad=0x0E02, Via=0x0F03, Track=0x1004, Text=0x1105, etc.).</summary>
    public uint TypeId { get; set; }

    /// <summary>Per-type sequential ordinal.</summary>
    public uint Index { get; set; }

    /// <summary>The primitive's object GUID (random; Altium regenerates these on demand).</summary>
    public Guid Guid { get; set; }
}

/// <summary>
/// One <c>UniqueIDPrimitiveInformation</c> record: an 8-char short-id token bound to a primitive by
/// its source-stream ordinal and type. Stored as a length-prefixed <c>|KEY=VALUE|</c> text record.
/// See docs/decompile/identity-streams.md.
/// </summary>
public sealed class PcbPrimitiveUniqueId
{
    /// <summary>0-based ordinal of the primitive within its source stream.</summary>
    public int PrimitiveIndex { get; set; }

    /// <summary>Primitive type name ("Pad", "Via", "Track", "Text", ...).</summary>
    public string ObjectId { get; set; } = "Pad";

    /// <summary>The 8-char short-id token.</summary>
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>Ordered parameters captured for byte-exact round-trip; null for from-scratch.</summary>
    internal List<KeyValuePair<string, string>>? RawParametersOrdered { get; set; }

    /// <summary>The canonical record text (used when no captured order exists).</summary>
    internal string ToText() =>
        $"|PRIMITIVEINDEX={PrimitiveIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
        $"|PRIMITIVEOBJECTID={ObjectId}|UNIQUEID={UniqueId}";
}

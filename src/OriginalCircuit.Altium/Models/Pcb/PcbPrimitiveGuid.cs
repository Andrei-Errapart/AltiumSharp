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

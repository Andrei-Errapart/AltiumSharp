using System.Globalization;

namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// The <c>Library/LayerKindMapping</c> metadata Altium writes in every PcbLib (and a PcbDoc): a format
/// version string plus a reserved tail. Modeled as a first-class record so it round-trips and is emitted
/// for from-scratch files (which Altium expects to carry it). See docs/decompile/feature-layerkind-mapping.md.
/// </summary>
public sealed class PcbLayerKindMapping
{
    /// <summary>Format version string (UTF-16LE on disk); currently <c>"1.0"</c>.</summary>
    public string FormatVersion { get; set; } = "1.0";

    /// <summary>
    /// The layer-id → layer-kind mapping table (PcbDoc only; empty in PcbLib). Each entry maps a
    /// physical layer id to a layer-kind enum value. Fully typed so the table round-trips and is
    /// authored from scratch without replaying the raw bytes.
    /// </summary>
    public List<PcbLayerKindEntry> Entries { get; set; } = new();

    /// <summary>
    /// A 32-bit content checksum Altium writes immediately before the mapping table (0 when the table
    /// is empty). It is <see cref="ComputeSignature"/> — MurmurHash2-32 over the packed entry buffer.
    /// On round-trip the value read from disk is preserved verbatim (some Altium-saved files hash a
    /// couple of bytes of uninitialised heap past the buffer, so their stored value is not exactly
    /// reproducible); for a from-scratch table the writer fills it in via <see cref="ComputeSignature"/>.
    /// Altium does not validate this field on load, so a computed value is always accepted.
    /// </summary>
    public uint Signature { get; set; }

    /// <summary>
    /// Computes the layer-kind-mapping signature exactly as Altium's <c>TLayerKindMappingSection</c>
    /// does: MurmurHash2-32 (seed <c>0xDEADBEEF</c>, multiplier <c>0x5BD1E995</c>) over a packed buffer
    /// of 5 bytes per entry (int32 little-endian <see cref="PcbLayerKindEntry.LayerId"/> followed by the
    /// low byte of <see cref="PcbLayerKindEntry.Kind"/>), consumed as ceil(n/4) 32-bit little-endian
    /// words with the final partial word zero-filled (Altium uses no MurmurHash2 tail step). Returns 0
    /// for an empty table.
    /// </summary>
    public uint ComputeSignature()
    {
        const uint m = 0x5bd1e995;
        if (Entries.Count == 0) return 0;
        var buf = new byte[Entries.Count * 5];
        for (var i = 0; i < Entries.Count; i++)
        {
            var p = i * 5;
            var id = Entries[i].LayerId;
            buf[p] = (byte)id; buf[p + 1] = (byte)(id >> 8); buf[p + 2] = (byte)(id >> 16); buf[p + 3] = (byte)(id >> 24);
            buf[p + 4] = (byte)Entries[i].Kind;
        }
        var n = buf.Length;
        var h = 0xdeadbeefu;
        var words = (n + 3) / 4;
        for (var i = 0; i < words; i++)
        {
            uint k = 0;
            for (var b = 0; b < 4; b++) { var idx = i * 4 + b; if (idx < n) k |= (uint)buf[idx] << (8 * b); }
            k *= m; k ^= k >> 24; k *= m;
            h = (h * m) ^ k;
        }
        h ^= h >> 13; h *= m; h ^= h >> 15;
        return h;
    }
}

/// <summary>One entry of the PcbDoc <c>LayerKindMapping</c> table: a physical layer id and its kind.</summary>
public readonly record struct PcbLayerKindEntry(uint LayerId, uint Kind);

/// <summary>
/// The <c>PadViaLibrary</c> metadata storage (PcbLib <c>Library/PadViaLibrary</c>, PcbDoc root
/// <c>PadViaLibrary</c>): the local pad/via template library identity. Modeled so it round-trips and is
/// authored from scratch. See docs/decompile/feature-padvia-library.md.
/// </summary>
public sealed class PcbPadViaLibrary
{
    /// <summary>Library GUID (braced, uppercase on disk).</summary>
    public Guid LibraryId { get; set; } = Guid.NewGuid();

    /// <summary>Library display name; default <c>"&lt;Local&gt;"</c>.</summary>
    public string LibraryName { get; set; } = "<Local>";

    /// <summary>Display-units enum (0=mil,1=mm,2=µm,3=in); default 1.</summary>
    public int DisplayUnits { get; set; } = 1;

    /// <summary>The storage header value (0 for PadViaLibrary; the template count for a populated cache).</summary>
    internal int HeaderCount { get; set; }

    /// <summary>
    /// The parameter block as an ordered, authorable key/value list — the canonical representation that
    /// preserves key order/duplicates. Written verbatim when set; null emits the typed fields in canonical order.
    /// </summary>
    public List<KeyValuePair<string, string>>? OrderedParameters { get; set; }

    /// <summary>
    /// Optional binary pad/via template cache that follows the parameter block in a
    /// <c>PadViaLibraryCache</c> stream (structure not reverse-engineered; preserved verbatim).
    /// Null/empty for the plain <c>PadViaLibrary</c> stream.
    /// </summary>
    internal byte[]? TemplateCache { get; set; }
}

/// <summary>
/// The <c>FileVersionInfo</c> stream — Altium's per-file version/compatibility message cache (a single
/// parameter block of COUNT/VERn/FWDMSGn/BKMSGn entries). Modeled as a typed record (the ordered params
/// are preserved for byte-exact round-trip) rather than an opaque stream. Present in PcbLib and PcbDoc.
/// </summary>
public sealed class PcbFileVersionInfo
{
    /// <summary>Parsed parameters (the encoded version messages).</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The parameter block as an ordered, authorable key/value list — the canonical representation,
    /// preserving key order and any duplicate keys the <see cref="Parameters"/> dictionary collapses.
    /// Written verbatim when set; null falls back to <see cref="Parameters"/>.
    /// </summary>
    public List<KeyValuePair<string, string>>? OrderedParameters { get; set; }

    /// <summary>True when this info was present in the source file (so the writer reproduces its presence).</summary>
    internal bool Present { get; set; }
}

/// <summary>
/// One entry of the PcbLib <c>Library/ComponentParamsTOC</c> table of contents (a per-footprint summary
/// row). See docs/decompile/feature-component-params-toc.md.
/// </summary>
public sealed class PcbComponentParamsTocEntry
{
    /// <summary>Footprint name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Pad count.</summary>
    public int PadCount { get; set; }
    /// <summary>Height token (as stored).</summary>
    public string Height { get; set; } = string.Empty;
    /// <summary>Description.</summary>
    public string Description { get; set; } = string.Empty;

    internal static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}

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

    /// <summary>8-byte reserved tail (all zero in current files), preserved verbatim.</summary>
    internal byte[] ReservedTail { get; set; } = new byte[8];
}

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

    /// <summary>
    /// Ordered parameter list captured from the source for byte-exact round-trip; null for from-scratch
    /// instances, which emit the typed fields in canonical order.
    /// </summary>
    internal List<KeyValuePair<string, string>>? RawParametersOrdered { get; set; }
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

    /// <summary>Ordered parameter list, preserved for byte-exact round-trip; null for from-scratch.</summary>
    internal List<KeyValuePair<string, string>>? RawParametersOrdered { get; set; }

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

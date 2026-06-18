namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// A PcbDoc <c>ExtendedPrimitiveInformation</c> record: a per-primitive solder/paste-mask expansion
/// override keyed by <c>PRIMITIVEINDEX</c> + <c>PRIMITIVEOBJECTID</c>. Previously round-tripped as an
/// opaque <c>AdditionalStreams</c> blob; now a first-class typed record. The on-disk format is a
/// length-prefixed C-string parameter block (same framing as <c>PrimitiveParameters</c>); the storage
/// <c>Header</c> is the record count. See docs/decompile/identity-streams.md §1.3.
/// </summary>
public sealed class PcbExtendedPrimitiveInfo
{
    /// <summary>0-based ordinal of the primitive within its source stream (e.g. nth Region in Regions6).</summary>
    public int PrimitiveIndex { get; set; }

    /// <summary>Primitive type token (<c>Region</c> / <c>Track</c> / <c>Arc</c> / <c>Fill</c> / <c>Pad</c> …).</summary>
    public string PrimitiveObjectId { get; set; } = "Region";

    /// <summary>Override kind (e.g. <c>"Mask"</c>).</summary>
    public string? Type { get; set; }

    /// <summary>Solder-mask expansion mode (e.g. <c>"Manual"</c>).</summary>
    public string? SolderMaskExpansionMode { get; set; }

    /// <summary>Manual solder-mask expansion value (e.g. <c>"0mil"</c>).</summary>
    public string? SolderMaskExpansionManual { get; set; }

    /// <summary>Paste-mask expansion mode (e.g. <c>"Manual"</c>).</summary>
    public string? PasteMaskExpansionMode { get; set; }

    /// <summary>Manual paste-mask expansion value (e.g. <c>"-78.7402mil"</c>).</summary>
    public string? PasteMaskExpansionManual { get; set; }

    /// <summary>All parsed parameters (preserves keys the typed fields don't model).</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The parameter block as an ordered, authorable key/value list — the canonical representation,
    /// preserving key order and any duplicate keys the <see cref="Parameters"/> dictionary collapses.
    /// Written verbatim when set; null falls back to the typed fields via <see cref="ToParameters"/>.
    /// </summary>
    public List<KeyValuePair<string, string>>? OrderedParameters { get; set; }

    /// <summary>Builds the parameter dictionary used for from-scratch serialization.</summary>
    public Dictionary<string, string> ToParameters()
    {
        Parameters["PRIMITIVEINDEX"] = PrimitiveIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Parameters["PRIMITIVEOBJECTID"] = PrimitiveObjectId;
        if (Type != null) Parameters["TYPE"] = Type;
        if (SolderMaskExpansionMode != null) Parameters["SOLDERMASKEXPANSIONMODE"] = SolderMaskExpansionMode;
        if (SolderMaskExpansionManual != null) Parameters["SOLDERMASKEXPANSION_MANUAL"] = SolderMaskExpansionManual;
        if (PasteMaskExpansionMode != null) Parameters["PASTEMASKEXPANSIONMODE"] = PasteMaskExpansionMode;
        if (PasteMaskExpansionManual != null) Parameters["PASTEMASKEXPANSION_MANUAL"] = PasteMaskExpansionManual;
        return Parameters;
    }
}

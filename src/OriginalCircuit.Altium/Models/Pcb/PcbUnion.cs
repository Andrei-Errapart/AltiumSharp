using System.Globalization;

namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// A PcbDoc <c>SmartUnions</c> record: a union grouping of board objects (via-stitching arrays,
/// trace-tuning patterns, polygon-pour unions, etc.). On disk each record concatenates several
/// member sub-blocks into one length-prefixed parameter block; every member begins with the common
/// primitive prefix (<c>SELECTION..UNIONINDEX</c>), so that prefix marks the member boundaries.
/// The record is fully typed and authorable — populate <see cref="Members"/>, each carrying its
/// typed prefix plus the member-specific keys (in source order) as <see cref="PcbUnionMember.Parameters"/>.
/// The member-specific keys vary enormously by union type (via geometry, indexed <c>ACCLIST{n}</c>
/// stitching/vertex arrays, trace-tuning keys), so they are kept as an ordered key/value list rather
/// than hundreds of named fields; this is the natural model and round-trips byte-for-byte.
/// </summary>
public sealed class PcbSmartUnion
{
    /// <summary>The member sub-blocks, in source order (descriptor first, then template/shape members).</summary>
    public List<PcbUnionMember> Members { get; } = new();

    /// <summary>The grouping id (from the first member's <c>UNIONINDEX</c> prefix key).</summary>
    public int UnionIndex => Members.Count > 0 ? Members[0].UnionIndex : 0;

    /// <summary>The union type code (the descriptor member's <c>UNIONTYPE</c> key; 0 when absent).</summary>
    public int UnionType
    {
        get
        {
            foreach (var m in Members)
                foreach (var kv in m.Parameters)
                    if (kv.Key.Equals("UNIONTYPE", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
                        return t;
            return 0;
        }
    }
}

/// <summary>
/// One member sub-block of a <see cref="PcbSmartUnion"/>: the typed common-primitive prefix plus the
/// member-specific parameters in source order. Members may share/duplicate keys with each other;
/// each member keeps its own ordered list so nothing is lost.
/// </summary>
public sealed class PcbUnionMember
{
    /// <summary>The <c>SELECTION</c> flag (transient; <c>FALSE</c> on disk).</summary>
    public bool Selection { get; set; }

    /// <summary>The layer token verbatim (e.g. <c>TOP</c>, <c>MULTILAYER</c>).</summary>
    public string Layer { get; set; } = "TOP";

    /// <summary>The <c>LOCKED</c> flag.</summary>
    public bool Locked { get; set; }

    /// <summary>The <c>POLYGONOUTLINE</c> flag.</summary>
    public bool PolygonOutline { get; set; }

    /// <summary>The <c>USERROUTED</c> flag.</summary>
    public bool UserRouted { get; set; }

    /// <summary>The <c>KEEPOUT</c> flag.</summary>
    public bool Keepout { get; set; }

    /// <summary>The <c>UNIONINDEX</c> grouping id (part of the common prefix).</summary>
    public int UnionIndex { get; set; }

    /// <summary>
    /// Member-specific keys after the common prefix, in source order (may contain repeated keys —
    /// e.g. a descriptor's duplicated <c>X1</c>/<c>Y1</c>, or indexed <c>ACCLIST{n}*</c> arrays).
    /// </summary>
    public List<KeyValuePair<string, string>> Parameters { get; } = new();
}

/// <summary>
/// A PcbDoc <c>UnionNames</c> record: a user-assigned name for a union group, keyed by union index.
/// Stored as binary (UTF-16LE). See docs/decompile/feature-unions.md.
/// </summary>
public sealed class PcbUnionName
{
    /// <summary>The union index this name applies to (matches a <see cref="PcbSmartUnion.UnionIndex"/>).</summary>
    public int UnionIndex { get; set; }

    /// <summary>The union's display name.</summary>
    public string Name { get; set; } = string.Empty;
}

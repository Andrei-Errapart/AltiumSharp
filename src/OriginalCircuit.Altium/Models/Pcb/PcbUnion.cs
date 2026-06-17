namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// A PcbDoc <c>SmartUnions</c> record (a typed grouping of board objects). The on-disk format is a
/// length-prefixed parameter block, like <c>Nets6</c>. See docs/decompile/feature-unions.md.
/// </summary>
public sealed class PcbSmartUnion
{
    /// <summary>Union index (the grouping id, also referenced by <see cref="PcbUnionName"/>).</summary>
    public int UnionIndex { get; set; }

    /// <summary>Union type (1–9).</summary>
    public int UnionType { get; set; }

    /// <summary>All parsed parameters.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ordered parameter list captured for byte-exact round-trip; null for from-scratch.</summary>
    internal List<KeyValuePair<string, string>>? RawParametersOrdered { get; set; }

    /// <summary>Builds the parameter dictionary for from-scratch serialization.</summary>
    public Dictionary<string, string> ToParameters()
    {
        Parameters["UNIONINDEX"] = UnionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Parameters["UNIONTYPE"] = UnionType.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Parameters;
    }
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

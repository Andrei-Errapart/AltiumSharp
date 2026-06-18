namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Represents a PCB differential pair from the DifferentialPairs6 storage (links a positive and negative
/// net). Fully typed so it round-trips byte-for-byte without replaying the raw parameter block.
/// </summary>
public sealed class PcbDifferentialPair
{
    // Common primitive parameter prefix.
    /// <summary>Selection flag (transient; FALSE on disk).</summary>
    public bool Selection { get; set; }
    /// <summary>Layer token (typically <c>TOP</c>).</summary>
    public string Layer { get; set; } = "TOP";
    /// <summary>Whether the pair is locked.</summary>
    public bool Locked { get; set; }
    /// <summary>Polygon-outline flag.</summary>
    public bool PolygonOutline { get; set; }
    /// <summary>User-routed flag.</summary>
    public bool UserRouted { get; set; } = true;
    /// <summary>Keepout flag.</summary>
    public bool Keepout { get; set; }
    /// <summary>Union index.</summary>
    public int UnionIndex { get; set; }

    /// <summary>Positive net name.</summary>
    public string PositiveNetName { get; set; } = string.Empty;

    /// <summary>Negative net name.</summary>
    public string NegativeNetName { get; set; } = string.Empty;

    /// <summary>Differential pair name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gather-control flag.</summary>
    public bool GatherControl { get; set; }

    /// <summary>Unique identifier.</summary>
    public string UniqueId { get; set; } = string.Empty;
}

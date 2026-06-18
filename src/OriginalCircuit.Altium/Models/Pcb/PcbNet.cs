namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// A per-layer minimum-routed-width entry of a <see cref="PcbNet"/> (the <c>{layer}_MRWIDTH</c> keys
/// Altium writes for nets on multilayer boards). <see cref="LayerKey"/> is the key prefix
/// (e.g. <c>TOPLAYER</c>, <c>MIDLAYER1</c>, <c>BOTTOMLAYER</c>); <see cref="WidthUnits"/> is the width
/// in internal coordinate units (1 mil = 10000), stored raw to round-trip the mil text exactly.
/// </summary>
public sealed class PcbNetLayerWidth
{
    public string LayerKey { get; set; } = string.Empty;
    public int WidthUnits { get; set; }
}

/// <summary>
/// Represents a PCB net (the <c>Nets6</c> storage). Fully typed so it round-trips byte-for-byte and is
/// authored from scratch without replaying the raw parameter block.
/// </summary>
public sealed class PcbNet
{
    // Common primitive parameter prefix (shared with Classes6 and other parameter-block records).
    /// <summary>Selection flag (transient; FALSE on disk).</summary>
    public bool Selection { get; set; }
    /// <summary>Layer name token (e.g. <c>TOP</c>).</summary>
    public string Layer { get; set; } = "TOP";
    /// <summary>Whether the net is locked.</summary>
    public bool Locked { get; set; }
    /// <summary>Polygon-outline flag.</summary>
    public bool PolygonOutline { get; set; }
    /// <summary>Whether the net was user-routed.</summary>
    public bool UserRouted { get; set; } = true;
    /// <summary>Keepout flag.</summary>
    public bool Keepout { get; set; }
    /// <summary>Union index (-1/0).</summary>
    public int UnionIndex { get; set; }
    /// <summary>Primitive-lock flag.</summary>
    public bool PrimitiveLock { get; set; }

    /// <summary>Net name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the net is visible.</summary>
    public bool Visible { get; set; } = true;
    /// <summary>Net color (Win32 BGR integer).</summary>
    public int Color { get; set; }
    /// <summary>Loop-removal flag.</summary>
    public bool LoopRemoval { get; set; } = true;
    /// <summary>Override-color-for-draw flag.</summary>
    public bool OverrideColorForDraw { get; set; }

    /// <summary>Target routed length in internal units (the <c>TARGETLENGTH</c> mil key); null when absent.</summary>
    public int? TargetLengthUnits { get; set; }

    /// <summary>
    /// Per-layer minimum routed widths (the <c>{layer}_MRWIDTH</c> block). Null/empty when the source
    /// net had no such block (typical for simple 2-layer boards); populated for multilayer boards.
    /// </summary>
    public List<PcbNetLayerWidth>? LayerMinRoutedWidths { get; set; }

    /// <summary>Minimum routed via size in internal units (<c>MRVIASIZE</c> mil key); null when absent.</summary>
    public int? MinRoutedViaSizeUnits { get; set; }
    /// <summary>Minimum routed via hole in internal units (<c>MRVIAHOLE</c> mil key); null when absent.</summary>
    public int? MinRoutedViaHoleUnits { get; set; }

    /// <summary>Unique 8-character identity token; minted fresh for new nets, round-tripped otherwise.</summary>
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>Whether net jumpers are visible.</summary>
    public bool JumpersVisible { get; set; } = true;

    /// <summary>Routed length cache in internal units (<c>ROUTEDLENGTH</c>); null when absent.</summary>
    public int? RoutedLength { get; set; }

    /// <summary>Manhattan length cache (only emitted when present in the source).</summary>
    public int? ManhattanLength { get; set; }

    // Routing-analysis cache values, written only on boards with length/delay analysis. Doubles are
    // stored as-read and re-emitted with 15 significant digits (Delphi FloatToStr precision).
    /// <summary>Signal length cache in internal units (<c>SIGNALLENGTH</c>); null when absent.</summary>
    public int? SignalLength { get; set; }
    /// <summary>Signal delay cache in seconds (<c>SIGNALDELAY</c>); null when absent.</summary>
    public double? SignalDelay { get; set; }
    /// <summary>Total delay cache in seconds (<c>DELAYTOTAL</c>); null when absent.</summary>
    public double? DelayTotal { get; set; }
    /// <summary>Total current cache (<c>CURRENTTOTAL</c>); null when absent.</summary>
    public double? CurrentTotal { get; set; }
    /// <summary>Total resistance cache (<c>RESISTANCETOTAL</c>); null when absent.</summary>
    public double? ResistanceTotal { get; set; }
}

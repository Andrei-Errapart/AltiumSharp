using System.Collections.Generic;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic harness connector (record type 215): the harness box that groups signals.
/// Its harness type label (217) and entries (216) are separate records that reference it by owner index.
/// </summary>
public sealed class SchHarnessConnector
{
    /// <summary>First corner (location) of the connector box.</summary>
    public CoordPoint Corner1 { get; set; }

    /// <summary>Second (opposite) corner of the connector box.</summary>
    public CoordPoint Corner2 { get; set; }

    /// <summary>Top-left anchor of the connector body (Altium <c>Location</c>); the body extends right
    /// by <see cref="XSize"/> and down by <see cref="YSize"/>.</summary>
    public CoordPoint Location { get; set; }

    /// <summary>Width of the connector body.</summary>
    public Coord XSize { get; set; }

    /// <summary>Height of the connector body.</summary>
    public Coord YSize { get; set; }

    /// <summary>Distance from the top of the body to the bundle (signal-harness) connection point.</summary>
    public Coord PrimaryConnectionPosition { get; set; }

    /// <summary>Line width index (0=Small, 1=Medium, 2=Large, 3=ExtraLarge).</summary>
    public int LineWidth { get; set; }

    /// <summary>The harness entries belonging to this connector (the bundle members).</summary>
    public List<SchHarnessEntry> Entries { get; } = new();

    /// <summary>The harness type label (e.g. "ETH_RGMII") shown on the connector, if any.</summary>
    public SchHarnessType? TypeLabel { get; set; }

    /// <summary>Border color (BGR integer).</summary>
    public int Color { get; set; }

    /// <summary>Fill (area) color.</summary>
    public int AreaColor { get; set; }

    /// <summary>Owner index linking to a parent record (-1 for sheet-level).</summary>
    public int OwnerIndex { get; set; } = -1;

    /// <summary>Owner part ID (-1 for sheet-level).</summary>
    public int OwnerPartId { get; set; } = -1;

    /// <summary>Index of this record within the sheet.</summary>
    public int IndexInSheet { get; set; }

    /// <summary>Whether the record is locked from selection.</summary>
    public bool IsNotAccessible { get; set; }

    /// <summary>Unique identifier.</summary>
    public string? UniqueId { get; set; }
}

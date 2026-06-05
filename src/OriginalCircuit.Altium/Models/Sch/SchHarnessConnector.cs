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

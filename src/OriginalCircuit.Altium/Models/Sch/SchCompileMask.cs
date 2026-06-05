using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic compile mask (record type 211): a rectangular region that excludes its
/// contents from compilation/ERC.
/// </summary>
public sealed class SchCompileMask
{
    /// <summary>First corner (location) of the mask rectangle.</summary>
    public CoordPoint Corner1 { get; set; }

    /// <summary>Second (opposite) corner of the mask rectangle.</summary>
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

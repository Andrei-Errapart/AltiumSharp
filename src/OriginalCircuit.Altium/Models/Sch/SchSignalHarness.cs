using System.Collections.Generic;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic signal harness (record type 218): the bundle wire that connects harness
/// connectors. Its path is defined by a list of vertices.
/// </summary>
public sealed class SchSignalHarness
{
    /// <summary>The harness path vertices.</summary>
    public List<CoordPoint> Vertices { get; } = new();

    /// <summary>Line color (BGR integer).</summary>
    public int Color { get; set; }

    /// <summary>Line width index (0=Small, 1=Medium, 2=Large, 3=ExtraLarge).</summary>
    public int LineWidth { get; set; }

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

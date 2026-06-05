using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic harness type label (record type 217): the harness type name shown on a
/// harness connector. References its owning connector (215) by <see cref="OwnerIndex"/>.
/// </summary>
public sealed class SchHarnessType
{
    /// <summary>The harness type name.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The label location.</summary>
    public CoordPoint Location { get; set; }

    /// <summary>Border color (BGR integer).</summary>
    public int Color { get; set; }

    /// <summary>Text color.</summary>
    public int TextColor { get; set; }

    /// <summary>Font identifier for the label text.</summary>
    public int FontId { get; set; }

    /// <summary>Owner index of the harness connector this label belongs to.</summary>
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

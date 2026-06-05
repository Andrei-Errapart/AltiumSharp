using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic hyperlink (record type 226): a text label that carries a clickable URL.
/// </summary>
public sealed class SchHyperlink
{
    /// <summary>The displayed text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The hyperlink target URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The label location.</summary>
    public CoordPoint Location { get; set; }

    /// <summary>Text color (BGR integer).</summary>
    public int Color { get; set; }

    /// <summary>Font identifier into the sheet font table.</summary>
    public int FontId { get; set; }

    /// <summary>Orientation (0-3, in 90° steps).</summary>
    public int Orientation { get; set; }

    /// <summary>Text justification.</summary>
    public int Justification { get; set; }

    /// <summary>Area (fill) color.</summary>
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

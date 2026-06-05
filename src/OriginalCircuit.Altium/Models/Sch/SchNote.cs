using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic note (record type 209): a text frame annotation with an author, used for
/// design notes that are not part of the electrical design.
/// </summary>
public sealed class SchNote
{
    /// <summary>The note text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The note author.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>First corner (location) of the note rectangle.</summary>
    public CoordPoint Corner1 { get; set; }

    /// <summary>Second (opposite) corner of the note rectangle.</summary>
    public CoordPoint Corner2 { get; set; }

    /// <summary>Border color (BGR integer).</summary>
    public int Color { get; set; }

    /// <summary>Fill (area) color.</summary>
    public int AreaColor { get; set; }

    /// <summary>Text color.</summary>
    public int TextColor { get; set; }

    /// <summary>Font identifier into the sheet font table.</summary>
    public int FontId { get; set; }

    /// <summary>Text alignment.</summary>
    public int Alignment { get; set; }

    /// <summary>Whether text wraps within the rectangle.</summary>
    public bool WordWrap { get; set; }

    /// <summary>Whether text is clipped to the rectangle.</summary>
    public bool ClipToRect { get; set; }

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

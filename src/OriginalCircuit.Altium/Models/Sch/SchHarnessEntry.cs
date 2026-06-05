using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic harness entry (record type 216): a named entry into a harness connector.
/// References its owning connector (215) by <see cref="OwnerIndex"/>.
/// </summary>
public sealed class SchHarnessEntry
{
    /// <summary>The entry name.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Which side of the connector the entry is on.</summary>
    public int Side { get; set; }

    /// <summary>Distance of the entry from the top of the connector.</summary>
    public Coord DistanceFromTop { get; set; }

    /// <summary>Text/border color (BGR integer).</summary>
    public int Color { get; set; }

    /// <summary>Fill (area) color.</summary>
    public int AreaColor { get; set; }

    /// <summary>Text color.</summary>
    public int TextColor { get; set; }

    /// <summary>Font identifier for the entry text.</summary>
    public int TextFontId { get; set; }

    /// <summary>Owner index of the harness connector this entry belongs to.</summary>
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

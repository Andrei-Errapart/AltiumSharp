using OriginalCircuit.Altium.Generators;

namespace OriginalCircuit.Altium.Serialization.Dto.Sch;

/// <summary>
/// Data transfer object representing a schematic hyperlink record (a label carrying a URL).
/// Record type 226 in Altium schematic files.
/// </summary>
[AltiumRecord("226")]
internal sealed partial record SchHyperlinkDto
{
    [AltiumParameter("OWNERINDEX")]
    public int OwnerIndex { get; init; } = -1;

    [AltiumParameter("ISNOTACCESIBLE")]
    public bool IsNotAccessible { get; init; }

    [AltiumParameter("INDEXINSHEET")]
    public int IndexInSheet { get; init; }

    [AltiumParameter("OWNERPARTID")]
    public int OwnerPartId { get; init; } = -1;

    [AltiumParameter("OWNERPARTDISPLAYMODE")]
    public int OwnerPartDisplayMode { get; init; }

    [AltiumParameter("LOCATION.X")]
    public int LocationX { get; init; }

    [AltiumParameter("LOCATION.X_FRAC")]
    public int LocationXFrac { get; init; }

    [AltiumParameter("LOCATION.Y")]
    public int LocationY { get; init; }

    [AltiumParameter("LOCATION.Y_FRAC")]
    public int LocationYFrac { get; init; }

    [AltiumParameter("COLOR")]
    public int Color { get; init; }

    [AltiumParameter("TEXT")]
    public string Text { get; init; } = string.Empty;

    [AltiumParameter("URL")]
    public string Url { get; init; } = string.Empty;

    [AltiumParameter("FONTID")]
    public int FontId { get; init; }

    [AltiumParameter("ORIENTATION")]
    public int Orientation { get; init; }

    [AltiumParameter("JUSTIFICATION")]
    public int Justification { get; init; }

    [AltiumParameter("AREACOLOR")]
    public int AreaColor { get; init; }

    [AltiumParameter("UNIQUEID")]
    public string? UniqueId { get; init; }
}

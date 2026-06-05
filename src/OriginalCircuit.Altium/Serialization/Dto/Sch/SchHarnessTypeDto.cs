using OriginalCircuit.Altium.Generators;

namespace OriginalCircuit.Altium.Serialization.Dto.Sch;

/// <summary>
/// Data transfer object representing a schematic harness type record (the harness type label).
/// Record type 217 in Altium schematic files; owned by a harness connector (215).
/// </summary>
[AltiumRecord("217")]
internal sealed partial record SchHarnessTypeDto
{
    [AltiumParameter("OWNERINDEX")]
    public int OwnerIndex { get; init; } = -1;

    [AltiumParameter("ISNOTACCESIBLE")]
    public bool IsNotAccessible { get; init; }

    [AltiumParameter("INDEXINSHEET")]
    public int IndexInSheet { get; init; }

    [AltiumParameter("OWNERPARTID")]
    public int OwnerPartId { get; init; } = -1;

    [AltiumParameter("LOCATION.X")]
    public int LocationX { get; init; }

    [AltiumParameter("LOCATION.X_FRAC")]
    public int LocationXFrac { get; init; }

    [AltiumParameter("LOCATION.Y")]
    public int LocationY { get; init; }

    [AltiumParameter("LOCATION.Y_FRAC")]
    public int LocationYFrac { get; init; }

    [AltiumParameter("TEXT")]
    public string Text { get; init; } = string.Empty;

    [AltiumParameter("COLOR")]
    public int Color { get; init; }

    [AltiumParameter("TEXTCOLOR")]
    public int TextColor { get; init; }

    [AltiumParameter("FONTID")]
    public int FontId { get; init; }

    [AltiumParameter("UNIQUEID")]
    public string? UniqueId { get; init; }
}

using OriginalCircuit.Altium.Generators;

namespace OriginalCircuit.Altium.Serialization.Dto.Sch;

/// <summary>
/// Data transfer object representing a schematic harness entry record (a named entry into a harness
/// connector). Record type 216 in Altium schematic files; owned by a harness connector (215).
/// </summary>
[AltiumRecord("216")]
internal sealed partial record SchHarnessEntryDto
{
    [AltiumParameter("OWNERINDEX")]
    public int OwnerIndex { get; init; } = -1;

    [AltiumParameter("ISNOTACCESIBLE")]
    public bool IsNotAccessible { get; init; }

    [AltiumParameter("INDEXINSHEET")]
    public int IndexInSheet { get; init; }

    [AltiumParameter("OWNERPARTID")]
    public int OwnerPartId { get; init; } = -1;

    [AltiumParameter("DISTANCEFROMTOP")]
    public int DistanceFromTop { get; init; }

    [AltiumParameter("DISTANCEFROMTOP_FRAC1")]
    public int DistanceFromTopFrac { get; init; }

    [AltiumParameter("SIDE")]
    public int Side { get; init; }

    [AltiumParameter("TEXT")]
    public string Text { get; init; } = string.Empty;

    [AltiumParameter("COLOR")]
    public int Color { get; init; }

    [AltiumParameter("AREACOLOR")]
    public int AreaColor { get; init; }

    [AltiumParameter("TEXTCOLOR")]
    public int TextColor { get; init; }

    [AltiumParameter("TEXTFONTID")]
    public int TextFontId { get; init; }

    [AltiumParameter("UNIQUEID")]
    public string? UniqueId { get; init; }
}

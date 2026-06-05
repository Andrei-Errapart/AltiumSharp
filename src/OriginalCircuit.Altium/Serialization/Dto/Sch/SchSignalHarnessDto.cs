using OriginalCircuit.Altium.Generators;

namespace OriginalCircuit.Altium.Serialization.Dto.Sch;

/// <summary>
/// Data transfer object representing a schematic signal harness record (a harness bundle wire).
/// Record type 218 in Altium schematic files. Vertices are stored as indexed X{n}/Y{n} parameters.
/// </summary>
[AltiumRecord("218")]
internal sealed partial record SchSignalHarnessDto
{
    [AltiumParameter("OWNERINDEX")]
    public int OwnerIndex { get; init; } = -1;

    [AltiumParameter("ISNOTACCESIBLE")]
    public bool IsNotAccessible { get; init; }

    [AltiumParameter("INDEXINSHEET")]
    public int IndexInSheet { get; init; }

    [AltiumParameter("OWNERPARTID")]
    public int OwnerPartId { get; init; } = -1;

    [AltiumParameter("COLOR")]
    public int Color { get; init; }

    [AltiumParameter("LINEWIDTH")]
    public int LineWidth { get; init; }

    [AltiumParameter("LOCATIONCOUNT")]
    public int LocationCount { get; init; }

    [AltiumParameter("UNIQUEID")]
    public string? UniqueId { get; init; }
}

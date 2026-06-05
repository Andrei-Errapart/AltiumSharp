using OriginalCircuit.Altium.Generators;

namespace OriginalCircuit.Altium.Serialization.Dto.Sch;

/// <summary>
/// Data transfer object representing a schematic harness connector record (the harness box).
/// Record type 215 in Altium schematic files.
/// </summary>
[AltiumRecord("215")]
internal sealed partial record SchHarnessConnectorDto
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

    [AltiumParameter("CORNER.X")]
    public int CornerX { get; init; }

    [AltiumParameter("CORNER.X_FRAC")]
    public int CornerXFrac { get; init; }

    [AltiumParameter("CORNER.Y")]
    public int CornerY { get; init; }

    [AltiumParameter("CORNER.Y_FRAC")]
    public int CornerYFrac { get; init; }

    [AltiumParameter("COLOR")]
    public int Color { get; init; }

    [AltiumParameter("AREACOLOR")]
    public int AreaColor { get; init; }

    [AltiumParameter("UNIQUEID")]
    public string? UniqueId { get; init; }
}

using OriginalCircuit.Altium.Generators;

namespace OriginalCircuit.Altium.Serialization.Dto.Sch;

/// <summary>
/// Data transfer object representing a schematic compile-mask region record.
/// Record type 211 in Altium schematic files; marks an area excluded from compilation.
/// </summary>
[AltiumRecord("211")]
internal sealed partial record SchCompileMaskDto
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

using OriginalCircuit.Altium.Generators;

namespace OriginalCircuit.Altium.Serialization.Dto.Sch;

/// <summary>
/// Data transfer object representing a schematic sheet-template reference record.
/// Record type 39 in Altium schematic files; points at the .SchDot template applied to the sheet.
/// </summary>
[AltiumRecord("39")]
internal sealed partial record SchTemplateDto
{
    /// <summary>
    /// Gets the owner index linking this record to its parent (templates are sheet-level, usually -1).
    /// </summary>
    [AltiumParameter("OWNERINDEX")]
    public int OwnerIndex { get; init; } = -1;

    /// <summary>
    /// Gets the index of this record within the sheet.
    /// </summary>
    [AltiumParameter("INDEXINSHEET")]
    public int IndexInSheet { get; init; }

    /// <summary>
    /// Gets whether the template is not accessible (locked from selection).
    /// </summary>
    [AltiumParameter("ISNOTACCESIBLE")]
    public bool IsNotAccessible { get; init; }

    /// <summary>
    /// Gets the owner part ID (templates are sheet-level, usually -1).
    /// </summary>
    [AltiumParameter("OWNERPARTID")]
    public int OwnerPartId { get; init; } = -1;

    /// <summary>
    /// Gets the path to the .SchDot template file applied to the sheet.
    /// </summary>
    [AltiumParameter("FILENAME")]
    public string FileName { get; init; } = string.Empty;
}

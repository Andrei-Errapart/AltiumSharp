using OriginalCircuit.Altium.Models.Sch;

namespace OriginalCircuit.Altium.Models.Project;

/// <summary>
/// One placed schematic component record on one sheet instance. A multi-part component (U1A,
/// U1B, ...) has one part per placed record; parts with the same physical designator and different
/// part numbers form one <see cref="PhysicalComponent"/>.
/// </summary>
public sealed class PhysicalComponentPart
{
    internal PhysicalComponentPart(SchComponent component, HierarchySheetInstance sheet,
        string logicalDesignator, string physicalDesignator)
    {
        Component = component;
        Sheet = sheet;
        LogicalDesignator = logicalDesignator;
        PhysicalDesignator = physicalDesignator;
        UniqueIdPath = sheet.UniqueIdPath + "\\" + component.UniqueId;
    }

    /// <summary>The component record.</summary>
    public SchComponent Component { get; }

    /// <summary>The sheet instance the record is placed on.</summary>
    public HierarchySheetInstance Sheet { get; }

    /// <summary>The designator as drawn on the sheet (the same in every channel), e.g. <c>"R2"</c>.</summary>
    public string LogicalDesignator { get; }

    /// <summary>The designator in the compiled design, e.g. <c>"R2_CH3"</c> in a channel, else the logical one.</summary>
    public string PhysicalDesignator { get; }

    /// <summary>The part number within a multi-part component (1 = A, 2 = B, ...).</summary>
    public int PartId => Component.CurrentPartId;

    /// <summary>
    /// The instance path of this record, e.g. <c>"\5ABCDEFGH\IJKLMNOP"</c>: the sheet's
    /// <see cref="HierarchySheetInstance.UniqueIdPath"/> plus the component's unique id. Equal to the
    /// PCB component's <c>SourceUniqueId</c> (which carries one part's id) and a variant's <c>UniqueId</c>.
    /// </summary>
    public string UniqueIdPath { get; }

    /// <summary>The physical component this part belongs to.</summary>
    public PhysicalComponent Owner { get; internal set; } = null!;

    /// <inheritdoc/>
    public override string ToString() => $"{PhysicalDesignator} part {PartId} on {Sheet}";
}

/// <summary>
/// A component of the compiled design: the placed parts of one component (one physical designator,
/// each part number once, from one library symbol), merged across sheets. This is what a BOM line
/// counts and what the PCB places once. Separate components given the same designator stay separate
/// (see <see cref="ProjectHierarchy.DuplicateDesignators"/>).
/// </summary>
public sealed class PhysicalComponent
{
    private readonly ProjectHierarchy _hierarchy;

    internal PhysicalComponent(ProjectHierarchy hierarchy, IReadOnlyList<PhysicalComponentPart> parts)
    {
        _hierarchy = hierarchy;
        Parts = parts;
        foreach (var part in parts)
            part.Owner = this;
    }

    /// <summary>The physical designator, e.g. <c>"R2_CH3"</c>, or the logical one outside channels.</summary>
    public string Designator => Parts[0].PhysicalDesignator;

    /// <summary>The logical designator drawn on the sheet, e.g. <c>"R2"</c>.</summary>
    public string LogicalDesignator => Parts[0].LogicalDesignator;

    /// <summary>The placed parts, ordered by part number.</summary>
    public IReadOnlyList<PhysicalComponentPart> Parts { get; }

    /// <summary>The record of the first part, which carries the component's parameters.</summary>
    public SchComponent Component => Parts[0].Component;

    /// <summary>The sheet instance of the first part.</summary>
    public HierarchySheetInstance Sheet => Parts[0].Sheet;

    /// <summary>The effective component type (see <see cref="SchComponent.ComponentType"/>).</summary>
    public ComponentType ComponentType => Component.ComponentType;

    /// <summary>Whether Altium lists this component in a BOM, judged by <see cref="ComponentType"/> alone.</summary>
    public bool IsInBom => ComponentType.IsInBom();

    /// <summary>The comment with its <c>=Name</c> reference resolved against the component, its sheet and the project.</summary>
    public string? Comment => Component.GetResolvedComment(Sheet.Document, _hierarchy.Project);

    /// <summary>The instance paths of every part (see <see cref="PhysicalComponentPart.UniqueIdPath"/>).</summary>
    public IEnumerable<string> UniqueIdPaths => Parts.Select(p => p.UniqueIdPath);

    /// <summary>
    /// Returns a parameter of the component with its <c>=Name</c> reference resolved against the
    /// component, its sheet and the project; <c>null</c> when the component has no such parameter.
    /// </summary>
    public string? GetParameterValue(string name) =>
        Component.GetResolvedParameterValue(name, Sheet.Document, _hierarchy.Project);

    /// <inheritdoc/>
    public override string ToString() => Parts.Count == 1 ? Designator : $"{Designator} ({Parts.Count} parts)";
}

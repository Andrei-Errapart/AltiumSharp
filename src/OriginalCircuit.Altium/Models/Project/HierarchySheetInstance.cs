using OriginalCircuit.Altium.Models.Sch;

namespace OriginalCircuit.Altium.Models.Project;

/// <summary>
/// One instance of a schematic sheet in a <see cref="ProjectHierarchy"/>. A sheet used by several
/// sheet symbols, or by a <c>Repeat(...)</c> symbol, has one instance per use: each is a channel
/// with its own room and physical designators.
/// </summary>
public sealed class HierarchySheetInstance
{
    private readonly List<HierarchySheetInstance> _children = new();
    private readonly List<PhysicalComponentPart> _parts = new();

    internal HierarchySheetInstance(
        int index, SchDocument document, string fileName, HierarchySheetInstance? parent,
        SchSheetSymbol? sheetSymbol, int? repeatIndex, IReadOnlyList<string> symbolUniqueIds)
    {
        Index = index;
        Document = document;
        FileName = fileName;
        Parent = parent;
        SheetSymbol = sheetSymbol;
        RepeatIndex = repeatIndex;
        SymbolUniqueIds = symbolUniqueIds;
        UniqueIdPath = symbolUniqueIds.Count == 0 ? string.Empty : "\\" + string.Join("\\", symbolUniqueIds);
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    /// <summary>The position of this instance in <see cref="ProjectHierarchy.Sheets"/> (depth-first order).</summary>
    public int Index { get; }

    /// <summary>The schematic document this is an instance of.</summary>
    public SchDocument Document { get; }

    /// <summary>The document's file name (e.g. <c>"ADC.SchDoc"</c>).</summary>
    public string FileName { get; }

    /// <summary>The instance whose sheet symbol created this one, or <c>null</c> for a root.</summary>
    public HierarchySheetInstance? Parent { get; }

    /// <summary>The sheet instances created by this sheet's sheet symbols, in numbering order.</summary>
    public IReadOnlyList<HierarchySheetInstance> Children => _children;

    /// <summary>The sheet symbol (on the parent sheet) that created this instance, or <c>null</c> for a root.</summary>
    public SchSheetSymbol? SheetSymbol { get; }

    /// <summary>
    /// The sheet symbol's designator as written, e.g. <c>"U_Power"</c> or <c>"Repeat(CH,1,8)"</c>;
    /// <c>null</c> for a root.
    /// </summary>
    public string? SymbolDesignator => SheetSymbol is null ? null : SheetSymbol.NameLabel?.Text ?? SheetSymbol.SheetName;

    /// <summary>The channel index <c>k</c> of a <c>Repeat(name, first, last)</c> instance, otherwise <c>null</c>.</summary>
    public int? RepeatIndex { get; }

    /// <summary>
    /// The channel name: the <c>Repeat</c> name for a repeated symbol (e.g. <c>"CH"</c>), otherwise
    /// the symbol's designator (falling back to the file name without extension); <c>null</c> for a root.
    /// </summary>
    public string? ChannelName { get; internal set; }

    /// <summary>
    /// The room name Altium gives this instance: the channel name plus <see cref="ChannelIndex"/>
    /// when one is assigned (e.g. <c>"CH3"</c> or <c>"U_Driver2"</c>), otherwise the symbol's
    /// designator; <c>null</c> for a root.
    /// </summary>
    public string? RoomName { get; internal set; }

    /// <summary>
    /// The number in <see cref="RoomName"/>: <see cref="RepeatIndex"/> for a repeated symbol, or the
    /// position (1-based, across the whole design in sheet-position order) among the instances whose
    /// symbols share one designator. <c>null</c> when the designator is unique.
    /// </summary>
    public int? ChannelIndex { get; internal set; }

    /// <summary>
    /// Whether this sheet's document is instantiated more than once in the design. Its components
    /// then get physical designators built from <see cref="RoomName"/> (see
    /// <see cref="ProjectHierarchy.DesignatorFormat"/>); otherwise they keep their logical designators.
    /// </summary>
    public bool IsMultiChannel { get; internal set; }

    /// <summary>
    /// The sheet-symbol unique ids from the root to this instance. A <c>Repeat</c> channel's entry is
    /// its index followed by the symbol's id (e.g. <c>"5ABCDEFGH"</c>), as Altium writes it.
    /// </summary>
    public IReadOnlyList<string> SymbolUniqueIds { get; }

    /// <summary>
    /// <see cref="SymbolUniqueIds"/> as Altium writes it, e.g. <c>"\ABCDEFGH\5IJKLMNOP"</c> (empty for a
    /// root). A component's PCB <c>SourceUniqueId</c> and a variant's <c>UniqueId</c> are this path
    /// followed by <c>\</c> and the component's own unique id.
    /// </summary>
    public string UniqueIdPath { get; }

    /// <summary>The nesting depth (0 for a root).</summary>
    public int Depth { get; }

    /// <summary>
    /// The chain of sheet-symbol designators from the root, separated by <c>\</c> and starting with
    /// the root sheet's file name without extension; a <c>Repeat</c> channel appears as its channel
    /// name and index (e.g. <c>"Top\U_ADC\CH3"</c>). This equals a PCB component's
    /// <c>SourceHierarchicalPath</c>. Unlike <see cref="RoomName"/> it does not number duplicated
    /// symbols, so it does not by itself tell their instances apart.
    /// </summary>
    public string HierarchicalPath => Parent is null
        ? System.IO.Path.GetFileNameWithoutExtension(FileName)
        : Parent.HierarchicalPath + "\\" + ChannelName
          + (RepeatIndex is { } k ? k.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);

    /// <summary>The component parts placed on this instance, in document order.</summary>
    public IReadOnlyList<PhysicalComponentPart> Parts => _parts;

    internal void AddChild(HierarchySheetInstance child) => _children.Add(child);
    internal void AddPart(PhysicalComponentPart part) => _parts.Add(part);

    /// <inheritdoc/>
    public override string ToString() => RoomName is null ? FileName : $"{RoomName} -> {FileName}";
}

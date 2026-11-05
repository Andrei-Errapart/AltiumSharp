using System.Globalization;
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
    // The children by the last entry of their SymbolUniqueIds, each in creation order.
    private Dictionary<string, List<HierarchySheetInstance>>? _childrenBySegment;
    // The first part of each component unique id, built on the first lookup.
    private Dictionary<string, PhysicalComponentPart>? _partsByUid;
    private string? _uniqueIdPath;
    private string? _hierarchicalPath;

    internal HierarchySheetInstance(
        int index, SchDocument document, string fileName, string documentKey, HierarchySheetInstance? parent,
        SchSheetSymbol? sheetSymbol, int? repeatIndex, IReadOnlyList<string> symbolUniqueIds)
    {
        Index = index;
        Document = document;
        FileName = fileName;
        DocumentKey = documentKey;
        Parent = parent;
        SheetSymbol = sheetSymbol;
        RepeatIndex = repeatIndex;
        SymbolUniqueIds = symbolUniqueIds;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    /// <summary>The position of this instance in <see cref="ProjectHierarchy.Sheets"/> (depth-first order).</summary>
    public int Index { get; }

    /// <summary>The schematic document this is an instance of.</summary>
    public SchDocument Document { get; }

    /// <summary>The document's file name (e.g. <c>"ADC.SchDoc"</c>).</summary>
    public string FileName { get; }

    /// <summary>
    /// The key the hierarchy holds the document by (see <see cref="ProjectHierarchy.DocumentsByKey"/>):
    /// the location a loader read it from, such as its full path, or <see cref="FileName"/> for a
    /// document known by its file name. Instances of one document share it, and it tells apart
    /// documents in different folders that share a file name. Compare it case-insensitively.
    /// </summary>
    public string DocumentKey { get; }

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
    /// Whether this sheet's document (<see cref="DocumentKey"/>) is instantiated more than once in the design. Its components
    /// then get physical designators built from <see cref="RoomName"/> (see
    /// <see cref="ProjectHierarchy.DesignatorFormat"/>); otherwise they keep their logical designators.
    /// </summary>
    public bool IsMultiChannel { get; internal set; }

    // The position of this instance among the instances of its document (1-based, in Sheets order).
    internal int Ordinal { get; set; }

    /// <summary>
    /// The sheet-symbol unique ids from the root to this instance. A <c>Repeat</c> channel's entry is
    /// its index followed by the symbol's id (e.g. <c>"5ABCDEFGH"</c>), as Altium writes it.
    /// </summary>
    public IReadOnlyList<string> SymbolUniqueIds { get; }

    /// <summary>
    /// <see cref="SymbolUniqueIds"/> as Altium writes it, e.g. <c>"\ABCDEFGH\5IJKLMNOP"</c> (empty for a
    /// root). A component's PCB <c>SourceUniqueId</c> and a variant's <c>UniqueId</c> are this path
    /// followed by <c>\</c> and the component's own unique id. It is built when first read.
    /// </summary>
    public string UniqueIdPath => _uniqueIdPath ??= SymbolUniqueIds.Count == 0 ? string.Empty : "\\" + string.Join("\\", SymbolUniqueIds);

    /// <summary>The nesting depth (0 for a root).</summary>
    public int Depth { get; }

    /// <summary>
    /// The chain of sheet-symbol designators from the root, separated by <c>\</c> and starting with
    /// the root sheet's file name without extension; a <c>Repeat</c> channel appears as its channel
    /// name and index (e.g. <c>"Top\U_ADC\CH3"</c>). This equals a PCB component's
    /// <c>SourceHierarchicalPath</c>. Unlike <see cref="RoomName"/> it does not number duplicated
    /// symbols, so it does not by itself tell their instances apart. It is built when first read.
    /// </summary>
    public string HierarchicalPath
    {
        get
        {
            if (_hierarchicalPath is { } known)
                return known;
            // From the nearest ancestor whose path is known, without recursing.
            var chain = new Stack<HierarchySheetInstance>();
            for (var sheet = this; sheet is { _hierarchicalPath: null }; sheet = sheet.Parent)
                chain.Push(sheet);
            while (chain.TryPop(out var sheet))
            {
                sheet._hierarchicalPath = sheet.Parent is not { } parent || sheet.SheetSymbol is null
                    ? Path.GetFileNameWithoutExtension(sheet.FileName)
                    : parent._hierarchicalPath + "\\" + sheet.ChannelName
                      + (sheet.RepeatIndex is { } k ? k.ToString(CultureInfo.InvariantCulture) : string.Empty);
            }
            return _hierarchicalPath!;
        }
    }

    /// <summary>The component parts placed on this instance, in document order.</summary>
    public IReadOnlyList<PhysicalComponentPart> Parts => _parts;

    internal void AddChild(HierarchySheetInstance child)
    {
        _children.Add(child);
        var segment = child.SymbolUniqueIds[^1];
        _childrenBySegment ??= new Dictionary<string, List<HierarchySheetInstance>>(StringComparer.OrdinalIgnoreCase);
        if (!_childrenBySegment.TryGetValue(segment, out var same))
            _childrenBySegment[segment] = same = new List<HierarchySheetInstance>();
        same.Add(child);
    }

    // The children whose last SymbolUniqueIds entry is the segment (ignoring case), or null.
    internal List<HierarchySheetInstance>? ChildrenWithSegment(string segment) =>
        _childrenBySegment?.GetValueOrDefault(segment);

    internal void AddPart(PhysicalComponentPart part) => _parts.Add(part);

    // The first part whose component has the unique id (ignoring case; empty for none), or null.
    internal PhysicalComponentPart? PartWithUniqueId(string uniqueId) =>
        LazyInitializer.EnsureInitialized(ref _partsByUid, () =>
        {
            var parts = new Dictionary<string, PhysicalComponentPart>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in _parts)
                parts.TryAdd(part.Component.UniqueId ?? string.Empty, part);
            return parts;
        }).GetValueOrDefault(uniqueId);

    /// <inheritdoc/>
    public override string ToString() => RoomName is null ? FileName : $"{RoomName} -> {FileName}";
}

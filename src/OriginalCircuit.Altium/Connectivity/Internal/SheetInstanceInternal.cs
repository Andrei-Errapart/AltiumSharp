using OriginalCircuit.Altium.Models.Sch;

namespace OriginalCircuit.Altium.Connectivity.Internal;

/// <summary>One instantiated sheet in the netlist solve (carries its solved graph); built from a <see cref="Models.Project.HierarchySheetInstance"/>.</summary>
internal sealed class SheetInstanceInternal
{
    public SheetInstanceInternal(
        int id, SchDocument doc, string fileName, string? designator, string path, int? parentId,
        IReadOnlyList<string> symbolUidPath, string? channelName, int? channelIndex, bool isRepeated)
    {
        Id = id;
        Doc = doc;
        FileName = fileName;
        Designator = designator;
        Path = path;
        ParentId = parentId;
        SymbolUidPath = symbolUidPath;
        ChannelName = channelName;
        ChannelIndex = channelIndex;
        IsRepeated = isRepeated;
    }

    public int Id { get; }
    public SchDocument Doc { get; }
    public string FileName { get; }
    public string? Designator { get; }
    public string Path { get; }
    public int? ParentId { get; }

    /// <summary>The chain of ancestor sheet-symbol UniqueIds from the root to this instance — the
    /// channel discriminator. Matches a PCB component's <c>SourceUniqueId</c> prefix.</summary>
    public IReadOnlyList<string> SymbolUidPath { get; }

    /// <summary>The channel name (Repeat channel name, or the repeated sheet symbol's designation).</summary>
    public string? ChannelName { get; }

    /// <summary>The 1-based channel index within a repeated group, or <c>null</c> when not a channel.</summary>
    public int? ChannelIndex { get; }

    /// <summary>Whether this instance is one of several channels of the same sheet under its parent.</summary>
    public bool IsRepeated { get; }

    public SheetGraph? Graph { get; set; }
}

/// <summary>A parent sheet symbol linking a parent instance to the child instance it created.</summary>
internal readonly record struct Boundary(SheetInstanceInternal Parent, SchSheetSymbol Symbol, SheetInstanceInternal Child);

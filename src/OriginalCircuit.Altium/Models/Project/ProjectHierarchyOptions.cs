using OriginalCircuit.Altium.Models.Sch;

namespace OriginalCircuit.Altium.Models.Project;

/// <summary>
/// Limits for building a <see cref="ProjectHierarchy"/>. A small crafted upload can describe a huge
/// or very deep design (nested <c>Repeat(...)</c> symbols, long chains of sheets), so the walk stops
/// at these bounds, reports an error in <see cref="ProjectHierarchy.Diagnostics"/> and sets
/// <see cref="ProjectHierarchy.IsTruncated"/>. The defaults are far above any real design.
/// </summary>
public sealed class ProjectHierarchyOptions
{
    private readonly int _maxDepth = 64;
    private readonly int _maxSheetInstances = 10_000;
    private readonly int _maxComponentParts = 200_000;
    private readonly int _maxUniqueIdLength = 255;
    private readonly int _maxDesignatorLength = 255;
    private readonly int _maxRoomNameLength = 255;

    /// <summary>The default limits.</summary>
    public static ProjectHierarchyOptions Default { get; } = new();

    /// <summary>
    /// The deepest sheet nesting followed; the top sheet is depth 0. Sheets below it are left out.
    /// Default 64.
    /// </summary>
    public int MaxDepth
    {
        get => _maxDepth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxDepth = value;
        }
    }

    /// <summary>The most sheet instances created; the walk stops there. Default 10,000.</summary>
    public int MaxSheetInstances
    {
        get => _maxSheetInstances;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxSheetInstances = value;
        }
    }

    /// <summary>
    /// Decides whether the hierarchy follows a sheet symbol; <c>null</c> (the default) follows every
    /// one. A symbol it does not follow places nothing: the loaders do not ask for its path, the choice
    /// of top sheets does not count it as placing its sheet, and the walk skips it with an
    /// <see cref="Diagnostics.AltiumDiagnosticCode.SheetSymbolNotFollowed"/> diagnostic (not
    /// <see cref="Diagnostics.AltiumDiagnosticCode.SubSheetNotSupplied"/>). Use it to leave out symbols
    /// without changing the documents. It may be asked more than once about a symbol and should give
    /// the same answer each time.
    /// </summary>
    public Func<SchSheetSymbol, bool>? FollowSheetSymbol { get; init; }

    /// <summary>
    /// The longest unique id taken: a sheet symbol's (or, for a symbol without one, the name its path
    /// uses instead) and a component's. A sheet symbol with a longer one is not followed and a component
    /// with a longer one is left out, with an error. Altium's unique ids have 8 characters. Default 255.
    /// </summary>
    public int MaxUniqueIdLength
    {
        get => _maxUniqueIdLength;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxUniqueIdLength = value;
        }
    }

    /// <summary>
    /// The longest component designator taken; a component with a longer one is left out, and a channel
    /// designator format longer than this is replaced by the default one, with a diagnostic. Default 255.
    /// </summary>
    public int MaxDesignatorLength
    {
        get => _maxDesignatorLength;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxDesignatorLength = value;
        }
    }

    /// <summary>
    /// The longest channel name taken (a <c>Repeat</c> name, or the designator a room is named after); a
    /// sheet symbol with a longer one is not followed, with an error. Default 255.
    /// </summary>
    public int MaxRoomNameLength
    {
        get => _maxRoomNameLength;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxRoomNameLength = value;
        }
    }

    /// <summary>
    /// The options the loaders (<c>ProjectHierarchy.LoadAsync</c>) read sheets with;
    /// <see cref="AltiumReaderOptions.Default"/> when <c>null</c>. Set
    /// <see cref="AltiumReaderOptions.ReadEmbeddedPayloads"/> to <c>false</c> to skip embedded images,
    /// which the hierarchy never needs.
    /// </summary>
    public AltiumReaderOptions? ReaderOptions { get; init; }

    internal bool Follows(SchSheetSymbol symbol) => FollowSheetSymbol?.Invoke(symbol) ?? true;

    /// <summary>
    /// The most placed component parts (<see cref="ProjectHierarchy.Parts"/>); the walk stops before a
    /// sheet instance that would exceed it. A small <c>Repeat(...)</c> over a large sheet otherwise
    /// produces millions of parts. Default 200,000.
    /// </summary>
    public int MaxComponentParts
    {
        get => _maxComponentParts;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxComponentParts = value;
        }
    }
}

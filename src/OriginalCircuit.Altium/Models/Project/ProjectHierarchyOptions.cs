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

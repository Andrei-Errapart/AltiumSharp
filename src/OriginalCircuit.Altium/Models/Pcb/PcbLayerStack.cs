namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Represents a single layer entry in the PCB layer stack.
/// </summary>
public sealed class PcbLayerEntry
{
    /// <summary>
    /// Layer index (1-based).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Layer name (e.g., "Top Layer", "Bottom Layer", "Mid-Layer 1").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Index of the previous layer in the stack.
    /// </summary>
    public int PreviousIndex { get; set; }

    /// <summary>
    /// Index of the next layer in the stack.
    /// </summary>
    public int NextIndex { get; set; }

    /// <summary>
    /// Whether copper is present on this layer.
    /// </summary>
    public bool CopperEnabled { get; set; }

    /// <summary>
    /// Dielectric material name (<c>DIELMATERIAL</c>) of the dielectric below this layer.
    /// </summary>
    public string DielectricMaterial { get; set; } = string.Empty;

    /// <summary>
    /// Layer color as packed integer.
    /// </summary>
    public int Color { get; set; }
}

/// <summary>
/// Represents the PCB layer stack parsed from Board6 parameters.
/// Provides convenient access to layer ordering and properties.
/// </summary>
public sealed class PcbLayerStack
{
    /// <summary>
    /// Ordered list of layer entries from top to bottom.
    /// </summary>
    public IReadOnlyList<PcbLayerEntry> Layers { get; }

    private PcbLayerStack(List<PcbLayerEntry> layers)
    {
        Layers = layers;
    }

    /// <summary>
    /// Parses a layer stack from Board6 parameters, or returns null when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy stack is one <c>LAYER{n}*</c> group per Altium layer id (<c>NAME</c>,
    /// <c>PREV</c>, <c>NEXT</c>, <c>COPTHICK</c>, <c>DIELTYPE</c>, <c>DIELMATERIAL</c>, ...),
    /// written with a <c>V7_</c> prefix by some Altium versions and without it by the boards
    /// seen so far (13 company boards checked; none had a <c>V7_</c> key, so this was null on
    /// every one of them). Either spelling is read, the prefixed one first.
    /// </para>
    /// <para>
    /// Every one of Altium's layers has a group, the unused mid-layers and the mechanical
    /// layers included (82 on TES0761); the stack is the chain that starts at the Top Layer
    /// (id 1) and follows <c>NEXT</c> until it ends, as KiCad's importer walks it - L01, L02,
    /// L03, L04 on TES0761 (ids 1, 4, 5, 32). Groups off the chain are not part of the stack.
    /// Only a board with no Top Layer group falls back to listing every group by id.
    /// </para>
    /// </remarks>
    public static PcbLayerStack? FromBoardParameters(Dictionary<string, string>? parameters)
    {
        if (parameters == null)
            return null;

        string? Get(int i, string key) =>
            parameters.TryGetValue($"V7_LAYER{i}{key}", out var prefixed) ? prefixed
            : parameters.TryGetValue($"LAYER{i}{key}", out var plain) ? plain
            : null;

        var entries = new Dictionary<int, PcbLayerEntry>();
        for (var i = 1; i <= 100; i++)
        {
            if (Get(i, "NAME") is not { } name)
                continue;

            var entry = new PcbLayerEntry { Index = i, Name = name };
            if (int.TryParse(Get(i, "PREV"), out var prevIdx))
                entry.PreviousIndex = prevIdx;
            if (int.TryParse(Get(i, "NEXT"), out var nextIdx))
                entry.NextIndex = nextIdx;
            if (Get(i, "COPTHICK") is { } cop)
                entry.CopperEnabled = cop != "0";
            entry.DielectricMaterial = Get(i, "DIELMATERIAL") ?? string.Empty;
            if (int.TryParse(Get(i, "COLOR"), out var c))
                entry.Color = c;

            entries[i] = entry;
        }

        if (entries.Count == 0)
            return null;

        // The stack is the chain from the Top Layer along NEXT.
        if (entries.TryGetValue(1, out var top))
        {
            var chain = new List<PcbLayerEntry>();
            var visited = new HashSet<int>();
            for (PcbLayerEntry? current = top; current != null && visited.Add(current.Index);)
            {
                chain.Add(current);
                current = current.NextIndex != 0 && entries.TryGetValue(current.NextIndex, out var next) ? next : null;
            }
            return new PcbLayerStack(chain);
        }

        return new PcbLayerStack(entries.Values.OrderBy(e => e.Index).ToList());
    }
}

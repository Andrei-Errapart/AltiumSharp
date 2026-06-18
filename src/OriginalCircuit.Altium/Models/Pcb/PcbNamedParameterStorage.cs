namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// A named PcbDoc parameter-block storage that holds editor/DRC settings or violation records — e.g.
/// <c>Design Rule Checker Options6</c>, <c>Advanced Placer Options6</c>, <c>Pin Swap Options6</c>,
/// <c>SimbeorCacheSection</c>, <c>TMatchedNetLengthsViolation</c>, <c>CustomShapes</c>,
/// <c>WaivedViolations</c>, <c>PinPairsSection</c>. Each is a <c>Header</c> + <c>Data</c> storage whose
/// Data is a sequence of length-prefixed <c>|KEY=VALUE|</c> blocks (the same framing as <c>Nets6</c>).
///
/// Modeled as typed parameter records instead of an opaque <c>AdditionalStreams</c> blob so the data is
/// fully readable and reproducible from scratch. The storage <c>Header</c> is captured verbatim because its
/// meaning varies by storage (a record count for some, a modified/dirty flag for others) and is not derivable.
/// </summary>
public sealed class PcbNamedParameterStorage
{
    /// <summary>The compound-file storage name (e.g. <c>"Design Rule Checker Options6"</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The storage <c>Header</c> value, preserved verbatim for byte-exact round-trip.</summary>
    internal int Header { get; set; }

    /// <summary>The parsed parameter records (one per length-prefixed block).</summary>
    public List<PcbParameterRecord> Records { get; } = new();
}

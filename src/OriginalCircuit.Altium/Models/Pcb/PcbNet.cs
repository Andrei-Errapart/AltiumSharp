namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Represents a PCB net.
/// </summary>
public sealed class PcbNet
{
    /// <summary>
    /// Net name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The net's full parameter block as an ordered key/value list (color, layer, visibility,
    /// routed state, etc.). Preserved verbatim for a byte-faithful round-trip; new nets fall back
    /// to emitting NAME only.
    /// </summary>
    internal List<KeyValuePair<string, string>>? RawParametersOrdered { get; set; }
}

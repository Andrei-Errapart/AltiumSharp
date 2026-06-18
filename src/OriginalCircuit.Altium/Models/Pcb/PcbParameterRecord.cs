namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// A generic typed parameter-block record (a parsed <c>|KEY=VALUE|</c> block) used for PcbDoc feature
/// storages whose records are pure parameter blocks (e.g. <c>PrimitiveParameters</c>, the per-component
/// user-parameter groups). Preserves the parsed parameters and original key order for byte-exact round-trip.
/// </summary>
public sealed class PcbParameterRecord
{
    /// <summary>The parsed parameters.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The parameter block as an ordered, authorable key/value list — the canonical representation,
    /// preserving key order and any duplicate keys the <see cref="Parameters"/> dictionary collapses.
    /// Written verbatim when set; null falls back to <see cref="Parameters"/>.
    /// </summary>
    public List<KeyValuePair<string, string>>? OrderedParameters { get; set; }
}

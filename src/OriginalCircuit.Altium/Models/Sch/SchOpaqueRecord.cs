namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// A schematic record whose type is not modelled by the library. Its raw parameter block is
/// captured on read so it can be re-emitted verbatim at its original position, letting unsupported
/// records (e.g. notes, hyperlinks, harness constructs in a SchLib) round-trip instead of being lost.
/// </summary>
internal sealed class SchOpaqueRecord
{
    public SchOpaqueRecord(Dictionary<string, string> parameters) => Parameters = parameters;

    /// <summary>The parsed parameters of the unmodelled record.</summary>
    public Dictionary<string, string> Parameters { get; }
}

namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// A PcbDoc <c>SignalClasses</c> record (an xSignal / net class). Modeled as a first-class typed record
/// — previously round-tripped as an opaque <c>AdditionalStreams</c> blob. The on-disk format is the same
/// record-framed parameter block as <c>Classes6</c>. See docs/decompile/feature-signal-classes.md.
/// </summary>
public sealed class PcbSignalClass
{
    /// <summary>Class name (e.g. "All xSignals").</summary>
    public string Name { get; set; } = "All xSignals";

    /// <summary>Class kind (default 10).</summary>
    public int Kind { get; set; } = 10;

    /// <summary>Unique id token.</summary>
    public string? UniqueId { get; set; }

    /// <summary>Whether this is a super-class (default true).</summary>
    public bool SuperClass { get; set; } = true;

    /// <summary>Whether the class is enabled (default true).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>All parsed parameters (preserves keys the typed fields don't model).</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ordered parameter list captured for byte-exact round-trip; null for from-scratch.</summary>
    internal List<KeyValuePair<string, string>>? RawParametersOrdered { get; set; }

    /// <summary>Builds the parameter dictionary used for from-scratch serialization.</summary>
    public Dictionary<string, string> ToParameters()
    {
        Parameters["NAME"] = Name;
        Parameters["KIND"] = Kind.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(UniqueId)) Parameters["UNIQUEID"] = UniqueId;
        Parameters["SUPERCLASS"] = SuperClass ? "TRUE" : "FALSE";
        Parameters["ENABLED"] = Enabled ? "TRUE" : "FALSE";
        return Parameters;
    }
}

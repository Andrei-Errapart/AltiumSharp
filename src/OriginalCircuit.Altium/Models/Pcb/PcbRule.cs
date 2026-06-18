using System.Globalization;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Base class for a PCB design rule (the <c>Rules6</c> storage). Each rule kind has its own derived
/// class with typed constraint properties (mirroring Altium's rule tree). A plain <see cref="PcbRule"/>
/// instance represents a rule kind not yet modeled with named properties — it round-trips via the
/// captured ordered parameter list.
/// </summary>
public class PcbRule
{
    // Common primitive parameter prefix.
    /// <summary>Selection flag (transient; FALSE on disk).</summary>
    public bool Selection { get; set; }
    /// <summary>Layer token (e.g. <c>TOP</c>).</summary>
    public string Layer { get; set; } = "TOP";
    /// <summary>Whether the rule is locked.</summary>
    public bool Locked { get; set; }
    /// <summary>Polygon-outline flag.</summary>
    public bool PolygonOutline { get; set; }
    /// <summary>User-routed flag.</summary>
    public bool UserRouted { get; set; } = true;
    /// <summary>Keepout flag.</summary>
    public bool Keepout { get; set; }
    /// <summary>Union index.</summary>
    public int UnionIndex { get; set; }

    /// <summary>Rule kind identifier (e.g., "Clearance", "Width", "RoutingTopology").</summary>
    public string RuleKind { get; set; } = string.Empty;
    /// <summary>Net scope (e.g. "AnyNet", "DifferentNets", "SameNet").</summary>
    public string NetScope { get; set; } = "AnyNet";
    /// <summary>Layer kind scope (e.g. "SameLayer").</summary>
    public string LayerKind { get; set; } = "SameLayer";
    /// <summary>First scope expression (query filter).</summary>
    public string Scope1Expression { get; set; } = "All";
    /// <summary>Second scope expression (query filter).</summary>
    public string Scope2Expression { get; set; } = "All";
    /// <summary>Rule name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Whether this rule is enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Rule priority (lower number = higher priority).</summary>
    public int Priority { get; set; } = 1;
    /// <summary>Comment/description for this rule.</summary>
    public string Comment { get; set; } = string.Empty;
    /// <summary>Unique identifier for this rule.</summary>
    public string UniqueId { get; set; } = string.Empty;
    /// <summary>Whether the rule was defined by the logical (schematic) document.</summary>
    public bool DefinedByLogicalDocument { get; set; }

    /// <summary>All parameters (kept for compatibility / unmodeled keys).</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The 2-byte record leader preceding this rule in the Rules6 stream; preserved per record.</summary>
    internal ushort RawLeader { get; set; }

    /// <summary>
    /// Ordered parameter list captured from the source; used to round-trip a rule kind not yet modeled
    /// with typed properties. Null for typed/from-scratch rules.
    /// </summary>
    internal List<KeyValuePair<string, string>>? RawParametersOrdered { get; set; }

    /// <summary>True when this rule is a fully typed per-kind subclass (no ordered-param replay needed).</summary>
    internal virtual bool IsModeled => false;

    /// <summary>Reads the kind-specific constraint keys from the parsed parameter dictionary.</summary>
    internal virtual void ReadBody(Dictionary<string, string> p) { }

    /// <summary>Appends the kind-specific constraint keys in Altium's canonical order.</summary>
    internal virtual void WriteBody(Action<string, string> add) { }

    /// <summary>Appends the common rule header (shared by every rule kind) in canonical order.</summary>
    internal void WriteCommonHeader(Action<string, string> add)
    {
        add("SELECTION", Bool(Selection));
        add("LAYER", Layer);
        add("LOCKED", Bool(Locked));
        add("POLYGONOUTLINE", Bool(PolygonOutline));
        add("USERROUTED", Bool(UserRouted));
        add("KEEPOUT", Bool(Keepout));
        add("UNIONINDEX", UnionIndex.ToString(CultureInfo.InvariantCulture));
        add("RULEKIND", RuleKind);
        add("NETSCOPE", NetScope);
        add("LAYERKIND", LayerKind);
        add("SCOPE1EXPRESSION", Scope1Expression);
        add("SCOPE2EXPRESSION", Scope2Expression);
        add("NAME", Name);
        add("ENABLED", Bool(Enabled));
        add("PRIORITY", Priority.ToString(CultureInfo.InvariantCulture));
        add("COMMENT", Comment);
        add("UNIQUEID", UniqueId);
        add("DEFINEDBYLOGICALDOCUMENT", Bool(DefinedByLogicalDocument));
    }

    /// <summary>Reads the common rule header fields from the parsed parameter dictionary.</summary>
    internal void ReadCommonHeader(Dictionary<string, string> p)
    {
        Selection = Bv(p, "SELECTION");
        if (p.TryGetValue("LAYER", out var layer)) Layer = layer;
        Locked = Bv(p, "LOCKED");
        PolygonOutline = Bv(p, "POLYGONOUTLINE");
        UserRouted = Bv(p, "USERROUTED");
        Keepout = Bv(p, "KEEPOUT");
        UnionIndex = Iv(p, "UNIONINDEX");
        if (p.TryGetValue("RULEKIND", out var rk)) RuleKind = rk;
        if (p.TryGetValue("NETSCOPE", out var ns)) NetScope = ns;
        if (p.TryGetValue("LAYERKIND", out var lk)) LayerKind = lk;
        if (p.TryGetValue("SCOPE1EXPRESSION", out var s1)) Scope1Expression = s1;
        if (p.TryGetValue("SCOPE2EXPRESSION", out var s2)) Scope2Expression = s2;
        if (p.TryGetValue("NAME", out var nm)) Name = nm;
        Enabled = !p.TryGetValue("ENABLED", out var en) || en.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
        Priority = p.TryGetValue("PRIORITY", out var pr) && int.TryParse(pr, out var pv) ? pv : Priority;
        if (p.TryGetValue("COMMENT", out var cm)) Comment = cm;
        if (p.TryGetValue("UNIQUEID", out var uid)) UniqueId = uid;
        DefinedByLogicalDocument = Bv(p, "DEFINEDBYLOGICALDOCUMENT");
    }

    /// <summary>Synchronizes typed properties back into the Parameters dictionary and returns it.</summary>
    public Dictionary<string, string> ToParameters()
    {
        Parameters["NAME"] = Name;
        Parameters["RULEKIND"] = RuleKind;
        Parameters["ENABLED"] = Enabled ? "TRUE" : "FALSE";
        Parameters["PRIORITY"] = Priority.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(Comment)) Parameters["COMMENT"] = Comment;
        if (!string.IsNullOrEmpty(UniqueId)) Parameters["UNIQUEID"] = UniqueId;
        if (!string.IsNullOrEmpty(Scope1Expression)) Parameters["SCOPE1EXPRESSION"] = Scope1Expression;
        if (!string.IsNullOrEmpty(Scope2Expression)) Parameters["SCOPE2EXPRESSION"] = Scope2Expression;
        return Parameters;
    }

    // ---- Shared formatting/parsing helpers for the per-kind subclasses ----
    private protected static string Bool(bool b) => b ? "TRUE" : "FALSE";
    private protected static string Mil(Coord c) => (c.ToRaw() / 10000.0).ToString("0.#####", CultureInfo.InvariantCulture) + "mil";
    private protected static bool Bv(Dictionary<string, string> p, string k) => p.TryGetValue(k, out var v) && v.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    private protected static int Iv(Dictionary<string, string> p, string k) => p.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private protected static string? Sv(Dictionary<string, string> p, string k) => p.TryGetValue(k, out var v) ? v : null;
    private protected static Coord MilV(Dictionary<string, string> p, string k)
    {
        if (!p.TryGetValue(k, out var v)) return default;
        var s = v.EndsWith("mil", StringComparison.OrdinalIgnoreCase) ? v[..^3] : v;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var mils) ? Coord.FromRaw((int)Math.Round(mils * 10000.0)) : default;
    }
    private protected static double Dv(Dictionary<string, string> p, string k) => p.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    private protected static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private protected static string F6(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
    // VOLTAGE format: a Delphi sign column (space for >=0, "-" for <0) + magnitude to 4 significant
    // figures in fixed-point with trailing zeros (e.g. " 0.000", " 5.000", " 22.20", "-5.000").
    private protected static string Volt(double v)
    {
        var a = Math.Abs(v);
        var intDigits = a < 1 ? 1 : (int)Math.Floor(Math.Log10(a)) + 1;
        var dec = Math.Max(0, 4 - intDigits);
        return (v < 0 ? "-" : " ") + a.ToString("F" + dec.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}

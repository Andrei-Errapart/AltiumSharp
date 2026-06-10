// ============================================================================
// Example: Extracting a Bill of Materials (BOM) from a schematic
// ============================================================================
//
// A bill of materials lists every part that must be purchased to build a board,
// grouped by part so identical components share one line with a quantity and the
// list of reference designators (R1, R2, ...). This example reads a schematic
// document (.SchDoc) and produces a grouped BOM as a console table, a CSV file,
// and an HTML table.
//
// WHERE THE DATA LIVES
// ────────────────────
// A schematic component is an ISchComponent; cast it to the concrete SchComponent
// to reach the fields a BOM needs:
//
//   Designator  - NOT a dedicated property. Altium stores the reference designator
//                 as a *parameter* named "Designator" on the component. We read it
//                 from SchComponent.Parameters. Components with no designator
//                 (power ports, net ties, graphics) are skipped.
//   Value       - SchComponent.Comment is Altium's "Value" field (e.g. "10k",
//                 "100nF"). We fall back to a "Value" parameter if Comment is blank.
//   Footprint   - The active PCB implementation link (SchComponent.Implementations
//                 where ModelType == "PCBLIB"). We fall back to a "Footprint"
//                 parameter.
//   Manufacturer / MPN - read from the usual custom parameters; MPN falls back to
//                 SchComponent.DesignItemId (the managed-part identifier).
//
// Parameters are free-form key/value pairs, so different libraries use different
// names. This example reads the most common ones and is easy to extend.
//
// NOTE ON CONNECTIVITY: the schematic model is geometric (pins are joined by wires
// on the canvas); there is no pin-to-net API. A BOM does not need connectivity, so
// this example needs only component metadata.
//
// RUNNING
// ───────
//   dotnet run --project examples/ExtractBom                       (uses a bundled
//                                                                    TestData sheet)
//   dotnet run --project examples/ExtractBom -- "C:\path\My.SchDoc" (your own file)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Primitives;

var input = ResolveInput(args);
if (input is null)
{
    Console.WriteLine("No .SchDoc supplied and no bundled TestData sample was found.");
    Console.WriteLine("Usage: dotnet run --project examples/ExtractBom -- <path-to-file.SchDoc>");
    return;
}

Console.WriteLine($"Reading schematic: {input}\n");

// ISchDocument is IAsyncDisposable, so dispose it with `await using`.
await using var doc = await AltiumLibrary.OpenSchDocAsync(input);

// ── 1. Flatten placed components into individual BOM placements ──────────────
var placements = new List<Placement>();
foreach (var component in doc.Components)
{
    var sc = (SchComponent)component;

    var designator = GetParameter(sc, "Designator");
    if (string.IsNullOrWhiteSpace(designator))
        continue;   // power port, net tie, graphic, etc. - not a purchasable part

    var value = !string.IsNullOrWhiteSpace(sc.Comment)
        ? sc.Comment!.Trim()
        : (GetParameter(sc, "Value") ?? "");

    var footprint = GetFootprint(sc) ?? GetParameter(sc, "Footprint") ?? "";
    var manufacturer = GetParameter(sc, "Manufacturer");
    var mpn = GetParameter(sc, "Manufacturer Part Number")
              ?? GetParameter(sc, "MPN")
              ?? (string.IsNullOrWhiteSpace(sc.DesignItemId) ? null : sc.DesignItemId);

    placements.Add(new Placement(designator!.Trim(), value, footprint, manufacturer, mpn));
}

if (placements.Count == 0)
{
    Console.WriteLine("No designated components found in this schematic " +
                      "(it may be a top sheet that only contains sub-sheet symbols).");
    return;
}

// ── 2. Group identical parts into BOM lines ─────────────────────────────────
// Two placements collapse onto one line when their value, footprint and MPN match.
var lines = placements
    .GroupBy(p => (p.Value, p.Footprint, Mpn: p.Mpn ?? ""), TupleComparer.Instance)
    .Select(g => new BomLine(
        g.Select(p => p.Designator).OrderBy(d => d, DesignatorComparer.Instance).ToList(),
        g.Key.Value,
        g.Key.Footprint,
        g.First().Manufacturer,
        g.First().Mpn))
    .OrderBy(l => l.Designators[0], DesignatorComparer.Instance)
    .ToList();

// ── 3. Print a console table ────────────────────────────────────────────────
Console.WriteLine($"{"Qty",3}  {"Value",-16} {"Footprint",-22} {"Designators",-30} MPN");
Console.WriteLine(new string('-', 100));
foreach (var l in lines)
{
    Console.WriteLine($"{l.Quantity,3}  {Trunc(l.Value, 16),-16} {Trunc(l.Footprint, 22),-22} " +
                      $"{Trunc(l.DesignatorList, 30),-30} {l.Mpn}");
}
Console.WriteLine(new string('-', 100));
Console.WriteLine($"{lines.Count} unique line item(s), {placements.Count} component placement(s).\n");

// ── 4. Write CSV + HTML ─────────────────────────────────────────────────────
var outDir = Path.Combine(Path.GetTempPath(), "AltiumBomExample");
Directory.CreateDirectory(outDir);
var baseName = Path.GetFileNameWithoutExtension(input);

var csvPath = Path.Combine(outDir, baseName + "-bom.csv");
WriteCsv(csvPath, lines);
Console.WriteLine($"CSV written:  {csvPath}");

var htmlPath = Path.Combine(outDir, baseName + "-bom.html");
WriteHtml(htmlPath, baseName, lines);
Console.WriteLine($"HTML written: {htmlPath}");

// ── Helpers ─────────────────────────────────────────────────────────────────

// Reads a component parameter by name (case-insensitive); null when absent/empty.
static string? GetParameter(SchComponent c, string name)
{
    foreach (var p in c.Parameters)
        if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrEmpty(p.Value) ? null : p.Value;
    return null;
}

// Resolves the footprint from the component's PCB implementation links, preferring
// the one marked current. Implementations live on the concrete SchComponent.
static string? GetFootprint(SchComponent c)
{
    SchImplementation? fallback = null;
    foreach (var impl in c.Implementations)
    {
        var i = (SchImplementation)impl;
        if (!string.Equals(i.ModelType, "PCBLIB", StringComparison.OrdinalIgnoreCase))
            continue;
        if (i.IsCurrent && !string.IsNullOrWhiteSpace(i.ModelName))
            return i.ModelName;
        fallback ??= i;
    }
    return fallback?.ModelName;
}

static string Trunc(string s, int max) =>
    s.Length <= max ? s : s.Substring(0, max - 1) + "…";

static void WriteCsv(string path, List<BomLine> lines)
{
    using var w = new StreamWriter(path);
    w.WriteLine("Quantity,Value,Footprint,Designators,Manufacturer,MPN");
    foreach (var l in lines)
        w.WriteLine(string.Join(',',
            l.Quantity,
            Csv(l.Value), Csv(l.Footprint), Csv(l.DesignatorList),
            Csv(l.Manufacturer), Csv(l.Mpn)));
}

static void WriteHtml(string path, string title, List<BomLine> lines)
{
    using var w = new StreamWriter(path);
    w.WriteLine($"<!doctype html><meta charset=\"utf-8\"><title>BOM - {Html(title)}</title>");
    w.WriteLine("<style>body{font-family:sans-serif}table{border-collapse:collapse}" +
                "th,td{border:1px solid #ccc;padding:4px 8px;text-align:left}" +
                "th{background:#f0f0f0}</style>");
    w.WriteLine($"<h1>Bill of Materials &mdash; {Html(title)}</h1>");
    w.WriteLine("<table><tr><th>Qty</th><th>Value</th><th>Footprint</th>" +
                "<th>Designators</th><th>Manufacturer</th><th>MPN</th></tr>");
    foreach (var l in lines)
        w.WriteLine($"<tr><td>{l.Quantity}</td><td>{Html(l.Value)}</td><td>{Html(l.Footprint)}</td>" +
                    $"<td>{Html(l.DesignatorList)}</td><td>{Html(l.Manufacturer)}</td>" +
                    $"<td>{Html(l.Mpn)}</td></tr>");
    w.WriteLine("</table>");
}

static string Csv(string? s)
{
    s ??= "";
    return s.IndexOfAny(['"', ',', '\n', '\r']) >= 0
        ? "\"" + s.Replace("\"", "\"\"") + "\""
        : s;
}

static string Html(string? s) => (s ?? "")
    .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

// Locates the input file: an explicit CLI path, otherwise a bundled TestData sheet.
static string? ResolveInput(string[] args)
{
    if (args.Length > 0)
    {
        if (File.Exists(args[0])) return args[0];
        Console.Error.WriteLine($"File not found: {args[0]}");
        return null;
    }
    var testData = FindRepoTestDataDir();
    return testData is null
        ? null
        : LocateSample(testData, ".SchDoc", "Power Supply", "DAC", "Overview");
}

// Walks up from the binary (and the working directory) to find the repo TestData folder.
static string? FindRepoTestDataDir()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TestData");
            if (Directory.Exists(candidate)) return candidate;
        }
    return null;
}

// Finds a top-level file with the given extension (case-insensitive), preferring names
// that contain one of the supplied hints. Extension is matched in code so it works on
// case-sensitive file systems (e.g. ".PCBLIB" vs ".PcbLib").
static string? LocateSample(string dir, string extension, params string[] preferred)
{
    var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
        .Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase))
        .ToList();
    foreach (var hint in preferred)
    {
        var hit = files.FirstOrDefault(f =>
            Path.GetFileName(f).Contains(hint, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
    }
    return files.FirstOrDefault();
}

// ── Types ───────────────────────────────────────────────────────────────────

record Placement(string Designator, string Value, string Footprint, string? Manufacturer, string? Mpn);

record BomLine(List<string> Designators, string Value, string Footprint, string? Manufacturer, string? Mpn)
{
    public int Quantity => Designators.Count;
    public string DesignatorList => string.Join(", ", Designators);
}

// Groups placements by (value, footprint, MPN), comparing each field case-insensitively.
sealed class TupleComparer : IEqualityComparer<(string Value, string Footprint, string Mpn)>
{
    public static readonly TupleComparer Instance = new();
    public bool Equals((string Value, string Footprint, string Mpn) a, (string Value, string Footprint, string Mpn) b) =>
        Eq(a.Value, b.Value) && Eq(a.Footprint, b.Footprint) && Eq(a.Mpn, b.Mpn);
    public int GetHashCode((string Value, string Footprint, string Mpn) t) =>
        HashCode.Combine(Norm(t.Value), Norm(t.Footprint), Norm(t.Mpn));
    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static string Norm(string s) => s.ToUpperInvariant();
}

// Natural sort so R2 sorts before R10 (splits an alpha prefix from a numeric suffix).
sealed class DesignatorComparer : IComparer<string>
{
    public static readonly DesignatorComparer Instance = new();
    public int Compare(string? x, string? y)
    {
        x ??= ""; y ??= "";
        var (px, nx) = Split(x);
        var (py, ny) = Split(y);
        var byPrefix = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
        if (byPrefix != 0) return byPrefix;
        if (nx.HasValue && ny.HasValue) return nx.Value.CompareTo(ny.Value);
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
    static (string Prefix, int? Number) Split(string s)
    {
        var i = 0;
        while (i < s.Length && !char.IsDigit(s[i])) i++;
        return int.TryParse(s.AsSpan(i), out var n) ? (s[..i], n) : (s, null);
    }
}

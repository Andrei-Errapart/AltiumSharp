// ============================================================================
// Example: Generating a pick-and-place (centroid) file from a PCB
// ============================================================================
//
// Assembly machines need a "pick-and-place" or "centroid" file: one row per
// placed component giving its reference designator, X/Y centroid, rotation, and
// which side of the board it sits on. This example reads a PCB document (.PcbDoc)
// and writes that file as CSV (plus a console preview).
//
// WHERE THE DATA LIVES
// ────────────────────
// Each placed part is an IPcbComponent; cast it to the concrete PcbComponent to
// reach the placement fields:
//
//   Designator - PcbComponent.SourceDesignator is the schematic reference (e.g.
//                "U1", "R7"). We fall back to Name (the footprint pattern) if it
//                is blank.
//   Centroid   - PcbComponent.X / .Y are Coord values (fixed-point). Convert with
//                .ToMm() (or .ToMils()). This is the component origin Altium placed
//                the part at.
//   Rotation   - PcbComponent.Rotation, in degrees.
//   Side       - PcbComponent.Layer (1 = Top, 32 = Bottom) plus the FlippedOnLayer
//                flag. Bottom-side parts are what tell the machine to flip.
//   Value/FP   - PcbComponent.Comment is the value; Pattern (or Name) is the
//                footprint.
//
// Units: this example emits millimetres. Swap .ToMm() for .ToMils() to emit mils.
//
// RUNNING
// ───────
//   dotnet run --project examples/GeneratePickAndPlace                  (bundled board)
//   dotnet run --project examples/GeneratePickAndPlace -- "C:\My.PcbDoc" (your own)
//
// ============================================================================

using System.Globalization;
using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;

var input = ResolveInput(args);
if (input is null)
{
    Console.WriteLine("No .PcbDoc supplied and no bundled TestData board was found.");
    Console.WriteLine("Usage: dotnet run --project examples/GeneratePickAndPlace -- <path-to-file.PcbDoc>");
    return;
}

Console.WriteLine($"Reading board: {input}\n");

await using var doc = await AltiumLibrary.OpenPcbDocAsync(input);

// ── 1. Build one centroid row per placed component ──────────────────────────
var rows = new List<Centroid>();
foreach (var component in doc.Components)
{
    var pc = (PcbComponent)component;

    var designator = !string.IsNullOrWhiteSpace(pc.SourceDesignator)
        ? pc.SourceDesignator!.Trim()
        : pc.Name;
    if (string.IsNullOrWhiteSpace(designator))
        continue;

    // A part is on the bottom if it sits on the bottom copper layer or is flipped.
    var bottom = pc.Layer == 32 || pc.FlippedOnLayer;
    var footprint = !string.IsNullOrWhiteSpace(pc.Pattern) ? pc.Pattern! : pc.Name;

    rows.Add(new Centroid(
        designator,
        pc.X.ToMm(),
        pc.Y.ToMm(),
        NormalizeAngle(pc.Rotation),
        bottom ? "Bottom" : "Top",
        pc.Comment ?? "",
        footprint));
}

if (rows.Count == 0)
{
    Console.WriteLine("This board has no placed components to export.");
    return;
}

rows = rows.OrderBy(r => r.Side)
           .ThenBy(r => r.Designator, DesignatorComparer.Instance)
           .ToList();

// ── 2. Console preview ──────────────────────────────────────────────────────
Console.WriteLine($"{"Designator",-12} {"X (mm)",10} {"Y (mm)",10} {"Rot",7}  {"Side",-7} Footprint");
Console.WriteLine(new string('-', 78));
foreach (var r in rows)
    Console.WriteLine($"{r.Designator,-12} {Num(r.XMm),10} {Num(r.YMm),10} {Num(r.Rotation),7}  " +
                      $"{r.Side,-7} {r.Footprint}");
Console.WriteLine(new string('-', 78));
var top = rows.Count(r => r.Side == "Top");
Console.WriteLine($"{rows.Count} component(s): {top} top, {rows.Count - top} bottom.\n");

// ── 3. Write the CSV ────────────────────────────────────────────────────────
var outDir = Path.Combine(Path.GetTempPath(), "AltiumPickAndPlaceExample");
Directory.CreateDirectory(outDir);
var csvPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + "-pos.csv");

using (var w = new StreamWriter(csvPath))
{
    w.WriteLine("Designator,Mid X (mm),Mid Y (mm),Rotation,Layer,Comment,Footprint");
    foreach (var r in rows)
        w.WriteLine(string.Join(',',
            Csv(r.Designator), Num(r.XMm), Num(r.YMm), Num(r.Rotation),
            r.Side, Csv(r.Comment), Csv(r.Footprint)));
}
Console.WriteLine($"CSV written: {csvPath}");

// ── Helpers ─────────────────────────────────────────────────────────────────

static double NormalizeAngle(double deg)
{
    deg %= 360;
    return deg < 0 ? deg + 360 : deg;
}

static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

static string Csv(string? s)
{
    s ??= "";
    return s.IndexOfAny(['"', ',', '\n', '\r']) >= 0
        ? "\"" + s.Replace("\"", "\"\"") + "\""
        : s;
}

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
        : LocateSample(testData, ".PcbDoc", "USB Power Adapter", "MAX5719", "VCOCXO");
}

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

record Centroid(string Designator, double XMm, double YMm, double Rotation, string Side, string Comment, string Footprint);

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

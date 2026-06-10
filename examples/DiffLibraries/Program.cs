// ============================================================================
// Example: Diffing two libraries
// ============================================================================
//
// Compares two libraries of the same type (.PcbLib vs .PcbLib, or .SchLib vs
// .SchLib) and reports which components were added, removed, or changed between
// them. Useful for reviewing a library update or wiring a check into CI.
//
// HOW IT WORKS
// ────────────
// Both files open through AltiumLibrary.OpenAsync, which returns an ILibrary whose
// AllComponents are IComponent (Name + Bounds), regardless of PCB vs schematic.
// We build a name -> signature map for each side and compare. The "signature" is a
// cheap fingerprint (primitive counts + footprint/symbol size); cast each component
// to its concrete type to count its primitives.
//
// With no arguments the example synthesizes two small footprint libraries that
// differ on purpose (one part added, one removed, one changed) so you can see all
// four outcomes. Pass two real libraries to diff your own.
//
// RUNNING
// ───────
//   dotnet run --project examples/DiffLibraries                 (synthesized demo)
//   dotnet run --project examples/DiffLibraries -- old.PcbLib new.PcbLib  (your own)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Models;
using OriginalCircuit.Eda.Primitives;

string? pathA, pathB;
if (args.Length >= 2)
{
    pathA = File.Exists(args[0]) ? args[0] : null;
    pathB = File.Exists(args[1]) ? args[1] : null;
}
else
{
    Console.WriteLine("No pair supplied — synthesizing two demo libraries that differ on purpose.\n");
    (pathA, pathB) = await SynthesizeDemoPair();
}

if (pathA is null || pathB is null)
{
    Console.WriteLine("Need two libraries of the same type to compare.");
    Console.WriteLine("Usage: dotnet run --project examples/DiffLibraries -- <old> <new>");
    return;
}

Console.WriteLine($"A: {Path.GetFileName(pathA)}");
Console.WriteLine($"B: {Path.GetFileName(pathB)}\n");

await using var a = await AltiumLibrary.OpenAsync(pathA);
await using var b = await AltiumLibrary.OpenAsync(pathB);

var mapA = BuildMap(a);
var mapB = BuildMap(b);

var added = mapB.Keys.Where(k => !mapA.ContainsKey(k)).OrderBy(k => k).ToList();
var removed = mapA.Keys.Where(k => !mapB.ContainsKey(k)).OrderBy(k => k).ToList();
var changed = mapA.Keys.Where(k => mapB.ContainsKey(k) && mapA[k] != mapB[k]).OrderBy(k => k).ToList();
var unchanged = mapA.Keys.Count(k => mapB.ContainsKey(k) && mapA[k] == mapB[k]);

Console.WriteLine($"A has {mapA.Count} component(s); B has {mapB.Count}.");
Console.WriteLine($"  + {added.Count} added   - {removed.Count} removed   " +
                  $"~ {changed.Count} changed   = {unchanged} unchanged\n");

foreach (var name in added) Console.WriteLine($"  + {name}");
foreach (var name in removed) Console.WriteLine($"  - {name}");
foreach (var name in changed)
{
    Console.WriteLine($"  ~ {name}");
    Console.WriteLine($"      A: {mapA[name]}");
    Console.WriteLine($"      B: {mapB[name]}");
}
if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
    Console.WriteLine("Libraries are equivalent (by component name and signature).");

// ── Comparison ──────────────────────────────────────────────────────────────

static Dictionary<string, string> BuildMap(ILibrary lib)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in lib.AllComponents)
        map[c.Name] = Signature(c);   // last wins on the rare duplicate name
    return map;
}

// A cheap fingerprint: primitive counts + size. Differences here flag a changed part.
static string Signature(IComponent c)
{
    var dim = $"{c.Bounds.Width.ToMm():F2}x{c.Bounds.Height.ToMm():F2}mm";
    return c switch
    {
        PcbComponent p => $"pads={p.Pads.Count} tracks={p.Tracks.Count} vias={p.Vias.Count} arcs={p.Arcs.Count} {dim}",
        SchComponent s => $"pins={s.Pins.Count} rects={s.Rectangles.Count} lines={s.Lines.Count} arcs={s.Arcs.Count} {dim}",
        _ => dim
    };
}

// ── Demo data ───────────────────────────────────────────────────────────────

static async Task<(string, string)> SynthesizeDemoPair()
{
    var dir = Path.Combine(Path.GetTempPath(), "AltiumDiffExample");
    Directory.CreateDirectory(dir);

    var v1 = new PcbLibrary();
    v1.Add(MakeFootprint("R0402", pads: 2));
    v1.Add(MakeFootprint("R0603", pads: 2));   // will be removed in v2
    v1.Add(MakeFootprint("C0402", pads: 2));

    var v2 = new PcbLibrary();
    v2.Add(MakeFootprint("R0402", pads: 3));   // changed: gained a pad
    v2.Add(MakeFootprint("C0402", pads: 2));   // unchanged
    v2.Add(MakeFootprint("L0402", pads: 2));   // added

    var pathA = Path.Combine(dir, "PartsLibrary.v1.PcbLib");
    var pathB = Path.Combine(dir, "PartsLibrary.v2.PcbLib");
    await v1.SaveAsync(pathA);
    await v2.SaveAsync(pathB);
    return (pathA, pathB);
}

static PcbComponent MakeFootprint(string name, int pads)
{
    var fp = PcbComponent.Create(name).WithDescription($"{name} demo footprint");
    for (var i = 0; i < pads; i++)
    {
        var designator = (i + 1).ToString();
        var x = i * 1.0;
        fp.AddPad(p => p
            .At(Coord.FromMm(x), Coord.FromMm(0))
            .Size(Coord.FromMm(0.6), Coord.FromMm(0.8))
            .Smd(1)
            .WithDesignator(designator));
    }
    return fp.Build();
}

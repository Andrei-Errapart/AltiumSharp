// ============================================================================
// Example: Inspecting a PCB — outline, layer stack, statistics, rules
// ============================================================================
//
// A quick "what's in this board?" report: physical size from the board outline,
// the copper layer stackup, primitive/net counts, and a design-rule summary.
//
// WHERE THE DATA LIVES
// ────────────────────
// The reader returns an IPcbDocument; cast it to the concrete PcbDocument to reach
// the board-level collections (Nets, Rules, Polygons, LayerStack, Diagnostics) and
// the board outline — none of which are on the IPcbDocument interface.
//
//   Outline     - PcbDocument.GetBoardOutline() returns the board edge as a closed
//                 polygon of world-space points (arcs tessellated). Its bounding box
//                 is the board's physical size.
//   Layer stack - PcbDocument.LayerStack (may be null) lists PcbLayerEntry items with
//                 Name and CopperEnabled, so you can count copper layers.
//   Rules       - PcbDocument.Rules are PcbRule objects; rule-specific values live in
//                 the Parameters dictionary (e.g. "GAP" for clearance, "MINWIDTH").
//   Bounds      - PcbDocument.Bounds is the extent of all primitives.
//
// RUNNING
// ───────
//   dotnet run --project examples/InspectBoard                       (bundled board)
//   dotnet run --project examples/InspectBoard -- "C:\My.PcbDoc"      (your own)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;

var input = ResolveInput(args);
if (input is null)
{
    Console.WriteLine("No .PcbDoc supplied and no bundled TestData board was found.");
    Console.WriteLine("Usage: dotnet run --project examples/InspectBoard -- <path-to-file.PcbDoc>");
    return;
}

Console.WriteLine($"Inspecting board: {Path.GetFileName(input)}\n");

await using var idoc = await AltiumLibrary.OpenPcbDocAsync(input);
var doc = (PcbDocument)idoc;

// ── Physical size from the board outline ────────────────────────────────────
var outline = doc.GetBoardOutline();
Console.WriteLine("Board outline");
if (outline.Count > 0)
{
    // Compute the bounding box directly: Union of zero-area point rects collapses.
    Coord minX = outline[0].X, maxX = outline[0].X, minY = outline[0].Y, maxY = outline[0].Y;
    foreach (var p in outline)
    {
        if (p.X < minX) minX = p.X;
        if (p.X > maxX) maxX = p.X;
        if (p.Y < minY) minY = p.Y;
        if (p.Y > maxY) maxY = p.Y;
    }
    Console.WriteLine($"  {(maxX - minX).ToMm():F2} x {(maxY - minY).ToMm():F2} mm  ({outline.Count} edge points)");
}
else
{
    Console.WriteLine("  (no board outline defined in Board6)");
}

// ── Layer stackup ───────────────────────────────────────────────────────────
Console.WriteLine("\nLayer stack");
if (doc.LayerStack is { Layers.Count: > 0 } stack)
{
    var copper = stack.Layers.Count(l => l.CopperEnabled);
    Console.WriteLine($"  {stack.Layers.Count} layers, {copper} copper:");
    foreach (var layer in stack.Layers)
        Console.WriteLine($"    [{layer.Index,2}] {layer.Name,-18} {(layer.CopperEnabled ? "copper" : "dielectric")}");
}
else
{
    Console.WriteLine("  (no layer stack present)");
}

// ── Primitive & object counts ───────────────────────────────────────────────
Console.WriteLine("\nContents");
Console.WriteLine($"  Components ........ {doc.Components.Count}");
Console.WriteLine($"  Pads .............. {doc.Pads.Count}");
Console.WriteLine($"  Tracks ............ {doc.Tracks.Count}");
Console.WriteLine($"  Vias .............. {doc.Vias.Count}");
Console.WriteLine($"  Arcs .............. {doc.Arcs.Count}");
Console.WriteLine($"  Regions ........... {doc.Regions.Count}");
Console.WriteLine($"  Fills ............. {doc.Fills.Count}");
Console.WriteLine($"  Polygons (pours) .. {doc.Polygons.Count}");
Console.WriteLine($"  Nets .............. {doc.Nets.Count}");
Console.WriteLine($"  Rules ............. {doc.Rules.Count}");
Console.WriteLine($"  Classes ........... {doc.Classes.Count}");
Console.WriteLine($"  Diff pairs ........ {doc.DifferentialPairs.Count}");
Console.WriteLine($"  Rooms ............. {doc.Rooms.Count}");

var b = doc.Bounds;
Console.WriteLine($"  Primitive extent .. {b.Width.ToMm():F2} x {b.Height.ToMm():F2} mm");

// ── Design-rule summary ─────────────────────────────────────────────────────
Console.WriteLine("\nDesign rules");
if (doc.Rules.Count > 0)
{
    foreach (var group in doc.Rules.GroupBy(r => r.RuleKind).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {group.Count(),3}  {group.Key}");

    // Surface a couple of the most common constraint values.
    foreach (var rule in doc.Rules.Where(r => r.RuleKind is "Clearance" or "Width")
                                   .OrderBy(r => r.Priority).Take(5))
    {
        var detail = rule.Parameters.TryGetValue("GAP", out var gap) ? $"gap={gap}"
                   : rule.Parameters.TryGetValue("MINWIDTH", out var w) ? $"min width={w}"
                   : "";
        Console.WriteLine($"     - {rule.RuleKind}: {rule.Name} {detail}".TrimEnd());
    }
}
else
{
    Console.WriteLine("  (no rules stored in this document)");
}

// ── Read diagnostics ────────────────────────────────────────────────────────
if (doc.Diagnostics.Count > 0)
{
    Console.WriteLine("\nDiagnostics");
    foreach (var d in doc.Diagnostics.Take(20))
        Console.WriteLine($"  [{d.Severity}] {d.Message}");
}

// ── Helpers ─────────────────────────────────────────────────────────────────

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
        : LocateSample(testData, ".PcbDoc", "VCOCXO", "MAX5719", "USB Power Adapter");
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

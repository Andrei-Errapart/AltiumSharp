// ============================================================================
// Example: Reporting nets and their copper membership on a PCB
// ============================================================================
//
// For each net on a board, count the copper objects that carry it (pads, tracks,
// vias, arcs, regions, polygon pours, fills). This is the kind of summary you use
// to spot an unrouted net (0 tracks) or an unexpectedly large net.
//
// WHERE THE DATA LIVES
// ────────────────────
// Cast the IPcbDocument to PcbDocument for the Nets list. Net membership is a
// string on each primitive: PcbPad.Net, PcbTrack.Net, PcbVia.Net, PcbArc.Net,
// PcbRegion.Net, PcbPolygon.Net, PcbFill.Net. There is no aggregate "give me all
// primitives on net X" call, so we make a single pass and bucket by net name.
//
// WHAT THE MODEL DOES AND DOES NOT TRACK
// ──────────────────────────────────────
// On the PCB side, net membership IS modelled — every copper primitive names its
// net. On the SCHEMATIC side it is NOT: pins are joined by wires drawn on the
// canvas and there is no pin-to-net API, so a schematic netlist would have to be
// inferred from wire geometry. This example therefore works from the PCB.
//
// RUNNING
// ───────
//   dotnet run --project examples/NetReport                          (bundled board)
//   dotnet run --project examples/NetReport -- "C:\My.PcbDoc"         (your own)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;

var input = ResolveInput(args);
if (input is null)
{
    Console.WriteLine("No .PcbDoc supplied and no bundled TestData board was found.");
    Console.WriteLine("Usage: dotnet run --project examples/NetReport -- <path-to-file.PcbDoc>");
    return;
}

Console.WriteLine($"Reading board: {Path.GetFileName(input)}\n");

await using var idoc = await AltiumLibrary.OpenPcbDocAsync(input);
var doc = (PcbDocument)idoc;

var nets = new Dictionary<string, NetCounts>(StringComparer.OrdinalIgnoreCase);
NetCounts BucketFor(string name) =>
    nets.TryGetValue(name, out var c) ? c : nets[name] = new NetCounts();

// Each primitive references its net by NetIndex (a ushort index into doc.Nets;
// 0xFFFF means none). Resolve that to the net name, falling back to any Net string
// (and treating a numeric string as an index too, which is how polygons store it).
string ResolveNet(string? netStr, ushort index)
{
    if (index != 0xFFFF && index < doc.Nets.Count) return doc.Nets[index].Name;
    if (!string.IsNullOrWhiteSpace(netStr))
        return int.TryParse(netStr, out var i) && i >= 0 && i < doc.Nets.Count
            ? doc.Nets[i].Name : netStr;
    return "(unassigned)";
}

// Seed declared nets so even those with zero copper still show up.
foreach (var net in doc.Nets)
    BucketFor(net.Name);

// Single pass over every copper collection.
foreach (var p in doc.Pads) { var x = (PcbPad)p; BucketFor(ResolveNet(x.Net, x.NetIndex)).Pads++; }
foreach (var t in doc.Tracks) { var x = (PcbTrack)t; BucketFor(ResolveNet(x.Net, x.NetIndex)).Tracks++; }
foreach (var v in doc.Vias) { var x = (PcbVia)v; BucketFor(ResolveNet(x.Net, x.NetIndex)).Vias++; }
foreach (var a in doc.Arcs) { var x = (PcbArc)a; BucketFor(ResolveNet(x.Net, x.NetIndex)).Arcs++; }
foreach (var r in doc.Regions) { var x = (PcbRegion)r; BucketFor(ResolveNet(x.Net, x.NetIndex)).Regions++; }
foreach (var poly in doc.Polygons) BucketFor(ResolveNet(poly.Net, 0xFFFF)).Polygons++;
foreach (var f in doc.Fills) { var x = (PcbFill)f; BucketFor(ResolveNet(x.Net, x.NetIndex)).Fills++; }

// ── Report ──────────────────────────────────────────────────────────────────
var ranked = nets.OrderByDescending(kv => kv.Value.Total).ThenBy(kv => kv.Key).ToList();

Console.WriteLine($"{"Net",-22} {"Pads",5} {"Trk",5} {"Via",5} {"Poly",5} {"Total",6}");
Console.WriteLine(new string('-', 56));
foreach (var (name, c) in ranked.Take(30))
    Console.WriteLine($"{Trunc(name, 22),-22} {c.Pads,5} {c.Tracks,5} {c.Vias,5} {c.Polygons,5} {c.Total,6}");
if (ranked.Count > 30)
    Console.WriteLine($"... and {ranked.Count - 30} more net(s)");
Console.WriteLine(new string('-', 56));

var unrouted = ranked.Count(kv => kv.Value.Pads >= 2 && kv.Value.Tracks == 0 && kv.Value.Vias == 0
                                  && !kv.Key.StartsWith('('));
Console.WriteLine($"{doc.Nets.Count} declared net(s); {nets.Count} net bucket(s) carry copper.");
if (unrouted > 0)
    Console.WriteLine($"{unrouted} net(s) have multiple pads but no tracks/vias (possibly unrouted).");

// ── Helpers ─────────────────────────────────────────────────────────────────

static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

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
        : LocateSample(testData, ".PcbDoc", "MAX5719", "VCOCXO Breakout", "USB Power Adapter");
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

sealed class NetCounts
{
    public int Pads, Tracks, Vias, Arcs, Regions, Polygons, Fills;
    public int Total => Pads + Tracks + Vias + Arcs + Regions + Polygons + Fills;
}

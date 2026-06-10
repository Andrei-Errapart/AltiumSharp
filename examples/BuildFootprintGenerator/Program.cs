// ============================================================================
// Example: Programmatic footprint generation
// ============================================================================
//
// Instead of drawing every footprint by hand, generate a family from parameters.
// This example builds QFN and DIP footprints from (pin count, pitch, body size)
// and writes them to a .PcbLib — the core of a "footprint wizard".
//
// WHAT IT SHOWS
// ─────────────
//   * The PcbComponent fluent builder: AddPad / AddTrack / AddArc / AddText.
//   * SMD pads via .Smd(layer) and through-hole pads via .ThroughHole(holeSize).
//   * Pad shapes via .Shape(PadShape.RoundedRectangle / Rectangular / Round).
//   * A silkscreen courtyard, a pin-1 marker, and a ".Designator" text token.
//   * Saving the library and reading it back to confirm the round-trip.
//
// RUNNING
// ───────
//   dotnet run --project examples/BuildFootprintGenerator
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;

var outDir = Path.Combine(Path.GetTempPath(), "AltiumFootprintExample");
Directory.CreateDirectory(outDir);

var lib = new PcbLibrary();
lib.Add(BuildQfn("QFN-16-0.5", pins: 16, pitch: 0.5, body: 3.0, padLen: 0.75, padWid: 0.28));
lib.Add(BuildQfn("QFN-32-0.5", pins: 32, pitch: 0.5, body: 5.0, padLen: 0.75, padWid: 0.28));
lib.Add(BuildDip("DIP-8", pins: 8, pitch: 2.54, rowSpacing: 7.62, hole: 0.8, pad: 1.5));
lib.Add(BuildDip("DIP-14", pins: 14, pitch: 2.54, rowSpacing: 7.62, hole: 0.8, pad: 1.5));

var path = Path.Combine(outDir, "Generated.PcbLib");
await lib.SaveAsync(path);
Console.WriteLine($"Generated {lib.Count} footprints -> {path}\n");

// Read back to confirm everything persisted.
var loaded = await AltiumLibrary.OpenPcbLibAsync(path);
foreach (var component in loaded.Components)
{
    var c = (PcbComponent)component;
    Console.WriteLine($"  {c.Name,-12} {c.Pads.Count,3} pads   " +
                      $"bounds {c.Bounds.Width.ToMm():F2} x {c.Bounds.Height.ToMm():F2} mm");
}

// ── Generators ──────────────────────────────────────────────────────────────

// A square quad-flat no-lead package: perimeter SMD pads on four sides.
static PcbComponent BuildQfn(string name, int pins, double pitch, double body, double padLen, double padWid)
{
    var perSide = pins / 4;
    var first = (perSide - 1) * pitch / 2.0;       // first pad offset along each side
    var c = body / 2.0 + padLen / 2.0 - 0.1;       // pad-center distance from origin (slight overhang)

    var fp = PcbComponent.Create(name)
        .WithDescription($"{pins}-pin QFN, {pitch:0.##}mm pitch, {body:0.##}mm body");

    var pin = 1;
    void Pad(double x, double y, double w, double h)
    {
        var n = pin++;
        fp.AddPad(p => p
            .At(Coord.FromMm(x), Coord.FromMm(y))
            .Size(Coord.FromMm(w), Coord.FromMm(h))
            .Shape(PadShape.RoundedRectangle)
            .Smd(1)
            .WithDesignator(n.ToString()));
    }

    for (var i = 0; i < perSide; i++) Pad(-c, first - i * pitch, padLen, padWid);   // left, top->bottom
    for (var i = 0; i < perSide; i++) Pad(-first + i * pitch, -c, padWid, padLen);  // bottom, left->right
    for (var i = 0; i < perSide; i++) Pad(c, -first + i * pitch, padLen, padWid);   // right, bottom->top
    for (var i = 0; i < perSide; i++) Pad(first - i * pitch, c, padWid, padLen);    // top, right->left

    Courtyard(fp, body / 2.0 + 0.25);
    return fp.Build();
}

// A dual-in-line package: two rows of through-hole pads.
static PcbComponent BuildDip(string name, int pins, double pitch, double rowSpacing, double hole, double pad)
{
    var perRow = pins / 2;
    var first = (perRow - 1) * pitch / 2.0;
    var x = rowSpacing / 2.0;

    var fp = PcbComponent.Create(name)
        .WithDescription($"{pins}-pin DIP, {pitch:0.##}mm pitch, {rowSpacing:0.##}mm rows");

    var pin = 1;
    void Pad(double px, double py)
    {
        var n = pin++;
        fp.AddPad(p => p
            .At(Coord.FromMm(px), Coord.FromMm(py))
            .Size(Coord.FromMm(pad), Coord.FromMm(pad))
            .Shape(n == 1 ? PadShape.Rectangular : PadShape.Round)   // square pad 1
            .ThroughHole(Coord.FromMm(hole))
            .Layer(74)                                               // 74 = Multi-layer
            .WithDesignator(n.ToString()));
    }

    for (var i = 0; i < perRow; i++) Pad(-x, first - i * pitch);             // left column, pins 1..N/2
    for (var i = 0; i < perRow; i++) Pad(x, -first + i * pitch);            // right column, continues

    Courtyard(fp, Math.Max(x + pad, first + pad));
    return fp.Build();
}

// Silkscreen square + pin-1 marker + designator text, sized to a half-extent.
static void Courtyard(ComponentBuilder fp, double half)
{
    var w = Coord.FromMm(0.12);
    var h = Coord.FromMm(half);
    var l = Coord.FromMm(-half);
    fp.AddTrack(t => t.From(l, l).To(h, l).Width(w).Layer(21));
    fp.AddTrack(t => t.From(h, l).To(h, h).Width(w).Layer(21));
    fp.AddTrack(t => t.From(h, h).To(l, h).Width(w).Layer(21));
    fp.AddTrack(t => t.From(l, h).To(l, l).Width(w).Layer(21));
    fp.AddArc(a => a.At(l, h).Radius(Coord.FromMm(0.15)).Angles(0, 360).Width(w).Layer(21));     // pin-1 dot
    fp.AddText(".Designator", t => t.At(l, Coord.FromMm(half + 0.4)).Height(Coord.FromMm(0.8)).Layer(21));
}

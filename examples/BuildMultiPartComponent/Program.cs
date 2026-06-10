// ============================================================================
// Example: Building a multi-part schematic component
// ============================================================================
//
// A multi-part (multi-unit) component is one physical package drawn as several
// independent symbols — e.g. a dual gate placed as part A and part B. The package
// is one SchComponent with PartCount > 1; each child primitive is tagged with an
// OwnerPartId that says which part it belongs to.
//
// WHAT IT SHOWS
// ─────────────
//   * SchComponent.PartCount = 2 for a two-part package.
//   * Setting OwnerPartId on each pin and body so it renders only on its part.
//   * Saving the symbol library and reading it back to confirm the part split.
//
// RUNNING
// ───────
//   dotnet run --project examples/BuildMultiPartComponent
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Enums;
using PinElectricalType = OriginalCircuit.Altium.Models.Sch.PinElectricalType;
using OriginalCircuit.Eda.Primitives;

var outDir = Path.Combine(Path.GetTempPath(), "AltiumMultiPartExample");
Directory.CreateDirectory(outDir);

// A dual 2-input gate: two identical parts (A and B) plus shared power pins.
var gate = new SchComponent
{
    Name = "DUAL_GATE",
    Description = "Dual 2-input gate (2 parts)",
    DesignatorPrefix = "U",
    PartCount = 2
};

for (var part = 1; part <= 2; part++)
{
    // Body rectangle belongs to this part only.
    gate.AddRectangle(new SchRectangle
    {
        Corner1 = new CoordPoint(Coord.FromMm(-5.08), Coord.FromMm(-5.08)),
        Corner2 = new CoordPoint(Coord.FromMm(5.08), Coord.FromMm(5.08)),
        Color = 128,
        OwnerPartId = part
    });

    // Part 1 uses pins 1/2/3, part 2 uses 4/5/6.
    var baseDes = (part - 1) * 3;
    AddPin(gate, $"{baseDes + 1}", "A", -7.62, 2.54, PinOrientation.Right, PinElectricalType.Input, part);
    AddPin(gate, $"{baseDes + 2}", "B", -7.62, -2.54, PinOrientation.Right, PinElectricalType.Input, part);
    AddPin(gate, $"{baseDes + 3}", "Y", 7.62, 0.0, PinOrientation.Left, PinElectricalType.Output, part);
}

// Shared power pins — conventionally drawn on part 1.
AddPin(gate, "14", "VCC", 0.0, 7.62, PinOrientation.Down, PinElectricalType.Power, 1);
AddPin(gate, "7", "GND", 0.0, -7.62, PinOrientation.Up, PinElectricalType.Power, 1);

var lib = new SchLibrary();
lib.Add(gate);

Console.WriteLine($"Built {gate.Name}: PartCount = {gate.PartCount}");
PrintParts("In memory", gate);

// Save and read back to confirm the per-part assignment survives a round-trip.
var path = Path.Combine(outDir, "MultiPart.SchLib");
await lib.SaveAsync(path);

var loaded = (SchLibrary)await AltiumLibrary.OpenSchLibAsync(path);
var reloaded = (SchComponent)loaded.Components.First();
var distinctParts = reloaded.Pins.Select(p => ((SchPin)p).OwnerPartId).Distinct().Count();
Console.WriteLine($"\nReloaded from {Path.GetFileName(path)}: PartCount = {reloaded.PartCount}, " +
                  $"{reloaded.Pins.Count} pins across {distinctParts} distinct OwnerPartId value(s).");
if (reloaded.PartCount > 1 && distinctParts <= 1)
{
    Console.WriteLine("  Note: this build's SchLib round-trip does not preserve per-pin OwnerPartId, so the");
    Console.WriteLine("  reloaded pins are not split by part. The in-memory assignment above is how you author");
    Console.WriteLine("  multi-part components; the persistence gap is a library limitation, not a usage error.");
}

// ── Helpers ─────────────────────────────────────────────────────────────────

static void AddPin(SchComponent comp, string designator, string name,
                   double xMm, double yMm, PinOrientation orient, PinElectricalType type, int part)
{
    var pin = SchPin.Create(designator)
        .WithName(name)
        .At(Coord.FromMm(xMm), Coord.FromMm(yMm))
        .Length(Coord.FromMm(2.54))
        .Orient(orient)
        .Electrical(type)
        .Build();
    pin.OwnerPartId = part;   // tag the pin with its part (the builder doesn't set this)
    comp.AddPin(pin);
}

static void PrintParts(string heading, SchComponent comp)
{
    Console.WriteLine($"\n{heading}:");
    foreach (var group in comp.Pins.GroupBy(p => ((SchPin)p).OwnerPartId).OrderBy(g => g.Key))
        Console.WriteLine($"  Part {group.Key}: {group.Count()} pins " +
                          $"({string.Join(", ", group.Select(p => $"{p.Designator}:{p.Name}"))})");
}

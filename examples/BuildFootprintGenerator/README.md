# Programmatic footprint generation

Instead of drawing every footprint by hand, generate a family from parameters. This
example builds QFN and DIP footprints from (pin count, pitch, body size) and writes them
to a `.PcbLib` — the core of a "footprint wizard".

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
dotnet run --project examples/BuildFootprintGenerator
```

## What it shows

The PCB component fluent builder (`PcbComponent.Create(...)` returns a `ComponentBuilder`)
with SMD and through-hole pads, pad shapes, a silkscreen courtyard, a pin-1 marker, and
the `".Designator"` text token.

```csharp
static PcbComponent BuildQfn(string name, int pins, double pitch, double body,
                             double padLen, double padWid)
{
    var fp = PcbComponent.Create(name).WithDescription($"{pins}-pin QFN");
    var pin = 1;
    void Pad(double x, double y, double w, double h) => fp.AddPad(p => p
        .At(Coord.FromMm(x), Coord.FromMm(y))
        .Size(Coord.FromMm(w), Coord.FromMm(h))
        .Shape(PadShape.RoundedRectangle)
        .Smd(1)                                  // sets Layer = 1 (top), HoleSize = 0
        .WithDesignator((pin++).ToString()));
    // ... place perSide pads along each of the four sides ...
    return fp.Build();
}
```

Through-hole pads use `.ThroughHole(holeSize)` and `.Layer(74)` (Multi-layer) instead of
`.Smd(...)`. Pad 1 is drawn `PadShape.Rectangular` to mark it.

| Builder call | Effect |
|--------------|--------|
| `.Smd(layer)` | surface-mount pad (HoleSize = 0) on the given layer |
| `.ThroughHole(hole)` | plated through-hole of the given drill size |
| `.Shape(PadShape.…)` | `Round` / `Rectangular` / `RoundedRectangle` |
| `.AddTrack` / `.AddArc` / `.AddText` | silkscreen courtyard, pin-1 dot, designator |

## Sample output

```
Generated 4 footprints -> ...\Generated.PcbLib

  QFN-16-0.5    16 pads   bounds 5.68 x 5.10 mm
  QFN-32-0.5    32 pads   bounds 6.30 x 7.10 mm
  DIP-8          8 pads   bounds 10.89 x 11.88 mm
  DIP-14        14 pads   bounds 18.51 x 19.50 mm
```

The library is saved and read back to confirm the round-trip.

## Notes

- Silkscreen is drawn on layer 21 (Top Overlay). The geometry here is illustrative —
  match a real datasheet's pad dimensions for production footprints.

See the [guides index](../../guides/README.md) for the full set of examples.

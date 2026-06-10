# Generating a pick-and-place (centroid) file

Read a PCB document (`.PcbDoc`) and emit a **pick-and-place / centroid file**: one row
per placed component giving its reference designator, X/Y centroid, rotation, and board
side. This is the file an assembly machine consumes to populate the board.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
# Uses a bundled TestData board (TestData/USB Power Adapter.PcbDoc)
dotnet run --project examples/GeneratePickAndPlace

# Point it at your own file
dotnet run --project examples/GeneratePickAndPlace -- "C:\path\to\MyBoard.PcbDoc"
```

## Where the data lives

Each placed part is an `IPcbComponent`. Cast it to the concrete `PcbComponent` to reach
the placement fields.

```csharp
await using var doc = await AltiumLibrary.OpenPcbDocAsync(input);

foreach (var component in doc.Components)
{
    var pc = (PcbComponent)component;

    var designator = !string.IsNullOrWhiteSpace(pc.SourceDesignator)
        ? pc.SourceDesignator!         // schematic reference, e.g. "U1"
        : pc.Name;                      // fall back to the footprint pattern

    // A part is on the bottom if it sits on bottom copper or is flipped.
    var bottom = pc.Layer == 32 || pc.FlippedOnLayer;

    var x = pc.X.ToMm();                // Coord -> millimetres (use ToMils() for mils)
    var y = pc.Y.ToMm();
    var rotation = pc.Rotation;         // degrees
}
```

| Column | Source |
|--------|--------|
| Designator | `PcbComponent.SourceDesignator` (fallback: `Name`) |
| Mid X / Mid Y | `PcbComponent.X` / `.Y` → `.ToMm()` |
| Rotation | `PcbComponent.Rotation` (degrees) |
| Layer (side) | `PcbComponent.Layer` (1 = Top, 32 = Bottom) + `FlippedOnLayer` |
| Comment / Footprint | `PcbComponent.Comment` / `Pattern` |

These placement fields live on the concrete `PcbComponent`, not the `IPcbComponent`
interface — hence the cast.

## Sample output

```
Designator       X (mm)     Y (mm)     Rot  Side    Footprint
------------------------------------------------------------------------------
FID1               32.5         38      90  Top     Fiducial-1mm
J1                   56         50       0  Top     CNC TECH 1002-021-01000
J2                 47.5         37       0  Top     Molex 2047110001
```

The CSV (`Designator,Mid X (mm),Mid Y (mm),Rotation,Layer,Comment,Footprint`) is written
to a temp folder; the program prints the path. Numbers are formatted with the invariant
culture so the decimal separator is always `.` regardless of the machine's locale.

## Notes

- Coordinates are the component **origin** Altium placed the part at. Some house formats
  want the body centroid instead; adjust if your assembler requires it.
- To emit mils instead of millimetres, swap `.ToMm()` for `.ToMils()`.

See the [guides index](../../guides/README.md) for the full set of examples.

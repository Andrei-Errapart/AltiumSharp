# Inspecting a PCB

A "what's in this board?" report for a `.PcbDoc`: physical size from the board outline,
the copper layer stackup, primitive/net counts, and a design-rule summary.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
dotnet run --project examples/InspectBoard                  # bundled board
dotnet run --project examples/InspectBoard -- "C:\My.PcbDoc"
```

## Where the data lives

The reader returns an `IPcbDocument`; cast it to the concrete `PcbDocument` to reach the
board-level collections and outline — none of which are on the interface.

```csharp
var doc = (PcbDocument)await AltiumLibrary.OpenPcbDocAsync(input);

// Physical size: the board outline is a closed polygon of world points.
var outline = doc.GetBoardOutline();
Coord minX = outline[0].X, maxX = outline[0].X, minY = outline[0].Y, maxY = outline[0].Y;
foreach (var p in outline)
{
    if (p.X < minX) minX = p.X;  if (p.X > maxX) maxX = p.X;
    if (p.Y < minY) minY = p.Y;  if (p.Y > maxY) maxY = p.Y;
}
var widthMm = (maxX - minX).ToMm();

// Layer stackup (may be null).
if (doc.LayerStack is { } stack)
    foreach (var layer in stack.Layers)
        Console.WriteLine($"{layer.Index} {layer.Name} {(layer.CopperEnabled ? "copper" : "dielectric")}");

// Design rules: rule-specific values are in the Parameters dictionary.
foreach (var rule in doc.Rules.Where(r => r.RuleKind == "Clearance"))
    rule.Parameters.TryGetValue("GAP", out var gap);
```

| Field | Source |
|-------|--------|
| Board size | `PcbDocument.GetBoardOutline()` bounding box |
| Layer stack | `PcbDocument.LayerStack` → `PcbLayerEntry.Name` / `.CopperEnabled` |
| Counts | `Components`, `Pads`, `Tracks`, `Vias`, `Nets`, `Rules`, `Polygons`, … |
| Rule values | `PcbRule.Parameters` (e.g. `"GAP"`, `"MINWIDTH"`) |

## Sample output

```
Board outline
  100.00 x 100.00 mm  (96 edge points)

Contents
  Components ........ 57
  Pads .............. 210
  Tracks ............ 1374
  Vias .............. 1269
  Nets .............. 35
  Rules ............. 41
```

## Notes

- Compute the outline bounding box from the points directly: `CoordRect.Union` of
  zero-area point rectangles collapses to empty.
- `LayerStack` is parsed from versioned `V7_LAYER*` keys in Board6 and can be `null` for
  boards that don't store them — the example handles that case.

See the [guides index](../../guides/README.md) for the full set of examples.

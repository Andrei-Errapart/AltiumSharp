# Diffing two libraries

Compares two libraries of the same type and reports which components were **added**,
**removed**, or **changed** — useful for reviewing a library update or gating it in CI.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
# Synthesizes two demo libraries that differ on purpose (added / removed / changed)
dotnet run --project examples/DiffLibraries

# Diff two of your own libraries (same type)
dotnet run --project examples/DiffLibraries -- old.PcbLib new.PcbLib
```

## How it works

Both files open through `AltiumLibrary.OpenAsync`, which returns an `ILibrary` whose
`AllComponents` are `IComponent` (Name + Bounds) regardless of PCB vs schematic. Build a
name → signature map for each side and compare. The signature is a cheap fingerprint —
primitive counts plus size — so cast each component to its concrete type to count its
primitives.

```csharp
await using var a = await AltiumLibrary.OpenAsync(pathA);
await using var b = await AltiumLibrary.OpenAsync(pathB);

static string Signature(IComponent c) => c switch
{
    PcbComponent p => $"pads={p.Pads.Count} tracks={p.Tracks.Count} {c.Bounds.Width.ToMm():F2}x{c.Bounds.Height.ToMm():F2}mm",
    SchComponent s => $"pins={s.Pins.Count} rects={s.Rectangles.Count} {c.Bounds.Width.ToMm():F2}x{c.Bounds.Height.ToMm():F2}mm",
    _ => ""
};

var added   = mapB.Keys.Where(k => !mapA.ContainsKey(k));
var removed = mapA.Keys.Where(k => !mapB.ContainsKey(k));
var changed = mapA.Keys.Where(k => mapB.ContainsKey(k) && mapA[k] != mapB[k]);
```

## Sample output

```
A has 3 component(s); B has 3.
  + 1 added   - 1 removed   ~ 1 changed   = 1 unchanged

  + L0402
  - R0603
  ~ R0402
      A: pads=2 tracks=0 vias=0 arcs=0 1.60x0.80mm
      B: pads=3 tracks=0 vias=0 arcs=0 2.60x0.80mm
```

## Notes

- The signature is intentionally coarse. Tighten it (compare pad positions, net names,
  3D models, …) for a stricter diff.
- Components are matched by name, so a rename reads as one removed + one added.

See the [guides index](../../guides/README.md) for the full set of examples.

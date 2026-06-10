# Validating / linting a library

Surfaces two kinds of problem in a `.PcbLib` or `.SchLib`: the non-fatal **diagnostics**
the reader recorded, and **lint** rules this example applies itself (unnamed components,
footprints with no pads / symbols with no pins, blank or duplicate designators). It's the
shape of a pre-commit check over a parts library.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
dotnet run --project examples/ValidateLibrary                   # bundled PcbLib
dotnet run --project examples/ValidateLibrary -- "C:\Parts.SchLib"
```

## Where the data lives

Every model exposes a `Diagnostics` list; it's on the concrete library type, so cast the
reader's result.

```csharp
var lib = (PcbLibrary)await AltiumLibrary.OpenPcbLibAsync(input);

foreach (var d in lib.Diagnostics)
    Console.WriteLine($"[{d.Severity}] {d.Message}");

foreach (var component in lib.Components)
{
    var c = (PcbComponent)component;
    if (c.Pads.Count == 0)
        Report("Warning", c.Name, "footprint has no pads");

    foreach (var dup in c.Pads.Select(p => ((PcbPad)p).Designator)
                 .Where(d => !string.IsNullOrWhiteSpace(d))
                 .GroupBy(d => d!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        Report("Warning", c.Name, $"duplicate pad designator '{dup.Key}'");
}
```

The `.SchLib` branch is symmetric: it checks for symbols with no pins and duplicate pin
designators. Add your own house rules (off-grid pads, courtyard checks, …) the same way.

## Sample output

```
Validated PCB - LEADLESS - QFN - PSEMI QFN-12 3x3x0.5.PcbLib — 1 component(s)

Reader diagnostics: 0
Lint issues: 0
Clean — no diagnostics or lint issues.
```

## Notes

- "Clean" is a valid result — a well-formed library should pass. Point the example at a
  rough library, or add stricter checks, to see issues reported.
- Diagnostics are read-time only; this is the place to add design-time validation.

See the [guides index](../../guides/README.md) for the full set of examples.

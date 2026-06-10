# Extracting a Bill of Materials

Read a schematic document (`.SchDoc`) and produce a grouped **bill of materials** —
one line per distinct part, with a quantity and the list of reference designators —
as a console table, a CSV file, and an HTML table.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
# Uses a bundled TestData schematic (TestData/Power Supply.SchDoc)
dotnet run --project examples/ExtractBom

# Point it at your own file
dotnet run --project examples/ExtractBom -- "C:\path\to\MyBoard.SchDoc"
```

## Where the data lives

A schematic component is an `ISchComponent`. Cast it to the concrete `SchComponent`
to reach the fields a BOM needs. The important gotcha: **there is no `Designator`
property** — Altium stores the reference designator as a *parameter* named
`"Designator"`.

```csharp
await using var doc = await AltiumLibrary.OpenSchDocAsync(input);

foreach (var component in doc.Components)
{
    var sc = (SchComponent)component;

    // Designator comes from the parameter collection, not a property.
    var designator = GetParameter(sc, "Designator");
    if (string.IsNullOrWhiteSpace(designator))
        continue;  // power port, net tie or graphic — not a purchasable part

    // Comment is Altium's "Value" field (e.g. "10k", "100nF").
    var value = !string.IsNullOrWhiteSpace(sc.Comment)
        ? sc.Comment!
        : (GetParameter(sc, "Value") ?? "");

    // Footprint: prefer the current PCB implementation link.
    var footprint = GetFootprint(sc) ?? GetParameter(sc, "Footprint") ?? "";
}

static string? GetParameter(SchComponent c, string name)
{
    foreach (var p in c.Parameters)
        if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrEmpty(p.Value) ? null : p.Value;
    return null;
}
```

| Field | Source |
|-------|--------|
| Designator | `SchComponent.Parameters` → `"Designator"` |
| Value | `SchComponent.Comment` (fallback: `"Value"` parameter) |
| Footprint | `SchComponent.Implementations` where `ModelType == "PCBLIB"` (fallback: `"Footprint"` parameter) |
| Manufacturer / MPN | custom parameters; MPN falls back to `SchComponent.DesignItemId` |

`Implementations` is only on the concrete `SchComponent`, not the `ISchComponent`
interface — another reason for the cast. Parameters are free-form key/value pairs, so
different libraries use different names; the example reads the common ones and is easy
to extend.

## Sample output

```
Qty  Value            Footprint              Designators                    MPN
----------------------------------------------------------------------------------
  2  CL10A226MP8NUNE  CAP 0603_1608          C12, C16                       CL10A226MP8NUNE
  2  GCM188R71E105KA… CAP 0603_1608          C13, C15                       GCM188R71E105KA64D
  1  LT1761IS5-5#TRM… AD SOT-23-5 S5 05-08…  IC4                            LT1761IS5-5#TRMPBF
  1  0022272031       MOLEX KK 0022272031    J4                             0022272031
```

The CSV and HTML are written to a temp folder (the program prints the paths).

## Notes

- A BOM needs only component metadata, so the lack of a pin-to-net connectivity API in
  the schematic model is irrelevant here.
- A *top sheet* that only contains sub-sheet symbols will report no designated
  components — run the example against a leaf schematic, or extend it to walk the
  hierarchy via `SheetSymbols` (see the planned `WalkHierarchy` example).

See the [guides index](../../guides/README.md) for the full set of examples.

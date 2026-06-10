# Walking a hierarchical schematic

A multi-sheet design has a top sheet whose sheet symbols point at child `.SchDoc` files,
which may themselves contain sheet symbols. This example walks that tree and prints it,
listing each sheet's component count and the ports that connect it to its parent.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
dotnet run --project examples/WalkHierarchy                  # best bundled top sheet
dotnet run --project examples/WalkHierarchy -- Top.SchDoc
```

## Where the data lives

Sheet symbols are on `SchDocument.SheetSymbols` (cast the `ISchDocument` the reader
returns). Each `SchSheetSymbol` has `FileName` (the child `.SchDoc` it references),
`SheetName` (its label), and `Entries` (the `SchSheetEntry` ports, each with a `Name`).

```csharp
async Task Walk(string path, int depth)
{
    await using var idoc = await AltiumLibrary.OpenSchDocAsync(path);
    var doc = (SchDocument)idoc;
    var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;

    foreach (var sheet in doc.SheetSymbols)
    {
        var ports = string.Join(", ", sheet.Entries.Select(e => e.Name));
        Console.WriteLine($"{sheet.SheetName} -> {sheet.FileName}  ports: {ports}");
        if (!string.IsNullOrWhiteSpace(sheet.FileName))
            await Walk(Path.Combine(folder, sheet.FileName!), depth + 1);   // recurse
    }
}
```

The example tracks visited files so a sheet reused by two parents (a shared sub-circuit)
is reported once and not re-expanded.

## Sample output

```
• Overview.SchDoc  —  9 components, 4 sub-sheet(s)
  ↳ DAC -> DAC.SchDoc  ports: CS, SCLK, MOSI, LDAC, BUFF_OUT
  • DAC.SchDoc  —  23 components, 0 sub-sheet(s)
  ↳ PSU -> Power Supply.SchDoc
  • Power Supply.SchDoc  —  7 components, 0 sub-sheet(s)
  ↳ SPI_SHIFT -> Level Shifter.SchDoc  ports: CS_3V3, SCLK_3V3, MOSI_3V3, ...
  • Level Shifter.SchDoc  —  5 components, 0 sub-sheet(s)
  ↳ LDAC_SHIFT -> Level Shifter.SchDoc  ports: ...
  • Level Shifter.SchDoc  (already visited)
```

## Notes

- There is **no project-file (`.PrjScr`) reader**, so child file names are resolved
  relative to the parent sheet's folder. Adapt the resolution if your project stores
  sheets elsewhere.

See the [guides index](../../guides/README.md) for the full set of examples.

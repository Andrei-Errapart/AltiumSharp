# Building a component catalog (thumbnail gallery)

Renders every component in a `.PcbLib` or `.SchLib` to a PNG and an SVG, then writes an
`index.html` gallery linking them all — the basis of a web library browser or a printable
parts sheet.

The complete, compiling source for this guide is [Program.cs](Program.cs).

This example references the rendering packages
(`OriginalCircuit.Altium.Rendering.Raster` and `.Svg`).

## Run

```bash
dotnet run --project examples/LibraryCatalog                  # bundled library
dotnet run --project examples/LibraryCatalog -- "C:\Parts.PcbLib"
```

## How it works

`AltiumLibrary.OpenAsync` returns an `ILibrary` whose `AllComponents` are `IComponent` —
the same interface both renderers accept — so the loop is identical for PCB and schematic
libraries.

```csharp
await using var lib = await AltiumLibrary.OpenAsync(input);

var raster = new RasterRenderer();   // -> PNG (SkiaSharp)
var svg = new SvgRenderer();         // -> SVG (no native deps)
var options = new RenderOptions { Width = 400, Height = 300 };

foreach (var component in lib.AllComponents)
{
    await raster.RenderAsync(component, Path.Combine(outDir, $"{component.Name}.png"), options);
    await svg.RenderAsync(component, Path.Combine(outDir, $"{component.Name}.svg"), options);
}
// ... then emit an index.html that <img>s each PNG and links each SVG ...
```

`RenderOptions` (Width/Height/BackgroundColor/AutoZoom/Scale) comes from
`OriginalCircuit.Eda.Rendering`; the renderers from the `.Raster` / `.Svg` packages.

## Sample output

```
Cataloguing: SCH - VGA - AD AD8367.SchLib
  rendered AD AD8367
1 component(s) rendered.
Gallery: ...\AltiumCatalogExample\SCH - VGA - AD AD8367\index.html
```

Open the `index.html` to see the gallery: a card per component with its thumbnail, size,
and a link to the vector SVG.

## Notes

- Raster output uses SkiaSharp; SVG output has no native dependencies.
- Many vendor libraries hold a single component, so the gallery may have one card — point
  the example at a multi-part library to fill the grid.

See the [guides index](../../guides/README.md) for the full set of examples.

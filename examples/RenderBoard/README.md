# RenderBoard

Renders a whole PCB document (`.PcbDoc`) as a **photorealistic 2D board** — a
fab-house / gerber-viewer look (think JLCPCB) — from the console.

```
dotnet run --project examples/RenderBoard                 # bundled sample board
dotnet run --project examples/RenderBoard -- MyBoard.PcbDoc
```

It writes a handful of variations (top and bottom views, several solder-mask /
finish combinations) as PNG, plus one SVG, into a temp folder and prints the paths.

## What it shows

- Loading a board with `AltiumLibrary.OpenPcbDocAsync(path)`.
- `RasterRenderer.RenderRealisticAsync(document, path, options, style)` for PNG.
- `SvgRenderer.RenderRealisticAsync(document, path, options, style)` for SVG.
- `PcbRealisticStyle` presets (`GreenEnig`, `BlackEnig`, `BlueHasl`, …), `.For(side)`
  to pick the top/bottom view, and `Supersample` for smoother raster edges.

The photorealistic renderer composites by physical layer — bare laminate, copper,
a translucent solder mask, plated finish on exposed copper, silkscreen and drills —
and crops the output to the board's bounding box. Colours are fully configurable on
`PcbRealisticStyle` (substrate, copper, solder mask, silkscreen, finish, hole).

The SVG keeps each physical layer as a named group
(`substrate` / `copper` / `soldermask` / `silkscreen` / `drills`), so a viewer can
turn layers on and off after export — see the **BoardViewer** example for an
interactive version.

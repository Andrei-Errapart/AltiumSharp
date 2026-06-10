# OriginalCircuit.Altium — Guides

Task-oriented guides for the library. **Every guide is rooted in a runnable example
project** under [`examples/`](../examples/): the prose lives in the example's own
`README.md`, right next to a `Program.cs` that compiles and runs. All example projects
are part of the solution, so they are built on every CI run — if a documented snippet
stops compiling, the build fails. The code in the guides is therefore always real,
working code, not pseudo-code that drifts out of date.

Run any example with:

```bash
dotnet run --project examples/<ExampleName>
```

Read-oriented examples load real sample files from [`TestData/`](../TestData/) by default,
and accept a path to your own file as an argument.

## Getting started

For installation, the quick-start, and the coordinate system, see the top-level
[README](../README.md) and the [API reference](../API-REFERENCE.md).

## Guides

### Reading & extracting

| Guide | What you get | Example |
|-------|--------------|---------|
| [Loading & inspecting files](../examples/LoadFiles/) | Open all four file types and walk their contents | `LoadFiles` |
| [Extracting a bill of materials](../examples/ExtractBom/) | Grouped BOM (CSV + HTML) from a `.SchDoc` | `ExtractBom` |
| [Pick-and-place / centroid file](../examples/GeneratePickAndPlace/) | Assembly centroid CSV from a `.PcbDoc` | `GeneratePickAndPlace` |
| [Extracting embedded assets](../examples/ExtractEmbeddedAssets/) | STEP 3D models and bitmap images to disk | `ExtractEmbeddedAssets` |

### Creating & modifying

| Guide | What you get | Example |
|-------|--------------|---------|
| [Creating files from scratch](../examples/CreateFiles/) | Build PcbLib, SchLib, SchDoc, PcbDoc with the fluent builders | `CreateFiles` |
| [Modifying existing files](../examples/ModifyFiles/) | Load → change → save round-trips for all four types | `ModifyFiles` |

### Rendering

| Guide | What you get | Example |
|-------|--------------|---------|
| [Rendering components & boards](../examples/RenderFiles/) | PNG/SVG output, board view sides, layer filtering | `RenderFiles` |

## Planned guides

These topics are on the roadmap; each will ship as a new example project plus its own
guide, following the same pattern:

- **Inspecting a board** — outline, layer stackup, net/primitive stats, design-rule
  summary (`GetBoardOutline()`, `LayerStack`, `Rules`).
- **Nets & connectivity** — querying PCB net membership, and what the model does and
  does not track (the schematic side is geometric; there is no pin-to-net API).
- **Component catalogs** — render every component in a library to a thumbnail gallery.
- **Programmatic footprint generation** — parametric footprint families.
- **Multi-part components** — symbols with `PartCount > 1` and `OwnerPartId`.
- **Hierarchical schematics** — walking sheet symbols into child sheets.
- **Diffing & validating libraries** — comparing libraries and surfacing diagnostics.

## A note on scope

The library reads and writes the four document/library types; there is **no project-file
(`.PrjPcb`/`.PrjScr`) reader**, and the schematic model is geometric (no built-in
pin-to-net connectivity). Guides call out these boundaries where they matter rather than
implying capabilities that aren't there.

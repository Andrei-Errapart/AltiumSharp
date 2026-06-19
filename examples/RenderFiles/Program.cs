// ============================================================================
// Example: Rendering Altium Components to PNG and SVG
// ============================================================================
//
// This example demonstrates the rendering system, which can produce visual
// previews of PCB footprints and schematic symbols.
//
// RENDERING ARCHITECTURE
// ──────────────────────
// The rendering system is split into three NuGet packages:
//
//   OriginalCircuit.Altium.Rendering.Core   - Shared abstractions
//     CoordTransform  : Converts between world coordinates and screen pixels
//     RenderOptions   : Width, Height, BackgroundColor, AutoZoom, Scale
//     LayerColors     : Maps PCB layer IDs to display colors and draw order
//     IRenderContext  : Abstraction implemented by each output format
//
//   OriginalCircuit.Altium.Rendering.Raster - PNG output (requires SkiaSharp)
//     RasterRenderer  : Renders to PNG via SkiaSharp
//
//   OriginalCircuit.Altium.Rendering.Svg    - SVG output (no external deps)
//     SvgRenderer     : Renders to SVG using System.Xml.Linq
//
// WHAT CAN BE RENDERED
// ────────────────────
// Both renderers accept any IComponent (PcbComponent or SchComponent) as well
// as whole documents (PcbDocument boards and SchDocument sheets). They draw all
// contained primitives: pads, tracks, arcs, pins, rectangles, polylines, etc.
//
// Board (PcbDocument) renders fill the board outline as a black substrate and
// take an optional PcbRenderSettings to pick the view side (Top / Bottom / Both,
// where Bottom is mirrored) and filter which layers are drawn — see section 8.
//
// The renderers handle coordinate transformation automatically:
// with AutoZoom=true (default), the content is scaled to fit the
// output dimensions while maintaining aspect ratio.
//
// ============================================================================

using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Raster;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;
using PinElectricalType = OriginalCircuit.Altium.Models.Sch.PinElectricalType;

var outputDir = Path.Combine(Path.GetTempPath(), "AltiumRenderExample");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output directory: {outputDir}");

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  1. Create a PCB component to render                                    ║
// ║                                                                         ║
// ║  We build a simplified QFP footprint with pads on one side, a           ║
// ║  silkscreen outline (4 tracks), a pin-1 marker (arc), and a             ║
// ║  designator text. This gives us multiple primitive types to render.     ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

var pcbComponent = PcbComponent.Create("QFP48")
    .WithDescription("48-pin QFP footprint")

    // Five SMD pads along the left side, spaced at 2.54mm pitch.
    // Layer 1 = Top Copper. Each pad is 1.5 x 0.6 mm (horizontal orientation).
    .AddPad(p => p.At(Coord.FromMm(-6.35), Coord.FromMm(-5.08))
        .Size(Coord.FromMm(1.5), Coord.FromMm(0.6)).HoleSize(Coord.FromMm(0))
        .WithDesignator("1").Layer(1))
    .AddPad(p => p.At(Coord.FromMm(-6.35), Coord.FromMm(-2.54))
        .Size(Coord.FromMm(1.5), Coord.FromMm(0.6)).HoleSize(Coord.FromMm(0))
        .WithDesignator("2").Layer(1))
    .AddPad(p => p.At(Coord.FromMm(-6.35), Coord.FromMm(0))
        .Size(Coord.FromMm(1.5), Coord.FromMm(0.6)).HoleSize(Coord.FromMm(0))
        .WithDesignator("3").Layer(1))
    .AddPad(p => p.At(Coord.FromMm(-6.35), Coord.FromMm(2.54))
        .Size(Coord.FromMm(1.5), Coord.FromMm(0.6)).HoleSize(Coord.FromMm(0))
        .WithDesignator("4").Layer(1))
    .AddPad(p => p.At(Coord.FromMm(-6.35), Coord.FromMm(5.08))
        .Size(Coord.FromMm(1.5), Coord.FromMm(0.6)).HoleSize(Coord.FromMm(0))
        .WithDesignator("5").Layer(1))

    // Silkscreen outline: four tracks forming a rectangle.
    // Layer 33 = Top Overlay (silkscreen). Rendered in overlay color.
    .AddTrack(t => t.From(Coord.FromMm(-5.08), Coord.FromMm(-6.35))
        .To(Coord.FromMm(5.08), Coord.FromMm(-6.35)).Width(Coord.FromMm(0.254)).Layer(33))
    .AddTrack(t => t.From(Coord.FromMm(5.08), Coord.FromMm(-6.35))
        .To(Coord.FromMm(5.08), Coord.FromMm(6.35)).Width(Coord.FromMm(0.254)).Layer(33))
    .AddTrack(t => t.From(Coord.FromMm(5.08), Coord.FromMm(6.35))
        .To(Coord.FromMm(-5.08), Coord.FromMm(6.35)).Width(Coord.FromMm(0.254)).Layer(33))
    .AddTrack(t => t.From(Coord.FromMm(-5.08), Coord.FromMm(6.35))
        .To(Coord.FromMm(-5.08), Coord.FromMm(-6.35)).Width(Coord.FromMm(0.254)).Layer(33))

    // Pin 1 marker: a small filled circle at the corner (full arc, 0-360 degrees)
    .AddArc(a => a.At(Coord.FromMm(-5.08), Coord.FromMm(-6.35))
        .Radius(Coord.FromMm(0.5)).Angles(0, 360).Width(Coord.FromMm(0.12)).Layer(33))

    // ".Designator" is a special token replaced by the component's ref des
    .AddText(".Designator", t => t
        .At(Coord.FromMm(0), Coord.FromMm(7.62)).Height(Coord.FromMm(1.0)).Layer(33))
    .Build();

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  2. Render to PNG (raster)                                              ║
// ║                                                                         ║
// ║  RasterRenderer uses SkiaSharp to produce PNG images.                   ║
// ║  RenderAsync takes: (component, outputStream, options)                  ║
// ║  With AutoZoom=true (default), the component is automatically           ║
// ║  centered and scaled to fill the output dimensions.                     ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== Rendering PCB Component to PNG ===");

var rasterRenderer = new RasterRenderer();
var pngPath = Path.Combine(outputDir, "pcb_component.png");
using (var fs = File.Create(pngPath))
    await rasterRenderer.RenderAsync(pcbComponent, fs, new RenderOptions { Width = 512, Height = 512 });

Console.WriteLine($"  PNG saved: {pngPath} ({new FileInfo(pngPath).Length} bytes)");

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  3. Render to SVG (vector)                                              ║
// ║                                                                         ║
// ║  SvgRenderer produces SVG XML. No external dependencies needed.         ║
// ║  The same RenderOptions and component interface is used.                ║
// ║  SVG output is ideal for web display or further processing.             ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== Rendering PCB Component to SVG ===");

var svgRenderer = new SvgRenderer();
var svgPath = Path.Combine(outputDir, "pcb_component.svg");
using (var fs = File.Create(svgPath))
    await svgRenderer.RenderAsync(pcbComponent, fs, new RenderOptions { Width = 512, Height = 512 });

Console.WriteLine($"  SVG saved: {svgPath} ({new FileInfo(svgPath).Length} bytes)");

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  4. Render a schematic component                                        ║
// ║                                                                         ║
// ║  The same renderers work for SchComponent too. Schematic rendering      ║
// ║  draws pins (with designator/name text), polylines, rectangles,         ║
// ║  labels, arcs, etc.                                                     ║
// ║                                                                         ║
// ║  Here we create an op-amp symbol using two construction styles:         ║
// ║    - Polyline body via the fluent builder (SchPolyline.Create())        ║
// ║    - Pins via direct construction (new SchPin { ... })                  ║
// ║  Both approaches are valid and interchangeable.                         ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== Rendering Schematic Component to PNG ===");

var schComponent = new SchComponent { Name = "OPAMP" };

// Op-amp triangle body: polyline closing back to the first point
schComponent.AddPolyline(SchPolyline.Create()
    .LineWidth(1).Color(128)
    .From(Coord.FromMm(-2.54), Coord.FromMm(-3.81))    // Bottom-left
    .To(Coord.FromMm(-2.54), Coord.FromMm(3.81))        // Top-left
    .To(Coord.FromMm(5.08), Coord.FromMm(0))             // Right tip
    .To(Coord.FromMm(-2.54), Coord.FromMm(-3.81))        // Close
    .Build());

// Pins using direct construction (alternative to SchPin.Create() builder).
// ShowName/ShowDesignator control whether the pin's name and number are
// rendered as text next to the pin line.
schComponent.AddPin(new SchPin
{
    Location = new CoordPoint(Coord.FromMm(-7.62), Coord.FromMm(1.27)),
    Length = Coord.FromMm(5.08),
    Orientation = PinOrientation.Right,  // Pin line extends to the right
    Designator = "3",
    Name = "+",
    ShowName = true,
    ShowDesignator = true
});

schComponent.AddPin(new SchPin
{
    Location = new CoordPoint(Coord.FromMm(-7.62), Coord.FromMm(-1.27)),
    Length = Coord.FromMm(5.08),
    Orientation = PinOrientation.Right,
    Designator = "2",
    Name = "-",
    ShowName = true,
    ShowDesignator = true
});

schComponent.AddPin(new SchPin
{
    Location = new CoordPoint(Coord.FromMm(10.16), Coord.FromMm(0)),
    Length = Coord.FromMm(5.08),
    Orientation = PinOrientation.Left,   // Pin line extends to the left
    Designator = "1",
    Name = "OUT",
    ShowName = true,
    ShowDesignator = true
});

// SchLabel adds a text annotation at the specified location
schComponent.AddLabel(new SchLabel
{
    Text = "OPAMP",
    Location = new CoordPoint(Coord.FromMm(-1.27), Coord.FromMm(5.08)),
    FontId = 1,                          // Font index (1 = default)
    Color = 128
});

var schPngPath = Path.Combine(outputDir, "sch_component.png");
using (var fs = File.Create(schPngPath))
    await rasterRenderer.RenderAsync(schComponent, fs, new RenderOptions { Width = 512, Height = 512 });

Console.WriteLine($"  PNG saved: {schPngPath} ({new FileInfo(schPngPath).Length} bytes)");

// SVG of the same schematic component
Console.WriteLine("\n=== Rendering Schematic Component to SVG ===");

var schSvgPath = Path.Combine(outputDir, "sch_component.svg");
using (var fs = File.Create(schSvgPath))
    await svgRenderer.RenderAsync(schComponent, fs, new RenderOptions { Width = 512, Height = 512 });

Console.WriteLine($"  SVG saved: {schSvgPath} ({new FileInfo(schSvgPath).Length} bytes)");

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  5. CoordTransform: world-to-screen coordinate mapping                  ║
// ║                                                                         ║
// ║  CoordTransform handles the mapping between Altium's internal           ║
// ║  coordinate space (Coord values) and screen pixel positions.            ║
// ║  This is what the renderers use internally, but you can also use it     ║
// ║  directly for custom rendering or hit-testing.                          ║
// ║                                                                         ║
// ║  AutoZoom() calculates Scale and Center to fit a CoordRect into the     ║
// ║  screen dimensions. WorldToScreen() then converts any Coord pair to     ║
// ║  pixel coordinates.                                                     ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== CoordTransform Example ===");

var transform = new CoordTransform
{
    ScreenWidth = 1024,
    ScreenHeight = 768
};

// Bounds returns the bounding box (CoordRect) of all primitives
var bounds = pcbComponent.Bounds;
transform.AutoZoom(bounds);

Console.WriteLine($"  Component bounds: ({bounds.Min.X.ToMm():F2}, {bounds.Min.Y.ToMm():F2}) to " +
                  $"({bounds.Max.X.ToMm():F2}, {bounds.Max.Y.ToMm():F2}) mm");
Console.WriteLine($"  Scale: {transform.Scale:F6}");
Console.WriteLine($"  Center: ({transform.CenterX:F0}, {transform.CenterY:F0})");

// Convert the world origin (0,0) to screen pixel coordinates
var (sx, sy) = transform.WorldToScreen(Coord.FromMm(0), Coord.FromMm(0));
Console.WriteLine($"  World (0, 0) -> Screen ({sx:F1}, {sy:F1})");

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  6. Layer Colors: predefined color scheme for PCB layers                ║
// ║                                                                         ║
// ║  LayerColors provides Altium's default display colors for each layer.   ║
// ║  GetColor() returns an ARGB uint (0xAARRGGBB).                          ║
// ║  GetDrawPriority() returns the draw order (higher = drawn later/on top).║
// ║  These are used by the renderers but available for custom rendering.    ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== Layer Colors ===");

var layers = new (int Id, string Name)[]
{
    (1, "Top Layer"),              // Top copper
    (32, "Bottom Layer"),          // Bottom copper
    (33, "Top Overlay"),           // Top silkscreen
    (34, "Bottom Overlay"),        // Bottom silkscreen
    (37, "Top Solder"),            // Top solder mask
    (74, "Multi Layer")            // Through-hole pads/vias (all copper)
};

foreach (var (id, name) in layers)
{
    var color = LayerColors.GetColor(id);
    var priority = LayerColors.GetDrawPriority(id);
    Console.WriteLine($"  Layer {id} ({name}): color=0x{color:X8}, priority={priority}");
}

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  7. Custom RenderOptions                                                ║
// ║                                                                         ║
// ║  RenderOptions controls the output:                                     ║
// ║    Width/Height     - Output image dimensions in pixels                 ║
// ║    BackgroundColor  - ARGB background (0xFF000020 = dark blue)          ║
// ║    AutoZoom         - Auto-fit component to viewport (default: true)    ║
// ║    Scale            - Additional scale factor (default: 1.0)            ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== Custom Render Options ===");

var options = new RenderOptions
{
    Width = 2048,
    Height = 2048,
    BackgroundColor = EdaColor.FromArgb(0xFF, 0x00, 0x00, 0x20), // dark blue background
    AutoZoom = true,
    Scale = 1.0
};

var hiResPath = Path.Combine(outputDir, "pcb_hires.png");
using (var fs = File.Create(hiResPath))
    await rasterRenderer.RenderAsync(pcbComponent, fs, options);

Console.WriteLine($"  Hi-res PNG: {hiResPath} ({new FileInfo(hiResPath).Length} bytes)");

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  8. Render a whole PCB board (PcbDocument)                               ║
// ║                                                                         ║
// ║  Whole boards render every layer onto a black substrate — the board     ║
// ║  outline filled behind everything, so copper/silk read like a real      ║
// ║  board. PcbRenderSettings selects the view side (Top / Bottom / Both)   ║
// ║  and which layers are drawn. The Bottom view is mirrored horizontally,  ║
// ║  so it reads as if the board were physically flipped over.              ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== Rendering a PCB Board (document) ===");

// In practice you load a board with `await new PcbDocReader().ReadAsync(path)`.
// Here we build a tiny one so the example stays self-contained.
var board = new PcbDocument();

// The board outline is normally read from the file's Board6 record. We set a
// simple 40 x 30 mm rectangle (KIND/VX/VY vertices, last repeats the first to
// close) so the renderer fills the board area black behind the layers.
board.BoardParameters = new Dictionary<string, string>
{
    ["KIND0"] = "0", ["VX0"] = "0mil",      ["VY0"] = "0mil",
    ["KIND1"] = "0", ["VX1"] = "1574.8mil", ["VY1"] = "0mil",
    ["KIND2"] = "0", ["VX2"] = "1574.8mil", ["VY2"] = "1181.1mil",
    ["KIND3"] = "0", ["VX3"] = "0mil",      ["VY3"] = "1181.1mil",
    ["KIND4"] = "0", ["VX4"] = "0mil",      ["VY4"] = "0mil",
};

// Tracks on a spread of layers, plus a through-hole via (1 = Top copper,
// 32 = Bottom copper, 5 = Mid-Layer 4 internal copper, 33 = Top Overlay silk,
// 69 = Mechanical 13).
board.AddTrack(PcbTrack.Create().From(Coord.FromMm(5), Coord.FromMm(5))
    .To(Coord.FromMm(35), Coord.FromMm(5)).Width(Coord.FromMm(0.5)).Layer(1).Build());
board.AddTrack(PcbTrack.Create().From(Coord.FromMm(5), Coord.FromMm(25))
    .To(Coord.FromMm(35), Coord.FromMm(25)).Width(Coord.FromMm(0.5)).Layer(32).Build());
board.AddTrack(PcbTrack.Create().From(Coord.FromMm(5), Coord.FromMm(15))
    .To(Coord.FromMm(35), Coord.FromMm(15)).Width(Coord.FromMm(0.3)).Layer(5).Build());
board.AddTrack(PcbTrack.Create().From(Coord.FromMm(2), Coord.FromMm(2))
    .To(Coord.FromMm(38), Coord.FromMm(2)).Width(Coord.FromMm(0.2)).Layer(33).Build());
board.AddTrack(PcbTrack.Create().From(Coord.FromMm(0), Coord.FromMm(0))
    .To(Coord.FromMm(40), Coord.FromMm(30)).Width(Coord.FromMm(0.1)).Layer(69).Build());
board.AddVia(PcbVia.Create().At(Coord.FromMm(20), Coord.FromMm(15))
    .Diameter(Coord.FromMm(1.2)).HoleSize(Coord.FromMm(0.6)).Build());

var boardOptions = new RenderOptions { Width = 800, Height = 600 };

// Top view (the default): bottom-side copper is hidden; the board is black.
var boardTopPath = Path.Combine(outputDir, "board_top.png");
using (var fs = File.Create(boardTopPath))
    await rasterRenderer.RenderAsync(board, fs, boardOptions,
        new PcbRenderSettings { ViewSide = PcbViewSide.Top });
Console.WriteLine($"  Top view:     {boardTopPath}");

// Bottom view: mirrored; bottom copper shows and top-side copper is hidden.
var boardBottomPath = Path.Combine(outputDir, "board_bottom.png");
using (var fs = File.Create(boardBottomPath))
    await rasterRenderer.RenderAsync(board, fs, boardOptions,
        new PcbRenderSettings { ViewSide = PcbViewSide.Bottom });
Console.WriteLine($"  Bottom view:  {boardBottomPath}");

// Decluttered top view: hide mechanical and internal-copper layers to read
// just the top-side routing and silk. Equivalent to a custom predicate:
//   LayerFilter = layer => PcbLayerGroups.IsSignalOrSilk(layer)
var boardRoutingPath = Path.Combine(outputDir, "board_top_routing.png");
using (var fs = File.Create(boardRoutingPath))
    await rasterRenderer.RenderAsync(board, fs, boardOptions,
        new PcbRenderSettings
        {
            ViewSide = PcbViewSide.Top,
            ShowMechanical = false,
            ShowInternalCopper = false
        });
Console.WriteLine($"  Top routing:  {boardRoutingPath}");

// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  9. Photorealistic board render (RenderRealisticAsync)                   ║
// ║                                                                         ║
// ║  A separate render path produces a fab-house / gerber-viewer look       ║
// ║  (think JLCPCB) instead of the Altium-editor view: a coloured solder    ║
// ║  mask over copper and laminate, plated pads (HASL/ENIG), silkscreen,    ║
// ║  and drilled holes. PCB-only. Configure colours via PcbRealisticStyle   ║
// ║  — use a preset (GreenEnig, BlackEnig, BlueHasl, …) or set your own.     ║
// ║  Available on both RasterRenderer (PNG/JPEG) and SvgRenderer (SVG).      ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

Console.WriteLine("\n=== Rendering a Photorealistic PCB Board ===");

var realisticOptions = new RenderOptions { Width = 1000, Height = 800 };

// Green mask + ENIG (gold) + white silk — the default fab look (top view).
var greenTopPath = Path.Combine(outputDir, "board_realistic_green.png");
using (var fs = File.Create(greenTopPath))
    await rasterRenderer.RenderRealisticAsync(board, fs, realisticOptions, PcbRealisticStyle.GreenEnig);
Console.WriteLine($"  Green/ENIG top:  {greenTopPath}");

// Matte-black mask preset, supersampled 2x for smoother edges.
var blackStyle = PcbRealisticStyle.BlackEnig;
blackStyle.Supersample = 2;
var blackPath = Path.Combine(outputDir, "board_realistic_black.png");
using (var fs = File.Create(blackPath))
    await rasterRenderer.RenderRealisticAsync(board, fs, realisticOptions, blackStyle);
Console.WriteLine($"  Black/ENIG:      {blackPath}");

// Bottom side (mirrored) of any preset via .For(PcbViewSide.Bottom).
var bottomPath = Path.Combine(outputDir, "board_realistic_bottom.png");
using (var fs = File.Create(bottomPath))
    await rasterRenderer.RenderRealisticAsync(board, fs, realisticOptions,
        PcbRealisticStyle.GreenEnig.For(PcbViewSide.Bottom));
Console.WriteLine($"  Green bottom:    {bottomPath}");

// Fully custom colours (purple mask, immersion-silver finish, yellow silk).
var customStyle = new PcbRealisticStyle
{
    SolderMaskColor = EdaColor.FromArgb(0xDC, 0x52, 0x1E, 0x7A), // translucent purple (A,R,G,B)
    FinishColor = EdaColor.FromRgb(0xD3, 0xD6, 0xDA),           // immersion silver
    SilkscreenColor = EdaColor.FromRgb(0xF5, 0xE6, 0x4B),       // yellow silk
};
var customPath = Path.Combine(outputDir, "board_realistic_custom.svg");
using (var fs = File.Create(customPath))
    await svgRenderer.RenderRealisticAsync(board, fs, realisticOptions, customStyle);
Console.WriteLine($"  Custom (SVG):    {customPath}");

Console.WriteLine($"\nAll rendered files are in: {outputDir}");

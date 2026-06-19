// ============================================================================
// Example: Photorealistic board rendering from the console
// ============================================================================
//
// Loads a whole PCB document (.PcbDoc) and renders it as a photorealistic 2D
// board — a fab-house / gerber-viewer look (think JLCPCB) — using the
// RenderRealisticAsync overloads on the raster and SVG renderers.
//
//   dotnet run --project examples/RenderBoard                 (bundled sample)
//   dotnet run --project examples/RenderBoard -- MyBoard.PcbDoc
//
// It writes a few variations (top/bottom, different finishes) as PNG and SVG so
// you can compare. For an interactive viewer, see the BoardViewer example.
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Raster;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Eda.Rendering;

var boardPath = args.FirstOrDefault(a => a.EndsWith(".PcbDoc", StringComparison.OrdinalIgnoreCase))
                ?? LocateBundledBoard();

if (boardPath is null)
{
    Console.WriteLine("No .PcbDoc found. Pass one as an argument:");
    Console.WriteLine("  dotnet run --project examples/RenderBoard -- MyBoard.PcbDoc");
    return;
}

Console.WriteLine($"Loading {Path.GetFileName(boardPath)} ...");
var document = (PcbDocument)await AltiumLibrary.OpenPcbDocAsync(boardPath);

var outDir = Path.Combine(Path.GetTempPath(), "AltiumRenderBoard");
Directory.CreateDirectory(outDir);

var raster = new RasterRenderer();
var svg = new SvgRenderer();
var options = new RenderOptions { Width = 1400, Height = 1100 };
var name = Path.GetFileNameWithoutExtension(boardPath);

// A few looks to compare. Each preset returns a fresh, mutable style; .For(side) clones it for a side.
var jobs = new (string Suffix, PcbRealisticStyle Style)[]
{
    ("green_enig_top",    PcbRealisticStyle.GreenEnig.For(PcbViewSide.Top)),
    ("green_enig_bottom", PcbRealisticStyle.GreenEnig.For(PcbViewSide.Bottom)),
    ("black_enig_top",    PcbRealisticStyle.BlackEnig.For(PcbViewSide.Top)),
    ("blue_hasl_top",     PcbRealisticStyle.BlueHasl.For(PcbViewSide.Top)),
};

foreach (var (suffix, style) in jobs)
{
    // Supersample the raster output a little for smoother silk/copper edges.
    style.Supersample = 2;

    var png = Path.Combine(outDir, $"{name}_{suffix}.png");
    await raster.RenderRealisticAsync(document, png, options, style);
    Console.WriteLine($"  PNG  {Path.GetFileName(png)}");
}

// SVG keeps the board as named layer groups (substrate/copper/soldermask/silkscreen/drills) that a
// viewer can toggle — see the BoardViewer example. Vector output ignores supersampling.
var svgPath = Path.Combine(outDir, $"{name}_green_enig_top.svg");
await svg.RenderRealisticAsync(document, svgPath, options, PcbRealisticStyle.GreenEnig);
Console.WriteLine($"  SVG  {Path.GetFileName(svgPath)}");

Console.WriteLine($"\nDone. Files are in: {outDir}");

// Finds a sample board bundled in the repo's TestData directory.
static string? LocateBundledBoard()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var testData = Path.Combine(dir.FullName, "TestData");
            if (!Directory.Exists(testData)) continue;
            var board = Directory.EnumerateFiles(testData, "*.PcbDoc", SearchOption.TopDirectoryOnly)
                .OrderBy(f => new FileInfo(f).Length) // smallest first — fastest to render
                .FirstOrDefault();
            if (board is not null) return board;
        }
    return null;
}

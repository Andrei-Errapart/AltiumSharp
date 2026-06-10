// ============================================================================
// Example: Building a component catalog (thumbnail gallery)
// ============================================================================
//
// Renders every component in a .PcbLib or .SchLib to a PNG and an SVG, then writes
// an index.html gallery linking them all. This is the basis of a web-based library
// browser or a printable parts sheet.
//
// HOW IT WORKS
// ────────────
// AltiumLibrary.OpenAsync returns an ILibrary whose AllComponents are IComponent —
// the same interface both renderers accept — so the loop is identical for PCB and
// schematic libraries. Each component is rendered with both renderers via the
// file-path RenderAsync overload.
//
//   RasterRenderer -> PNG (SkiaSharp)      OriginalCircuit.Altium.Rendering.Raster
//   SvgRenderer    -> SVG (no native deps) OriginalCircuit.Altium.Rendering.Svg
//   RenderOptions  -> Width/Height/etc.    OriginalCircuit.Altium.Rendering
//
// RUNNING
// ───────
//   dotnet run --project examples/LibraryCatalog                  (bundled library)
//   dotnet run --project examples/LibraryCatalog -- "C:\Parts.PcbLib"  (your own)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Rendering.Raster;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Eda.Rendering;   // RenderOptions

var input = ResolveInput(args);
if (input is null)
{
    Console.WriteLine("No library supplied and no bundled TestData library was found.");
    Console.WriteLine("Usage: dotnet run --project examples/LibraryCatalog -- <file.PcbLib|.SchLib>");
    return;
}

Console.WriteLine($"Cataloguing: {Path.GetFileName(input)}\n");

await using var lib = await AltiumLibrary.OpenAsync(input);

var outDir = Path.Combine(Path.GetTempPath(), "AltiumCatalogExample",
    SanitizeFileName(Path.GetFileNameWithoutExtension(input)));
Directory.CreateDirectory(outDir);

var raster = new RasterRenderer();
var svg = new SvgRenderer();
var options = new RenderOptions { Width = 400, Height = 300 };

var cards = new List<Card>();
var index = 0;
foreach (var component in lib.AllComponents)
{
    var safe = SanitizeFileName(string.IsNullOrWhiteSpace(component.Name) ? $"component{++index}" : component.Name);
    var png = safe + ".png";
    var svgFile = safe + ".svg";

    await raster.RenderAsync(component, Path.Combine(outDir, png), options);
    await svg.RenderAsync(component, Path.Combine(outDir, svgFile), options);

    var dim = $"{component.Bounds.Width.ToMm():F2} × {component.Bounds.Height.ToMm():F2} mm";
    cards.Add(new Card(component.Name, png, svgFile, dim));
    Console.WriteLine($"  rendered {component.Name}");
}

var indexPath = Path.Combine(outDir, "index.html");
WriteGallery(indexPath, Path.GetFileName(input), cards);

Console.WriteLine($"\n{cards.Count} component(s) rendered.");
Console.WriteLine($"Gallery: {indexPath}");

// ── Helpers ─────────────────────────────────────────────────────────────────

static void WriteGallery(string path, string title, List<Card> cards)
{
    using var w = new StreamWriter(path);
    w.WriteLine($"<!doctype html><meta charset=\"utf-8\"><title>Catalog — {Html(title)}</title>");
    w.WriteLine("<style>body{font-family:sans-serif;margin:24px}h1{font-size:20px}" +
                ".grid{display:flex;flex-wrap:wrap;gap:16px}" +
                ".card{border:1px solid #ddd;border-radius:8px;padding:8px;width:240px}" +
                ".card img{width:100%;height:auto;background:#fff;border:1px solid #eee}" +
                ".card .name{font-weight:600;margin-top:6px}.card .dim{color:#666;font-size:12px}" +
                ".card a{font-size:12px}</style>");
    w.WriteLine($"<h1>{Html(title)} — {cards.Count} components</h1><div class=\"grid\">");
    foreach (var c in cards)
        w.WriteLine($"<div class=\"card\"><img src=\"{Url(c.Png)}\" alt=\"{Html(c.Name)}\">" +
                    $"<div class=\"name\">{Html(c.Name)}</div><div class=\"dim\">{Html(c.Dim)}</div>" +
                    $"<a href=\"{Url(c.Svg)}\">SVG</a></div>");
    w.WriteLine("</div>");
}

static string Html(string? s) => (s ?? "")
    .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

static string Url(string s) => Uri.EscapeDataString(s);

static string SanitizeFileName(string name)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        name = name.Replace(c, '_');
    return name;
}

static string? ResolveInput(string[] args)
{
    if (args.Length > 0)
    {
        if (File.Exists(args[0])) return args[0];
        Console.Error.WriteLine($"File not found: {args[0]}");
        return null;
    }
    var testData = FindRepoTestDataDir();
    if (testData is null) return null;
    return LocateSample(testData, ".SchLib", "AD8367", "ADL5801", "PE42442")
        ?? LocateSample(testData, ".PcbLib", "QFN", "LFCSP", "BGA");
}

static string? FindRepoTestDataDir()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TestData");
            if (Directory.Exists(candidate)) return candidate;
        }
    return null;
}

static string? LocateSample(string dir, string extension, params string[] preferred)
{
    var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
        .Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase))
        .ToList();
    foreach (var hint in preferred)
    {
        var hit = files.FirstOrDefault(f =>
            Path.GetFileName(f).Contains(hint, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
    }
    return files.FirstOrDefault();
}

// ── Types ───────────────────────────────────────────────────────────────────

record Card(string Name, string Png, string Svg, string Dim);

// ============================================================================
// Example: Serving component previews from a web app
// ============================================================================
//
// Renders Altium components straight to the HTTP response stream — the pattern you
// use to put live previews in a web UI without writing temp files. Both renderers
// accept any Stream, so the response body is the render target.
//
// TWO MODES
// ─────────
//   (default)   Renders one component to an in-memory stream and saves it, then
//               exits. This is the exact code an endpoint runs, runnable offline
//               and in CI without standing up a server.
//   serve       Starts a minimal HTTP server exposing:
//                 GET /preview?lib=<path>&component=<name>&format=png|svg
//
// RUNNING
// ───────
//   dotnet run --project examples/WebPreviewService                 (render + exit)
//   dotnet run --project examples/WebPreviewService -- serve        (HTTP server)
//
// Then, while serving:
//   http://localhost:5000/preview?lib=C:\Parts.SchLib&component=RES&format=svg
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Rendering.Raster;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Eda.Rendering;

if (args.Contains("serve", StringComparer.OrdinalIgnoreCase))
{
    RunServer(args);
    return;
}

await SelfTest();

// ── Default mode: render to a stream and exit (CI-safe, no server) ───────────
static async Task SelfTest()
{
    var libPath = LocateBundledLibrary();
    if (libPath is null)
    {
        Console.WriteLine("No bundled library found. Start the server and point it at your own file:");
        Console.WriteLine("  dotnet run --project examples/WebPreviewService -- serve");
        return;
    }

    await using var library = await AltiumLibrary.OpenAsync(libPath);
    var component = library.AllComponents.FirstOrDefault();
    if (component is null) { Console.WriteLine("Library has no components."); return; }

    // Exactly what the HTTP handler does: render straight to a stream.
    using var ms = new MemoryStream();
    await new RasterRenderer().RenderAsync(component, ms, new RenderOptions { Width = 400, Height = 300 });

    var outDir = Path.Combine(Path.GetTempPath(), "AltiumWebPreviewExample");
    Directory.CreateDirectory(outDir);
    var outPath = Path.Combine(outDir, SanitizeFileName(component.Name) + ".png");
    await File.WriteAllBytesAsync(outPath, ms.ToArray());

    Console.WriteLine($"Rendered '{component.Name}' from {Path.GetFileName(libPath)} " +
                      $"to {ms.Length:N0} bytes (saved {outPath}).");
    Console.WriteLine("Run with 'serve' to start the preview HTTP server:");
    Console.WriteLine("  dotnet run --project examples/WebPreviewService -- serve");
}

// ── Serve mode: a minimal preview API ────────────────────────────────────────
static void RunServer(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
    var app = builder.Build();

    var raster = new RasterRenderer();
    var svg = new SvgRenderer();

    app.MapGet("/", () => Results.Content(
        "<h1>Altium preview service</h1>" +
        "<p>GET <code>/preview?lib=PATH&amp;component=NAME&amp;format=png|svg</code></p>",
        "text/html"));

    app.MapGet("/preview", async (string lib, string component, string? format, HttpContext ctx) =>
    {
        if (!File.Exists(lib))
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync($"library not found: {lib}");
            return;
        }

        await using var library = await AltiumLibrary.OpenAsync(lib);
        var comp = library.AllComponents.FirstOrDefault(c =>
            string.Equals(c.Name, component, StringComparison.OrdinalIgnoreCase));
        if (comp is null)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync($"component '{component}' not found in {Path.GetFileName(lib)}");
            return;
        }

        // The renderers write synchronously; Kestrel disallows sync IO on the response
        // body, so render into a buffer first and stream that out asynchronously.
        var options = new RenderOptions { Width = 600, Height = 450 };
        using var buffer = new MemoryStream();
        if (string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase))
        {
            await svg.RenderAsync(comp, buffer, options);
            ctx.Response.ContentType = "image/svg+xml";
        }
        else
        {
            await raster.RenderAsync(comp, buffer, options);
            ctx.Response.ContentType = "image/png";
        }
        buffer.Position = 0;
        await buffer.CopyToAsync(ctx.Response.Body);
    });

    Console.WriteLine("Altium preview service listening.");
    Console.WriteLine("Try: /preview?lib=<path-to-.SchLib-or-.PcbLib>&component=<name>&format=png");
    app.Run();
}

// ── Helpers ─────────────────────────────────────────────────────────────────

static string? LocateBundledLibrary()
{
    var testData = FindRepoTestDataDir();
    if (testData is null) return null;
    return LocateSample(testData, ".SchLib", "AD8367", "ADL5801")
        ?? LocateSample(testData, ".PcbLib", "QFN", "BGA");
}

static string SanitizeFileName(string name)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        name = name.Replace(c, '_');
    return name;
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

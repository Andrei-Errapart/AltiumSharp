// ============================================================================
// Example: Altium Board Viewer — an interactive web app
// ============================================================================
//
// A small single-page web app that lets you drag-and-drop an Altium PCB document
// (.PcbDoc) and view a photorealistic render of it in the browser. You can:
//   • switch between the top and bottom side,
//   • change the solder-mask / silkscreen / finish / substrate colours,
//   • toggle each physical layer on and off (done client-side using the named
//     <g id=...> groups in the SVG — no server round-trip),
//   • download the result as SVG or PNG.
//
// The server is a minimal API: it parses the uploaded board once, caches it, and
// re-renders on demand with the chosen PcbRealisticStyle. The front-end lives in
// wwwroot/index.html.
//
//   dotnet run --project examples/BoardViewer
//   → open the printed URL (e.g. http://localhost:5000) and drop in a .PcbDoc.
// ============================================================================

using System.Collections.Concurrent;
using System.Globalization;
using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

var builder = WebApplication.CreateBuilder(args);

// Boards can be a few MB; allow a generous upload size.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024 * 1024);
builder.Services.AddSingleton<BoardCache>();

var app = builder.Build();
app.UseDefaultFiles();   // serve wwwroot/index.html at "/"
app.UseStaticFiles();

var svg = new SvgRenderer();

// ── Upload: parse the board once and cache it, return an id + metadata ───────
app.MapPost("/api/upload", async (IFormFile file, BoardCache cache, CancellationToken ct) =>
{
    if (file.Length == 0) return Results.BadRequest(new { error = "Empty file." });

    PcbDocument document;
    try
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        document = (PcbDocument)await AltiumLibrary.OpenPcbDocAsync(buffer, ct);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Could not read '{file.FileName}': {ex.Message}" });
    }

    var id = cache.Add(document, Path.GetFileNameWithoutExtension(file.FileName));
    var bounds = document.GetFramingBounds();
    return Results.Json(new
    {
        id,
        name = Path.GetFileNameWithoutExtension(file.FileName),
        widthMm = Math.Round(bounds.Width.ToMm(), 1),
        heightMm = Math.Round(bounds.Height.ToMm(), 1),
        components = document.Components.Count,
        pads = document.Pads.Count,
    });
}).DisableAntiforgery();

// ── Render to SVG (named layer groups, toggled client-side) ──────────────────
// The SVG carries each physical layer as a <g id=...> group, so the front-end toggles layers and
// exports PNG entirely client-side — the server only needs to (re)render the SVG when colours change.
app.MapPost("/api/render.svg", async (RenderRequest req, BoardCache cache, CancellationToken ct) =>
{
    if (cache.Get(req.Id) is not { } doc) return Results.NotFound(new { error = "Board not found — re-upload it." });
    using var ms = new MemoryStream();
    await svg.RenderRealisticAsync(doc, ms, BuildOptions(req), BuildStyle(req), ct);
    return Results.Text(System.Text.Encoding.UTF8.GetString(ms.ToArray()), "image/svg+xml");
});

app.Run();

// ── Style / option mapping ───────────────────────────────────────────────────

static RenderOptions BuildOptions(RenderRequest r) => new()
{
    Width = Math.Clamp(r.Width ?? 1400, 200, 4000),
    Height = Math.Clamp(r.Height ?? 1100, 200, 4000),
    BackgroundColor = ParseColor(r.Background) ?? EdaColor.FromRgb(0x12, 0x16, 0x1c),
    AutoZoom = true,
};

static PcbRealisticStyle BuildStyle(RenderRequest r)
{
    var s = new PcbRealisticStyle
    {
        ViewSide = string.Equals(r.ViewSide, "bottom", StringComparison.OrdinalIgnoreCase)
            ? PcbViewSide.Bottom : PcbViewSide.Top,
        ShowSilkscreen = r.ShowSilkscreen ?? true,
        ShowSolderMask = r.ShowSolderMask ?? true,
        ShowDrillHoles = r.ShowDrillHoles ?? true,
    };
    if (ParseColor(r.Substrate) is { } sub) s.SubstrateColor = sub;
    if (ParseColor(r.Copper) is { } cu) s.CopperColor = cu;
    if (ParseColor(r.Silkscreen) is { } silk) s.SilkscreenColor = silk;
    if (ParseColor(r.Finish) is { } fin) s.FinishColor = fin;
    if (ParseColor(r.Hole) is { } hole) s.HoleColor = hole;
    if (ParseColor(r.SolderMask) is { } mask)
        s.SolderMaskColor = EdaColor.FromArgb((byte)Math.Clamp(r.SolderMaskAlpha ?? 214, 0, 255), mask.R, mask.G, mask.B);
    return s;
}

// Parses "#rrggbb" (or "rrggbb") into an opaque EdaColor; null when blank/invalid.
static EdaColor? ParseColor(string? hex)
{
    if (string.IsNullOrWhiteSpace(hex)) return null;
    var h = hex.Trim().TrimStart('#');
    if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        return null;
    return EdaColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

// The render request sent by the front-end. Colours are "#rrggbb"; nulls fall back to the style defaults.
record RenderRequest(
    string Id, string? Name, string? ViewSide,
    string? Substrate, string? Copper, string? SolderMask, int? SolderMaskAlpha,
    string? Silkscreen, string? Finish, string? Hole, string? Background,
    bool? ShowSilkscreen, bool? ShowSolderMask, bool? ShowDrillHoles,
    int? Width, int? Height);

// A tiny bounded in-memory cache of parsed boards, so colour/side tweaks re-render without re-uploading.
sealed class BoardCache
{
    private const int Capacity = 16;
    private readonly ConcurrentDictionary<string, (PcbDocument Doc, long Seq)> _items = new();
    private long _seq;

    public string Add(PcbDocument doc, string name)
    {
        var id = Guid.NewGuid().ToString("N");
        _items[id] = (doc, Interlocked.Increment(ref _seq));
        while (_items.Count > Capacity)
        {
            var oldest = _items.OrderBy(kv => kv.Value.Seq).First().Key;
            _items.TryRemove(oldest, out _);
        }
        return id;
    }

    public PcbDocument? Get(string? id) =>
        id is not null && _items.TryGetValue(id, out var v) ? v.Doc : null;
}

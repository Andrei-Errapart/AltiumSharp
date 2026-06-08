using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Models;
using OriginalCircuit.Eda.Models.Pcb;
using OriginalCircuit.Eda.Models.Sch;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;
using OriginalCircuit.Eda.Rendering.Raster;
using SkiaSharp;

namespace OriginalCircuit.Altium.Rendering.Raster;

/// <summary>
/// Renders components and whole documents to raster images (PNG) using SkiaSharp.
/// </summary>
public sealed class RasterRenderer : IRenderer, IPcbLibRenderer
{
    /// <inheritdoc />
    public ValueTask RenderAsync(
        IComponent component,
        Stream output,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(output);

        options ??= new RenderOptions();
        // Schematic symbols need more breathing room so outward pin-name text isn't clipped.
        var margin = component is SchComponent ? 0.82 : 0.9;
        RenderTo(output, options, component.Bounds, margin, (transform, context) =>
        {
            if (component is PcbComponent pcb)
            {
                new PcbComponentRenderer(transform).Render(pcb, context);
            }
            else if (component is SchComponent sch)
            {
                var renderer = new SchComponentRenderer(transform);
                renderer.SetFonts(sch.Fonts);
                // A multi-part library symbol stores every part's pins; show one part (like Altium).
                renderer.PartFilter = sch.CurrentPartId > 0 ? sch.CurrentPartId : 1;
                renderer.Render(sch, context);
            }
        });
        return ValueTask.CompletedTask;
    }

    /// <summary>Renders a whole PCB document (board) to PNG.</summary>
    public ValueTask RenderAsync(
        PcbDocument document,
        Stream output,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        options ??= new RenderOptions();
        RenderTo(output, options, document.Bounds, 0.95,
            (transform, context) => new PcbComponentRenderer(transform).Render(document, context));
        return ValueTask.CompletedTask;
    }

    /// <summary>Renders a whole schematic document (sheet) to PNG.</summary>
    public ValueTask RenderAsync(
        SchDocument document,
        Stream output,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        options ??= new RenderOptions();
        // Frame to the sheet page (like Altium's "fit sheet") rather than the content bounds: schematic
        // component bounds over-estimate text extents, which would otherwise zoom the page out.
        RenderTo(output, options, document.SheetInfo.SheetRect, 0.96, (transform, context) =>
        {
            var renderer = new SchComponentRenderer(transform);
            renderer.SetFonts(document.Fonts);
            renderer.Render(document, context);
        });
        return ValueTask.CompletedTask;
    }

    private static void RenderTo(Stream output, RenderOptions options, CoordRect bounds, double margin,
        Action<CoordTransform, IRenderContext> draw)
    {
        using var bitmap = new SKBitmap(options.Width, options.Height);
        using var canvas = new SKCanvas(bitmap);
        using var context = new SkiaRenderContext(canvas);

        context.Clear(ColorHelper.EdaColorToArgb(options.BackgroundColor));

        var transform = new CoordTransform
        {
            ScreenWidth = options.Width,
            ScreenHeight = options.Height,
            Scale = options.Scale,
        };
        if (options.AutoZoom)
            transform.AutoZoom(bounds, margin);

        draw(transform, context);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(output);
    }

    /// <inheritdoc />
    public async ValueTask RenderAsync(
        IComponent component,
        string path,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await RenderAsync(component, stream, options, cancellationToken);
    }

    /// <summary>Renders a whole PCB document (board) to a PNG file.</summary>
    public async ValueTask RenderAsync(
        PcbDocument document,
        string path,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await RenderAsync(document, stream, options, cancellationToken);
    }

    /// <summary>Renders a whole schematic document (sheet) to a PNG file.</summary>
    public async ValueTask RenderAsync(
        SchDocument document,
        string path,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await RenderAsync(document, stream, options, cancellationToken);
    }

    /// <inheritdoc />
    ValueTask IPcbLibRenderer.RenderAsync(
        IPcbComponent component,
        Stream output,
        RenderOptions? options,
        CancellationToken cancellationToken)
    {
        return RenderAsync(component, output, options, cancellationToken);
    }
}

using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Models;
using OriginalCircuit.Eda.Models.Pcb;
using OriginalCircuit.Eda.Models.Sch;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;
using OriginalCircuit.Eda.Rendering.Svg;

namespace OriginalCircuit.Altium.Rendering.Svg;

/// <summary>
/// Renders components and whole documents to SVG vector graphics.
/// </summary>
public sealed class SvgRenderer : IRenderer
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
        RenderTo(output, options, component.Bounds, margin, (transform, ctx) =>
        {
            if (component is PcbComponent pcbComponent)
            {
                new PcbComponentRenderer(transform).Render(pcbComponent, ctx);
            }
            else if (component is SchComponent schComponent)
            {
                var renderer = new SchComponentRenderer(transform);
                renderer.SetFonts(schComponent.Fonts);
                // A multi-part library symbol stores every part's pins; show one part (like Altium).
                renderer.PartFilter = schComponent.CurrentPartId > 0 ? schComponent.CurrentPartId : 1;
                renderer.Render(schComponent, ctx);
            }
        });
        return ValueTask.CompletedTask;
    }

    /// <summary>Renders a whole PCB document (board) to SVG.</summary>
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
            (transform, ctx) => new PcbComponentRenderer(transform).Render(document, ctx));
        return ValueTask.CompletedTask;
    }

    /// <summary>Renders a whole schematic document (sheet) to SVG.</summary>
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
        RenderTo(output, options, document.SheetInfo.SheetRect, 0.96, (transform, ctx) =>
        {
            var renderer = new SchComponentRenderer(transform);
            renderer.SetFonts(document.Fonts);
            renderer.Render(document, ctx);
        });
        return ValueTask.CompletedTask;
    }

    private static void RenderTo(Stream output, RenderOptions options, CoordRect bounds, double margin,
        Action<CoordTransform, IRenderContext> draw)
    {
        var ctx = new SvgRenderContext(options.Width, options.Height);
        ctx.Clear(ColorHelper.EdaColorToArgb(options.BackgroundColor));

        var transform = new CoordTransform
        {
            ScreenWidth = options.Width,
            ScreenHeight = options.Height,
            Scale = options.Scale
        };
        if (options.AutoZoom)
            transform.AutoZoom(bounds, margin);

        draw(transform, ctx);
        ctx.WriteTo(output);
    }

    /// <inheritdoc />
    public async ValueTask RenderAsync(
        IComponent component,
        string path,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await RenderAsync(component, stream, options, cancellationToken);
    }

    /// <summary>Renders a whole PCB document (board) to an SVG file.</summary>
    public async ValueTask RenderAsync(
        PcbDocument document,
        string path,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await RenderAsync(document, stream, options, cancellationToken);
    }

    /// <summary>Renders a whole schematic document (sheet) to an SVG file.</summary>
    public async ValueTask RenderAsync(
        SchDocument document,
        string path,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await RenderAsync(document, stream, options, cancellationToken);
    }
}

using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;
using PadShape = OriginalCircuit.Altium.Models.Pcb.PadShape;
using PadHoleType = OriginalCircuit.Altium.Models.Pcb.PadHoleType;

namespace OriginalCircuit.Altium.Rendering;

/// <summary>Which side of the board a PCB view represents, controlling layer visibility.</summary>
public enum PcbViewSide
{
    /// <summary>Top view: bottom-side copper/overlay/paste/solder are hidden.</summary>
    Top,
    /// <summary>Bottom view: top-side copper/overlay/paste/solder are hidden.</summary>
    Bottom,
    /// <summary>Show every layer (Altium's see-through 2D editor view).</summary>
    Both,
}

/// <summary>
/// Renders PCB component primitives to an <see cref="IRenderContext"/>.
/// Uses interface properties only — no concrete type casts.
/// </summary>
public sealed class PcbComponentRenderer
{
    private readonly CoordTransform _transform;

    /// <summary>
    /// Which board side this view represents. Defaults to <see cref="PcbViewSide.Top"/> so a board
    /// renders as a clean top view (bottom-side silk/copper/paste/solder hidden) rather than overlaying
    /// both sides' text. Set to <see cref="PcbViewSide.Both"/> to show every layer.
    /// </summary>
    public PcbViewSide ViewSide { get; set; } = PcbViewSide.Top;

    /// <summary>
    /// Initializes a new instance of <see cref="PcbComponentRenderer"/> with the specified coordinate transform.
    /// </summary>
    /// <param name="transform">The coordinate transform used to map world coordinates to screen coordinates.</param>
    public PcbComponentRenderer(CoordTransform transform)
    {
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));
    }

    // Bottom-side layers (copper/overlay/paste/solder) hidden in a Top view, and the top-side
    // equivalents hidden in a Bottom view. Multilayer, mechanical, drill and internal layers always show.
    private static bool IsBottomSideLayer(int layer) => layer is 32 or 34 or 36 or 38;
    private static bool IsTopSideLayer(int layer) => layer is 1 or 33 or 35 or 37;

    private bool IsLayerVisible(int layer) => ViewSide switch
    {
        PcbViewSide.Top => !IsBottomSideLayer(layer),
        PcbViewSide.Bottom => !IsTopSideLayer(layer),
        _ => true,
    };

    /// <summary>
    /// Renders all primitives of a PCB component to the specified context, sorted by layer draw priority.
    /// </summary>
    /// <param name="component">The PCB component to render.</param>
    /// <param name="context">The render context to draw into.</param>
    public void Render(PcbComponent component, IRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(context);

        var primitives = new List<(int layer, int priority, Action render)>();
        Collect(context, primitives,
            component.Tracks, component.Arcs, component.Fills, component.Regions,
            component.Texts, component.Pads, component.Vias, component.ComponentBodies);
        DrawSorted(primitives);
    }

    /// <summary>
    /// Renders a whole PCB document (board): its top-level primitives plus every component's
    /// primitives, all sorted by layer draw priority so the stack-up matches Altium.
    /// </summary>
    public void Render(PcbDocument document, IRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var primitives = new List<(int layer, int priority, Action render)>();
        Collect(context, primitives,
            document.Tracks, document.Arcs, document.Fills, document.Regions,
            document.Texts, document.Pads, document.Vias, document.ComponentBodies);

        foreach (var component in document.Components.Cast<PcbComponent>())
            Collect(context, primitives,
                component.Tracks, component.Arcs, component.Fills, component.Regions,
                component.Texts, component.Pads, component.Vias, component.ComponentBodies);

        DrawSorted(primitives);

        // Embedded boards (panels): draw the placement outline of each array instance. Rendering the
        // referenced board's full content would require resolving and loading the external PcbDoc.
        foreach (var board in document.EmbeddedBoards)
            RenderEmbeddedBoard(context, board);
    }

    private void RenderEmbeddedBoard(IRenderContext context, PcbEmbeddedBoard board)
    {
        if (board.IsHidden || !board.Enabled) return;
        if (board.X1Location == board.X2Location || board.Y1Location == board.Y2Location) return;

        int cols = Math.Max(1, board.ColCount);
        int rows = Math.Max(1, board.RowCount);
        var color = LayerColors.GetColor(board.Layer != 0 ? board.Layer : 57);
        var lineWidth = Math.Max(1.0, _transform.ScaleValue(Coord.FromMils(8)));

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            var dx = Coord.FromRaw(board.ColSpacing.ToRaw() * c);
            var dy = Coord.FromRaw(board.RowSpacing.ToRaw() * r);
            var (sx1, sy1) = _transform.WorldToScreen(board.X1Location + dx, board.Y1Location + dy);
            var (sx2, sy2) = _transform.WorldToScreen(board.X2Location + dx, board.Y2Location + dy);
            double x = Math.Min(sx1, sx2), y = Math.Min(sy1, sy2);
            double w = Math.Abs(sx2 - sx1), h = Math.Abs(sy2 - sy1);
            context.DrawRectangle(x, y, w, h, color, lineWidth);

            var title = !string.IsNullOrEmpty(board.ViewportTitle)
                ? board.ViewportTitle
                : System.IO.Path.GetFileNameWithoutExtension(board.DocumentPath ?? "Embedded Board");
            if (!string.IsNullOrEmpty(title))
                context.DrawText(title, x + w / 2, y + h / 2, 14, color,
                    new TextRenderOptions { HorizontalAlignment = TextHAlign.Center, VerticalAlignment = TextVAlign.Middle });
        }
    }

    private static void DrawSorted(List<(int layer, int priority, Action render)> primitives)
    {
        // Sort by draw priority (lower priority = drawn first / behind). OrderBy is stable so
        // primitives on the same layer keep their original order.
        primitives.Sort((a, b) => a.priority.CompareTo(b.priority));
        foreach (var (_, _, render) in primitives)
            render();
    }

    private void Collect(IRenderContext context, List<(int layer, int priority, Action render)> primitives,
        IEnumerable<IPcbTrack> tracks, IEnumerable<IPcbArc> arcs, IEnumerable<IPcbFill> fills,
        IEnumerable<IPcbRegion> regions, IEnumerable<IPcbText> texts, IEnumerable<IPcbPad> pads,
        IEnumerable<IPcbVia> vias, IEnumerable<IPcbComponentBody> bodies)
    {
        foreach (var track in tracks.Cast<PcbTrack>())
            if (IsLayerVisible(track.Layer))
                primitives.Add((track.Layer, LayerColors.GetDrawPriority(track.Layer), () => RenderTrack(context, track)));
        foreach (var arc in arcs.Cast<PcbArc>())
            if (IsLayerVisible(arc.Layer))
                primitives.Add((arc.Layer, LayerColors.GetDrawPriority(arc.Layer), () => RenderArc(context, arc)));
        foreach (var fill in fills.Cast<PcbFill>())
            if (IsLayerVisible(fill.Layer))
                primitives.Add((fill.Layer, LayerColors.GetDrawPriority(fill.Layer), () => RenderFill(context, fill)));
        foreach (var region in regions.Cast<PcbRegion>())
            if (IsLayerVisible(region.Layer))
                primitives.Add((region.Layer, LayerColors.GetDrawPriority(region.Layer), () => RenderRegion(context, region)));
        foreach (var text in texts.Cast<PcbText>())
            if (IsLayerVisible(text.Layer))
                primitives.Add((text.Layer, LayerColors.GetDrawPriority(text.Layer), () => RenderText(context, text)));
        foreach (var pad in pads.Cast<PcbPad>())
            if (IsLayerVisible(pad.Layer))
                primitives.Add((pad.Layer, LayerColors.GetDrawPriority(pad.Layer), () => RenderPad(context, pad)));
        foreach (var via in vias.Cast<PcbVia>())
            if (IsLayerVisible(via.Layer))
                primitives.Add((via.Layer, LayerColors.GetDrawPriority(via.Layer), () => RenderVia(context, via)));
        foreach (var body in bodies.Cast<PcbComponentBody>())
            if (IsLayerVisible(body.Layer))
                primitives.Add((body.Layer, LayerColors.GetDrawPriority(body.Layer), () => RenderComponentBody(context, body)));
    }

    // ── Track ───────────────────────────────────────────────────────

    private void RenderTrack(IRenderContext context, PcbTrack track)
    {
        var (x1, y1) = _transform.WorldToScreen(track.Start.X, track.Start.Y);
        var (x2, y2) = _transform.WorldToScreen(track.End.X, track.End.Y);
        var width = _transform.ScaleValue(track.Width);
        var color = LayerColors.GetColor(track.Layer);

        context.DrawLine(x1, y1, x2, y2, color, Math.Max(1, width));
    }

    // ── Arc ─────────────────────────────────────────────────────────

    private void RenderArc(IRenderContext context, PcbArc arc)
    {
        var (cx, cy) = _transform.WorldToScreen(arc.Center.X, arc.Center.Y);
        var r = _transform.ScaleValue(arc.Radius);
        var color = LayerColors.GetColor(arc.Layer);
        var strokeWidth = Math.Max(1, _transform.ScaleValue(arc.Width));

        var startAngle = -arc.StartAngle;
        var sweep = arc.EndAngle - arc.StartAngle;
        if (sweep <= 0) sweep += 360;

        // Full circle detection
        if (Math.Abs(sweep - 360) < 0.01)
        {
            context.DrawEllipse(cx, cy, r, r, color, strokeWidth);
        }
        else
        {
            context.DrawArc(cx, cy, r, r, startAngle, -sweep, color, strokeWidth);
        }
    }

    // ── Pad ─────────────────────────────────────────────────────────

    private void RenderPad(IRenderContext context, PcbPad pad)
    {
        var (cx, cy) = _transform.WorldToScreen(pad.Location.X, pad.Location.Y);

        context.SaveState();
        context.Translate(cx, cy);
        if (pad.Rotation != 0)
            context.Rotate(-pad.Rotation);

        // Draw pad layers from back to front (matching V1 order):
        // 1. Bottom solder mask (expanded shape)
        // 2. Top solder mask (expanded shape)
        // 3. Bottom copper layer
        // 4. Top copper layer
        var expansion = _transform.ScaleValue(pad.SolderMaskExpansion);

        // Bottom solder mask
        DrawPadShape(context, pad.ShapeBottom, pad.SizeBottom, pad.CornerRadiusPercentage,
            LayerColors.GetColor(38), expansion); // 38 = BottomSolder

        // Top solder mask
        DrawPadShape(context, pad.ShapeTop, pad.SizeTop, pad.CornerRadiusPercentage,
            LayerColors.GetColor(37), expansion); // 37 = TopSolder

        // Bottom copper
        DrawPadShape(context, pad.ShapeBottom, pad.SizeBottom, pad.CornerRadiusPercentage,
            LayerColors.GetColor(pad.Layer), 0);

        // Top copper
        DrawPadShape(context, pad.ShapeTop, pad.SizeTop, pad.CornerRadiusPercentage,
            LayerColors.GetColor(pad.Layer), 0);

        // Drill hole
        if (pad.HoleSize > Coord.Zero)
        {
            // Hole may have its own rotation relative to the pad
            if (pad.HoleRotation != 0)
            {
                context.SaveState();
                context.Rotate(-pad.HoleRotation);
            }

            var holeR = _transform.ScaleValue(pad.HoleSize) / 2.0;
            if (holeR > 0.5)
            {
                const uint holeColor = 0xFF009190; // PadHoleLayer color from V1
                if (pad.HoleType == PadHoleType.Square)
                    context.FillRectangle(-holeR, -holeR, holeR * 2, holeR * 2, holeColor);
                else if (pad.HoleType == PadHoleType.Slot)
                    context.FillRoundedRectangle(-holeR * 1.5, -holeR, holeR * 3, holeR * 2, holeR, holeColor);
                else
                    context.FillEllipse(0, 0, holeR, holeR, holeColor);
            }

            if (pad.HoleRotation != 0)
                context.RestoreState();
        }

        context.RestoreState();

        // Designator text (drawn without rotation, at screen center of pad)
        if (!string.IsNullOrEmpty(pad.Designator))
        {
            var ry = _transform.ScaleValue(pad.SizeTop.Y) / 2.0;
            var holeR = pad.HoleSize > Coord.Zero ? _transform.ScaleValue(pad.HoleSize) / 2.0 : ry;
            var fontSize = Math.Min(29, Math.Max(0, holeR * 0.5));
            if (fontSize > 7)
            {
                uint fontColor = pad.HoleSize > Coord.Zero
                    ? 0xFFE38F00  // Orange-ish for thru-hole (V1: 255,227,143)
                    : 0xFFB5B5FF; // Light blue for SMD (V1: 255,181,181)
                context.DrawText(pad.Designator, cx, cy, fontSize, fontColor,
                    new TextRenderOptions
                    {
                        HorizontalAlignment = TextHAlign.Center,
                        VerticalAlignment = TextVAlign.Middle
                    });
            }
        }
    }

    private void DrawPadShape(IRenderContext context, PadShape shape, CoordPoint size,
        int cornerRadiusPercent, uint color, double expansion)
    {
        var rx = _transform.ScaleValue(size.X) / 2.0 + expansion;
        var ry = _transform.ScaleValue(size.Y) / 2.0 + expansion;
        if (rx < 0.5) rx = 0.5;
        if (ry < 0.5) ry = 0.5;

        switch (shape)
        {
            case PadShape.Round:
                var rr = Math.Min(rx, ry);
                context.FillRoundedRectangle(-rx, -ry, rx * 2, ry * 2, rr, color);
                break;
            case PadShape.Rectangular:
                context.FillRectangle(-rx, -ry, rx * 2, ry * 2, color);
                break;
            case PadShape.Octagonal:
                RenderOctagon(context, 0, 0, rx, ry, color);
                break;
            case PadShape.RoundedRectangle:
                var minDim = Math.Min(rx, ry) * 2;
                var cornerR = minDim * cornerRadiusPercent / 100.0;
                context.FillRoundedRectangle(-rx, -ry, rx * 2, ry * 2, cornerR, color);
                break;
            default:
                context.FillEllipse(0, 0, rx, ry, color);
                break;
        }
    }

    // ── Via ─────────────────────────────────────────────────────────

    private void RenderVia(IRenderContext context, PcbVia via)
    {
        var (cx, cy) = _transform.WorldToScreen(via.Location.X, via.Location.Y);

        var outerR = _transform.ScaleValue(via.Diameter) / 2.0;
        if (outerR < 1) outerR = 1;

        // Draw split halves for from/to layers
        var fromColor = LayerColors.GetColor(via.StartLayer);
        var toColor = LayerColors.GetColor(via.EndLayer);

        if (fromColor == toColor)
        {
            context.FillEllipse(cx, cy, outerR, outerR, fromColor);
        }
        else
        {
            // V1: FromLayer = left half-pie (90° sweep 180°), ToLayer = right half-pie (-90° sweep 180°)
            context.FillPie(cx, cy, outerR, outerR, 90, 180, fromColor);
            context.FillPie(cx, cy, outerR, outerR, -90, 180, toColor);
        }

        // Drill hole
        var holeR = _transform.ScaleValue(via.HoleSize) / 2.0;
        if (holeR > 0.5)
        {
            context.FillEllipse(cx, cy, holeR, holeR, 0xFF202020);
        }
    }

    // ── Fill ────────────────────────────────────────────────────────

    private void RenderFill(IRenderContext context, PcbFill fill)
    {
        var (x1, y1) = _transform.WorldToScreen(fill.Corner1.X, fill.Corner1.Y);
        var (x2, y2) = _transform.WorldToScreen(fill.Corner2.X, fill.Corner2.Y);
        var color = LayerColors.GetColor(fill.Layer);

        var x = Math.Min(x1, x2);
        var y = Math.Min(y1, y2);
        var w = Math.Abs(x2 - x1);
        var h = Math.Abs(y2 - y1);
        if (w < 1) w = 1;
        if (h < 1) h = 1;

        if (fill.Rotation != 0)
        {
            // Rotate fill rectangle vertices
            var centerX = (x1 + x2) / 2.0;
            var centerY = (y1 + y2) / 2.0;
            var rad = -fill.Rotation * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);

            var halfW = w / 2.0;
            var halfH = h / 2.0;
            var corners = new[]
            {
                (-halfW, -halfH), (halfW, -halfH),
                (halfW, halfH), (-halfW, halfH)
            };

            var xs = new double[4];
            var ys = new double[4];
            for (int i = 0; i < 4; i++)
            {
                xs[i] = centerX + corners[i].Item1 * cos - corners[i].Item2 * sin;
                ys[i] = centerY + corners[i].Item1 * sin + corners[i].Item2 * cos;
            }
            context.FillPolygon(xs, ys, color);
        }
        else
        {
            context.FillRectangle(x, y, w, h, color);
        }
    }

    // ── Region ──────────────────────────────────────────────────────

    private void RenderRegion(IRenderContext context, PcbRegion region)
    {
        if (region.Outline.Count < 3) return;

        var color = LayerColors.GetColor(region.Layer);

        // No holes: simple polygon fill.
        if (region.Holes == null || region.Holes.Count == 0)
        {
            var (xs, ys) = MapContour(region.Outline);
            context.FillPolygon(xs, ys, color);
            return;
        }

        // Holes present: even-odd fill of outline + cutout contours so the holes show through.
        var contours = new List<(double[] X, double[] Y)>(1 + region.Holes.Count)
        {
            MapContour(region.Outline)
        };
        foreach (var hole in region.Holes)
        {
            if (hole.Count >= 3)
                contours.Add(MapContour(hole));
        }
        context.FillContours(contours, color);
    }

    private (double[] X, double[] Y) MapContour(IReadOnlyList<CoordPoint> points)
    {
        var xs = new double[points.Count];
        var ys = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
            (xs[i], ys[i]) = _transform.WorldToScreen(points[i].X, points[i].Y);
        return (xs, ys);
    }

    // ── Text ────────────────────────────────────────────────────────

    private void RenderText(IRenderContext context, PcbText text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        var (x, y) = _transform.WorldToScreen(text.Location.X, text.Location.Y);
        var color = LayerColors.GetColor(text.Layer);

        var height = _transform.ScaleValue(text.Height);
        if (height < 1) height = 1;

        // Stroke (vector) font — Altium's default. Render the real glyph strokes, not a system font.
        // Honor an explicit TrueType request even when TextKind wasn't set (e.g. builder-created text).
        if (text.TextKind == PcbTextKind.Stroke && !text.IsTrueType)
        {
            var segments = AltiumStrokeFont.Layout(
                text.Text, AltiumStrokeFont.FromStrokeFont(text.StrokeFont), out _);
            if (segments.Count == 0) return;

            var strokeWidth = _transform.ScaleValue(text.StrokeWidth);
            if (strokeWidth < 1) strokeWidth = 1;

            // Non-frame Altium text always anchors bottom-left at (X,Y) regardless of justification.
            context.SaveState();
            context.Translate(x, y);
            if (text.Rotation != 0) context.Rotate(-text.Rotation);
            if (text.IsMirrored) context.Scale(-1, 1);

            // Glyph space is Y-up; screen Y is down, so negate Y.
            foreach (var s in segments)
                context.DrawLine(s.X1 * height, -s.Y1 * height, s.X2 * height, -s.Y2 * height, color, strokeWidth);

            context.RestoreState();
            return;
        }

        // TrueType / barcode: render the named system font, scaled to the text height.
        double fontSize = height;
        var options = new TextRenderOptions
        {
            FontFamily = text.FontName ?? "Arial",
            Bold = text.FontBold,
            Italic = text.FontItalic,
            HorizontalAlignment = TextHAlign.Left,
            VerticalAlignment = TextVAlign.Baseline
        };

        bool needsTransform = text.Rotation != 0 || text.IsMirrored;
        if (needsTransform)
        {
            context.SaveState();
            context.Translate(x, y);
            if (text.Rotation != 0) context.Rotate(-text.Rotation);
            if (text.IsMirrored) context.Scale(-1, 1);
            context.DrawText(text.Text, 0, 0, fontSize, color, options);
            context.RestoreState();
        }
        else
        {
            context.DrawText(text.Text, x, y, fontSize, color, options);
        }
    }

    // ── Component Body ──────────────────────────────────────────────

    private void RenderComponentBody(IRenderContext context, PcbComponentBody body)
    {
        if (body.Outline.Count < 3) return;

        const uint bodyColor = 0x80808080; // Semi-transparent gray

        var count = body.Outline.Count;
        var xPoints = new double[count];
        var yPoints = new double[count];

        for (int i = 0; i < count; i++)
        {
            (xPoints[i], yPoints[i]) = _transform.WorldToScreen(body.Outline[i].X, body.Outline[i].Y);
        }

        context.FillPolygon(xPoints, yPoints, bodyColor);
        context.DrawPolygon(xPoints, yPoints, 0xFF808080, 1);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void RenderOctagon(IRenderContext context, double cx, double cy,
        double rx, double ry, uint color)
    {
        var cutX = rx / 3.0;
        var cutY = ry / 3.0;

        var xPoints = new double[8];
        var yPoints = new double[8];

        xPoints[0] = cx + rx - cutX; yPoints[0] = cy - ry;
        xPoints[1] = cx + rx;        yPoints[1] = cy - ry + cutY;
        xPoints[2] = cx + rx;        yPoints[2] = cy + ry - cutY;
        xPoints[3] = cx + rx - cutX; yPoints[3] = cy + ry;
        xPoints[4] = cx - rx + cutX; yPoints[4] = cy + ry;
        xPoints[5] = cx - rx;        yPoints[5] = cy + ry - cutY;
        xPoints[6] = cx - rx;        yPoints[6] = cy - ry + cutY;
        xPoints[7] = cx - rx + cutX; yPoints[7] = cy - ry;

        context.FillPolygon(xPoints, yPoints, color);
    }
}

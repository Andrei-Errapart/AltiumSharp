using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;
using PadShape = OriginalCircuit.Altium.Models.Pcb.PadShape;
using PadHoleType = OriginalCircuit.Altium.Models.Pcb.PadHoleType;

namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Renders a <see cref="PcbDocument"/> to an <see cref="IRenderContext"/> as a photorealistic 2D board
/// (a fab-house / gerber-viewer look), as opposed to the Altium-editor view produced by
/// <see cref="PcbComponentRenderer"/>. It composites by physical stack — substrate, copper, a translucent
/// inverse solder mask, surface finish on exposed copper, silkscreen, then drilled holes — so a single
/// top or bottom side reads like a real manufactured board. Colours come from a <see cref="PcbRealisticStyle"/>.
/// </summary>
/// <remarks>
/// PCB-only: there is no footprint (<c>PcbComponent</c>) or schematic path. Works against any
/// <see cref="IRenderContext"/> backend (so the same engine drives both PNG/JPEG and SVG output).
/// <para>
/// The solder mask is synthesized: Altium stores no mask polarity, so the mask sheet is the board outline
/// minus an <em>opening</em> grown from each non-tented pad/via copper shape. Mask openings whose expansion
/// is rule-driven use <see cref="PcbRealisticStyle.DefaultSolderMaskExpansion"/> (per-object manual
/// expansion is honoured exactly); resolving the actual Altium design rule is a deliberate v1 simplification.
/// </para>
/// </remarks>
public sealed class PcbRealisticRenderer
{
    private readonly CoordTransform _transform;
    private readonly PcbRealisticStyle _style;

    // Per-corner samples when tessellating rounded shapes into fill contours.
    private const int ArcSteps = 8;
    private const int CircleSteps = 40;

    /// <summary>Creates a renderer that maps world coordinates with <paramref name="transform"/> and paints with <paramref name="style"/>.</summary>
    public PcbRealisticRenderer(CoordTransform transform, PcbRealisticStyle style)
    {
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>
    /// Resolves the solder-mask opening expansion for a pad/via copper shape from its expansion mode:
    /// <c>0</c> = none (opening == copper), <c>2</c> = the object's manual expansion, anything else
    /// (the Altium default <c>1</c> = From-Rule) = <paramref name="defaultExpansion"/>.
    /// </summary>
    public static Coord EffectiveSolderMaskExpansion(int mode, Coord manual, Coord defaultExpansion) => mode switch
    {
        0 => Coord.Zero,
        2 => manual,
        _ => defaultExpansion,
    };

    /// <summary>Renders the board into <paramref name="context"/> using the configured style.</summary>
    public void Render(PcbDocument document, IRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        bool bottom = _style.ViewSide == PcbViewSide.Bottom;
        int copperLayer = bottom ? 32 : 1;
        int silkLayer = bottom ? 34 : 33;
        int sideSignalLayer = bottom ? 32 : 1;

        var substrateArgb = ColorHelper.EdaColorToArgb(_style.SubstrateColor);
        var copperArgb = ColorHelper.EdaColorToArgb(_style.CopperColor);
        var maskArgb = ColorHelper.EdaColorToArgb(_style.SolderMaskColor);
        var silkArgb = ColorHelper.EdaColorToArgb(_style.SilkscreenColor);
        var finishArgb = ColorHelper.EdaColorToArgb(_style.FinishColor);
        var holeArgb = ColorHelper.EdaColorToArgb(_style.HoleColor);

        // Bottom view: mirror about the canvas centre so the board reads as if physically flipped over.
        if (bottom) { context.SaveState(); ApplyHorizontalFlip(context); }

        var collected = Collect(document);
        var outline = MapOutline(document.GetBoardOutline());

        // 1 ── Substrate (bare laminate). Filled with non-zero winding, because a real board outline is
        //      often a non-simple polygon (tessellated arcs, slots) that an even-odd fill would cancel.
        RenderSubstrate(context, outline, substrateArgb);

        // 2 ── Copper sheet on this side (tracks/arcs/fills/regions + pad/via metal), under the mask.
        foreach (var t in collected.Tracks) if (t.Layer == copperLayer) DrawTrack(context, t, copperArgb);
        foreach (var a in collected.Arcs) if (a.Layer == copperLayer) DrawArc(context, a, copperArgb);
        foreach (var f in collected.Fills) if (f.Layer == copperLayer) DrawFill(context, f, copperArgb);
        foreach (var r in collected.Regions) if (r.Layer == copperLayer) DrawRegion(context, r, copperArgb);

        var metal = CollectMetal(collected, bottom, sideSignalLayer);
        foreach (var m in metal) context.FillPolygon(m.CopperXs, m.CopperYs, copperArgb);

        // 3 ── Solder mask: one translucent sheet over the whole board. Over copper it composites darker
        //      green, over bare laminate lighter green — the requested "mask over copper is darker" effect.
        //      Openings are knocked back below by overpainting (robust against the non-simple outline that
        //      makes an even-odd inverse fill unreliable).
        if (_style.ShowSolderMask && outline is not null)
            context.FillPolygon(outline.Value.X, outline.Value.Y, maskArgb);

        // 4 ── Mask openings + plating: each non-tented pad/via exposes bare laminate (the expansion ring)
        //      with the plated metal (finish, or bare copper) on top. Tented pads/vias stay under the mask.
        uint plateArgb = _style.ShowSurfaceFinish ? finishArgb : copperArgb;
        foreach (var m in metal)
        {
            if (!m.HasOpening) continue;
            if (_style.ShowSolderMask)
                context.FillPolygon(m.OpeningXs, m.OpeningYs, substrateArgb); // bare laminate in the opening
            context.FillPolygon(m.CopperXs, m.CopperYs, plateArgb);          // plated pad/via metal
        }

        // 5 ── Silkscreen, printed over the mask.
        if (_style.ShowSilkscreen)
        {
            foreach (var t in collected.Tracks) if (t.Layer == silkLayer) DrawTrack(context, t, silkArgb);
            foreach (var a in collected.Arcs) if (a.Layer == silkLayer) DrawArc(context, a, silkArgb);
            foreach (var f in collected.Fills) if (f.Layer == silkLayer) DrawFill(context, f, silkArgb);
            foreach (var r in collected.Regions) if (r.Layer == silkLayer) DrawRegion(context, r, silkArgb);
            foreach (var (text, owner) in collected.Texts)
                if (text.Layer == silkLayer && IsTextVisible(text, owner)) DrawText(context, text, silkArgb);
        }

        // 6 ── Drilled holes / barrels, punched through everything.
        if (_style.ShowDrillHoles)
        {
            foreach (var pad in collected.Pads) DrawPadHole(context, pad, holeArgb);
            foreach (var via in collected.Vias) DrawViaHole(context, via, holeArgb);
        }

        if (bottom) context.RestoreState();
    }

    // ── Collection ──────────────────────────────────────────────────

    private sealed class Collected
    {
        public readonly List<PcbTrack> Tracks = new();
        public readonly List<PcbArc> Arcs = new();
        public readonly List<PcbFill> Fills = new();
        public readonly List<PcbRegion> Regions = new();
        public readonly List<PcbPad> Pads = new();
        public readonly List<PcbVia> Vias = new();
        public readonly List<(PcbText Text, PcbComponent? Owner)> Texts = new();
    }

    // Flattens the document's own primitives plus every component's children. Component children are
    // already baked into world coordinates by the reader, so they collect flat alongside doc-level ones.
    private static Collected Collect(PcbDocument document)
    {
        var c = new Collected();
        var components = document.Components;

        c.Tracks.AddRange(document.Tracks.Cast<PcbTrack>());
        c.Arcs.AddRange(document.Arcs.Cast<PcbArc>());
        c.Fills.AddRange(document.Fills.Cast<PcbFill>());
        c.Regions.AddRange(document.Regions.Cast<PcbRegion>());
        c.Pads.AddRange(document.Pads.Cast<PcbPad>());
        c.Vias.AddRange(document.Vias.Cast<PcbVia>());
        foreach (var t in document.Texts.Cast<PcbText>())
        {
            var owner = t.ComponentIndex >= 0 && t.ComponentIndex < components.Count
                ? components[t.ComponentIndex] as PcbComponent
                : null;
            c.Texts.Add((t, owner));
        }

        foreach (var comp in components.Cast<PcbComponent>())
        {
            c.Tracks.AddRange(comp.Tracks.Cast<PcbTrack>());
            c.Arcs.AddRange(comp.Arcs.Cast<PcbArc>());
            c.Fills.AddRange(comp.Fills.Cast<PcbFill>());
            c.Regions.AddRange(comp.Regions.Cast<PcbRegion>());
            c.Pads.AddRange(comp.Pads.Cast<PcbPad>());
            c.Vias.AddRange(comp.Vias.Cast<PcbVia>());
            foreach (var t in comp.Texts.Cast<PcbText>())
                c.Texts.Add((t, comp));
        }
        return c;
    }

    // ── Metal features (pad/via copper + mask openings) ─────────────

    private readonly struct MetalFeature
    {
        public readonly double[] CopperXs;
        public readonly double[] CopperYs;
        public readonly double[] OpeningXs;
        public readonly double[] OpeningYs;
        public readonly bool HasOpening;

        public MetalFeature(double[] cx, double[] cy, double[] ox, double[] oy, bool hasOpening)
        {
            CopperXs = cx; CopperYs = cy; OpeningXs = ox; OpeningYs = oy; HasOpening = hasOpening;
        }
    }

    // Builds the bare-copper and (when not tented) mask-opening contour for every pad/via that has copper
    // on the rendered side. SMD pads appear only on their own layer; through-hole pads and spanning vias
    // appear on both sides.
    private List<MetalFeature> CollectMetal(Collected collected, bool bottom, int sideSignalLayer)
    {
        var features = new List<MetalFeature>(collected.Pads.Count + collected.Vias.Count);

        foreach (var pad in collected.Pads)
        {
            bool throughHole = pad.HoleSize > Coord.Zero;
            if (!throughHole && pad.Layer != sideSignalLayer) continue; // SMD on the other side

            var size = bottom ? pad.SizeBottom : pad.SizeTop;
            var shape = bottom ? pad.ShapeBottom : pad.ShapeTop;
            if (size.X <= Coord.Zero || size.Y <= Coord.Zero) continue;

            var copper = PadContour(pad.Location, size.X, size.Y, shape, pad.CornerRadiusPercentage,
                Coord.Zero, pad.Rotation);

            bool tented = bottom ? pad.IsTentingBottom : pad.IsTentingTop;
            double[] ox = Array.Empty<double>(), oy = Array.Empty<double>();
            if (!tented)
            {
                var expansion = EffectiveSolderMaskExpansion(
                    pad.SolderMaskExpansionMode, pad.SolderMaskExpansion, _style.DefaultSolderMaskExpansion);
                (ox, oy) = PadContour(pad.Location, size.X, size.Y, shape, pad.CornerRadiusPercentage,
                    expansion, pad.Rotation);
            }
            features.Add(new MetalFeature(copper.X, copper.Y, ox, oy, !tented));
        }

        foreach (var via in collected.Vias)
        {
            if (via.Diameter <= Coord.Zero) continue;
            int lo = Math.Min(via.StartLayer, via.EndLayer), hi = Math.Max(via.StartLayer, via.EndLayer);
            if (sideSignalLayer < lo || sideSignalLayer > hi) continue; // doesn't reach this side

            var (cx, cy) = _transform.WorldToScreen(via.Location.X, via.Location.Y);
            double r = _transform.ScaleValue(via.Diameter) / 2.0;
            var copper = CircleContour(cx, cy, r);

            bool tented = via.IsTented || (bottom ? via.IsTentingBottom : via.IsTentingTop);
            double[] ox = Array.Empty<double>(), oy = Array.Empty<double>();
            if (!tented)
            {
                var expansion = EffectiveSolderMaskExpansion(
                    via.SolderMaskExpansionMode, via.SolderMaskExpansion, _style.DefaultSolderMaskExpansion);
                var opening = CircleContour(cx, cy, r + _transform.ScaleValue(expansion));
                ox = opening.X; oy = opening.Y;
            }
            features.Add(new MetalFeature(copper.X, copper.Y, ox, oy, !tented));
        }

        return features;
    }

    // ── Substrate ───────────────────────────────────────────────────

    private void RenderSubstrate(IRenderContext context, (double[] X, double[] Y)? outline, uint substrateArgb)
    {
        if (outline is not null)
        {
            context.FillPolygon(outline.Value.X, outline.Value.Y, substrateArgb);
            return;
        }

        // No Board6 outline: fall back to filling the whole canvas so the board area still reads.
        context.FillRectangle(0, 0, _transform.ScreenWidth, _transform.ScreenHeight, substrateArgb);
    }

    private (double[] X, double[] Y)? MapOutline(IReadOnlyList<CoordPoint> outline)
        => outline.Count >= 3 ? MapContour(outline) : null;

    // ── Per-primitive copper/silk drawing ───────────────────────────

    private void DrawTrack(IRenderContext context, PcbTrack track, uint color)
    {
        var (x1, y1) = _transform.WorldToScreen(track.Start.X, track.Start.Y);
        var (x2, y2) = _transform.WorldToScreen(track.End.X, track.End.Y);
        var width = Math.Max(1, _transform.ScaleValue(track.Width));
        context.DrawLine(x1, y1, x2, y2, color, width);
    }

    private void DrawArc(IRenderContext context, PcbArc arc, uint color)
    {
        var (cx, cy) = _transform.WorldToScreen(arc.Center.X, arc.Center.Y);
        var r = _transform.ScaleValue(arc.Radius);
        var strokeWidth = Math.Max(1, _transform.ScaleValue(arc.Width));

        var startAngle = -arc.StartAngle;
        var sweep = arc.EndAngle - arc.StartAngle;
        if (sweep <= 0) sweep += 360;

        if (Math.Abs(sweep - 360) < 0.01)
            context.DrawEllipse(cx, cy, r, r, color, strokeWidth);
        else
            context.DrawArc(cx, cy, r, r, startAngle, -sweep, color, strokeWidth);
    }

    private void DrawFill(IRenderContext context, PcbFill fill, uint color)
    {
        var (x1, y1) = _transform.WorldToScreen(fill.Corner1.X, fill.Corner1.Y);
        var (x2, y2) = _transform.WorldToScreen(fill.Corner2.X, fill.Corner2.Y);

        var x = Math.Min(x1, x2);
        var y = Math.Min(y1, y2);
        var w = Math.Max(1, Math.Abs(x2 - x1));
        var h = Math.Max(1, Math.Abs(y2 - y1));

        if (fill.Rotation != 0)
        {
            var centerX = (x1 + x2) / 2.0;
            var centerY = (y1 + y2) / 2.0;
            var rad = -fill.Rotation * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var halfW = w / 2.0;
            var halfH = h / 2.0;
            var corners = new[] { (-halfW, -halfH), (halfW, -halfH), (halfW, halfH), (-halfW, halfH) };
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

    private void DrawRegion(IRenderContext context, PcbRegion region, uint color)
    {
        if (region.Outline.Count < 3) return;

        if (region.Holes is null || region.Holes.Count == 0)
        {
            var (xs, ys) = MapContour(region.Outline);
            context.FillPolygon(xs, ys, color);
            return;
        }

        var contours = new List<(double[] X, double[] Y)>(1 + region.Holes.Count) { MapContour(region.Outline) };
        foreach (var hole in region.Holes)
            if (hole.Count >= 3) contours.Add(MapContour(hole));
        context.FillContours(contours, color);
    }

    private void DrawText(IRenderContext context, PcbText text, uint color)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        var (x, y) = _transform.WorldToScreen(text.Location.X, text.Location.Y);
        var height = Math.Max(1, _transform.ScaleValue(text.Height));

        if (text.TextKind == PcbTextKind.Stroke && !text.IsTrueType)
        {
            var segments = AltiumStrokeFont.Layout(
                text.Text, AltiumStrokeFont.FromStrokeFont(text.StrokeFont), out _);
            if (segments.Count == 0) return;

            var strokeWidth = Math.Max(1, _transform.ScaleValue(text.StrokeWidth));
            context.SaveState();
            context.Translate(x, y);
            if (text.Rotation != 0) context.Rotate(-text.Rotation);
            if (text.IsMirrored) context.Scale(-1, 1);
            foreach (var s in segments)
                context.DrawLine(s.X1 * height, -s.Y1 * height, s.X2 * height, -s.Y2 * height, color, strokeWidth);
            context.RestoreState();
            return;
        }

        var options = new TextRenderOptions
        {
            FontFamily = text.FontName ?? "Arial",
            Bold = text.FontBold,
            Italic = text.FontItalic,
            HorizontalAlignment = TextHAlign.Left,
            VerticalAlignment = TextVAlign.Baseline,
        };

        if (text.Rotation != 0 || text.IsMirrored)
        {
            context.SaveState();
            context.Translate(x, y);
            if (text.Rotation != 0) context.Rotate(-text.Rotation);
            if (text.IsMirrored) context.Scale(-1, 1);
            context.DrawText(text.Text, 0, 0, height, color, options);
            context.RestoreState();
        }
        else
        {
            context.DrawText(text.Text, x, y, height, color, options);
        }
    }

    // A component's designator/comment text shows only when the component enables that field
    // (Altium's NameOn/CommentOn). Free text and non-designator/comment text always show.
    private static bool IsTextVisible(PcbText text, PcbComponent? owner)
    {
        if (owner is null) return true;
        if (text.IsComment && !owner.CommentOn) return false;
        if (text.IsDesignator && !owner.NameOn) return false;
        return true;
    }

    // ── Holes ───────────────────────────────────────────────────────

    private void DrawPadHole(IRenderContext context, PcbPad pad, uint holeColor)
    {
        if (pad.HoleSize <= Coord.Zero) return;

        var (cx, cy) = _transform.WorldToScreen(pad.Location.X, pad.Location.Y);
        var holeR = _transform.ScaleValue(pad.HoleSize) / 2.0;
        if (holeR <= 0.5) return;

        context.SaveState();
        context.Translate(cx, cy);
        if (pad.HoleRotation != 0) context.Rotate(-pad.HoleRotation);

        if (pad.HoleType == PadHoleType.Square)
            context.FillRectangle(-holeR, -holeR, holeR * 2, holeR * 2, holeColor);
        else if (pad.HoleType == PadHoleType.Slot)
        {
            var half = _transform.ScaleValue(pad.HoleSlotLength > 0 ? Coord.FromRaw(pad.HoleSlotLength) : pad.HoleSize) / 2.0;
            if (half < holeR) half = holeR;
            context.FillRoundedRectangle(-half, -holeR, half * 2, holeR * 2, holeR, holeColor);
        }
        else
            context.FillEllipse(0, 0, holeR, holeR, holeColor);

        context.RestoreState();
    }

    private void DrawViaHole(IRenderContext context, PcbVia via, uint holeColor)
    {
        if (via.HoleSize <= Coord.Zero) return;
        var (cx, cy) = _transform.WorldToScreen(via.Location.X, via.Location.Y);
        var holeR = _transform.ScaleValue(via.HoleSize) / 2.0;
        if (holeR > 0.5) context.FillEllipse(cx, cy, holeR, holeR, holeColor);
    }

    // ── Geometry helpers ────────────────────────────────────────────

    private void ApplyHorizontalFlip(IRenderContext context)
    {
        double cx = _transform.ScreenWidth / 2.0;
        context.Translate(cx, 0);
        context.Scale(-1, 1);
        context.Translate(-cx, 0);
    }

    private (double[] X, double[] Y) MapContour(IReadOnlyList<CoordPoint> points)
    {
        var xs = new double[points.Count];
        var ys = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
            (xs[i], ys[i]) = _transform.WorldToScreen(points[i].X, points[i].Y);
        return (xs, ys);
    }

    // Builds a pad's outline as screen-space contour points, matching how PcbComponentRenderer draws
    // pad shapes (rotation applied as -rotation about the pad centre; Round = obround/circle).
    private (double[] X, double[] Y) PadContour(CoordPoint center, Coord sizeX, Coord sizeY,
        PadShape shape, int cornerRadiusPercent, Coord expansion, double rotationDeg)
    {
        var (cx, cy) = _transform.WorldToScreen(center.X, center.Y);
        var exp = _transform.ScaleValue(expansion);
        double rx = _transform.ScaleValue(sizeX) / 2.0 + exp;
        double ry = _transform.ScaleValue(sizeY) / 2.0 + exp;
        if (rx < 0.5) rx = 0.5;
        if (ry < 0.5) ry = 0.5;

        var local = shape switch
        {
            PadShape.Rectangular => RectLocal(rx, ry),
            PadShape.Octagonal => OctagonLocal(rx, ry),
            PadShape.RoundedRectangle => RoundedRectLocal(rx, ry, Math.Min(rx, ry) * 2.0 * cornerRadiusPercent / 100.0),
            _ => RoundedRectLocal(rx, ry, Math.Min(rx, ry)), // Round → obround/circle
        };
        return Place(local, cx, cy, rotationDeg);
    }

    private (double[] X, double[] Y) CircleContour(double cx, double cy, double r)
    {
        if (r < 0.5) r = 0.5;
        var xs = new double[CircleSteps];
        var ys = new double[CircleSteps];
        for (int i = 0; i < CircleSteps; i++)
        {
            double a = 2.0 * Math.PI * i / CircleSteps;
            xs[i] = cx + r * Math.Cos(a);
            ys[i] = cy + r * Math.Sin(a);
        }
        return (xs, ys);
    }

    private static List<(double X, double Y)> RectLocal(double rx, double ry)
        => new() { (-rx, -ry), (rx, -ry), (rx, ry), (-rx, ry) };

    private static List<(double X, double Y)> OctagonLocal(double rx, double ry)
    {
        var cutX = rx / 3.0;
        var cutY = ry / 3.0;
        return new()
        {
            (rx - cutX, -ry), (rx, -ry + cutY), (rx, ry - cutY), (rx - cutX, ry),
            (-rx + cutX, ry), (-rx, ry - cutY), (-rx, -ry + cutY), (-rx + cutX, -ry),
        };
    }

    private static List<(double X, double Y)> RoundedRectLocal(double rx, double ry, double cornerR)
    {
        cornerR = Math.Max(0, Math.Min(cornerR, Math.Min(rx, ry)));
        if (cornerR <= 0.01) return RectLocal(rx, ry);

        var pts = new List<(double X, double Y)>(4 * (ArcSteps + 1));
        // Four corner centres, swept 90° each, going continuously from -90° through 270°.
        var centers = new (double X, double Y)[]
        {
            (rx - cornerR, -(ry - cornerR)),
            (rx - cornerR, ry - cornerR),
            (-(rx - cornerR), ry - cornerR),
            (-(rx - cornerR), -(ry - cornerR)),
        };
        for (int k = 0; k < 4; k++)
        {
            double baseAngle = (-90 + 90 * k) * Math.PI / 180.0;
            for (int s = 0; s <= ArcSteps; s++)
            {
                double a = baseAngle + (Math.PI / 2.0) * s / ArcSteps;
                pts.Add((centers[k].X + cornerR * Math.Cos(a), centers[k].Y + cornerR * Math.Sin(a)));
            }
        }
        return pts;
    }

    // Rotates local (screen-space, y-down) points by -rotation about the origin and offsets to (cx,cy),
    // matching PcbComponentRenderer's Translate(cx,cy)+Rotate(-rotation) pad drawing.
    private static (double[] X, double[] Y) Place(List<(double X, double Y)> local, double cx, double cy, double rotationDeg)
    {
        double a = -rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(a), sin = Math.Sin(a);
        var xs = new double[local.Count];
        var ys = new double[local.Count];
        for (int i = 0; i < local.Count; i++)
        {
            var (lx, ly) = local[i];
            xs[i] = cx + lx * cos - ly * sin;
            ys[i] = cy + lx * sin + ly * cos;
        }
        return (xs, ys);
    }
}

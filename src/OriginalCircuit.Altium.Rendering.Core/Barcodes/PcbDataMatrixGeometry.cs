using System;
using System.Collections.Generic;
using OriginalCircuit.Altium.Barcodes;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Turns a PCB <see cref="PcbText"/> that is a Data Matrix barcode into world-space geometry the renderers
/// can draw. Altium stores only the barcode's source text — never the module pattern — so the symbol is
/// (re-)encoded with <see cref="DataMatrixEncoder"/> on demand and laid out at the text's location/size.
/// </summary>
/// <remarks>
/// Sizing follows Altium's barcode "Size Mode": the square symbol fits inside a box (<see cref="Layout.Box"/>)
/// whose side comes from the text-box width/height fields (where Altium stores a 2-D barcode's full size —
/// e.g. 7.5&#160;mm on the Coherent Digitiser), inset on all sides by the X/Y margin (the quiet zone). The box's
/// bottom-left corner is anchored at <see cref="PcbText.Location"/> and the box extends right (+X) / up (+Y)
/// before the text rotation and mirror are applied — matching the copper backing fill that shares that origin.
///
/// The renderable region depends on <see cref="PcbText.BarCodeInverted"/>:
/// <list type="bullet">
/// <item>Not inverted: the foreground is the dark data modules (dark-on-light).</item>
/// <item>Inverted: the foreground is the whole box <em>minus</em> the dark modules — the quiet-zone frame plus
/// the light modules — so the symbol reads light-on-dark. On the solder-mask layer that foreground is a mask
/// opening revealing the copper/finish (a gold field with the data modules left masked / green).</item>
/// </list>
/// </remarks>
internal static class PcbDataMatrixGeometry
{
    /// <summary>The laid-out geometry of a Data Matrix barcode in world coordinates.</summary>
    public sealed class Layout
    {
        /// <summary>The quads to render as the barcode's foreground (dark modules, or — when inverted — the
        /// field around the dark modules). On the solder-mask layer these are mask openings.</summary>
        public required IReadOnlyList<CoordPoint[]> Foreground { get; init; }

        /// <summary>Whether the symbol is inverted (foreground is the field, modules are the holes).</summary>
        public required bool Inverted { get; init; }
    }

    /// <summary>
    /// Builds the world-space geometry for a Data Matrix barcode text, or returns null if <paramref name="text"/>
    /// is not a renderable Data Matrix barcode.
    /// </summary>
    public static Layout? TryBuild(PcbText text)
    {
        if (text.TextKind != PcbTextKind.BarCode || text.BarCodeType != PcbBarCodeKind.DataMatrix)
            return null;

        var payload = !string.IsNullOrEmpty(text.ConvertedString) ? text.ConvertedString! : text.Text;
        if (string.IsNullOrEmpty(payload)) return null;
        if (!DataMatrixEncoder.TryEncode(payload, out var symbol) || symbol is null) return null;

        int n = symbol.Rows; // square symbol: Rows == Columns

        // Box (full barcode extent) and quiet-zone margins, in raw world units.
        double boxW = BoxExtent(text.InvertedRectWidth, text.BarCodeFullWidth, n, text.BarCodeMinWidth, text.Height);
        double boxH = BoxExtent(text.InvertedRectHeight, text.BarCodeFullHeight, n, text.BarCodeMinWidth, text.Height);
        if (boxW <= 0 || boxH <= 0) return null;
        double marginX = Math.Max(0, text.BarCodeXMargin.ToRaw());
        double marginY = Math.Max(0, text.BarCodeYMargin.ToRaw());

        // The square module field fits inside the box minus margins; centre it when the box is not square.
        double availW = boxW - 2 * marginX, availH = boxH - 2 * marginY;
        double fieldSide = Math.Min(availW, availH);
        if (fieldSide <= 0) { fieldSide = Math.Min(boxW, boxH); marginX = marginY = 0; }
        double module = fieldSide / n;
        double fieldX = marginX + (availW - fieldSide) / 2.0; // local bottom-left of the module field
        double fieldY = marginY + (availH - fieldSide) / 2.0;

        // Local frame: X right, Y up, origin at the box bottom-left (the text Location), pre-rotation.
        double ox = text.Location.X.ToRaw();
        double oy = text.Location.Y.ToRaw();
        double rad = text.Rotation * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        int mirror = text.IsMirrored ? -1 : 1;

        CoordPoint ToWorld(double lx, double ly)
        {
            double mx = mirror * lx;
            return new CoordPoint(
                Coord.FromRaw((int)Math.Round(ox + (mx * cos - ly * sin))),
                Coord.FromRaw((int)Math.Round(oy + (mx * sin + ly * cos))));
        }

        CoordPoint[] Rect(double x0, double y0, double x1, double y1)
            => new[] { ToWorld(x0, y0), ToWorld(x1, y0), ToWorld(x1, y1), ToWorld(x0, y1) };

        // Cell rectangle for module [row, col]: row 0 is the top of the symbol (world Y up -> highest Y).
        CoordPoint[] Cell(int row, int col)
        {
            double cx = fieldX + col * module;
            double cy = fieldY + (n - 1 - row) * module;
            return Rect(cx, cy, cx + module, cy + module);
        }

        bool inverted = text.BarCodeInverted;
        var foreground = new List<CoordPoint[]>();

        if (!inverted)
        {
            for (int r = 0; r < n; r++)
                for (int c = 0; c < symbol.Columns; c++)
                    if (symbol[r, c]) foreground.Add(Cell(r, c));
        }
        else
        {
            // Inverted: fill the whole box except the dark modules — the quiet-zone frame plus light modules.
            double fr = fieldX + fieldSide, ft = fieldY + fieldSide;
            foreground.Add(Rect(0, 0, fieldX, boxH));          // left margin strip
            foreground.Add(Rect(fr, 0, boxW, boxH));           // right margin strip
            foreground.Add(Rect(fieldX, 0, fr, fieldY));       // bottom margin strip
            foreground.Add(Rect(fieldX, ft, fr, boxH));        // top margin strip
            for (int r = 0; r < n; r++)
                for (int c = 0; c < symbol.Columns; c++)
                    if (!symbol[r, c]) foreground.Add(Cell(r, c));
        }

        return new Layout { Foreground = foreground, Inverted = inverted };
    }

    // Box side in raw world units: Altium stores a 2-D barcode's full size in the text-box (inverted-rect)
    // width/height; fall back to the barcode full-width field, then to N modules of the minimum width plus a
    // little quiet zone, then to the text height (sensible defaults for from-scratch barcodes).
    private static double BoxExtent(Coord textBox, Coord barcodeFull, int modules, Coord minModule, Coord height)
    {
        if (textBox > Coord.Zero) return textBox.ToRaw();
        if (barcodeFull > Coord.Zero) return barcodeFull.ToRaw();
        if (minModule > Coord.Zero) return (double)minModule.ToRaw() * (modules + 2);
        if (height > Coord.Zero) return height.ToRaw();
        return 0;
    }
}

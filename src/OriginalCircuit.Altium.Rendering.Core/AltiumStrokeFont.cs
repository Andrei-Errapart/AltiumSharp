using OriginalCircuit.Altium.Models.Pcb;

namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Lays out text using Altium's built-in stroke (vector) fonts, ported from altium_monkey's
/// <c>StrokeTextRenderer</c>. Produces line segments in a normalized coordinate space where the
/// text height is 1.0, the baseline is at y=0, and glyphs grow in +x (right) and +y (up).
/// Callers scale by the desired height and map into screen space.
/// </summary>
internal static class AltiumStrokeFont
{
    /// <summary>Altium stroke font styles (PCB TEXT <c>stroke_font_type</c>: 1=Default, 2=Sans, 3=Serif).</summary>
    internal enum Style
    {
        /// <summary>Default Altium stroke font.</summary>
        Default,
        /// <summary>Sans-serif stroke font.</summary>
        SansSerif,
        /// <summary>Serif stroke font.</summary>
        Serif,
    }

    // Width used for characters absent from the per-character width table.
    private const float DefaultStrokeWidthNorm = 0.6665f;
    // Native PCB stroke multiline text steps ~1.68x the glyph height between baselines.
    private const float MultilineSpacingFactor = 1.68f;
    // Sans-serif derives its advance from width + a fixed spacing rather than the advance table.
    private const float SansSerifCharSpacing = 0.2060f;

    /// <summary>A single stroke segment in normalized units (height = 1.0, baseline y=0, +y up).</summary>
    internal readonly record struct Segment(float X1, float Y1, float X2, float Y2);

    /// <summary>Maps a <see cref="PcbStrokeFont"/> to the corresponding stroke style.</summary>
    internal static Style FromStrokeFont(PcbStrokeFont font) => font switch
    {
        PcbStrokeFont.SansSerif => Style.SansSerif,
        PcbStrokeFont.Serif => Style.Serif,
        _ => Style.Default,
    };

    /// <summary>
    /// Lays out <paramref name="text"/> into normalized stroke segments. <paramref name="advanceWidth"/>
    /// receives the widest line's total advance (normalized units), for justification/measurement.
    /// </summary>
    internal static List<Segment> Layout(string text, Style style, out float advanceWidth)
    {
        var segments = new List<Segment>();
        advanceWidth = 0f;
        if (string.IsNullOrEmpty(text)) return segments;

        var glyphs = GlyphsFor(style);
        var widths = WidthsFor(style);
        var advances = AdvancesFor(style);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int n = lines.Length;
        for (int li = 0; li < n; li++)
        {
            float yOffset = (n > 1) ? MultilineSpacingFactor * (n - 1 - li) : 0f;
            float lineWidth = LayoutLine(lines[li], style, glyphs, widths, advances, yOffset, segments);
            if (lineWidth > advanceWidth) advanceWidth = lineWidth;
        }
        return segments;
    }

    /// <summary>Total advance width (normalized units) of the widest line, without producing geometry.</summary>
    internal static float MeasureWidth(string text, Style style)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var widths = WidthsFor(style);
        var advances = AdvancesFor(style);
        float max = 0f;
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            float cursor = 0f;
            foreach (var ch in line)
                cursor += Advance(ch, style, widths, advances);
            if (cursor > max) max = cursor;
        }
        return max;
    }

    private static float LayoutLine(string line, Style style,
        IReadOnlyDictionary<int, float[][]> glyphs,
        IReadOnlyDictionary<int, float> widths,
        IReadOnlyDictionary<int, float> advances,
        float yOffset, List<Segment> segments)
    {
        float cursor = 0f;
        foreach (var ch in line)
        {
            int code = ch;
            if (glyphs.TryGetValue(code, out var polylines))
            {
                foreach (var pl in polylines)
                {
                    for (int j = 0; j + 3 < pl.Length; j += 2)
                    {
                        segments.Add(new Segment(
                            pl[j] + cursor, pl[j + 1] + yOffset,
                            pl[j + 2] + cursor, pl[j + 3] + yOffset));
                    }
                }
            }
            cursor += Advance(ch, style, widths, advances);
        }
        return cursor;
    }

    private static float Advance(char ch, Style style,
        IReadOnlyDictionary<int, float> widths,
        IReadOnlyDictionary<int, float> advances)
    {
        int code = ch;
        float width = widths.TryGetValue(code, out var w) ? w : DefaultStrokeWidthNorm;
        float advance = style == Style.SansSerif
            ? width + SansSerifCharSpacing
            : (advances.TryGetValue(code, out var a) ? a : width);
        return advance > 0f ? advance : 0f;
    }

    private static IReadOnlyDictionary<int, float[][]> GlyphsFor(Style s) => s switch
    {
        Style.SansSerif => AltiumStrokeFontData.SansSerifGlyphs,
        Style.Serif => AltiumStrokeFontData.SerifGlyphs,
        _ => AltiumStrokeFontData.DefaultGlyphs,
    };

    private static IReadOnlyDictionary<int, float> WidthsFor(Style s) => s switch
    {
        Style.SansSerif => AltiumStrokeFontData.SansSerifWidths,
        Style.Serif => AltiumStrokeFontData.SerifWidths,
        _ => AltiumStrokeFontData.DefaultWidths,
    };

    private static IReadOnlyDictionary<int, float> AdvancesFor(Style s) => s switch
    {
        Style.SansSerif => AltiumStrokeFontData.SansSerifAdvances,
        Style.Serif => AltiumStrokeFontData.SerifAdvances,
        _ => AltiumStrokeFontData.DefaultAdvances,
    };
}

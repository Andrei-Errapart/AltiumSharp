using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Altium-specific extension methods for <see cref="CoordTransform"/>.
/// </summary>
public static class CoordTransformExtensions
{
    /// <summary>
    /// Maps the Altium schematic line-width index (0=Small, 1=Medium, 2=Large, 3=extra) to a
    /// screen pixel width. The widths are real world widths in mils (per altium_monkey:
    /// Small=1, Medium=3, Large=5) scaled by the current zoom, with a 1px floor so thin lines
    /// stay visible when zoomed out — matching Altium's minimum on-screen line width.
    /// </summary>
    public static double MapLineWidthEnum(this CoordTransform transform, int lineWidthEnum)
    {
        double mils = lineWidthEnum switch
        {
            0 => 1.0,
            1 => 3.0,
            2 => 5.0,
            _ => 6.0
        };
        var px = transform.ScaleValue(Coord.FromMils(mils));
        return px < 1.0 ? 1.0 : px;
    }
}

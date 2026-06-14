using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium;

/// <summary>
/// Geometry helpers for circular arcs.
/// </summary>
internal static class ArcGeometry
{
    /// <summary>
    /// Computes the tight axis-aligned bounding box of a circular arc swept counter-clockwise from
    /// <paramref name="startDeg"/> to <paramref name="endDeg"/> (Altium's convention), expanded by
    /// the stroke half-width. Only the axis extremes the arc actually passes through are included,
    /// so a partial arc no longer reports the whole circle. A full sweep yields the full circle box.
    /// </summary>
    public static CoordRect Bounds(CoordPoint center, Coord radius, double startDeg, double endDeg, Coord halfWidth)
    {
        double r = radius.ToRaw();
        double hw = halfWidth.ToRaw();
        double cx = center.X.ToRaw();
        double cy = center.Y.ToRaw();

        var sweep = Mod360(endDeg - startDeg);
        if (sweep <= 0) sweep = 360; // a coincident start/end is a full circle

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        void Include(double angleDeg)
        {
            var rad = angleDeg * System.Math.PI / 180.0;
            var px = r * System.Math.Cos(rad);
            var py = r * System.Math.Sin(rad);
            if (px < minX) minX = px;
            if (px > maxX) maxX = px;
            if (py < minY) minY = py;
            if (py > maxY) maxY = py;
        }

        // The endpoints are always on the arc; each axis extreme (0/90/180/270 deg) is included only
        // when the arc sweeps through it.
        Include(startDeg);
        Include(endDeg);
        for (var k = 0; k < 4; k++)
        {
            var cardinal = k * 90.0;
            if (Mod360(cardinal - startDeg) <= sweep + 1e-9)
                Include(cardinal);
        }

        return new CoordRect(
            new CoordPoint(Coord.FromRaw((int)System.Math.Floor(cx + minX - hw)), Coord.FromRaw((int)System.Math.Floor(cy + minY - hw))),
            new CoordPoint(Coord.FromRaw((int)System.Math.Ceiling(cx + maxX + hw)), Coord.FromRaw((int)System.Math.Ceiling(cy + maxY + hw))));
    }

    private static double Mod360(double degrees) => ((degrees % 360) + 360) % 360;
}

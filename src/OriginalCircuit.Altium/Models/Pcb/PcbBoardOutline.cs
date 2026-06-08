using System.Globalization;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Parses the board outline (physical board shape) from the Board6 parameter block.
///
/// Altium stores the outline as indexed vertices <c>KIND{i}, VX{i}, VY{i}, CX{i}, CY{i},
/// SA{i}, EA{i}, R{i}</c> forming a closed polygon, where <c>KIND=0</c> is a straight segment to
/// the next vertex and <c>KIND=1</c> is a circular arc (centre CX/CY, radius R, start/end angle
/// SA/EA). The final vertex repeats the first to close the loop.
/// </summary>
internal static class PcbBoardOutline
{
    private const double ArcStepDegrees = 4.0; // tessellation resolution for arc edges
    private const double EndpointTolMils = 0.5;

    private readonly record struct Vertex(
        double XMils, double YMils, bool IsArc,
        double CxMils, double CyMils, double RMils, double StartAngle, double EndAngle);

    /// <summary>
    /// Builds the board outline as a closed polygon of world-space points (arcs tessellated),
    /// or an empty list when the parameter block carries no outline.
    /// </summary>
    public static IReadOnlyList<CoordPoint> Parse(IReadOnlyDictionary<string, string>? boardParameters)
    {
        if (boardParameters is null) return Array.Empty<CoordPoint>();

        var vertices = new List<Vertex>();
        for (int i = 0; ; i++)
        {
            if (!boardParameters.TryGetValue($"VX{i}", out var vx) ||
                !boardParameters.TryGetValue($"VY{i}", out var vy))
                break;

            var kind = boardParameters.TryGetValue($"KIND{i}", out var k) ? k.Trim() : "0";
            if (kind == "1")
            {
                vertices.Add(new Vertex(Mils(vx), Mils(vy), true,
                    Mils(Get(boardParameters, $"CX{i}")), Mils(Get(boardParameters, $"CY{i}")),
                    Mils(Get(boardParameters, $"R{i}")),
                    Deg(Get(boardParameters, $"SA{i}")), Deg(Get(boardParameters, $"EA{i}"))));
            }
            else
            {
                vertices.Add(new Vertex(Mils(vx), Mils(vy), false, 0, 0, 0, 0, 0));
            }
        }

        // Drop the closing duplicate vertex (last == first).
        if (vertices.Count >= 2)
        {
            var first = vertices[0];
            var last = vertices[^1];
            if (Math.Abs(first.XMils - last.XMils) < 0.01 && Math.Abs(first.YMils - last.YMils) < 0.01)
                vertices.RemoveAt(vertices.Count - 1);
        }

        if (vertices.Count < 3) return Array.Empty<CoordPoint>();

        var points = new List<CoordPoint>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            if (v.IsArc)
                TessellateArc(v, vertices[(i + 1) % vertices.Count], points);
            else
                points.Add(new CoordPoint(Coord.FromMils(v.XMils), Coord.FromMils(v.YMils)));
        }
        return points;
    }

    // Appends the arc's start point plus intermediate samples (the next vertex contributes the
    // end point on its own iteration), walking the circle in the resolved direction.
    private static void TessellateArc(Vertex v, Vertex next, List<CoordPoint> points)
    {
        var (clockwise, sweep) = ResolveArc(v, next);
        double r = v.RMils > 0 ? v.RMils : Hypot(v.XMils - v.CxMils, v.YMils - v.CyMils);
        if (r <= 0 || sweep <= 0)
        {
            points.Add(new CoordPoint(Coord.FromMils(v.XMils), Coord.FromMils(v.YMils)));
            return;
        }

        double startAngle = Math.Atan2(v.YMils - v.CyMils, v.XMils - v.CxMils);
        int steps = Math.Max(1, (int)Math.Ceiling(sweep / ArcStepDegrees));
        double sweepRad = sweep * Math.PI / 180.0 * (clockwise ? -1.0 : 1.0);

        for (int s = 0; s < steps; s++) // exclude the end point (added by the next vertex)
        {
            double a = startAngle + sweepRad * (s / (double)steps);
            double x = v.CxMils + r * Math.Cos(a);
            double y = v.CyMils + r * Math.Sin(a);
            points.Add(new CoordPoint(Coord.FromMils(x), Coord.FromMils(y)));
        }
    }

    // Port of altium_monkey resolve_outline_arc_segment: returns (clockwise, sweepDegrees).
    private static (bool clockwise, double sweep) ResolveArc(Vertex start, Vertex end)
    {
        double sx = start.XMils - start.CxMils, sy = start.YMils - start.CyMils;
        double ex = end.XMils - start.CxMils, ey = end.YMils - start.CyMils;

        bool clockwise = (sx * ey - sy * ex) < 0.0;
        double startAng = Mod360(RadToDeg(Math.Atan2(sy, sx)));
        double endAng = Mod360(RadToDeg(Math.Atan2(ey, ex)));
        double sweep = clockwise ? Mod360(startAng - endAng) : Mod360(endAng - startAng);

        double r = start.RMils > 0 ? start.RMils : Hypot(sx, sy);
        if (r <= 0.0) return (clockwise, sweep);

        double saR = start.StartAngle * Math.PI / 180.0, eaR = start.EndAngle * Math.PI / 180.0;
        double saX = start.CxMils + r * Math.Cos(saR), saY = start.CyMils + r * Math.Sin(saR);
        double eaX = start.CxMils + r * Math.Cos(eaR), eaY = start.CyMils + r * Math.Sin(eaR);

        double currToSa = Hypot(start.XMils - saX, start.YMils - saY);
        double currToEa = Hypot(start.XMils - eaX, start.YMils - eaY);
        double nextToSa = Hypot(end.XMils - saX, end.YMils - saY);
        double nextToEa = Hypot(end.XMils - eaX, end.YMils - eaY);

        double span = Mod360(start.EndAngle - start.StartAngle);
        if (span == 0.0) span = 360.0;

        bool ccwMatch = currToSa <= EndpointTolMils && nextToEa <= EndpointTolMils;
        bool cwMatch = currToEa <= EndpointTolMils && nextToSa <= EndpointTolMils;

        if (ccwMatch && cwMatch)
            return ((currToEa + nextToSa) < (currToSa + nextToEa), span);
        if (ccwMatch) return (false, span);
        if (cwMatch) return (true, span);
        return (clockwise, sweep);
    }

    private static string? Get(IReadOnlyDictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) ? v : null;

    private static double Mils(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.Replace("mil", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static double Deg(string? s)
        => double.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
    private static double Mod360(double a) => ((a % 360.0) + 360.0) % 360.0;
    private static double RadToDeg(double r) => r * 180.0 / Math.PI;
}

using System.Globalization;
using System.Xml.Linq;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// A port's body runs along X for styles 0-3 and along Y for 4-6 (Top, Bottom, Top &amp; Bottom),
/// <see cref="SchPort.Width"/> being its length along whichever axis. The renderer drew every port
/// along X, so a vertical one lay on its side and covered whatever was drawn east of it (TES0760
/// has 26 such ports across its 48 sheets).
/// </summary>
public sealed class SchPortRenderingTests
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    private static async Task<(double Width, double Height)> PortBodyExtent(int style)
    {
        var document = new SchDocument();
        document.AddPrimitive(new SchPort
        {
            Name = "P",
            Style = style,
            Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(1000)),
            Width = Coord.FromMils(600),
            Height = Coord.FromMils(100),
        });
        // A second primitive far away, so the auto-zoom frames more than the port alone.
        document.AddPrimitive(new SchPort
        {
            Name = "Q",
            Style = 0,
            Location = new CoordPoint(Coord.FromMils(3000), Coord.FromMils(3000)),
            Width = Coord.FromMils(100),
            Height = Coord.FromMils(100),
        });

        using var ms = new MemoryStream();
        await new SvgRenderer().RenderAsync(document, ms, new RenderOptions { Width = 1000, Height = 1000 });
        ms.Position = 0;

        // The two port bodies are the six-point polygons; the bigger is the one under test.
        var bodies = XDocument.Load(ms).Descendants(Ns + "polygon")
            .Select(p => p.Attribute("points")!.Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(pt => pt.Split(','))
                .Select(xy => (X: double.Parse(xy[0], CultureInfo.InvariantCulture), Y: double.Parse(xy[1], CultureInfo.InvariantCulture)))
                .ToList())
            .Where(points => points.Count == 6)
            .Select(points => (Width: points.Max(p => p.X) - points.Min(p => p.X), Height: points.Max(p => p.Y) - points.Min(p => p.Y)))
            .ToList();
        return bodies.OrderByDescending(b => Math.Max(b.Width, b.Height)).First();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task AHorizontalPortIsDrawnAlongX(int style)
    {
        var (width, height) = await PortBodyExtent(style);
        Assert.True(width > 5 * height, $"style {style}: {width} x {height}");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task AVerticalPortIsDrawnAlongY(int style)
    {
        var (width, height) = await PortBodyExtent(style);
        Assert.True(height > 5 * width, $"style {style}: {width} x {height}");
    }
}

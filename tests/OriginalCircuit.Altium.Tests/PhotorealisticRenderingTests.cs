using System.Xml.Linq;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Raster;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for the photorealistic PCB renderer (<see cref="PcbRealisticRenderer"/> driven through the
/// raster and SVG backends' <c>RenderRealisticAsync</c> entry points).
/// </summary>
public sealed class PhotorealisticRenderingTests
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    // A small but representative board: 40 x 30 mm outline, an SMD pad and a through-hole pad (so we
    // exercise copper, mask openings and drill holes), a via, a copper track and a silkscreen track,
    // plus a designator text on the overlay.
    private static PcbDocument BuildBoard()
    {
        var board = new PcbDocument
        {
            BoardParameters = new Dictionary<string, string>
            {
                ["KIND0"] = "0", ["VX0"] = "0mil",      ["VY0"] = "0mil",
                ["KIND1"] = "0", ["VX1"] = "1574.8mil", ["VY1"] = "0mil",
                ["KIND2"] = "0", ["VX2"] = "1574.8mil", ["VY2"] = "1181.1mil",
                ["KIND3"] = "0", ["VX3"] = "0mil",      ["VY3"] = "1181.1mil",
                ["KIND4"] = "0", ["VX4"] = "0mil",      ["VY4"] = "0mil",
            },
        };

        // SMD pad (top), through-hole pad, and a bottom-side SMD pad.
        board.AddPad(PcbPad.Create("1").At(Coord.FromMm(8), Coord.FromMm(8))
            .Size(Coord.FromMm(2), Coord.FromMm(1.2)).Smd(1).Build());
        board.AddPad(PcbPad.Create("2").At(Coord.FromMm(14), Coord.FromMm(8))
            .Size(Coord.FromMm(2), Coord.FromMm(2)).ThroughHole(Coord.FromMm(1)).Build());
        board.AddPad(PcbPad.Create("3").At(Coord.FromMm(30), Coord.FromMm(22))
            .Size(Coord.FromMm(2), Coord.FromMm(1.2)).Smd(32).Build());

        board.AddVia(PcbVia.Create().At(Coord.FromMm(20), Coord.FromMm(15))
            .Diameter(Coord.FromMm(1.2)).HoleSize(Coord.FromMm(0.6)).Build());

        board.AddTrack(PcbTrack.Create().From(Coord.FromMm(8), Coord.FromMm(8))
            .To(Coord.FromMm(20), Coord.FromMm(15)).Width(Coord.FromMm(0.4)).Layer(1).Build());
        board.AddTrack(PcbTrack.Create().From(Coord.FromMm(4), Coord.FromMm(26))
            .To(Coord.FromMm(36), Coord.FromMm(26)).Width(Coord.FromMm(0.2)).Layer(33).Build());

        board.AddText(new PcbText
        {
            Text = "U1",
            Location = new CoordPoint(Coord.FromMm(8), Coord.FromMm(11)),
            Height = Coord.FromMm(1.5),
            Layer = 33,
        });

        return board;
    }

    // PNG IHDR carries width/height as big-endian uint32 at byte offsets 16 and 20.
    private static (int W, int H) ReadPngSize(byte[] png)
    {
        int W = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int H = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (W, H);
    }

    private static void AssertIsPng(byte[] bytes)
    {
        Assert.True(bytes.Length > 8, "PNG output should be non-empty");
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public async Task Raster_RealisticBoard_ProducesNonEmptyPng()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();
        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 400, Height = 300 });

        AssertIsPng(ms.ToArray());
    }

    [Fact]
    public async Task Raster_RealisticBoard_NullStyleUsesDefaultPreset()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();
        using var ms = new MemoryStream();
        // No style argument at all — should fall back to the default preset without throwing.
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 256, Height = 256 });

        AssertIsPng(ms.ToArray());
    }

    [Fact]
    public async Task Raster_Supersample_KeepsOutputDimensions()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();
        var style = PcbRealisticStyle.GreenEnig;
        style.Supersample = 3;

        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 320, Height = 240 }, style);

        var bytes = ms.ToArray();
        AssertIsPng(bytes);
        var (w, h) = ReadPngSize(bytes);
        Assert.Equal(320, w);
        Assert.Equal(240, h);
    }

    [Fact]
    public async Task Raster_JpegFormat_ProducesJpeg()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();
        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms,
            new RenderOptions { Width = 256, Height = 256, Format = RasterImageFormat.Jpeg, Quality = 80 });

        var bytes = ms.ToArray();
        Assert.True(bytes.Length > 3);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        Assert.Equal(0xFF, bytes[2]);
    }

    [Fact]
    public async Task Svg_RealisticBoard_ProducesTranslucentMaskAndSubstrate()
    {
        var board = BuildBoard();
        var renderer = new SvgRenderer();
        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 400, Height = 300 });
        ms.Position = 0;
        var doc = XDocument.Load(ms);

        Assert.NotNull(doc.Root);
        Assert.Equal("svg", doc.Root!.Name.LocalName);

        // The solder mask is a translucent sheet (the board-outline polygon painted at fractional
        // opacity over substrate+copper); that translucency is what makes mask-over-copper darker.
        var translucent = doc.Descendants().Any(e =>
            (e.Name == SvgNs + "polygon" || e.Name == SvgNs + "path") &&
            e.Attribute("opacity") is { } o &&
            double.Parse(o.Value, System.Globalization.CultureInfo.InvariantCulture) is > 0.0 and < 0.999);
        Assert.True(translucent, "Expected a translucent fill for the solder mask sheet");

        // The board substrate is a filled polygon (the outline) — opaque fills must be present too.
        var solidFills = doc.Descendants(SvgNs + "polygon").Count(e => e.Attribute("fill") != null);
        Assert.True(solidFills >= 1, "Expected the substrate/copper polygon fills");
    }

    [Fact]
    public async Task Svg_Silkscreen_RendersTextAndTrack()
    {
        var board = BuildBoard();
        var renderer = new SvgRenderer();
        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 400, Height = 300 });
        ms.Position = 0;
        var doc = XDocument.Load(ms);

        // Silk track on layer 33 renders as a stroked line; stroke-font text "U1" renders as line segments.
        var lines = doc.Descendants(SvgNs + "line").ToList();
        Assert.True(lines.Count >= 1, "Expected silkscreen lines (track + stroke-font glyphs)");
    }

    [Fact]
    public async Task SolderMaskLayer_Geometry_KnocksMaskBackToSubstrate()
    {
        // Geometry on the Top Solder layer (37) is a negative: it marks where mask is removed (e.g. the
        // bare-laminate clearance ring Altium draws around the board outline). It should paint in the
        // substrate colour, knocking the green mask back.
        var board = BuildBoard();
        board.AddTrack(PcbTrack.Create().From(Coord.FromMm(2), Coord.FromMm(2))
            .To(Coord.FromMm(38), Coord.FromMm(2)).Width(Coord.FromMm(0.3)).Layer(37).Build());

        var renderer = new SvgRenderer();
        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 400, Height = 300 });
        ms.Position = 0;
        var doc = XDocument.Load(ms);

        // The default substrate colour is (0xC8,0xB9,0x8C) -> rgb(200,185,140); the layer-37 track is
        // drawn as a line in that colour.
        var substrateStroke = doc.Descendants(SvgNs + "line")
            .Any(l => (string?)l.Attribute("stroke") == "rgb(200,185,140)");
        Assert.True(substrateStroke, "Expected the solder-mask-layer track painted in the substrate colour");
    }

    [Fact]
    public async Task TopAndBottom_BothRenderSuccessfully()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();

        using var top = new MemoryStream();
        await renderer.RenderRealisticAsync(board, top, new RenderOptions { Width = 256, Height = 256 },
            PcbRealisticStyle.GreenEnig.For(PcbViewSide.Top));

        using var bottom = new MemoryStream();
        await renderer.RenderRealisticAsync(board, bottom, new RenderOptions { Width = 256, Height = 256 },
            PcbRealisticStyle.GreenEnig.For(PcbViewSide.Bottom));

        AssertIsPng(top.ToArray());
        AssertIsPng(bottom.ToArray());
        // The board content is asymmetric (top vs bottom pads at different positions, mirrored view),
        // so the two renders must not be byte-identical.
        Assert.False(top.ToArray().AsSpan().SequenceEqual(bottom.ToArray()),
            "Top and bottom views should differ");
    }

    [Theory]
    [InlineData(0)]   // None  -> opening == copper (zero expansion)
    [InlineData(2)]   // Manual -> use the object's own expansion
    [InlineData(1)]   // FromRule -> fall back to the configured default
    [InlineData(99)]  // anything else behaves like FromRule
    public void EffectiveSolderMaskExpansion_ResolvesByMode(int mode)
    {
        var manual = Coord.FromMils(6);
        var def = Coord.FromMils(2);

        var result = PcbRealisticRenderer.EffectiveSolderMaskExpansion(mode, manual, def);

        var expected = mode switch
        {
            0 => Coord.Zero,
            2 => manual,
            _ => def,
        };
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Presets_AreDistinctInstances_AndDifferInColor()
    {
        var a = PcbRealisticStyle.GreenEnig;
        var b = PcbRealisticStyle.GreenEnig;
        Assert.NotSame(a, b); // each access is a fresh, mutable instance

        Assert.NotEqual(PcbRealisticStyle.GreenEnig.SolderMaskColor, PcbRealisticStyle.BlackEnig.SolderMaskColor);
        Assert.NotEqual(PcbRealisticStyle.GreenHasl.FinishColor, PcbRealisticStyle.GreenEnig.FinishColor);
    }

    [Fact]
    public void For_ReturnsCloneWithViewSide_LeavingOriginalUnchanged()
    {
        var top = PcbRealisticStyle.GreenEnig; // default is Top
        var bottom = top.For(PcbViewSide.Bottom);

        Assert.Equal(PcbViewSide.Top, top.ViewSide);
        Assert.Equal(PcbViewSide.Bottom, bottom.ViewSide);
        Assert.NotSame(top, bottom);
    }

    [Fact]
    public async Task Toggles_HideMaskAndSilk_StillRenders()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();
        var style = PcbRealisticStyle.GreenEnig;
        style.ShowSolderMask = false;
        style.ShowSilkscreen = false;
        style.ShowDrillHoles = false;

        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 256, Height = 256 }, style);

        AssertIsPng(ms.ToArray());
    }

    [Fact]
    public async Task FilePath_InfersJpegFromExtension()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();
        var path = Path.Combine(Path.GetTempPath(), $"realistic_{Guid.NewGuid():N}.jpg");
        try
        {
            await renderer.RenderRealisticAsync(board, path, new RenderOptions { Width = 256, Height = 256 });
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.True(bytes.Length > 3);
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

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
        style.CropToBoardBounds = false; // test the verbatim width/height path

        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 320, Height = 240 }, style);

        var bytes = ms.ToArray();
        AssertIsPng(bytes);
        var (w, h) = ReadPngSize(bytes);
        Assert.Equal(320, w);
        Assert.Equal(240, h);
    }

    [Fact]
    public async Task Raster_CropToBoardBounds_OutputTakesBoardAspectRatio()
    {
        // The 40 x 30 mm board (4:3) requested into a 400 x 400 square should crop to 4:3 = 400 x 300,
        // so there is no surrounding letterbox.
        var board = BuildBoard();
        var renderer = new RasterRenderer();

        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 400, Height = 400 },
            PcbRealisticStyle.GreenEnig); // CropToBoardBounds defaults to true

        var (w, h) = ReadPngSize(ms.ToArray());
        Assert.Equal(400, w);
        Assert.Equal(300, h);
    }

    [Fact]
    public async Task Raster_CropDisabled_UsesRequestedDimensions()
    {
        var board = BuildBoard();
        var renderer = new RasterRenderer();
        var style = PcbRealisticStyle.GreenEnig;
        style.CropToBoardBounds = false;

        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 400, Height = 400 }, style);

        var (w, h) = ReadPngSize(ms.ToArray());
        Assert.Equal(400, w);
        Assert.Equal(400, h);
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
    public async Task Svg_RendersNamedLayerGroups()
    {
        // The renderer composites by physical layer, each emitted as a named <g> so an SVG export can
        // toggle/style layers individually.
        var doc = await RenderRealisticSvg(BuildBoard());

        var ids = doc.Descendants(SvgNs + "g")
            .Select(g => (string?)g.Attribute("id"))
            .Where(id => id is not null)
            .ToList();
        foreach (var layer in new[] { "substrate", "copper", "soldermask", "silkscreen", "drills" })
            Assert.Contains(layer, ids);
    }

    [Fact]
    public async Task Svg_SolderMask_IsTranslucentSheetWithClippedReveal()
    {
        // The mask is a SOLID translucent sheet (a polygon), and the openings are re-vealed by re-drawing
        // the stack clipped to a non-zero (union) clip path — NOT an even-odd hole fill — so overlapping
        // openings can't cancel ("double negative") and refill the mask.
        var doc = await RenderRealisticSvg(BuildBoard());

        var maskGroup = doc.Descendants(SvgNs + "g").First(g => (string?)g.Attribute("id") == "soldermask");

        Assert.Contains(maskGroup.Descendants(SvgNs + "polygon"), p =>
            p.Attribute("opacity") is { } o &&
            double.Parse(o.Value, System.Globalization.CultureInfo.InvariantCulture) is > 0.0 and < 0.999);
        Assert.Contains(maskGroup.Descendants(), e => e.Attribute("clip-path") != null);
        Assert.NotEmpty(doc.Descendants(SvgNs + "clipPath"));
    }

    [Fact]
    public async Task Svg_ExposedCopper_RendersInFinishColour()
    {
        // The copper layer is drawn in the finish colour, so exposed copper (through mask openings) reads
        // as plating. Default ENIG finish (0xBE,0x90,0x42) -> rgb(190,144,66).
        var doc = await RenderRealisticSvg(BuildBoard());

        var copperGroup = doc.Descendants(SvgNs + "g").First(g => (string?)g.Attribute("id") == "copper");
        Assert.Contains(copperGroup.Descendants(),
            e => (string?)e.Attribute("fill") == "rgb(190,144,66)");
    }

    [Fact]
    public async Task Svg_SolderMaskLayerGeometry_AddsToOpeningClip()
    {
        // Negative geometry on the solder-mask layer (37) adds another contour to the openings clip path:
        // a board with an extra layer-37 track has more clip subpaths than one without.
        var bare = await RenderRealisticSvg(BuildBoard());

        var withOpening = BuildBoard();
        withOpening.AddTrack(PcbTrack.Create().From(Coord.FromMm(4), Coord.FromMm(26))
            .To(Coord.FromMm(36), Coord.FromMm(26)).Width(Coord.FromMm(0.3)).Layer(37).Build());
        var opened = await RenderRealisticSvg(withOpening);

        Assert.True(ClipSubpathCount(opened) > ClipSubpathCount(bare),
            "A solder-mask-layer track should add a contour to the openings clip");
    }

    [Fact]
    public async Task Svg_OverlappingPadAndSolderOpenings_DoNotRefillMask()
    {
        // Regression: a pad's mask opening overlapping a solder-mask-layer region must NOT re-fill the mask
        // in the overlap. The union-clip approach has no even-odd mask path whose overlap could cancel.
        var board = new PcbDocument
        {
            BoardParameters = new Dictionary<string, string>
            {
                ["KIND0"] = "0", ["VX0"] = "0mil",   ["VY0"] = "0mil",
                ["KIND1"] = "0", ["VX1"] = "800mil", ["VY1"] = "0mil",
                ["KIND2"] = "0", ["VX2"] = "800mil", ["VY2"] = "600mil",
                ["KIND3"] = "0", ["VX3"] = "0mil",   ["VY3"] = "600mil",
                ["KIND4"] = "0", ["VX4"] = "0mil",   ["VY4"] = "0mil",
            },
        };
        board.AddPad(PcbPad.Create("1").At(Coord.FromMm(10), Coord.FromMm(7.5))
            .Size(Coord.FromMm(6), Coord.FromMm(6)).Smd(1).Build());
        board.AddRegion(PcbRegion.Create().OnLayer(37)
            .AddPoint(Coord.FromMm(10), Coord.FromMm(3)).AddPoint(Coord.FromMm(16), Coord.FromMm(3))
            .AddPoint(Coord.FromMm(16), Coord.FromMm(12)).AddPoint(Coord.FromMm(10), Coord.FromMm(12))
            .Build());

        var doc = await RenderRealisticSvg(board);
        var maskGroup = doc.Descendants(SvgNs + "g").First(g => (string?)g.Attribute("id") == "soldermask");

        // No even-odd holed mask path (the thing that caused the double-negative).
        Assert.DoesNotContain(maskGroup.Descendants(SvgNs + "path"),
            p => (string?)p.Attribute("fill-rule") == "evenodd");
        Assert.NotEmpty(doc.Descendants(SvgNs + "clipPath"));
    }

    private static int ClipSubpathCount(XDocument doc) =>
        doc.Descendants(SvgNs + "clipPath")
            .SelectMany(cp => cp.Descendants(SvgNs + "path"))
            .Sum(p => (p.Attribute("d")?.Value ?? "").Count(ch => ch == 'M'));

    [Fact]
    public async Task Svg_InvertedText_RendersFilledBoxWithKnockoutGlyphs()
    {
        // Inverted (negative) text: a filled silk rectangle with the glyphs knocked out to the board colour.
        var board = BuildBoard();
        board.AddText(new PcbText
        {
            Text = "INV", Location = new CoordPoint(Coord.FromMm(20), Coord.FromMm(15)),
            Height = Coord.FromMm(2), Layer = 33, UseInvertedRectangle = true,
            InvertedRectWidth = Coord.FromMm(8), InvertedRectHeight = Coord.FromMm(3),
            InvertedRectJustification = PcbTextJustification.CenterCenter,
        });

        var doc = await RenderRealisticSvg(board);
        var silk = doc.Descendants(SvgNs + "g").First(g => (string?)g.Attribute("id") == "silkscreen");

        // The silk box, in the silkscreen colour rgb(242,242,242).
        Assert.Contains(silk.Descendants(SvgNs + "rect"), r => (string?)r.Attribute("fill") == "rgb(242,242,242)");
        // The glyphs knocked out to the opaque mask colour rgb(27,110,60).
        Assert.Contains(silk.Descendants(SvgNs + "text"),
            t => t.Value == "INV" && (string?)t.Attribute("fill") == "rgb(27,110,60)");
    }

    [Fact]
    public async Task Svg_TextOnCopperLayer_RendersInCopperGroup()
    {
        var board = BuildBoard();
        board.AddText(new PcbText
        {
            Text = "GND", Location = new CoordPoint(Coord.FromMm(10), Coord.FromMm(20)),
            Height = Coord.FromMm(1.5), Layer = 1, IsTrueType = true, FontName = "Arial",
        });

        var doc = await RenderRealisticSvg(board);
        var copper = doc.Descendants(SvgNs + "g").First(g => (string?)g.Attribute("id") == "copper");
        Assert.Contains(copper.Descendants(SvgNs + "text"),
            t => t.Value == "GND" && (string?)t.Attribute("fill") == "rgb(190,144,66)");
    }

    [Fact]
    public async Task Svg_MillingLayerGeometry_RendersAsCutout()
    {
        // Geometry on a mechanical layer marked RouteToolPath is milled out (painted in the page background).
        var board = BuildBoard();
        board.BoardParameters!["LAYER64MECHKIND"] = "RouteToolPath";
        board.AddRegion(PcbRegion.Create().OnLayer(64)
            .AddPoint(Coord.FromMm(18), Coord.FromMm(13)).AddPoint(Coord.FromMm(22), Coord.FromMm(13))
            .AddPoint(Coord.FromMm(22), Coord.FromMm(17)).AddPoint(Coord.FromMm(18), Coord.FromMm(17))
            .Build());

        var doc = await RenderRealisticSvg(board);
        var cutouts = doc.Descendants(SvgNs + "g").FirstOrDefault(g => (string?)g.Attribute("id") == "cutouts");
        Assert.NotNull(cutouts);
        // Default page background is white -> rgb(255,255,255).
        Assert.Contains(cutouts!.Descendants(SvgNs + "polygon"), p => (string?)p.Attribute("fill") == "rgb(255,255,255)");
    }

    [Fact]
    public void MapPcbJustification_Via_EffectiveExpansion_StillResolvesByMode()
    {
        // Sanity: manual mode uses the object's value; rule mode falls back to the supplied default.
        Assert.Equal(Coord.FromMils(6), PcbRealisticRenderer.EffectiveSolderMaskExpansion(2, Coord.FromMils(6), Coord.FromMils(2)));
        Assert.Equal(Coord.FromMils(2), PcbRealisticRenderer.EffectiveSolderMaskExpansion(1, Coord.FromMils(6), Coord.FromMils(2)));
    }

    private static async Task<XDocument> RenderRealisticSvg(PcbDocument board)
    {
        var renderer = new SvgRenderer();
        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 400, Height = 300 });
        ms.Position = 0;
        return XDocument.Load(ms);
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

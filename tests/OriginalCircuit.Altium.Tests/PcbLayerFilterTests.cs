using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Raster;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Unit tests for PCB layer-group classification and the layer-visibility settings that drive
/// the renderer's filtering and flipped (bottom) view.
/// </summary>
public sealed class PcbLayerFilterTests
{
    [Theory]
    [InlineData(1, true)]    // Top copper
    [InlineData(16, true)]   // Mid copper
    [InlineData(32, true)]   // Bottom copper
    [InlineData(33, false)]  // Top overlay
    [InlineData(40, false)]  // Internal plane
    public void IsCopper_classifiesSignalLayers(int layer, bool expected)
        => Assert.Equal(expected, PcbLayerGroups.IsCopper(layer));

    [Theory]
    [InlineData(33, true)]   // Top overlay
    [InlineData(34, true)]   // Bottom overlay
    [InlineData(1, false)]
    [InlineData(57, false)]
    public void IsOverlay_classifiesSilk(int layer, bool expected)
        => Assert.Equal(expected, PcbLayerGroups.IsOverlay(layer));

    [Theory]
    [InlineData(57, true)]   // Mechanical 1
    [InlineData(69, true)]   // Mechanical 13
    [InlineData(72, true)]   // Mechanical 16
    [InlineData(56, false)]  // Keep-out (not mechanical)
    [InlineData(33, false)]
    public void IsMechanical_classifiesMechanicalRange(int layer, bool expected)
        => Assert.Equal(expected, PcbLayerGroups.IsMechanical(layer));

    [Theory]
    [InlineData(1, true)]    // copper
    [InlineData(33, true)]   // overlay
    [InlineData(74, true)]   // multilayer
    [InlineData(40, true)]   // internal plane
    [InlineData(57, false)]  // mechanical excluded
    [InlineData(35, false)]  // paste excluded
    public void IsSignalOrSilk_excludesFabricationLayers(int layer, bool expected)
        => Assert.Equal(expected, PcbLayerGroups.IsSignalOrSilk(layer));

    [Fact]
    public void IsLayerAllowed_defaultShowsEverything()
    {
        var s = new PcbRenderSettings();
        Assert.True(s.IsLayerAllowed(1));
        Assert.True(s.IsLayerAllowed(57));  // mechanical
        Assert.True(s.IsLayerAllowed(40));  // internal plane
    }

    [Fact]
    public void IsLayerAllowed_hideMechanical_dropsOnlyMechanical()
    {
        var s = new PcbRenderSettings { ShowMechanical = false };
        Assert.False(s.IsLayerAllowed(57));
        Assert.False(s.IsLayerAllowed(72));
        Assert.True(s.IsLayerAllowed(1));   // copper still shows
        Assert.True(s.IsLayerAllowed(33));  // silk still shows
    }

    [Fact]
    public void IsLayerAllowed_hideInternalCopper_dropsMidAndPlanes()
    {
        var s = new PcbRenderSettings { ShowInternalCopper = false };
        Assert.False(s.IsLayerAllowed(16));  // mid copper
        Assert.False(s.IsLayerAllowed(40));  // internal plane
        Assert.True(s.IsLayerAllowed(1));    // top copper kept
        Assert.True(s.IsLayerAllowed(32));   // bottom copper kept
    }

    [Fact]
    public void IsLayerAllowed_explicitFilter_overridesToggles()
    {
        // Predicate keeps only the top layer; toggles say "hide mechanical" but the predicate wins.
        var s = new PcbRenderSettings { ShowMechanical = false, LayerFilter = l => l == 1 };
        Assert.True(s.IsLayerAllowed(1));
        Assert.False(s.IsLayerAllowed(33));
        Assert.False(s.IsLayerAllowed(57));
    }

    [Fact]
    public void Presets_setExpectedViewSide()
    {
        Assert.Equal(PcbViewSide.Top, PcbRenderSettings.Top.ViewSide);
        Assert.Equal(PcbViewSide.Bottom, PcbRenderSettings.Bottom.ViewSide);
    }

    [Fact]
    public async Task RenderDocument_topBottomAndFiltered_produceValidPng()
    {
        var doc = new PcbDocument();
        doc.AddTrack(PcbTrack.Create().From(Coord.FromMm(0), Coord.FromMm(0))
            .To(Coord.FromMm(10), Coord.FromMm(10)).Width(Coord.FromMm(0.2)).Layer(1).Build());   // top copper
        doc.AddTrack(PcbTrack.Create().From(Coord.FromMm(0), Coord.FromMm(10))
            .To(Coord.FromMm(10), Coord.FromMm(0)).Width(Coord.FromMm(0.2)).Layer(32).Build());   // bottom copper

        var renderer = new RasterRenderer();
        var settingsToTry = new[]
        {
            new PcbRenderSettings { ViewSide = PcbViewSide.Top },
            new PcbRenderSettings { ViewSide = PcbViewSide.Bottom }, // exercises the flip save/restore
            new PcbRenderSettings { ViewSide = PcbViewSide.Top, ShowMechanical = false, ShowInternalCopper = false },
        };

        foreach (var settings in settingsToTry)
        {
            using var ms = new MemoryStream();
            await renderer.RenderAsync(doc, ms, new RenderOptions { Width = 128, Height = 128 }, settings);
            var bytes = ms.ToArray();
            Assert.True(bytes.Length > 0, "PNG output should be non-empty");
            Assert.Equal(0x89, bytes[0]); // PNG signature byte
            Assert.Equal((byte)'P', bytes[1]);
        }
    }
}

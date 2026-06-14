using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Verifies that pad/text bounds account for rotation (so AutoZoom framing does not clip
/// rotated pads or silkscreen designators).
/// </summary>
public sealed class RotatedBoundsTests
{
    [Fact]
    public void PcbPad_Bounds_Unrotated_AreExact()
    {
        var pad = new PcbPad
        {
            Location = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)),
            SizeTop = new CoordPoint(Coord.FromMils(100), Coord.FromMils(20))
        };
        Assert.Equal(100, pad.Bounds.Width.ToMils(), 0);
        Assert.Equal(20, pad.Bounds.Height.ToMils(), 0);
    }

    [Fact]
    public void PcbPad_Bounds_Rotated45_ExpandAabb()
    {
        var pad = new PcbPad
        {
            Location = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)),
            SizeTop = new CoordPoint(Coord.FromMils(100), Coord.FromMils(20)),
            Rotation = 45
        };
        var expected = (100 + 20) / System.Math.Sqrt(2); // ~84.85 mils each side
        Assert.Equal(expected, pad.Bounds.Width.ToMils(), 0);
        Assert.Equal(expected, pad.Bounds.Height.ToMils(), 0);
    }

    [Fact]
    public void PcbText_Bounds_Rotated90_SwapExtents()
    {
        var text = new PcbText
        {
            Location = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)),
            Text = "REF",
            Height = Coord.FromMils(50),
            Rotation = 90
        };
        // At 0deg: width = 50*3*0.6 = 90 mils, height = 50 mils. At 90deg they swap.
        Assert.Equal(50, text.Bounds.Width.ToMils(), 0);
        Assert.Equal(90, text.Bounds.Height.ToMils(), 0);
    }

    [Fact]
    public void PcbArc_FullCircle_Bounds_CoverWholeCircle()
    {
        var arc = new PcbArc
        {
            Center = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)),
            Radius = Coord.FromMils(100),
            StartAngle = 0,
            EndAngle = 360,
            Width = Coord.FromMils(10)
        };
        // radius 100 + 5 half-width on each side => 210 across.
        Assert.Equal(210, arc.Bounds.Width.ToMils(), 0);
        Assert.Equal(210, arc.Bounds.Height.ToMils(), 0);
    }

    [Fact]
    public void PcbArc_QuarterArc_Bounds_AreTight()
    {
        var arc = new PcbArc
        {
            Center = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)),
            Radius = Coord.FromMils(100),
            StartAngle = 0,
            EndAngle = 90,
            Width = Coord.FromMils(10)
        };
        // The 0..90deg sweep only occupies the +x/+y quadrant: x and y in [0,100], +/-5 half-width.
        Assert.Equal(110, arc.Bounds.Width.ToMils(), 0);
        Assert.Equal(110, arc.Bounds.Height.ToMils(), 0);
    }

    [Fact]
    public void SchArc_PartialArc_IsTighterThanFullCircle()
    {
        SchArc Make(double start, double end) => new()
        {
            Center = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)),
            Radius = Coord.FromMils(100),
            StartAngle = start,
            EndAngle = end
        };
        var full = Make(0, 360).Bounds.Width.ToMils();
        var quarter = Make(0, 90).Bounds.Width.ToMils();
        Assert.True(full >= 200, $"full circle should span ~2*radius, was {full}");
        Assert.True(quarter < full, $"quarter arc ({quarter}) should be tighter than full circle ({full})");
    }
}

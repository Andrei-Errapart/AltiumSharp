using OriginalCircuit.Altium.Models.Pcb;
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
}

using OriginalCircuit.Altium.Rendering;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for Altium's backslash overline convention: a character is overlined when it is
/// immediately followed by a backslash (e.g. "L\D\A\C\" overlines every letter of LDAC).
/// </summary>
public sealed class OverlineHelperTests
{
    [Fact]
    public void Parse_EachLetterFollowedByBackslash_OverlinesWholeWord()
    {
        var segs = OverlineHelper.Parse(@"L\D\A\C\");
        Assert.Single(segs);
        Assert.Equal("LDAC", segs[0].Text);
        Assert.True(segs[0].HasOverline);
    }

    [Fact]
    public void Parse_NoBackslash_NoOverline()
    {
        var segs = OverlineHelper.Parse("SCLK");
        Assert.Single(segs);
        Assert.Equal("SCLK", segs[0].Text);
        Assert.False(segs[0].HasOverline);
    }

    [Fact]
    public void Parse_PartialOverline_SplitsIntoRuns()
    {
        // "RST" with only the R overlined: "R\ST"
        var segs = OverlineHelper.Parse(@"R\ST");
        Assert.Equal(2, segs.Count);
        Assert.Equal("R", segs[0].Text);
        Assert.True(segs[0].HasOverline);
        Assert.Equal("ST", segs[1].Text);
        Assert.False(segs[1].HasOverline);
    }

    [Fact]
    public void GetDisplayText_RemovesBackslashes()
    {
        Assert.Equal("LDAC", OverlineHelper.GetDisplayText(@"L\D\A\C\"));
        Assert.Equal("CS", OverlineHelper.GetDisplayText(@"C\S\"));
    }
}

using OriginalCircuit.Altium.Models.Sch;
using Xunit;

namespace OriginalCircuit.Altium.Tests.Connectivity;

/// <summary>Unit tests for parsing the Altium <c>Repeat(...)</c> multi-channel directive.</summary>
public class RepeatInfoTests
{
    [Theory]
    [InlineData("Repeat(CH,1,4)", "CH", 1, 4, 4)]
    [InlineData("Repeat(ADC, 1, 8)", "ADC", 1, 8, 8)]
    [InlineData("repeat(Bank,0,3)", "Bank", 0, 3, 4)]
    [InlineData("Repeat(X, 5, 5)", "X", 5, 5, 1)]
    [InlineData("Repeat(Rev,4,1)", "Rev", 1, 4, 4)] // tolerate reversed bounds
    public void Parse_Recognises_Repeat(string text, string name, int first, int last, int count)
    {
        var r = RepeatInfo.Parse(text);
        Assert.True(r.IsRepeated);
        Assert.Equal(name, r.ChannelName);
        Assert.Equal(first, r.FirstInstance);
        Assert.Equal(last, r.LastInstance);
        Assert.Equal(count, r.InstanceCount);
    }

    [Theory]
    [InlineData("U_Analogue")]
    [InlineData("U1")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Plain_Designator_Is_Not_Repeated(string? text)
    {
        var r = RepeatInfo.Parse(text);
        Assert.False(r.IsRepeated);
        Assert.Equal(1, r.InstanceCount);
    }

    [Theory]
    [InlineData("Repeat(CH,1,99999999999)")]      // overflows int
    [InlineData("Repeat(CH,١,٤)")]       // Arabic-Indic digits
    [InlineData("Repeat(CH,１,４)")]       // full-width digits
    [InlineData("Repeat(CH,-1,4)")]
    [InlineData("Repeat(CH,1,+4)")]
    [InlineData("Repeat(CH,1 000,2)")]
    [InlineData("Repeat( ,1,4)")]
    [InlineData("Repeat(CH,1,200000)")]            // more than 100,000 channels
    public void Parse_Unreadable_Repeat_Is_Not_Repeated_And_Does_Not_Throw(string text)
    {
        var r = RepeatInfo.Parse(text);
        Assert.False(r.IsRepeated);
        Assert.True(r.IsUnreadable);
        Assert.True(RepeatInfo.MentionsRepeat(text));
        Assert.Equal(text.Trim(), r.ChannelName);
        Assert.Equal(1, r.InstanceCount);
    }

    [Theory]
    [InlineData("Repeat(,1,3)")]
    [InlineData("Repeat(CH,1,3")]
    [InlineData("REPEAT (CH)")]
    public void Parse_Malformed_Repeat_Is_Unreadable(string text)
    {
        var r = RepeatInfo.Parse(text);
        Assert.False(r.IsRepeated);
        Assert.True(r.IsUnreadable);
    }

    [Theory]
    [InlineData("Repeat(CH,1,4)")]
    [InlineData("U_Analogue")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Readable_Or_Plain_Designator_Is_Not_Unreadable(string? text)
    {
        Assert.False(RepeatInfo.Parse(text).IsUnreadable);
        Assert.False(default(RepeatInfo).IsUnreadable);
        Assert.Equal(text?.Contains("Repeat", StringComparison.Ordinal) == true, RepeatInfo.MentionsRepeat(text));
    }

    [Fact]
    public void Parse_Long_Input_Stays_Fast()
    {
        var text = "Repeat(" + new string(' ', 200_000) + "x";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(RepeatInfo.Parse(text).IsRepeated);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed}");
    }
}

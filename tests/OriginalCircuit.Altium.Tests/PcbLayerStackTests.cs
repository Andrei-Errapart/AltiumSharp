using OriginalCircuit.Altium.Models.Pcb;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// The legacy layer stack (<see cref="PcbLayerStack"/>): one <c>LAYER{n}*</c> group per Altium
/// layer, with or without a <c>V7_</c> prefix, the stack being the chain from the Top Layer.
/// It read the prefixed spelling only and was null on every company board.
/// </summary>
public class PcbLayerStackTests
{
    private static void Layer(Dictionary<string, string> p, string prefix, int id, string name, int prev, int next, string material = "")
    {
        p[$"{prefix}LAYER{id}NAME"] = name;
        p[$"{prefix}LAYER{id}PREV"] = prev.ToString();
        p[$"{prefix}LAYER{id}NEXT"] = next.ToString();
        p[$"{prefix}LAYER{id}COPTHICK"] = "1.378mil";
        p[$"{prefix}LAYER{id}DIELMATERIAL"] = material;
        p[$"{prefix}LAYER{id}DIELTYPE"] = "0";
    }

    [Theory]
    [InlineData("")]
    [InlineData("V7_")]
    public void TheStackIsTheChainFromTheTopLayer(string prefix)
    {
        // As on TES0761: L01 > L02 GND (Mid-Layer 3, id 4) > L03 (id 5) > L04 (Bottom, 32), and
        // a group for every unused layer as well.
        var p = new Dictionary<string, string>();
        Layer(p, prefix, 1, "L01", 0, 4, "PP-006");
        Layer(p, prefix, 4, "L02 GND", 1, 5, "FR-4");
        Layer(p, prefix, 5, "L03", 4, 32);
        Layer(p, prefix, 32, "L04", 5, 0);
        Layer(p, prefix, 2, "Mid-Layer 1", 0, 0);       // unused
        Layer(p, prefix, 57, "Mechanical 1", 0, 0);     // not a stack layer

        var stack = PcbLayerStack.FromBoardParameters(p)!;

        Assert.Equal(new[] { (1, "L01"), (4, "L02 GND"), (5, "L03"), (32, "L04") }, stack.Layers.Select(l => (l.Index, l.Name)));
        Assert.Equal("PP-006", stack.Layers[0].DielectricMaterial);
        Assert.Equal("FR-4", stack.Layers[1].DielectricMaterial);
        Assert.All(stack.Layers, l => Assert.True(l.CopperEnabled));
    }

    [Fact]
    public void ThePrefixedSpellingWinsWhereBothArePresent()
    {
        var p = new Dictionary<string, string>();
        Layer(p, "", 1, "Plain", 0, 0);
        Layer(p, "V7_", 1, "Prefixed", 0, 0);

        Assert.Equal("Prefixed", Assert.Single(PcbLayerStack.FromBoardParameters(p)!.Layers).Name);
    }

    [Fact]
    public void WithoutATopLayerTheGroupsAreListedById()
    {
        var p = new Dictionary<string, string>();
        Layer(p, "", 32, "Bottom", 0, 0);
        Layer(p, "", 2, "Mid", 0, 0);

        Assert.Equal(new[] { 2, 32 }, PcbLayerStack.FromBoardParameters(p)!.Layers.Select(l => l.Index));
    }

    [Fact]
    public void NoGroupsNoStack()
    {
        Assert.Null(PcbLayerStack.FromBoardParameters(new Dictionary<string, string> { ["FOO"] = "BAR" }));
        Assert.Null(PcbLayerStack.FromBoardParameters(null));
    }
}

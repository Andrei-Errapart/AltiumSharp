using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using OriginalCircuit.Eda.Primitives;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// A typed rule read from a file writes back the parameters it was read with. The writer used to
/// rebuild every typed rule from its properties in canonical order, so a read-write round trip
/// dropped the keys a kind does not model (SolderMaskExpansion's <c>SOLDERMASKFROMHOLE</c>,
/// UnRoutedNet's <c>CHECKBADCONNECTIONS</c>, AssemblyTestPointUsage's <c>VALID</c>) and added
/// defaults the file never had (MatchedLengths' <c>USEDELAYUNITS</c> and <c>DELAYTOLERANCE</c>) —
/// on four of five company boards.
/// </summary>
public sealed class PcbDocRuleParameterRoundTripTests
{
    private static List<KeyValuePair<string, string>> Pairs(params (string Key, string Value)[] pairs) =>
        pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();

    private static PcbRule Stored(string kind, string name, params (string Key, string Value)[] body)
    {
        // An untyped rule replays its ordered parameters verbatim, which stands in for the block a
        // file stores; read back, it becomes the typed rule of its kind.
        var header = new[]
        {
            ("SELECTION", "FALSE"), ("LAYER", "UNKNOWN"), ("LOCKED", "FALSE"), ("POLYGONOUTLINE", "FALSE"),
            ("USERROUTED", "TRUE"), ("KEEPOUT", "FALSE"), ("UNIONINDEX", "0"), ("RULEKIND", kind),
            ("NETSCOPE", "DifferentNets"), ("LAYERKIND", "SameLayer"), ("SCOPE1EXPRESSION", "All"),
            ("SCOPE2EXPRESSION", "All"), ("NAME", name), ("ENABLED", "TRUE"), ("PRIORITY", "1"),
            ("COMMENT", ""), ("UNIQUEID", "ABCDEFGH"), ("DEFINEDBYLOGICALDOCUMENT", "FALSE"),
        };
        return new PcbRule { RuleKind = kind, Name = name, RawParametersOrdered = Pairs(header.Concat(body).ToArray()) };
    }

    private static PcbDocument RoundTrip(PcbDocument document)
    {
        using var ms = new MemoryStream();
        new PcbDocWriter().Write(document, ms);
        ms.Position = 0;
        return new PcbDocReader().Read(ms);
    }

    private static PcbDocument Board() => RoundTrip(With(
        Stored("MatchedLengths", "Lanes",
            ("TOLERANCE", "10mil"), ("CHECKNETSINDIFFPAIR", "TRUE"), ("CHECKDIFFPAIRVSDIFFPAIR", "FALSE"),
            ("CHECKXSIGNALS", "TRUE"), ("CHECKOTHERS", "FALSE")),
        Stored("SolderMaskExpansion", "SolderMaskExpansion_Via",
            ("EXPANSION", "0mil"), ("SOLDERMASKFROMHOLE", "TRUE"), ("ISTENTINGTOP", "TRUE")),
        Stored("UnRoutedNet", "UnRoutedNet", ("CHECKBADCONNECTIONS", "TRUE")),
        Stored("AssemblyTestPointUsage", "AssemblyTestPointUsage", ("VALID", "2"), ("ALLOWMULTIPLE", "FALSE"))));

    private static PcbDocument With(params PcbRule[] rules)
    {
        var document = new PcbDocument();
        foreach (var rule in rules)
            document.AddRule(rule);
        return document;
    }

    [Fact]
    public void TypedRulesWriteBackTheParametersTheyWereReadWith()
    {
        var read = Board();
        Assert.IsType<PcbMatchedLengthsRule>(read.Rules[0]);

        var again = RoundTrip(read);

        Assert.Equal(read.Rules.Count, again.Rules.Count);
        for (var i = 0; i < read.Rules.Count; i++)
            Assert.Equal(read.Rules[i].Parameters.OrderBy(p => p.Key), again.Rules[i].Parameters.OrderBy(p => p.Key));

        Assert.Equal("TRUE", again.Rules[1].Parameters["SOLDERMASKFROMHOLE"]);
        Assert.Equal("TRUE", again.Rules[2].Parameters["CHECKBADCONNECTIONS"]);
        Assert.Equal("2", again.Rules[3].Parameters["VALID"]);
        Assert.False(again.Rules[0].Parameters.ContainsKey("USEDELAYUNITS"));
        Assert.False(again.Rules[0].Parameters.ContainsKey("DELAYTOLERANCE"));
    }

    [Fact]
    public void AChangedPropertyIsWrittenAndAnUnchangedOneKeepsItsStoredText()
    {
        var read = Board();
        var lanes = Assert.IsType<PcbMatchedLengthsRule>(read.Rules[0]);
        lanes.Tolerance = Coord.FromMils(25);
        lanes.UseDelayUnits = true;                           // a key the file did not have
        read.Rules[1].Name = "SolderMaskExpansion_Renamed";   // a header key

        var again = RoundTrip(read);

        var written = Assert.IsType<PcbMatchedLengthsRule>(again.Rules[0]);
        Assert.Equal(25, written.Tolerance.ToMils(), 3);
        Assert.True(written.UseDelayUnits);
        Assert.False(again.Rules[0].Parameters.ContainsKey("DELAYTOLERANCE"));   // unchanged, still absent
        Assert.Equal("TRUE", again.Rules[0].Parameters["CHECKXSIGNALS"]);
        Assert.Equal("SolderMaskExpansion_Renamed", again.Rules[1].Name);
        Assert.Equal("TRUE", again.Rules[1].Parameters["SOLDERMASKFROMHOLE"]);
    }

    [Fact]
    public void ATypedRuleBuiltFromScratchWritesTheCanonicalKeys()
    {
        var document = new PcbDocument();
        document.AddRule(new PcbMatchedLengthsRule { RuleKind = "MatchedLengths", Name = "New", Tolerance = Coord.FromMils(5) });

        var read = RoundTrip(document);

        var keys = read.Rules[0].Parameters.Keys;
        Assert.Contains("USEDELAYUNITS", keys);
        Assert.Contains("DELAYTOLERANCE", keys);
        Assert.Equal(5, Assert.IsType<PcbMatchedLengthsRule>(read.Rules[0]).Tolerance.ToMils(), 3);
    }
}

using OriginalCircuit.Altium.Models.Pcb;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Verifies <see cref="PcbLayerKindMapping.ComputeSignature"/> reproduces Altium's LayerKindMapping
/// signature (MurmurHash2-32, seed 0xDEADBEEF) for the real corpus tables whose entry count makes the
/// packed buffer end on a clean word boundary (count*5 mod 4 in {0,1}). Counts whose final word
/// overreads uninitialised heap (mod {2,3}, e.g. counts 10/13) are intentionally excluded — Altium's
/// stored value there depends on heap contents and is preserved verbatim on round-trip instead.
/// </summary>
public sealed class LayerKindSignatureTests
{
    private static PcbLayerKindMapping Map(params (uint id, uint kind)[] entries)
    {
        var m = new PcbLayerKindMapping();
        foreach (var (id, kind) in entries) m.Entries.Add(new PcbLayerKindEntry(id, kind));
        return m;
    }

    [Fact]
    public void ComputeSignature_MatchesCorpus_CleanTailCases()
    {
        // SPI Isolator Panel.PcbDoc (count=2)
        Assert.Equal(0x608D2A5Du, Map((65, 25), (64, 28)).ComputeSignature());

        // SPI Isolator.PcbDoc (count=11)
        Assert.Equal(0x800E084Du, Map((64, 28), (72, 12), (60, 8), (58, 27), (71, 11), (62, 10),
            (59, 7), (57, 26), (69, 1), (70, 2), (61, 9)).ComputeSignature());

        // VCOCXO Breakout.PcbDoc (count=11)
        Assert.Equal(0xE166C937u, Map((63, 28), (72, 12), (60, 8), (58, 27), (71, 11), (62, 10),
            (59, 7), (57, 26), (69, 1), (70, 2), (61, 9)).ComputeSignature());

        // Power Adapter Panel / USB Power Adapter.PcbDoc (count=12)
        Assert.Equal(0xBEBE9740u, Map((63, 28), (72, 12), (60, 8), (58, 27), (71, 11), (62, 10),
            (59, 7), (57, 26), (69, 1), (70, 2), (64, 25), (61, 9)).ComputeSignature());
    }

    [Fact]
    public void ComputeSignature_EmptyTable_IsZero()
        => Assert.Equal(0u, new PcbLayerKindMapping().ComputeSignature());
}

using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Regression guard for the previously-latent data loss where the not-yet-modeled optional feature
/// storages (Dimensions6, Coordinates6, FromTos6, Embeddeds6, Textures, ModelsNoEmbed,
/// PadViaLibraryLinks) were re-emitted empty on write, dropping whatever they held. The corpus boards
/// all have them empty, so the loss never showed there; a board with dimension objects would lose them.
/// They are now captured verbatim by the AdditionalStreams catch-all and replayed unchanged.
/// </summary>
public sealed class PcbDocFeatureStoragePassthroughTests
{
    public static TheoryData<string> FeatureStorages() => new()
    {
        "Dimensions6", "Coordinates6", "FromTos6", "Embeddeds6",
        "Textures", "ModelsNoEmbed", "PadViaLibraryLinks",
    };

    [Theory]
    [MemberData(nameof(FeatureStorages))]
    public void NonEmptyFeatureStorage_SurvivesRoundTrip(string storage)
    {
        // A non-empty storage standing in for real content (e.g. dimension objects): a record-count
        // Header and an opaque Data payload the model does not yet parse.
        var header = BitConverter.GetBytes((uint)3);
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04, 0x00, 0xFF };

        var doc = new PcbDocument
        {
            AdditionalStreams = new(StringComparer.OrdinalIgnoreCase)
            {
                [$"{storage}/Header"] = header,
                [$"{storage}/Data"] = data,
            },
        };

        // Two full round-trips: write -> read must preserve the bytes, and do so repeatably.
        var once = RoundTrip(doc);
        AssertPreserved(once, storage, header, data);
        var twice = RoundTrip(once);
        AssertPreserved(twice, storage, header, data);
    }

    private static PcbDocument RoundTrip(PcbDocument doc)
    {
        using var ms = new MemoryStream();
        new PcbDocWriter().Write(doc, ms);
        ms.Position = 0;
        return new PcbDocReader().Read(ms);
    }

    private static void AssertPreserved(PcbDocument rt, string storage, byte[] header, byte[] data)
    {
        Assert.NotNull(rt.AdditionalStreams);
        Assert.True(rt.AdditionalStreams!.TryGetValue($"{storage}/Data", out var gotData),
            $"{storage}/Data was dropped on write");
        Assert.Equal(data, gotData);
        Assert.True(rt.AdditionalStreams.TryGetValue($"{storage}/Header", out var gotHeader),
            $"{storage}/Header was dropped on write");
        Assert.Equal(header, gotHeader);
    }
}

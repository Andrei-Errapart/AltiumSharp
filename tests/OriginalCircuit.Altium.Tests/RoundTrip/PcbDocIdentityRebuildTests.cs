using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Verifies the object-identity streams (PrimitiveGuids, UniqueIDPrimitiveInformation) are rebuilt from
/// the current collections on save, so adding/removing a primitive keeps each surviving primitive's
/// identity bound to its (now re-ordinaled) record instead of leaving the ordinal-keyed tables pointing
/// at the wrong primitives. See <see cref="OriginalCircuit.Altium.Serialization.PcbIdentityReconciler"/>.
/// </summary>
public sealed class PcbDocIdentityRebuildTests
{
    private const uint TrackType = 0x1004;

    [SkippableFact]
    public void RemoveTrack_ReordinalsSurvivorGuids_AndDropsTheRemovedOne()
    {
        var doc = LoadBoard();
        var n = doc.Tracks.Count;
        Skip.If(n < 12, "Board has too few tracks");

        var before = GuidsByOrdinal(doc, TrackType);
        // Every track must carry exactly one GUID for this invariant to be meaningful.
        Assert.Equal(n, before.Count);

        const int removeAt = 5;
        var removedGuid = before[(uint)removeAt];
        Assert.True(doc.RemoveTrack(doc.Tracks[removeAt]));

        var after = GuidsByOrdinal(RoundTrip(doc), TrackType);

        Assert.Equal(n - 1, after.Count);                 // exactly one entry dropped
        Assert.DoesNotContain(removedGuid, after.Values); // the removed track's GUID is gone

        // Every surviving track keeps its GUID, now at its shifted ordinal.
        for (uint i = 0; i < n; i++)
        {
            if (i == removeAt) continue;
            var newOrdinal = i < removeAt ? i : i - 1;
            Assert.Equal(before[i], after[newOrdinal]);
        }
    }

    [SkippableFact]
    public void AddTrack_AppendsFreshGuid_AndPreservesExistingOnes()
    {
        var doc = LoadBoard();
        var n = doc.Tracks.Count;
        Skip.If(n == 0, "Board has no tracks");

        var before = GuidsByOrdinal(doc, TrackType);
        Assert.Equal(n, before.Count);

        doc.AddTrack(new PcbTrack
        {
            Start = CoordPoint.Zero,
            End = new CoordPoint(Coord.OneMm, Coord.Zero),
            Width = Coord.OneMil,
            Layer = 1,
        });

        var after = GuidsByOrdinal(RoundTrip(doc), TrackType);

        Assert.Equal(n + 1, after.Count);                 // the new track got its own GUID
        for (uint i = 0; i < n; i++)
            Assert.Equal(before[i], after[i]);            // existing tracks unchanged
        Assert.True(after.TryGetValue((uint)n, out var newGuid));
        Assert.NotEqual(Guid.Empty, newGuid);             // a real, freshly minted object GUID
    }

    [SkippableFact]
    public void RemovePad_ReordinalsUniqueIdRecords_AndDropsTheRemovedOne()
    {
        var doc = LoadBoard();
        Skip.If(doc.PrimitiveUniqueIds.Count == 0, "Board has no UniqueID primitive information");
        var n = doc.Pads.Count;
        // The corpus stores one pad short-id per pad; the invariant needs that clean binding.
        Skip.If(doc.PrimitiveUniqueIds.Count != n || n < 4, "UniqueID stream is not a clean per-pad binding");

        var before = UniqueIdByOrdinal(doc);
        Assert.Equal(n, before.Count);

        const int removeAt = 2;
        var removedUid = before[removeAt];
        Assert.True(doc.RemovePad(doc.Pads[removeAt]));

        var after = UniqueIdByOrdinal(RoundTrip(doc));

        Assert.Equal(n - 1, after.Count);
        for (var i = 0; i < n; i++)
        {
            if (i == removeAt) continue;
            var newOrdinal = i < removeAt ? i : i - 1;
            Assert.Equal(before[i], after[newOrdinal]);
        }
        // The removed pad's short-id only disappears if it was unique; assert re-ordinalling regardless.
        Assert.False(after.ContainsKey(n - 1)); // the top ordinal no longer exists
        _ = removedUid;
    }

    private static Dictionary<uint, Guid> GuidsByOrdinal(PcbDocument doc, uint typeId)
        => doc.PrimitiveGuids.Where(g => g.TypeId == typeId).ToDictionary(g => g.Index, g => g.Guid);

    private static Dictionary<int, string> UniqueIdByOrdinal(PcbDocument doc)
        => doc.PrimitiveUniqueIds.ToDictionary(u => u.PrimitiveIndex, u => u.UniqueId);

    private static PcbDocument RoundTrip(PcbDocument doc)
    {
        using var ms = new MemoryStream();
        new PcbDocWriter().Write(doc, ms);
        ms.Position = 0;
        return new PcbDocReader().Read(ms);
    }

    private static PcbDocument LoadBoard()
    {
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "TestData", "SPI Isolator.PcbDoc");
        if (!File.Exists(path)) { Skip.If(true, "Test data not available"); }
        return new PcbDocReader().Read(File.OpenRead(path));
    }
}

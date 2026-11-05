using OpenMcdf;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests that small damaged or crafted documents are read in bounded time and memory: sizes and
/// indexes the readers take from a file must not decide what they allocate.
/// </summary>
public sealed class DamagedDocumentTests
{
    private static byte[] Board(params string[] texts)
    {
        var board = new PcbDocument();
        var x = 1.0;
        foreach (var text in texts)
        {
            board.AddText(new PcbText { Text = text, Location = new CoordPoint(Coord.FromMm(x), Coord.FromMm(1)), Layer = 1 });
            x += 5;
        }
        using var ms = new MemoryStream();
        new PcbDocWriter().Write(board, ms);
        return ms.ToArray();
    }

    // Replaces a stream's content in a compound file.
    private static byte[] WithStream(byte[] file, string storage, string stream, Func<byte[], byte[]> change)
    {
        var ms = new MemoryStream();
        ms.Write(file);
        ms.Position = 0;
        using (var root = RootStorage.Open(ms, StorageModeFlags.LeaveOpen))
        {
            var parent = root.OpenStorage(storage);
            byte[] data;
            using (var read = parent.OpenStream(stream))
            {
                data = new byte[read.Length];
                read.ReadExactly(data);
            }
            var changed = change(data);
            using var write = parent.OpenStream(stream);
            write.SetLength(0);
            write.Write(changed);
        }
        return ms.ToArray();
    }

    private static (T Result, long Allocated, TimeSpan Took) Measure<T>(Func<T> read)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = read();
        return (result, GC.GetAllocatedBytesForCurrentThread() - before, started.Elapsed);
    }

    [Fact]
    public void AWideStringIndexFarPastTheRecords_CostsNothing()
    {
        var board = Board("R1", "C1");
        // The second record's text index (after the first record's 8-byte head and "R1\0" in UTF-16).
        var damaged = WithStream(board, "WideStrings6", "Data", data =>
        {
            BitConverter.TryWriteBytes(data.AsSpan(8 + 6, 4), 0x7F000001);
            return data;
        });

        var (document, allocated, took) = Measure(() => new PcbDocReader().Read(new MemoryStream(damaged)));

        Assert.True(allocated < 50_000_000, $"allocated {allocated:N0} bytes");
        Assert.True(took < TimeSpan.FromSeconds(5), $"took {took}");
        // As before: the text whose wide text is missing below the highest index is dropped.
        Assert.Equal(new[] { "R1" }, document.Texts.Select(t => t.Text).ToArray());
    }

    [Fact]
    public void AWideStringTableOfOneHugeIndex_CostsNothing()
    {
        var board = Board("R1");
        var damaged = WithStream(board, "WideStrings6", "Data", _ =>
        {
            var entry = new byte[8 + 4];
            BitConverter.TryWriteBytes(entry.AsSpan(0, 4), 400_000_000);
            BitConverter.TryWriteBytes(entry.AsSpan(4, 4), 4);
            "A\0"u8.ToArray().CopyTo(entry, 8);
            return entry;
        });

        var (_, allocated, took) = Measure(() => new PcbDocReader().Read(new MemoryStream(damaged)));

        Assert.True(allocated < 50_000_000, $"allocated {allocated:N0} bytes");
        Assert.True(took < TimeSpan.FromSeconds(5), $"took {took}");
    }

    [Fact]
    public void WideTexts_AreStillReadByTheirIndex()
    {
        var document = new PcbDocReader().Read(new MemoryStream(Board("R1", "C1", "Ω1")));

        Assert.Equal(new[] { "R1", "C1", "Ω1" }, document.Texts.Select(t => t.Text).ToArray());
    }
}

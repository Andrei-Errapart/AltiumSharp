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

    // Sets the size a compound file's directory entry claims for a stream.
    private static byte[] WithClaimedSize(byte[] file, string streamName, uint size)
    {
        var name = System.Text.Encoding.Unicode.GetBytes(streamName + "\0");
        for (var at = 512; at + 128 <= file.Length; at += 128)
        {
            if (!file.AsSpan(at, name.Length).SequenceEqual(name))
                continue;
            var damaged = (byte[])file.Clone();
            BitConverter.TryWriteBytes(damaged.AsSpan(at + 120, 4), size);
            BitConverter.TryWriteBytes(damaged.AsSpan(at + 124, 4), 0);
            return damaged;
        }
        throw new InvalidOperationException($"No directory entry for {streamName}.");
    }

    [Theory]
    [InlineData(0x7FFFFFC7u)]
    [InlineData(0x7FFFFFFFu)]
    [InlineData(0xFFFFFFFFu)]
    public void AStreamClaimingMoreThanTheFile_IsRefusedBeforeItIsAllocated(uint claimed)
    {
        var sheet = new Models.Sch.SchDocument();
        using var ms = new MemoryStream();
        new SchDocWriter().Write(sheet, ms);
        var damaged = WithClaimedSize(ms.ToArray(), "FileHeader", claimed);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Assert.ThrowsAny<AltiumCorruptFileException>(() => new SchDocReader().Read(new MemoryStream(damaged)));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 50_000_000);
        // OpenMcdf refuses a size past 2 GB itself; anything below is refused by the size check.
        if (claimed <= int.MaxValue)
            Assert.Contains("claims", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoardStreamClaimingMoreThanTheFile_IsRefusedBeforeItIsAllocated()
    {
        var damaged = WithClaimedSize(Board("R1"), "Data", 0x7FFFFFC7u);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Assert.ThrowsAny<AltiumCorruptFileException>(() => new PcbDocReader().Read(new MemoryStream(damaged)));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 50_000_000);
        Assert.Contains("claims", ex.Message, StringComparison.Ordinal);
    }

    // ---- a single read can be cancelled ----

    // Cancels the token as soon as the reader starts reading the document's bytes.
    private sealed class CancelOnRead(byte[] bytes, CancellationTokenSource cancel) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            cancel.Cancel();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            cancel.Cancel();
            return base.Read(buffer);
        }
    }

    private static byte[] Written(Action<Stream> write)
    {
        using var ms = new MemoryStream();
        write(ms);
        return ms.ToArray();
    }

    public static TheoryData<string> Kinds => new() { "SchDoc", "SchLib", "PcbDoc", "PcbLib" };

    private static byte[] Document(string kind) => kind switch
    {
        "SchDoc" => Written(s => new SchDocWriter().Write(new Models.Sch.SchDocument(), s)),
        "SchLib" => Written(s => new SchLibWriter().Write(new Models.Sch.SchLibrary(), s)),
        "PcbDoc" => Board("R1"),
        _ => Written(s => new PcbLibWriter().Write(new PcbLibrary(), s)),
    };

    private static Task Open(string kind, Stream stream, CancellationToken token) => kind switch
    {
        "SchDoc" => AltiumLibrary.OpenSchDocAsync(stream, token).AsTask(),
        "SchLib" => AltiumLibrary.OpenSchLibAsync(stream, token).AsTask(),
        "PcbDoc" => AltiumLibrary.OpenPcbDocAsync(stream, token).AsTask(),
        _ => AltiumLibrary.OpenPcbLibAsync(stream, token).AsTask(),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OpeningFromAStream_StopsWhenCancelledDuringTheRead(string kind)
    {
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Open(kind, new CancelOnRead(Document(kind), cancel), cancel.Token));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OpeningFromAStream_StopsWhenCancelledAlready(string kind)
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Open(kind, new MemoryStream(Document(kind)), cancel.Token));
    }

    [Fact]
    public async Task OpeningAProjectFromAStream_StopsWhenCancelled()
    {
        var text = "[Design]\r\n" + string.Concat(Enumerable.Range(0, 10_000).Select(i => $"Key{i}=Value\r\n"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AltiumLibrary.OpenProjectAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), cancel.Token).AsTask());
        using var duringRead = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new PrjPcbReader().Read(new CancelOnRead(System.Text.Encoding.UTF8.GetBytes(text), duringRead), duringRead.Token));
    }

    [Fact]
    public async Task LoadingAHierarchyFromStreams_StopsWhenCancelledDuringASheetsRead()
    {
        var sheet = Written(s => new SchDocWriter().Write(new Models.Sch.SchDocument(), s));
        using var cancel = new CancellationTokenSource();
        var project = AltiumLibrary.CreateProject();
        project.AddDocument("Top.SchDoc");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Models.Project.ProjectHierarchy.LoadAsync(project, _ => new CancelOnRead(sheet, cancel), cancellationToken: cancel.Token));
    }

    [Fact]
    public void WideTexts_AreStillReadByTheirIndex()
    {
        var document = new PcbDocReader().Read(new MemoryStream(Board("R1", "C1", "Ω1")));

        Assert.Equal(new[] { "R1", "C1", "Ω1" }, document.Texts.Select(t => t.Text).ToArray());
    }
}

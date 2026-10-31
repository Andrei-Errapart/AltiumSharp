using OpenMcdf;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Reading from a writable stream must not modify it. OpenMcdf opens a writer whenever the stream
/// can write and flushes on dispose: it rewrites the header and trims the stream to its last used
/// sector. On a fixed-size <see cref="MemoryStream"/> that throws "Memory stream is not expandable"
/// (seen on V4 files, whose flush grows the FAT); on any other writable stream it silently changes
/// the caller's data.
/// </summary>
public sealed class WritableStreamReadTests
{
    private static byte[] SchDocBytes()
    {
        var doc = new SchDocument();
        var component = new SchComponent { Name = "RES", PartCount = 1 };
        component.AddParameter(new SchParameter { Name = "Designator", Value = "R1" });
        doc.AddComponent(component);
        using var ms = new MemoryStream();
        new SchDocWriter().Write(doc, ms);
        return ms.ToArray();
    }

    // A SchDoc with an extra stream the reader never opens, cut one sector short so that stream's
    // tail is missing. Reading succeeds; only a flush, which walks the whole FAT, notices.
    private static byte[] SchDocWithTruncatedUnreadStream()
    {
        using var work = new MemoryStream();
        work.Write(SchDocBytes());
        work.Position = 0;
        using (var root = RootStorage.Open(work, StorageModeFlags.LeaveOpen))
        {
            using (var extra = root.CreateStream("Unread"))
                extra.Write(new byte[8192]);
            root.Flush();
        }
        var bytes = work.ToArray();
        return bytes[..(bytes.Length - 512)];
    }

    // Appends whole unused sectors, which OpenMcdf's flush would trim away.
    private static byte[] WithTrailingSectors(byte[] bytes, int sectors) =>
        bytes.Concat(new byte[512 * sectors]).ToArray();

    private static MemoryStream Growable(byte[] bytes)
    {
        var ms = new MemoryStream();
        ms.Write(bytes);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task SchDoc_ThatReadsReadOnly_AlsoReadsFromWritableStreams()
    {
        var bytes = SchDocWithTruncatedUnreadStream();
        var original = bytes.ToArray();

        using (var readOnly = new MemoryStream(bytes, writable: false))
            Assert.Single((await AltiumLibrary.OpenSchDocAsync(readOnly)).Components);

        using var fixedSize = new MemoryStream(bytes); // writable, not expandable
        var doc = await AltiumLibrary.OpenSchDocAsync(fixedSize);
        Assert.Equal("R1", ((SchComponent)Assert.Single(doc.Components)).Designator);
        Assert.Equal(original, bytes);

        using var growable = Growable(bytes);
        Assert.Single((await AltiumLibrary.OpenSchDocAsync(growable)).Components);
        Assert.Equal(original, growable.ToArray());
    }

    [Fact]
    public async Task SchDoc_FromGrowableStream_LeavesTheStreamUnchanged()
    {
        var bytes = WithTrailingSectors(SchDocBytes(), 2);

        using var stream = Growable(bytes);
        var doc = await AltiumLibrary.OpenSchDocAsync(stream);

        Assert.Single(doc.Components);
        Assert.Equal(bytes, stream.ToArray());
    }

    [Fact]
    public async Task SchLib_FromGrowableStream_LeavesTheStreamUnchanged()
    {
        var lib = new SchLibrary();
        lib.Add(new SchComponent { Name = "RES" });
        using var written = new MemoryStream();
        new SchLibWriter().Write(lib, written);
        var bytes = WithTrailingSectors(written.ToArray(), 2);

        using var stream = Growable(bytes);
        var read = await AltiumLibrary.OpenSchLibAsync(stream);

        Assert.Single(read.Components);
        Assert.Equal(bytes, stream.ToArray());
    }

    [Fact]
    public async Task PcbLib_FromGrowableStream_LeavesTheStreamUnchanged()
    {
        var lib = new PcbLibrary();
        lib.Add(PcbComponent.Create("R0402").Build());
        using var written = new MemoryStream();
        new PcbLibWriter().Write(lib, written);
        var bytes = WithTrailingSectors(written.ToArray(), 2);

        using var stream = Growable(bytes);
        var read = await AltiumLibrary.OpenPcbLibAsync(stream);

        Assert.Single(read.Components);
        Assert.Equal(bytes, stream.ToArray());
    }

    [Fact]
    public async Task PcbDoc_FromGrowableStream_LeavesTheStreamUnchanged()
    {
        var pcb = new PcbDocument();
        pcb.AddComponent(new PcbComponent { Name = "R0402" });
        using var written = new MemoryStream();
        new PcbDocWriter().Write(pcb, written);
        var bytes = WithTrailingSectors(written.ToArray(), 2);

        using var stream = Growable(bytes);
        var read = await AltiumLibrary.OpenPcbDocAsync(stream);

        Assert.Single(read.Components);
        Assert.Equal(bytes, stream.ToArray());
    }

    [Fact]
    public async Task ReadOnlyStream_StillReads_AndStreamStaysOpen()
    {
        var bytes = SchDocBytes();

        using var stream = new MemoryStream(bytes, writable: false);
        var doc = await AltiumLibrary.OpenSchDocAsync(stream);

        Assert.Single(doc.Components);
        Assert.True(stream.CanRead); // the caller still owns the stream
    }
}

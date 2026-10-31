namespace OriginalCircuit.Altium.Serialization.Compound;

/// <summary>
/// A read-only, seekable view of another stream. OpenMcdf opens a writer whenever its stream can
/// write, and flushes on dispose (rewriting the header and trimming the stream), so readers hand
/// it this view to keep the caller's stream untouched.
/// </summary>
internal sealed class ReadOnlyStreamView : Stream
{
    private readonly Stream _inner;
    private readonly bool _leaveInnerOpen;

    public ReadOnlyStreamView(Stream inner, bool leaveInnerOpen)
    {
        _inner = inner;
        _leaveInnerOpen = leaveInnerOpen;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override int ReadByte() => _inner.ReadByte();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException("The stream is read-only.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The stream is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveInnerOpen)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}

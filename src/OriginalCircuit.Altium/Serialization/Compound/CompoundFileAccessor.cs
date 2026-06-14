using OpenMcdf;

namespace OriginalCircuit.Altium.Serialization.Compound;

/// <summary>
/// Provides async-friendly access to COM Structured Storage (compound) files.
/// </summary>
/// <remarks>
/// This is a thin wrapper around OpenMcdf 3.x. It fully encapsulates the OpenMcdf API surface
/// (<see cref="RootStorage"/>/<see cref="Storage"/>/<see cref="CfbStream"/>/<see cref="EntryInfo"/>)
/// behind the library-neutral <see cref="Compound.CompoundStorage"/>/<see cref="CompoundStream"/>
/// types so readers and writers never reference OpenMcdf directly.
///
/// OpenMcdf 3.x replaced the 2.x <c>CompoundFile</c>/<c>CFStorage</c>/<c>CFStream</c> model with a
/// root-storage that streams directly to its backing store. For reads we open the source stream
/// (which now hardens against the cyclic directory-entry DoS the 2.3.0 advisories described). For
/// writes we build in an in-memory root storage and copy the finished image out in <see cref="Save"/>,
/// preserving the previous "build, then Save(stream)" usage shape.
/// </remarks>
internal sealed class CompoundFileAccessor : IAsyncDisposable, IDisposable
{
    private readonly OpenMcdf.RootStorage _root;
    private readonly Stream? _stream;
    private readonly bool _leaveStreamOpen;
    private CompoundStorage? _rootStorage;
    private bool _disposed;

    /// <summary>
    /// Gets the root storage of the compound file.
    /// </summary>
    public CompoundStorage RootStorage => _rootStorage ??= new CompoundStorage(_root);

    private CompoundFileAccessor(OpenMcdf.RootStorage root, Stream? stream = null, bool leaveOpen = false)
    {
        _root = root;
        _stream = stream;
        _leaveStreamOpen = leaveOpen;
    }

    /// <summary>
    /// Opens a compound file from a path.
    /// </summary>
    public static async ValueTask<CompoundFileAccessor> OpenAsync(
        string path,
        bool writable = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const FileMode mode = FileMode.Open; // Always open an existing file
        var access = writable ? FileAccess.ReadWrite : FileAccess.Read;
        var share = writable ? FileShare.None : FileShare.Read;

        var stream = new FileStream(path, mode, access, share, 4096, useAsync: true);

        try
        {
            // OpenMcdf reads synchronously from the seekable stream. LeaveOpen lets this accessor
            // own the FileStream's lifetime.
            var root = OpenMcdf.RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
            return new CompoundFileAccessor(root, stream, leaveOpen: false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens a compound file from a stream.
    /// </summary>
    public static CompoundFileAccessor Open(Stream stream, bool leaveOpen = false)
    {
        var root = OpenMcdf.RootStorage.Open(stream, StorageModeFlags.LeaveOpen);
        return new CompoundFileAccessor(root, stream, leaveOpen);
    }

    /// <summary>
    /// Creates a new (in-memory) compound file for writing. Call <see cref="Save"/> to emit it.
    /// </summary>
    public static CompoundFileAccessor Create()
    {
        var root = OpenMcdf.RootStorage.CreateInMemory(OpenMcdf.Version.V3);
        return new CompoundFileAccessor(root);
    }

    /// <summary>
    /// Gets a storage (directory) by path.
    /// </summary>
    /// <param name="path">Path using / as separator.</param>
    public CompoundStorage? TryGetStorage(string path)
    {
        var (storage, stream) = TryNavigate(path);
        return stream is null ? storage : null;
    }

    /// <summary>
    /// Gets a stream by path.
    /// </summary>
    /// <param name="path">Path using / as separator.</param>
    public CompoundStream? TryGetStream(string path) => TryNavigate(path).Stream;

    /// <summary>
    /// Gets the data from a stream.
    /// </summary>
    public byte[] GetStreamData(string path)
    {
        var stream = TryGetStream(path)
            ?? throw new ArgumentException($"Stream '{path}' not found");
        return stream.GetData();
    }

    /// <summary>
    /// Gets stream data as a Memory for async-friendly access.
    /// </summary>
    public ReadOnlyMemory<byte> GetStreamDataAsMemory(string path) => GetStreamData(path);

    /// <summary>
    /// Enumerates all child items in a storage.
    /// </summary>
    public IEnumerable<CompoundEntry> EnumerateChildren(CompoundStorage storage)
        => storage.EnumerateEntries();

    /// <summary>
    /// Enumerates all child storages in a storage.
    /// </summary>
    public IEnumerable<CompoundStorage> EnumerateStorages(CompoundStorage storage)
        => storage.EnumerateStorages();

    /// <summary>
    /// Enumerates all child streams in a storage.
    /// </summary>
    public IEnumerable<CompoundStream> EnumerateStreams(CompoundStorage storage)
        => storage.EnumerateStreams();

    /// <summary>
    /// Saves the compound file to a new path.
    /// </summary>
    public async ValueTask SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        _root.Flush();
        var image = _root.BaseStream;
        image.Position = 0;

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await image.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the compound file to a stream.
    /// </summary>
    public void Save(Stream stream)
    {
        _root.Flush();
        var image = _root.BaseStream;
        image.Position = 0;
        image.CopyTo(stream);
    }

    private (CompoundStorage? Storage, CompoundStream? Stream) TryNavigate(string path)
    {
        if (string.IsNullOrEmpty(path))
            return (RootStorage, null);

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = RootStorage;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;

            if (isLast && current.TryGetStream(part, out var stream))
                return (null, stream);

            if (!current.TryGetStorage(part, out var child))
                return (null, null);

            current = child;
        }

        return (current, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _root.Dispose();

        if (_stream != null && !_leaveStreamOpen)
        {
            _stream.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _root.Dispose();

        if (_stream != null && !_leaveStreamOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}

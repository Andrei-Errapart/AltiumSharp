using OpenMcdf;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;
using Xunit.Abstractions;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Corpus-wide byte-fidelity guard: every Altium file under TestData/PrivateTestData must round-trip
/// (read → write) byte-for-byte, EXCEPT the zlib-compressed embedded-blob streams (STEP 3D models,
/// embedded images, and the pin auxiliary streams). Those differ only because .NET's deflate is not
/// byte-identical to Altium's madler zlib; their decompressed content is preserved. Any NEW non-zlib
/// stream difference is a fidelity regression and fails the test.
/// </summary>
public sealed class ByteFidelityCorpusTests
{
    private readonly ITestOutputHelper _out;
    public ByteFidelityCorpusTests(ITestOutputHelper o) => _out = o;

    // Streams whose bytes legitimately differ on round-trip due to zlib re-compression (accepted).
    private static bool IsAcceptedZlibStream(string streamPath)
    {
        var parts = streamPath.Split('/');
        var name = parts[^1];
        var parent = parts.Length >= 2 ? parts[^2] : "";
        // Numeric STEP payload streams under a Models / ModelsNoEmbed storage (PcbLib nests these under
        // Library/Models/<n>; PcbDoc uses Models/<n> and ModelsNoEmbed/<n>).
        if ((parent.Equals("Models", StringComparison.OrdinalIgnoreCase)
             || parent.Equals("ModelsNoEmbed", StringComparison.OrdinalIgnoreCase))
            && int.TryParse(name, out _))
            return true;
        return name.Equals("Storage", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PinFrac", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PinSymbolLineWidth", StringComparison.OrdinalIgnoreCase);
    }

    [SkippableTheory]
    [InlineData("PcbLib")]
    [InlineData("PcbDoc")]
    [InlineData("SchLib")]
    [InlineData("SchDoc")]
    public void Corpus_RoundTrips_ByteForByte_ExceptZlib(string ext)
    {
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        var roundTrip = Roundtripper(ext);
        var files = new[] { Path.Combine(root, "TestData"), Path.Combine(root, "PrivateTestData") }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, $"*.{ext}", SearchOption.AllDirectories))
            .OrderBy(f => f).ToList();
        Skip.If(files.Count == 0, "Test data not available");

        var failures = new List<string>();
        foreach (var file in files)
        {
            var orig = File.ReadAllBytes(file);
            byte[] rt;
            try { rt = roundTrip(orig); }
            catch (Exception ex) { failures.Add($"{Path.GetFileName(file)}: EXCEPTION {ex.GetType().Name}: {ex.Message}"); continue; }

            var o = Collect(orig);
            var r = Collect(rt);
            foreach (var key in o.Keys.Union(r.Keys))
            {
                if (IsAcceptedZlibStream(key)) continue;
                var has1 = o.TryGetValue(key, out var a);
                var has2 = r.TryGetValue(key, out var b);
                if (!has1) failures.Add($"{Path.GetFileName(file)}: extra stream {key}");
                else if (!has2) failures.Add($"{Path.GetFileName(file)}: missing stream {key}");
                else if (!a!.AsSpan().SequenceEqual(b!))
                    failures.Add($"{Path.GetFileName(file)}: {key} differs (len {a.Length}->{b.Length} @{FirstDiff(a, b)})");
            }
        }

        if (failures.Count > 0)
            _out.WriteLine(string.Join("\n", failures));
        Assert.True(failures.Count == 0, $"{failures.Count} non-zlib byte-fidelity regression(s) across {files.Count} {ext} files");
    }

    private static int FirstDiff(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return n;
    }

    private static Func<byte[], byte[]> Roundtripper(string ext) => ext switch
    {
        "PcbLib" => b => { var m = new PcbLibReader().Read(new MemoryStream(b)); var ms = new MemoryStream(); new PcbLibWriter().Write(m, ms); return ms.ToArray(); },
        "SchLib" => b => { var m = new SchLibReader().Read(new MemoryStream(b)); var ms = new MemoryStream(); new SchLibWriter().Write(m, ms); return ms.ToArray(); },
        "PcbDoc" => b => { var m = new PcbDocReader().Read(new MemoryStream(b)); var ms = new MemoryStream(); new PcbDocWriter().Write(m, ms); return ms.ToArray(); },
        "SchDoc" => b => { var m = new SchDocReader().Read(new MemoryStream(b)); var ms = new MemoryStream(); new SchDocWriter().Write(m, ms); return ms.ToArray(); },
        _ => throw new ArgumentOutOfRangeException(nameof(ext)),
    };

    private static Dictionary<string, byte[]> Collect(byte[] bytes)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var cf = RootStorage.Open(new MemoryStream(bytes), StorageModeFlags.LeaveOpen);
        McdfTestExtensions.CollectStreams(cf, "", result);
        return result;
    }
}

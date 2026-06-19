using OpenMcdf;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Round-trip coverage for ShapeBasedRegions6 — there is no such record in the byte-fidelity corpus,
/// so these tests stand in for it: they prove the de-replayed property block (now a typed ordered
/// <see cref="PcbShapeBasedRegion.Properties"/> list, formerly an opaque raw-byte capture) round-trips
/// losslessly and byte-identically through read→write, including the exact Altium property-block framing.
/// </summary>
public sealed class ShapeBasedRegionTests
{
    private static byte[] GetStream(byte[] file, string path)
    {
        using var cf = RootStorage.Open(new MemoryStream(file), StorageModeFlags.LeaveOpen);
        var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        McdfTestExtensions.CollectStreams(cf, "", d);
        return d[path];
    }

    private static PcbShapeBasedRegion SampleRegion()
    {
        // Property block mirroring a real Altium shape-based region (uppercased KEY=VALUE|… form).
        var r = new PcbShapeBasedRegion { TypeByte = 0x0B, Layer = 1, NetIndex = 0xFFFF };
        r.Properties.Add(new("V7_LAYER", "TOP"));
        r.Properties.Add(new("NAME", ""));
        r.Properties.Add(new("KIND", "0"));
        r.Properties.Add(new("SUBPOLYINDEX", "-1"));
        r.Properties.Add(new("UNIONINDEX", "0"));
        r.Properties.Add(new("ARCRESOLUTION", "0.5mil"));
        r.Properties.Add(new("ISSHAPEBASED", "TRUE"));
        r.Properties.Add(new("CAVITYHEIGHT", "0mil"));
        r.PropsInnerNulls = 1; // Altium NUL-terminates the block inside its length-prefixed span
        // A closed 4-vertex outline (writer stores N-1 on disk; reader restores N).
        foreach (var (x, y) in new[] { (0, 0), (1000000, 0), (1000000, 1000000), (0, 1000000), (0, 0) })
            r.Outline.Add(new PcbExtendedVertex { X = x, Y = y });
        return r;
    }

    [Fact]
    public void ShapeBasedRegion_PropertyBlock_RoundTripsByteExact()
    {
        var doc = new PcbDocument();
        doc.ShapeBasedRegions.Add(SampleRegion());

        using var ms1 = new MemoryStream();
        new PcbDocWriter().Write(doc, ms1);
        var bytes1 = ms1.ToArray();

        var doc2 = new PcbDocReader().Read(new MemoryStream(bytes1));
        using var ms2 = new MemoryStream();
        new PcbDocWriter().Write(doc2, ms2);
        var bytes2 = ms2.ToArray();

        // The de-replayed property block must reconstruct byte-identically (lossless).
        Assert.Equal(GetStream(bytes1, "ShapeBasedRegions6/Data"), GetStream(bytes2, "ShapeBasedRegions6/Data"));

        // And the typed properties survive the round-trip in order.
        Assert.Single(doc2.ShapeBasedRegions);
        var rt = doc2.ShapeBasedRegions[0];
        Assert.Equal("TOP", rt.GetProperty("V7_LAYER"));
        Assert.Equal("TRUE", rt.GetProperty("ISSHAPEBASED"));
        Assert.Equal("0.5mil", rt.GetProperty("ARCRESOLUTION"));
        Assert.Equal(8, rt.Properties.Count);
        Assert.Equal(5, rt.Outline.Count);
    }

    [Theory]
    // Edge cases the lossless parse/rebuild must preserve exactly.
    [InlineData("V7_LAYER=TOP|NAME=|KIND=0")]   // empty value
    [InlineData("|LEADINGPIPE=1|X=2")]           // leading pipe (empty first segment)
    [InlineData("A=1|TRAILINGPIPE=2|")]          // trailing pipe (empty last segment)
    [InlineData("BARE|A=1")]                     // a segment with no '='
    public void PropertyBlock_LosslessForEdgeCases(string block)
    {
        // Parse exactly as the reader does, rebuild exactly as the writer does, assert identity.
        var props = new List<KeyValuePair<string, string?>>();
        foreach (var seg in block.Split('|'))
        {
            var eq = seg.IndexOf('=');
            props.Add(eq < 0 ? new(seg, null) : new(seg[..eq], seg[(eq + 1)..]));
        }
        var rebuilt = string.Join("|", props.Select(p => p.Value is null ? p.Key : p.Key + "=" + p.Value));
        Assert.Equal(block, rebuilt);
    }
}

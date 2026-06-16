using System.IO.Compression;
using System.Text;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

public sealed class PcbDocModelTests
{
    /// <summary>
    /// The PcbDoc <c>Models</c> storage is preserved verbatim in AdditionalStreams; PcbDocument.Models
    /// decodes that captured data (Models/Data metadata + zlib-compressed Models/&lt;n&gt; payloads) on
    /// demand. Verified hermetically with synthetic streams (no test-data file required).
    /// </summary>
    [Fact]
    public void Models_DecodesEmbeddedStepFromAdditionalStreams()
    {
        const string stepText = "ISO-10303-21;\nHEADER;\nENDSEC;\nEND-ISO-10303-21;";

        var doc = new PcbDocument
        {
            AdditionalStreams = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Models/Data"] = BuildModelsData("|ID={GUID-1}|NAME=widget.step|EMBED=TRUE|MODELSOURCE=Undefined|CHECKSUM=42|ROTX=0|ROTY=0|ROTZ=90|DZ=0"),
                ["Models/0"] = ZlibCompress(stepText),
            }
        };

        var models = doc.Models;
        Assert.Single(models);
        var m = models[0];
        Assert.Equal("{GUID-1}", m.Id);
        Assert.Equal("widget.step", m.Name);
        Assert.True(m.IsEmbedded);
        Assert.Equal(42, m.Checksum);
        Assert.Equal(90, m.RotationZ);
        Assert.Equal(stepText, m.StepData);

        // Cached: repeat access returns the same instance.
        Assert.Same(models, doc.Models);
    }

    [Fact]
    public void Models_EmptyWhenNoModelStorage()
    {
        Assert.Empty(new PcbDocument().Models);
        Assert.Empty(new PcbDocument { AdditionalStreams = new() }.Models);
    }

    /// <summary>
    /// Integration check against a real board with embedded 3D models: PcbDocument.Models decodes
    /// them, and the captured Models/ModelsNoEmbed streams round-trip byte-for-byte.
    /// </summary>
    [SkippableFact]
    public void Models_RealFile_DecodeAndRoundTrip()
    {
        var current = Directory.GetCurrentDirectory();
        var root = Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..", ".."));
        var file = Path.Combine(root, "PrivateTestData", "Coherent Digitiser.PcbDoc");
        Skip.IfNot(File.Exists(file), "Test data not available");

        var doc = new PcbDocReader().Read(File.OpenRead(file));

        Assert.NotEmpty(doc.Models);
        Assert.All(doc.Models, m => Assert.StartsWith("ISO-10303-21;", m.StepData));

        // The verbatim model streams must survive a save unchanged.
        var addl = doc.AdditionalStreams!;
        var modelKeys = addl.Keys.Where(k => k.StartsWith("Models/") || k.StartsWith("ModelsNoEmbed/")).ToList();
        Assert.NotEmpty(modelKeys);

        using var ms = new MemoryStream();
        new PcbDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = new PcbDocReader().Read(ms);
        var rtAddl = rt.AdditionalStreams!;

        foreach (var k in modelKeys)
        {
            Assert.True(rtAddl.ContainsKey(k), $"missing stream {k} after round-trip");
            Assert.True(rtAddl[k].AsSpan().SequenceEqual(addl[k]), $"stream {k} changed on round-trip");
        }
    }

    private static byte[] BuildModelsData(string paramString)
    {
        var payload = Encoding.ASCII.GetBytes(paramString);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII);
        bw.Write(payload.Length);
        bw.Write(payload);
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] ZlibCompress(string text)
    {
        using var outMs = new MemoryStream();
        using (var zs = new ZLibStream(outMs, CompressionMode.Compress, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            zs.Write(bytes, 0, bytes.Length);
        }
        return outMs.ToArray();
    }
}

using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

public sealed class PcbDocModelTests
{
    /// <summary>
    /// The PcbDoc <c>Models</c> storage is a first-class typed model: a 3D model added to
    /// <see cref="PcbDocument.Models"/> is serialized (metadata + zlib STEP payload) and decoded again on
    /// read. Verified from scratch with no test-data file. The metadata round-trips exactly; the STEP
    /// payload round-trips its decoded text (the zlib bytes themselves are the accepted exception).
    /// </summary>
    [Fact]
    public void Models_RoundTripFromScratch_PreservesMetadataAndStep()
    {
        const string stepText = "ISO-10303-21;\nHEADER;\nENDSEC;\nEND-ISO-10303-21;";

        var doc = new PcbDocument();
        doc.Models.Add(new PcbModel
        {
            Id = "{GUID-1}",
            Name = "widget.step",
            IsEmbedded = true,
            ModelSource = "Undefined",
            Checksum = 42,
            RotationZ = 90,
            StepData = stepText,
        });

        using var ms = new MemoryStream();
        new PcbDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = new PcbDocReader().Read(ms);

        var m = Assert.Single(rt.Models);
        Assert.Equal("{GUID-1}", m.Id);
        Assert.Equal("widget.step", m.Name);
        Assert.True(m.IsEmbedded);
        Assert.Equal(42, m.Checksum);
        Assert.Equal(90, m.RotationZ);
        Assert.Equal(stepText, m.StepData);
    }

    /// <summary>
    /// Altium embeds whatever model file the designer attached, and not all of them are STEP
    /// text — a SolidWorks part is binary. The payload must therefore round-trip as bytes:
    /// carrying it through a string replaces every byte that is not valid UTF-8 with U+FFFD,
    /// which silently destroys the model both on read and on the next save.
    /// </summary>
    [Fact]
    public void Models_BinaryPayload_RoundTripsByteForByte()
    {
        // 0xFF and a lone 0x80 continuation byte never appear in valid UTF-8.
        var payload = new byte[] { 0xFF, 0xFE, 0x80, 0x00, 0x01, 0x7A, 0xC3, 0x28, 0x00, 0xBD };

        var doc = new PcbDocument();
        var model = new PcbModel { Id = "{GUID-1}", Name = "part.SLDPRT", StepBytes = payload };
        model.RecomputeChecksum();
        doc.Models.Add(model);

        using var ms = new MemoryStream();
        new PcbDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = new PcbDocReader().Read(ms);

        var read = Assert.Single(rt.Models);
        Assert.Equal(payload, read.StepBytes);
        Assert.Equal(model.Checksum, read.Checksum);
        Assert.Equal(unchecked((int)PcbModel.ComputeChecksum(read.StepBytes)), read.Checksum);
    }

    /// <summary>
    /// <see cref="PcbModel.StepData"/> stays a text view over the same payload: assigning text
    /// fills the bytes, and reading text back decodes them.
    /// </summary>
    [Fact]
    public void Models_StepDataIsATextViewOverTheBytes()
    {
        const string stepText = "ISO-10303-21;\nEND-ISO-10303-21;";

        var model = new PcbModel { StepData = stepText };
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(stepText), model.StepBytes);

        model.StepBytes = System.Text.Encoding.UTF8.GetBytes("OTHER;");
        Assert.Equal("OTHER;", model.StepData);
    }

    [Fact]
    public void Models_EmptyWhenNoModelStorage()
    {
        Assert.Empty(new PcbDocument().Models);
    }

    /// <summary>
    /// Integration check against a real board with embedded 3D models: PcbDocument.Models decodes them,
    /// and a save→reload preserves every model's metadata and decoded STEP content (the zlib payload
    /// bytes may differ, the accepted library-wide limitation).
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

        using var ms = new MemoryStream();
        new PcbDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = new PcbDocReader().Read(ms);

        Assert.Equal(doc.Models.Count, rt.Models.Count);
        for (var i = 0; i < doc.Models.Count; i++)
        {
            Assert.Equal(doc.Models[i].Id, rt.Models[i].Id);
            Assert.Equal(doc.Models[i].Name, rt.Models[i].Name);
            Assert.Equal(doc.Models[i].Checksum, rt.Models[i].Checksum);
            Assert.Equal(doc.Models[i].StepBytes, rt.Models[i].StepBytes);

            // Altium's own checksum over the payload is the oracle for a lossless read.
            if (doc.Models[i].Checksum != 0)
            {
                Assert.Equal(
                    unchecked((int)PcbModel.ComputeChecksum(doc.Models[i].StepBytes)),
                    doc.Models[i].Checksum);
            }
        }
    }
}

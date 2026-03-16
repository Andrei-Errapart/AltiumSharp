using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

public class SchDocUserFileRoundTripTest
{
    private static string? FindUserFile()
    {
        var current = Directory.GetCurrentDirectory();
        var root = Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "docs", "DC Power Systems_before.SchDoc");
        return File.Exists(path) ? path : null;
    }

    [SkippableFact]
    public void RoundTrip_UserFile_PreservesFileSize()
    {
        var path = FindUserFile();
        Skip.If(path == null, "User test file not available");

        var originalSize = new FileInfo(path!).Length;

        var doc = new SchDocReader().Read(File.OpenRead(path));

        using var ms = new MemoryStream();
        new SchDocWriter().Write(doc, ms);

        var delta = ms.Length - originalSize;
        var pct = delta * 100.0 / originalSize;

        // Output diagnostic info
        var output = $"Original: {originalSize}B, Round-trip: {ms.Length}B, Delta: {delta}B ({pct:+0.0;-0.0}%)";

        // Allow up to 5% size difference (compound file padding can vary)
        Assert.True(Math.Abs(pct) < 5.0, $"Size difference too large: {output}");
    }

    [SkippableFact]
    public void RoundTrip_UserFile_PreservesAllRecordCounts()
    {
        var path = FindUserFile();
        Skip.If(path == null, "User test file not available");

        var doc = (SchDocument)new SchDocReader().Read(File.OpenRead(path!));

        using var ms = new MemoryStream();
        new SchDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = (SchDocument)new SchDocReader().Read(ms);

        Assert.Equal(doc.Components.Count, rt.Components.Count);
        Assert.Equal(doc.Wires.Count, rt.Wires.Count);
        Assert.Equal(doc.Parameters.Count, rt.Parameters.Count);
        Assert.Equal(doc.NetLabels.Count, rt.NetLabels.Count);
        Assert.Equal(doc.Junctions.Count, rt.Junctions.Count);
        Assert.Equal(doc.PowerObjects.Count, rt.PowerObjects.Count);
        Assert.Equal(doc.Labels.Count, rt.Labels.Count);
        Assert.Equal(doc.Lines.Count, rt.Lines.Count);
        Assert.Equal(doc.Rectangles.Count, rt.Rectangles.Count);
        Assert.Equal(doc.Polygons.Count, rt.Polygons.Count);
        Assert.Equal(doc.Polylines.Count, rt.Polylines.Count);
        Assert.Equal(doc.Arcs.Count, rt.Arcs.Count);
        Assert.Equal(doc.NoErcs.Count, rt.NoErcs.Count);
        Assert.Equal(doc.Buses.Count, rt.Buses.Count);
        Assert.Equal(doc.BusEntries.Count, rt.BusEntries.Count);
        Assert.Equal(doc.Ports.Count, rt.Ports.Count);
        Assert.Equal(doc.SheetSymbols.Count, rt.SheetSymbols.Count);
        Assert.Equal(doc.SheetEntries.Count, rt.SheetEntries.Count);
        Assert.Equal(doc.Blankets.Count, rt.Blankets.Count);
        Assert.Equal(doc.ParameterSets.Count, rt.ParameterSets.Count);
        Assert.Equal(doc.OpaqueRecords.Count, rt.OpaqueRecords.Count);

        // Verify SheetSettings preserved
        Assert.Equal(doc.SheetSettings != null, rt.SheetSettings != null);
        if (doc.SheetSettings != null)
            Assert.Equal(doc.SheetSettings.Count, rt.SheetSettings!.Count);

        // Verify AdditionalStreams preserved
        Assert.Equal(doc.AdditionalStreams != null, rt.AdditionalStreams != null);
        if (doc.AdditionalStreams != null)
            Assert.Equal(doc.AdditionalStreams.Count, rt.AdditionalStreams!.Count);
    }

    [SkippableFact]
    public void RoundTrip_UserFile_PreservesComponentChildren()
    {
        var path = FindUserFile();
        Skip.If(path == null, "User test file not available");

        var doc = (SchDocument)new SchDocReader().Read(File.OpenRead(path!));

        using var ms = new MemoryStream();
        new SchDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = (SchDocument)new SchDocReader().Read(ms);

        Assert.Equal(doc.Components.Count, rt.Components.Count);

        for (int i = 0; i < doc.Components.Count; i++)
        {
            var orig = (SchComponent)doc.Components[i];
            var rtComp = (SchComponent)rt.Components[i];

            Assert.Equal(orig.Name, rtComp.Name);
            Assert.Equal(orig.Pins.Count, rtComp.Pins.Count);
            Assert.Equal(orig.Parameters.Count, rtComp.Parameters.Count);
            Assert.Equal(orig.Implementations.Count, rtComp.Implementations.Count);
            Assert.Equal(orig.Lines.Count, rtComp.Lines.Count);
            Assert.Equal(orig.Rectangles.Count, rtComp.Rectangles.Count);
            Assert.Equal(orig.Labels.Count, rtComp.Labels.Count);
            Assert.Equal(orig.Arcs.Count, rtComp.Arcs.Count);
            Assert.Equal(orig.Polygons.Count, rtComp.Polygons.Count);
            Assert.Equal(orig.Polylines.Count, rtComp.Polylines.Count);
        }
    }

    [SkippableFact]
    public void RoundTrip_UserFile_PreservesComponentLocations()
    {
        var path = FindUserFile();
        Skip.If(path == null, "User test file not available");

        var doc = (SchDocument)new SchDocReader().Read(File.OpenRead(path!));

        using var ms = new MemoryStream();
        new SchDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = (SchDocument)new SchDocReader().Read(ms);

        for (int i = 0; i < doc.Components.Count; i++)
        {
            var orig = (SchComponent)doc.Components[i];
            var rtComp = (SchComponent)rt.Components[i];

            Assert.Equal(orig.Location.X.ToRaw(), rtComp.Location.X.ToRaw());
            Assert.Equal(orig.Location.Y.ToRaw(), rtComp.Location.Y.ToRaw());
        }
    }

    [SkippableFact]
    public void RoundTrip_UserFile_PreservesImplementationDetails()
    {
        var path = FindUserFile();
        Skip.If(path == null, "User test file not available");

        var doc = (SchDocument)new SchDocReader().Read(File.OpenRead(path!));

        using var ms = new MemoryStream();
        new SchDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = (SchDocument)new SchDocReader().Read(ms);

        for (int i = 0; i < doc.Components.Count; i++)
        {
            var orig = (SchComponent)doc.Components[i];
            var rtComp = (SchComponent)rt.Components[i];

            for (int j = 0; j < orig.Implementations.Count; j++)
            {
                var oImpl = (SchImplementation)orig.Implementations[j];
                var rImpl = (SchImplementation)rtComp.Implementations[j];

                Assert.Equal(oImpl.ModelName, rImpl.ModelName);
                Assert.Equal(oImpl.ModelType, rImpl.ModelType);
                Assert.Equal(oImpl.DataFileKinds.Count, rImpl.DataFileKinds.Count);
                Assert.Equal(oImpl.DataFileEntities.Count, rImpl.DataFileEntities.Count);
                Assert.Equal(oImpl.MapDefiners.Count, rImpl.MapDefiners.Count);
            }
        }
    }
}

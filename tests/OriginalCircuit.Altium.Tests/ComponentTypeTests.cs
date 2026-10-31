using System.Globalization;
using OriginalCircuit.Altium.Models;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for the effective component type, which newer Altium versions store in
/// <c>COMPONENTKINDVERSION2</c> rather than the legacy <c>COMPONENTKIND</c> key.
/// </summary>
public sealed class ComponentTypeTests
{
    private static string TestDataPath()
    {
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        return Path.Combine(root, "TestData");
    }

    [SkippableTheory]
    [InlineData("DAC.SchDoc")]
    [InlineData("Overview.SchDoc")]
    [InlineData("SPI Isolator.SchDoc")]
    [InlineData("USB Power.SchDoc")]
    public async Task SchDoc_ComponentKindVersion2_MatchesRawRecord(string fileName)
    {
        var path = Path.Combine(TestDataPath(), fileName);
        Skip.IfNot(File.Exists(path), $"{fileName} not available.");

        var doc = (SchDocument)await AltiumLibrary.OpenSchDocAsync(path);
        Assert.NotNull(doc.ReadOrderedRecords);

        var checkedCount = 0;
        foreach (var record in doc.ReadOrderedRecords!)
        {
            if (record.Model is not SchComponent component)
                continue;
            var raw = record.OrderedParams
                .Where(kv => string.Equals(kv.Key, "COMPONENTKINDVERSION2", StringComparison.OrdinalIgnoreCase))
                .Select(kv => (int?)int.Parse(kv.Value, CultureInfo.InvariantCulture))
                .FirstOrDefault();
            Assert.Equal(raw, component.ComponentKindVersion2);
            checkedCount++;
        }

        Assert.True(checkedCount > 0);
        // Every one of these sheets has at least one "Standard (No BOM)" part (a test point or fiducial).
        Assert.Contains(doc.Components.Cast<SchComponent>(), c => c.ComponentType == ComponentType.StandardNoBom);
    }

    [Fact]
    public void ComponentType_PrefersVersion2_OverLegacyKind()
    {
        Assert.Equal(ComponentType.Standard, new SchComponent().ComponentType);
        Assert.Equal(ComponentType.Mechanical, new SchComponent { ComponentKind = 1 }.ComponentType);
        Assert.Equal(ComponentType.StandardNoBom, new SchComponent { ComponentKindVersion2 = 5 }.ComponentType);
        Assert.Equal(ComponentType.Jumper, new SchComponent { ComponentKind = 0, ComponentKindVersion2 = 6 }.ComponentType);
    }

    [Fact]
    public void SchDoc_ComponentKindVersion2_RoundTrips()
    {
        var doc = new SchDocument();
        doc.AddComponent(new SchComponent { Name = "TP", ComponentKindVersion2 = 5 });
        doc.AddComponent(new SchComponent { Name = "R", ComponentKind = 1 });

        using var ms = new MemoryStream();
        new SchDocWriter().Write(doc, ms);
        ms.Position = 0;
        var read = new SchDocReader().Read(ms);

        var tp = read.Components.Cast<SchComponent>().Single(c => c.Name == "TP");
        var r = read.Components.Cast<SchComponent>().Single(c => c.Name == "R");
        Assert.Equal(5, tp.ComponentKindVersion2);
        Assert.Equal(ComponentType.StandardNoBom, tp.ComponentType);
        Assert.Null(r.ComponentKindVersion2);
        Assert.Equal(ComponentType.Mechanical, r.ComponentType);
    }

    [Fact]
    public void SchLib_ComponentKindVersion2_RoundTrips()
    {
        var lib = new SchLibrary();
        lib.Add(new SchComponent { Name = "FIDUCIAL", ComponentKindVersion2 = 5 });

        using var ms = new MemoryStream();
        new SchLibWriter().Write(lib, ms);
        ms.Position = 0;
        var read = new SchLibReader().Read(ms);

        var component = (SchComponent)Assert.Single(read.Components);
        Assert.Equal(ComponentType.StandardNoBom, component.ComponentType);
    }

    [Theory]
    [InlineData(ComponentType.Standard, true)]
    [InlineData(ComponentType.Mechanical, true)]
    [InlineData(ComponentType.Graphical, false)]
    [InlineData(ComponentType.NetTieInBom, true)]
    [InlineData(ComponentType.NetTieNoBom, false)]
    [InlineData(ComponentType.StandardNoBom, false)]
    [InlineData(ComponentType.Jumper, true)]
    [InlineData((ComponentType)42, true)]
    public void IsInBom_FollowsAltiumKinds(ComponentType type, bool expected) =>
        Assert.Equal(expected, type.IsInBom());

    [Fact]
    public void PcbComponent_ComponentType_PrefersVersion2()
    {
        Assert.Equal(ComponentType.Standard, new PcbComponent().ComponentType);
        Assert.Equal(ComponentType.Graphical, new PcbComponent { ComponentKind = 2 }.ComponentType);
        Assert.Equal(ComponentType.StandardNoBom, new PcbComponent { ComponentKind = 0, ComponentKindVersion2 = 5 }.ComponentType);
    }
}

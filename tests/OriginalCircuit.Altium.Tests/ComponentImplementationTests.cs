using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for adding implementation links (footprint and other models) to a schematic component.
/// </summary>
public sealed class ComponentImplementationTests
{
    [Fact]
    public void AddedFootprintModel_IsWrittenAndReadBack()
    {
        var component = new SchComponent { Name = "RES", UniqueId = "RESISTOR" };
        component.AddParameter(new SchParameter { Name = "Designator", Value = "R1" });
        component.AddImplementation(new SchImplementation
        {
            ModelName = "RESC1005X40N",
            ModelType = "PCBLIB",
            Description = "0402 resistor",
            IsCurrent = true,
        });
        var document = new SchDocument();
        document.AddComponent(component);

        using var ms = new MemoryStream();
        new SchDocWriter().Write(document, ms);
        ms.Position = 0;
        var read = (SchComponent)new SchDocReader().Read(ms).Components.Single();

        var footprint = Assert.Single(read.Implementations);
        Assert.Equal("RESC1005X40N", footprint.ModelName);
        Assert.Equal("PCBLIB", footprint.ModelType);
        Assert.True(footprint.IsCurrent);
    }
}

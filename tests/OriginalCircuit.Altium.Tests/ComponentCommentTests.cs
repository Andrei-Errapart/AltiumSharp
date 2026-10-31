using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for a placed component's Comment (its "Comment" parameter, not the Design Item ID) and for
/// resolving Altium's "=Name" parameter references.
/// </summary>
public sealed class ComponentCommentTests
{
    private static string TestDataPath()
    {
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        return Path.Combine(root, "TestData");
    }

    private static SchComponent Component(string designator, params (string Name, string Value)[] parameters)
    {
        var component = new SchComponent { Name = "RES", DesignItemId = "RES-0402-10K" };
        component.AddParameter(new SchParameter { Name = "Designator", Value = designator });
        foreach (var (name, value) in parameters)
            component.AddParameter(new SchParameter { Name = name, Value = value });
        return component;
    }

    private static SchDocument RoundTrip(SchDocument document)
    {
        using var ms = new MemoryStream();
        new SchDocWriter().Write(document, ms);
        ms.Position = 0;
        return new SchDocReader().Read(ms);
    }

    [Fact]
    public void SchDoc_Comment_ComesFromCommentParameter_NotDesignItemId()
    {
        var doc = new SchDocument();
        doc.AddComponent(Component("R1", ("Comment", "=Value"), ("Value", "10k")));

        var component = (SchComponent)Assert.Single(RoundTrip(doc).Components);

        Assert.Equal("RES-0402-10K", component.DesignItemId);
        Assert.Equal("=Value", component.Comment);
        Assert.Equal("10k", component.GetResolvedComment());
        Assert.Equal("R1", component.Designator);
    }

    [Fact]
    public void SchDoc_Comment_IsNullWithoutCommentParameter()
    {
        var doc = new SchDocument();
        doc.AddComponent(Component("R1"));

        var component = (SchComponent)Assert.Single(RoundTrip(doc).Components);

        Assert.Null(component.Comment);
        Assert.Null(component.GetResolvedComment());
    }

    [SkippableTheory]
    [InlineData("DAC.SchDoc")]
    [InlineData("Fanout Buffer.SchDoc")]
    [InlineData("Level Shifter.SchDoc")]
    [InlineData("Overview.SchDoc")]
    [InlineData("Power Supplies.SchDoc")]
    [InlineData("Power Supply.SchDoc")]
    [InlineData("SPI Isolator.SchDoc")]
    [InlineData("USB Power.SchDoc")]
    public async Task RealSheets_Comment_IsTheCommentParameter_AndResolvesOnTheComponent(string fileName)
    {
        var path = Path.Combine(TestDataPath(), fileName);
        Skip.IfNot(File.Exists(path), $"{fileName} not available.");

        var doc = (SchDocument)await AltiumLibrary.OpenSchDocAsync(path);
        foreach (var component in doc.Components.Cast<SchComponent>())
        {
            var parameter = component.Parameters.FirstOrDefault(p => p.Name == "Comment");
            Assert.Equal(parameter?.Value, component.Comment);

            if (ParameterReference.TryGetReferencedName(component.Comment, out var name) &&
                component.FindParameter(name) is { } referenced)
            {
                Assert.Equal(referenced.Value, component.GetResolvedComment(doc));
            }
        }
    }

    [Fact]
    public void Resolve_FallsBackToSheetThenProjectParameters()
    {
        var sheet = new SchDocument();
        sheet.AddPrimitive(new SchParameter { Name = "Revision", Value = "B" });
        sheet.AddPrimitive(new SchParameter { Name = "Variant", Value = "Sheet value" });

        var project = AltiumLibrary.CreateProject();
        var section = new ProjectSection("Parameter1");
        section.Add("Name", "Variant");
        section.Add("Value", "Project value");
        project.Sections.Add(section);
        var section2 = new ProjectSection("Parameter2");
        section2.Add("Name", "Assembly");
        section2.Add("Value", "PCA-100");
        project.Sections.Add(section2);

        var component = Component("U1", ("Comment", "=Assembly"), ("Variant", "Component value"));

        Assert.Equal("Component value", component.ResolveParameterText("=Variant", sheet, project));
        Assert.Equal("B", component.ResolveParameterText("=Revision", sheet, project));
        Assert.Equal("PCA-100", component.GetResolvedComment(sheet, project));
        Assert.Equal("=Assembly", component.GetResolvedComment(sheet));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive_AndTrimsTheName()
    {
        var component = Component("R1", ("Comment", "= part number"), ("Part Number", "ERJ-2RKF1002X"));

        Assert.Equal("ERJ-2RKF1002X", component.GetResolvedComment());
        Assert.Equal("ERJ-2RKF1002X", component.GetResolvedParameterValue("COMMENT"));
    }

    [Fact]
    public void Resolve_FollowsChains_AndLeavesCyclesAndUnknownsUnchanged()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = "=B",
            ["B"] = "=C",
            ["C"] = "final",
            ["Loop1"] = "=Loop2",
            ["Loop2"] = "=Loop1",
            ["Dangling"] = "=Missing",
        };
        string? Lookup(string name) => values.TryGetValue(name, out var v) ? v : null;

        Assert.Equal("final", ParameterReference.Resolve("=A", Lookup));
        Assert.Equal("=Loop1", ParameterReference.Resolve("=Loop1", Lookup));
        Assert.Equal("=Dangling", ParameterReference.Resolve("=Dangling", Lookup));
        Assert.Equal("=Nope", ParameterReference.Resolve("=Nope", Lookup));
        Assert.Equal("10k", ParameterReference.Resolve("10k", Lookup));
        Assert.Equal("=", ParameterReference.Resolve("=", Lookup));
        Assert.Null(ParameterReference.Resolve(null, Lookup));
    }

    [Fact]
    public void TryGetReferencedName_RecognisesOnlyEqualsPrefixedNames()
    {
        Assert.True(ParameterReference.TryGetReferencedName("=Value", out var name));
        Assert.Equal("Value", name);
        Assert.False(ParameterReference.TryGetReferencedName("Value", out _));
        Assert.False(ParameterReference.TryGetReferencedName("=  ", out _));
        Assert.False(ParameterReference.TryGetReferencedName(null, out _));
    }
}

using System.Diagnostics;
using System.Text;
using OriginalCircuit.Altium.Models.Project;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests that large or crafted project files are read in time linear in their size: variants,
/// parameters and the project text itself.
/// </summary>
public sealed class ProjectScaleTests
{
    // ---- parameter references ----

    private static AltiumProject ProjectWithParameters(int count, Func<int, (string? Name, string? Value)> parameter)
    {
        var project = AltiumLibrary.CreateProject();
        for (var i = 1; i <= count; i++)
        {
            var section = new ProjectSection($"Parameter{i}");
            var (name, value) = parameter(i);
            if (name is not null)
                section.Add("Name", name);
            if (value is not null)
                section.Add("Value", value);
            project.Sections.Add(section);
        }
        return project;
    }

    [Fact]
    public void ResolvingEveryComment_IsLinear()
    {
        var project = ProjectWithParameters(100_000, i => ($"P{i}", $"V{i}"));
        project.AddDocument("Top.SchDoc");
        var sheet = HierarchyDesigns.Sheet("Top.SchDoc");
        for (var i = 1; i <= 20_000; i++)
        {
            var part = HierarchyDesigns.Part($"R{i}", $"R{i:D7}");
            part.AddParameter(new Models.Sch.SchParameter { Name = "Comment", Value = i % 2 == 0 ? "=Missing" : "=P99999" });
            sheet.AddComponent(part);
        }
        var hierarchy = ProjectHierarchy.Build(project, new[] { sheet });

        var watch = Stopwatch.StartNew();
        var comments = hierarchy.Components.Select(c => c.Comment).ToList();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.Equal(10_000, comments.Count(c => c == "=Missing"));
        Assert.Equal(10_000, comments.Count(c => c == "V99999"));
    }

    [Fact]
    public void AScope_ResolvesAsTheSheetAndProjectDo()
    {
        var random = new Random(20261105);
        string[] names = { "Value", "VALUE", "Part Number", "Rev", "rev", "X" };
        string?[] values = { null, "", "10k", "=Value", "=Rev", "=X", "=Missing", "=part number", "4u7" };
        for (var round = 0; round < 2_000; round++)
        {
            var project = ProjectWithParameters(random.Next(0, 6),
                _ => (random.Next(6) == 0 ? null : names[random.Next(names.Length)], values[random.Next(values.Length)]));
            var sheet = new Models.Sch.SchDocument();
            for (var i = random.Next(0, 4); i > 0; i--)
                sheet.AddPrimitive(new Models.Sch.SchParameter { Name = names[random.Next(names.Length)], Value = values[random.Next(1, values.Length)]! });
            var component = HierarchyDesigns.Part("R1", "RONEAAAA");
            for (var i = random.Next(0, 3); i > 0; i--)
                component.AddParameter(new Models.Sch.SchParameter { Name = names[random.Next(names.Length)], Value = values[random.Next(1, values.Length)]! });
            component.AddParameter(new Models.Sch.SchParameter { Name = "Comment", Value = values[random.Next(1, values.Length)]! });
            var scope = new Models.Sch.ParameterScope(sheet, project);

            Assert.Equal(component.GetResolvedComment(sheet, project), component.GetResolvedComment(scope));
            foreach (var name in names)
                Assert.Equal(component.GetResolvedParameterValue(name, sheet, project), component.GetResolvedParameterValue(name, scope));
            Assert.Equal(component.GetResolvedComment(null, project), component.GetResolvedComment(new Models.Sch.ParameterScope(null, project)));
            Assert.Equal(component.GetResolvedComment(sheet, project), component.GetResolvedComment(new Models.Sch.ParameterScope(null, project).WithSheet(sheet)));
        }
    }

    // ---- variants ----

    private static ProjectVariant Variant(params (string Key, string Value)[] entries)
    {
        var section = new ProjectSection("ProjectVariant1");
        foreach (var (key, value) in entries)
            section.Add(key, value);
        return new ProjectVariant(section);
    }

    // The rule as it was written before: every number from 1 to the count, each looked up in turn.
    private static List<string> VariationsAsBefore(ProjectVariant variant)
    {
        var result = new List<string>();
        var count = variant.Section.GetInt("VariationCount");
        for (var i = 1; i <= count; i++)
        {
            if (variant.Section.Get($"Variation{i}") is { } raw)
                result.Add(raw);
        }
        return result;
    }

    private static List<string> ParameterVariationsAsBefore(ProjectVariant variant)
    {
        var result = new List<string>();
        var count = variant.Section.GetInt("ParamVariationCount");
        for (var i = 1; i <= count; i++)
        {
            if (variant.Section.Get($"ParamVariation{i}") is { } raw)
                result.Add((variant.Section.Get($"ParamDesignator{i}") ?? string.Empty) + "|" + raw);
        }
        return result;
    }

    [Fact]
    public void ManyVariations_AreReadInLinearTime()
    {
        const int Count = 100_000;
        var entries = new List<(string, string)> { ("VariationCount", Count.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        for (var i = 1; i <= Count; i++)
            entries.Add(($"Variation{i}", $"Designator=R{i}|UniqueId=\\RONE{i}|Kind=1|AlternatePart="));
        var variant = Variant(entries.ToArray());

        var watch = Stopwatch.StartNew();
        var variations = variant.Variations;

        Assert.Equal(Count, variations.Count);
        Assert.Equal("R1", variations[0].Designator);
        Assert.Equal($"R{Count}", variations[^1].Designator);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [Fact]
    public void AHugeVariationCount_DoesNotLoopToIt()
    {
        var variant = Variant(("VariationCount", "2147483647"), ("Variation7", "Designator=R7|Kind=1"),
            ("ParamVariationCount", "2147483647"), ("ParamVariation3", "ParameterName=Value|VariantValue=10k"), ("ParamDesignator3", "R3"));

        var watch = Stopwatch.StartNew();
        Assert.Equal("R7", Assert.Single(variant.Variations).Designator);
        Assert.Equal("R3", Assert.Single(variant.ParameterVariations).Designator);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Variations_AreTheOnesTheNumberedLookupGave()
    {
        var random = new Random(20261105);
        string[] keys = { "Variation", "variation", "VARIATION", "ParamVariation", "paramvariation", "ParamDesignator", "PARAMDESIGNATOR" };
        string[] numbers = { "1", "2", "3", "4", "7", "10", "01", "+2", "-1", "0", "2147483647", "99999999999", " 3", "3 ", "x" };
        for (var round = 0; round < 2_000; round++)
        {
            var entries = new List<(string, string)>
            {
                ("VariationCount", random.Next(-1, 12).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("ParamVariationCount", random.Next(-1, 12).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            };
            for (var i = random.Next(0, 15); i > 0; i--)
                entries.Insert(random.Next(entries.Count + 1),
                    (keys[random.Next(keys.Length)] + numbers[random.Next(numbers.Length)], $"Designator=D{round}_{i}|ParameterName=P{i}"));
            var variant = Variant(entries.ToArray());

            Assert.Equal(VariationsAsBefore(variant), variant.Variations.Select(v => string.Join("|", v.Fields.Select(f => $"{f.Key}={f.Value}"))).ToList());
            Assert.Equal(ParameterVariationsAsBefore(variant),
                variant.ParameterVariations.Select(v => v.Designator + "|" + string.Join("|", v.Fields.Select(f => $"{f.Key}={f.Value}"))).ToList());
        }
    }
}

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
    // ---- the project text ----

    private static string ProjectText(int junkKeys)
    {
        var sb = new StringBuilder("[Design]\r\nVersion=1.0\r\n\r\n[Document1]\r\nDocumentPath=Top.SchDoc\r\n\r\n[Junk]\r\n");
        for (var i = 0; i < junkKeys; i++)
            sb.Append("Key").Append(i.ToString("D7", System.Globalization.CultureInfo.InvariantCulture)).Append('=').Append('v', 40).Append("\r\n");
        return sb.ToString();
    }

    [Fact]
    public void ALargeProjectFile_IsParsedWithoutCopiesOfIt()
    {
        var bytes = Encoding.UTF8.GetBytes(ProjectText(200_000));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var project = new Serialization.Readers.PrjPcbReader().Read(new MemoryStream(bytes));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(200_000, project.GetSection("Junk")!.Entries.Count);
        Assert.Equal("Top.SchDoc", Assert.Single(project.Documents).DocumentPath);
        // The bytes, the text (UTF-16) and the entries it holds; no array of lines or trimmed copies.
        Assert.True(allocated < bytes.Length * 7L, $"allocated {allocated:N0} bytes for {bytes.Length:N0}");
    }

    [Fact]
    public void ALargeStructureFile_IsParsedWithoutCopiesOfIt()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 100_000; i++)
            sb.Append("Record=SheetSymbol|ProjectPath=ACME-CTRL01|SourceDocument=Top.SchDoc|Designator=U").Append(i).Append("|FileName=Sheet.SchDoc\r\n");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var structure = ProjectStructure.Parse(new MemoryStream(bytes));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(100_000, structure.Records.Count);
        Assert.Equal("U99999", structure.Records[^1].Designator);
        Assert.True(allocated < bytes.Length * 9L, $"allocated {allocated:N0} bytes for {bytes.Length:N0}");
    }

    [Theory]
    [InlineData("[A]\r\nk=v\r\n\r\n[B]\nx\n=y\r\r\nlast=1")]
    [InlineData("﻿[A]\r\nk==v\r\n")]
    [InlineData("before\r\n[A]\r\n")]
    [InlineData("")]
    [InlineData("[A]\r\n\r\n\r\n")]
    public void ProjectText_IsParsedAsBefore(string text)
    {
        var project = new Serialization.Readers.PrjPcbReader().Read(text);

        var expected = new List<string>();
        string? section = null;
        foreach (var raw in (text.Length > 0 && text[0] == '﻿' ? text[1..] : text).Split('\n'))
        {
            var line = raw.Length > 0 && raw[^1] == '\r' ? raw[..^1] : raw;
            if (line.Length == 0)
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                expected.Add("[" + (section = line[1..^1]) + "]");
                continue;
            }
            if (section is null)
                expected.Add("[" + (section = string.Empty) + "]");
            var eq = line.IndexOf('=');
            expected.Add(eq < 0 ? line + "=" : line[..eq] + "=" + line[(eq + 1)..]);
        }
        var actual = project.Sections.SelectMany(s => new[] { "[" + s.Name + "]" }.Concat(s.Entries.Select(e => e.Key + "=" + e.Value))).ToList();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Record=TopLevelDocument|FileName=Top.SchDoc\r\nRecord=SheetSymbol|A|B==C\r\r\n\r\n")]
    [InlineData("﻿Record=X\nRecord=Y")]
    [InlineData("|||\r\n")]
    public void StructureText_IsParsedAsBefore(string text)
    {
        var structure = ProjectStructure.Parse(text);

        var expected = new List<string>();
        foreach (var raw in (text[0] == '﻿' ? text[1..] : text).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            expected.Add(string.Join("|", line.Split('|').Select(s => s.IndexOf('=') is var eq and >= 0 ? s[..eq] + "=" + s[(eq + 1)..] : s + "=")));
        }
        Assert.Equal(expected, structure.Records.Select(r => string.Join("|", r.Fields.Select(f => f.Key + "=" + f.Value))).ToList());
    }

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

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

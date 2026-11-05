using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Models.Sch;
using static OriginalCircuit.Altium.Tests.HierarchyDesigns;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests that a small design cannot make the hierarchy build gigabytes of strings: unique ids,
/// designators, channel names and the designator format are bounded by
/// <see cref="ProjectHierarchyOptions"/>, and instance paths are built only when read.
/// </summary>
public sealed class HierarchyLimitTests
{
    private static SchDocument Channel(int parts, Func<int, string>? designator = null, Func<int, string>? uid = null) =>
        Sheet("Channel.SchDoc", Enumerable.Range(1, parts)
            .Select(n => (object)Part(designator?.Invoke(n) ?? $"R{n}", uid?.Invoke(n) ?? $"RES{n:D5}")).ToArray());

    private static (ProjectHierarchy Hierarchy, long Allocated) Build(params SchDocument[] sheets) => Build(null, sheets);

    private static (ProjectHierarchy Hierarchy, long Allocated) Build(AltiumProject? project, params SchDocument[] sheets)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var hierarchy = ProjectHierarchy.Build(project, sheets);
        return (hierarchy, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ASymbolWithALongUniqueId_IsNotFollowed()
    {
        var top = Sheet("Top.SchDoc", Symbol("Repeat(CH,1,1999)", "Channel.SchDoc", "S" + new string('Y', 40_000)));

        var (hierarchy, allocated) = Build(top, Channel(50));

        Assert.Single(hierarchy.Sheets);
        Assert.Empty(hierarchy.Parts);
        Assert.True(hierarchy.IsTruncated);
        var error = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.MaxUniqueIdLengthExceeded);
        Assert.NotNull(error.SheetSymbol);
        Assert.True(error.Message.Length < 400);
        Assert.True(allocated < 20_000_000, $"allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ComponentsWithLongUniqueIds_AreLeftOut()
    {
        var top = Sheet("Top.SchDoc", Symbol("Repeat(CH,1,1999)", "Channel.SchDoc", "SYMCHANL"));
        var channel = Channel(2, uid: n => n == 1 ? new string('U', 40_000) : "RTWOAAAA");

        var (hierarchy, allocated) = Build(top, channel);

        Assert.Equal(1999, hierarchy.Parts.Count);
        Assert.All(hierarchy.Parts, p => Assert.Equal("R2", p.LogicalDesignator));
        Assert.True(hierarchy.IsTruncated);
        Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.MaxUniqueIdLengthExceeded);
        Assert.True(allocated < 100_000_000, $"allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ComponentsWithLongDesignators_AreLeftOut()
    {
        var top = Sheet("Top.SchDoc", Symbol("Repeat(CH,1,1999)", "Channel.SchDoc", "SYMCHANL"));
        var channel = Channel(50, designator: n => $"R{new string('A', 10_000)}{n}");

        var (hierarchy, allocated) = Build(top, channel);

        Assert.Empty(hierarchy.Parts);
        Assert.True(hierarchy.IsTruncated);
        Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.MaxDesignatorLengthExceeded);
        Assert.True(allocated < 20_000_000, $"allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ASymbolWithALongChannelName_IsNotFollowed()
    {
        var top = Sheet("Top.SchDoc", Symbol($"Repeat({new string('C', 10_000)},1,1999)", "Channel.SchDoc", "SYMCHANL"));

        var (hierarchy, allocated) = Build(top, Channel(50));

        Assert.Single(hierarchy.Sheets);
        Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.MaxRoomNameLengthExceeded);
        Assert.True(hierarchy.IsTruncated);
        Assert.True(allocated < 20_000_000, $"allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ALongDesignatorFormat_IsReplacedByTheDefault()
    {
        var project = AltiumLibrary.CreateProject();
        project.Design.Section.Set("ChannelDesignatorFormatString", "$Component_" + new string('X', 10_000));
        var top = Sheet("Top.SchDoc", Symbol("Repeat(CH,1,2)", "Channel.SchDoc", "SYMCHANL"));

        var (hierarchy, _) = Build(project, top, Channel(1));

        Assert.Equal(new[] { "R1_CH1", "R1_CH2" }, Designators(hierarchy));
        Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.MaxDesignatorLengthExceeded);
        Assert.False(hierarchy.IsTruncated);
    }

    [Fact]
    public void InstancePaths_AreBuiltOnlyWhenRead()
    {
        // A 62-level chain of symbols with 200-character unique ids, ending in a Repeat of 1,937 channels.
        var sheets = new List<SchDocument>();
        for (var level = 0; level < 62; level++)
            sheets.Add(Sheet($"L{level}.SchDoc", Symbol($"U{level}", $"L{level + 1}.SchDoc", new string((char)('A' + level % 26), 200))));
        sheets.Add(Sheet("L62.SchDoc", Symbol("Repeat(CH,1,1937)", "Channel.SchDoc", "SYMCHANL")));
        sheets.Add(Channel(10));
        var options = new ProjectHierarchyOptions { MaxDepth = 100 };

        var before = GC.GetAllocatedBytesForCurrentThread();
        var hierarchy = ProjectHierarchy.Build(null, sheets, options);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(19_370, hierarchy.Parts.Count);
        Assert.False(hierarchy.IsTruncated);
        Assert.True(allocated < 100_000_000, $"allocated {allocated:N0} bytes");
        var part = hierarchy.Parts[^1];
        Assert.Equal(62 * 201 + 13, part.Sheet.UniqueIdPath.Length);
        Assert.Same(part.Owner, hierarchy.FindComponentByUniqueIdPath(part.UniqueIdPath));
        Assert.Same(part.Sheet, hierarchy.FindSheetByUniqueIdPath(part.Sheet.UniqueIdPath.ToLowerInvariant()));
        Assert.EndsWith(@"\U61\CH1937", part.Sheet.HierarchicalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_RefuseLengthsThatAreNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectHierarchyOptions { MaxUniqueIdLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectHierarchyOptions { MaxDesignatorLength = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectHierarchyOptions { MaxRoomNameLength = 0 });
    }
}

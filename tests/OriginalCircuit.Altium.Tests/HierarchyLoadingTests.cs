using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Altium.Models.Project;
using static OriginalCircuit.Altium.Tests.HierarchyDesigns;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for how <see cref="ProjectHierarchy.LoadAsync(AltiumProject, ProjectHierarchyOptions?, CancellationToken)"/>
/// and its stream overloads find the sheets a design uses. All designs are synthetic.
/// </summary>
public sealed class HierarchyLoadingTests
{
    private static Func<string, Stream?> Open(Dictionary<string, byte[]> files, List<string>? asked = null) => path =>
    {
        asked?.Add(path);
        return files.TryGetValue(ProjectHierarchy.NormalizePath(path)!, out var bytes) ? new MemoryStream(bytes) : null;
    };

    // ---- every path of a name is tried until one opens ----

    [Fact]
    public async Task AListedSheetThatIsMissing_DoesNotStopASymbolsPathOfItsName()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("PB", @"..\Other\Power.SchDoc", "SYMPOWER"))),
            (@"..\Other\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))));
        var asked = new List<string>();

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc", "Power.SchDoc"), Open(files, asked));

        Assert.Equal(new[] { "Top.SchDoc", "Power.SchDoc", @"..\Other\Power.SchDoc" }, asked);
        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Equal("Power.SchDoc", Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.DocumentNotSupplied).DocumentName);
        Assert.Equal(new[] { "Power.SchDoc" }, hierarchy.MissingDocuments);
    }

    [Fact]
    public async Task ASymbolsPathThatIsMissing_DoesNotStopALaterSymbolsPathOfItsName()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PA", @"A\Power.SchDoc", "SYMPOWRA", xMils: 1000),
                Symbol("PB", @"B\Power.SchDoc", "SYMPOWRB", xMils: 3000))),
            (@"B\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))));
        var asked = new List<string>();

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), Open(files, asked));

        Assert.Equal(new[] { "Top.SchDoc", @"A\Power.SchDoc", @"B\Power.SchDoc" }, asked);
        Assert.Contains("Power.SchDoc", hierarchy.Documents.Keys);
        Assert.Empty(hierarchy.MissingDocuments);
    }

    [Fact]
    public async Task ListedPathsOfOneName_AreTriedUntilOneOpens()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("MAIN", "Main.SchDoc", "SYMMAINA"))),
            (@"Rev B\Main.SchDoc", Sheet("Main.SchDoc", Part("R2", "RTWOAAAA"))));
        var asked = new List<string>();

        var hierarchy = await ProjectHierarchy.LoadAsync(
            Project("Top.SchDoc", @"Rev A\Main.SchDoc", @"Rev B\Main.SchDoc"), Open(files, asked));

        Assert.Equal(new[] { "Top.SchDoc", @"Rev A\Main.SchDoc", @"Rev B\Main.SchDoc" }, asked);
        Assert.Equal(new[] { "R2" }, Designators(hierarchy));
        Assert.Equal(@"Rev A\Main.SchDoc", Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.DocumentNotSupplied).DocumentName);
        Assert.Contains("only one is read", Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.DuplicateDocumentName).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathThatGaveNothing_IsAskedForOnce()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("A1", "Gone.SchDoc", "SYMGONE1", xMils: 1000),
                Symbol("A2", "gone.schdoc", "SYMGONE2", xMils: 3000),
                Symbol("A3", @".\Gone.SchDoc", "SYMGONE3", xMils: 5000))));
        var asked = new List<string>();

        await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), Open(files, asked));

        Assert.Equal(new[] { "Top.SchDoc", "Gone.SchDoc" }, asked);
    }
}

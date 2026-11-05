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

    // ---- the disk loader knows a sheet by its file ----

    [Fact]
    public async Task TwoFilesOfOneNameInDifferentFolders_AreTwoSheets()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PA", @"A\Power.SchDoc", "SYMPOWRA", xMils: 1000),
                Symbol("PB", @"B\Power.SchDoc", "SYMPOWRB", xMils: 3000))),
            (@"A\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))),
            (@"B\Power.SchDoc", Sheet("Power.SchDoc", Part("C1", "CONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "C1", "R1" }, Designators(hierarchy));
        Assert.All(hierarchy.Sheets, s => Assert.False(s.IsMultiChannel));
        Assert.Equal(3, hierarchy.DocumentsByKey.Count);
        Assert.Equal(new[] { "Top.SchDoc", "Power.SchDoc" }, hierarchy.Documents.Keys.ToArray());
        var pa = hierarchy.Sheets.Single(s => s.RoomName == "PA");
        var pb = hierarchy.Sheets.Single(s => s.RoomName == "PB");
        Assert.Equal(Path.Combine(folder.Root, "A", "Power.SchDoc"), pa.DocumentKey, ignoreCase: true);
        Assert.Equal(Path.Combine(folder.Root, "B", "Power.SchDoc"), pb.DocumentKey, ignoreCase: true);
        Assert.Same(hierarchy.DocumentsByKey[pb.DocumentKey], pb.Document);
        var shared = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SharedFileName);
        Assert.Equal(@"B\Power.SchDoc", shared.DocumentName);
        Assert.DoesNotContain(hierarchy.Diagnostics, d => d.Severity >= Eda.Enums.DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task SheetsNextToTwoSameNamedParents_AreEachTheirParentsOwn()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("MA", @"A\Mid.SchDoc", "SYMMIDAA", xMils: 1000),
                Symbol("MB", @"B\Mid.SchDoc", "SYMMIDBB", xMils: 3000))),
            (@"A\Mid.SchDoc", Sheet("Mid.SchDoc", Symbol("LEAF", "Leaf.SchDoc", "SYMLEAFA"))),
            (@"B\Mid.SchDoc", Sheet("Mid.SchDoc", Symbol("LEAF", "Leaf.SchDoc", "SYMLEAFB"))),
            (@"A\Leaf.SchDoc", Sheet("Leaf.SchDoc", Part("R1", "RONEAAAA"))),
            (@"B\Leaf.SchDoc", Sheet("Leaf.SchDoc", Part("C1", "CONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "C1", "R1" }, Designators(hierarchy));
        Assert.Equal(5, hierarchy.Sheets.Count);
    }

    [Fact]
    public async Task ASymbolWhosePathLeadsNowhere_PlacesNothing_ThoughAnotherPathOfItsNameOpens()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PA", "Power.SchDoc", "SYMPOWRA", xMils: 1000),
                Symbol("PB", @"B\Power.SchDoc", "SYMPOWRB", xMils: 3000))),
            (@"B\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        var missing = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SubSheetNotSupplied);
        Assert.Equal("PA", missing.SheetSymbol!.SheetName);
        Assert.Equal("PB", hierarchy.Sheets.Single(s => s.Parent is not null).RoomName);
    }

    [Fact]
    public async Task AnotherFileOfAnAncestorsName_IsNoCycle()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("PA", @"A\Power.SchDoc", "SYMPOWRA"))),
            (@"A\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"), Symbol("PB", @"..\B\Power.SchDoc", "SYMPOWRB"))),
            (@"B\Power.SchDoc", Sheet("Power.SchDoc", Part("C1", "CONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "C1", "R1" }, Designators(hierarchy));
        Assert.DoesNotContain(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetCycle);
        Assert.Equal(3, hierarchy.Sheets[2].Depth + 1);
    }

    [Fact]
    public async Task AnUnlistedFileOfAListedAncestorsName_IsTheListedOne_SoACycle()
    {
        // Altium matches the symbol to the project's document of that name, so it reports this cycle too.
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc", Part("R1", "RONEAAAA"), Symbol("SUB", @"Sub\Top.SchDoc", "SYMSUBAA"))),
            (@"Sub\Top.SchDoc", Sheet("Top.SchDoc", Part("R5", "RFIVEAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetCycle);
        Assert.Single(hierarchy.DocumentsByKey);
    }

    [Fact]
    public async Task ASymbolOfAListedDocumentsName_PlacesTheListedDocument()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("PWR", "Power.SchDoc", "SYMPOWER"))),
            (@"Sheets\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))),
            ("Power.SchDoc", Sheet("Power.SchDoc", Part("C9", "COLDCOPY"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc", @"Sheets\Power.SchDoc"));

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Equal(2, hierarchy.DocumentsByKey.Count);
    }

    [Fact]
    public async Task ListedDocumentsOfOneName_AreEachRead_AndASymbolPlacesTheOneItsPathLeadsTo()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("MAIN", @"Rev B\Main.SchDoc", "SYMMAINA"))),
            (@"Rev A\Main.SchDoc", Sheet("Main.SchDoc", Part("R1", "RONEAAAA"))),
            (@"Rev B\Main.SchDoc", Sheet("Main.SchDoc", Part("R2", "RTWOAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(
            folder.Project("Top.SchDoc", @"Rev A\Main.SchDoc", @"Rev B\Main.SchDoc"));

        Assert.Equal(new[] { "R1", "R2" }, Designators(hierarchy));
        Assert.Equal(2, hierarchy.Roots.Count);
        Assert.Equal("MAIN", hierarchy.FindComponent("R2")!.Sheet.RoomName);
        Assert.DoesNotContain(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.DuplicateDocumentName);
        Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SharedFileName);
    }

    [Fact]
    public async Task ASheetIsMultiChannel_WhenItsOwnFileIsPlacedMoreThanOnce()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PA1", @"A\Power.SchDoc", "SYMPOWA1", xMils: 1000),
                Symbol("PA2", @"A\Power.SchDoc", "SYMPOWA2", xMils: 3000),
                Symbol("PB", @"B\Power.SchDoc", "SYMPOWRB", xMils: 5000))),
            (@"A\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))),
            (@"B\Power.SchDoc", Sheet("Power.SchDoc", Part("C1", "CONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "C1", "R1_PA1", "R1_PA2" }, Designators(hierarchy));
    }

    // ---- the disk loader resolves paths as Windows does, on every platform ----

    [Fact]
    public async Task ASheetFoundThroughADotFolderPath_LooksForItsOwnSheetsInItsFolder()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("MID", @"Sheets\Mid.SchDoc\.", "SYMMIDAA"))),
            (@"Sheets\Mid.SchDoc", Sheet("Mid.SchDoc", Symbol("LEAF", "Leaf.SchDoc", "SYMLEAFA"))),
            (@"Sheets\Leaf.SchDoc", Sheet("Leaf.SchDoc", Part("R1", "RONEAAAA"))),
            ("Leaf.SchDoc", Sheet("Leaf.SchDoc", Part("C1", "COLDCOPY"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Equal(Path.Combine(folder.Root, "Sheets", "Mid.SchDoc"), hierarchy.Sheets[1].DocumentKey, ignoreCase: true);
    }

    [Fact]
    public async Task PathsWithDotFoldersTrailingSpacesAndOtherCase_FindTheirFiles()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PA", @"Sheets.\Power.SchDoc", "SYMPOWRA", xMils: 1000),
                Symbol("PB", "Amp.SchDoc ", "SYMAMPBB", xMils: 3000),
                Symbol("PC", "filter.schdoc", "SYMFILTC", xMils: 5000),
                Symbol("PD", @"Sheets\Old\..\Extra.SchDoc.", "SYMEXTRD", xMils: 7000))),
            (@"Sheets\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))),
            ("Amp.SchDoc", Sheet("Amp.SchDoc", Part("C1", "CONEAAAA"))),
            ("Filter.SchDoc", Sheet("Filter.SchDoc", Part("L1", "LONEAAAA"))),
            (@"Sheets\Extra.SchDoc", Sheet("Extra.SchDoc", Part("D1", "DONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        Assert.Equal(new[] { "C1", "D1", "L1", "R1" }, Designators(hierarchy));
        Assert.Empty(hierarchy.MissingDocuments);
        Assert.Equal(Path.Combine(folder.Root, "Sheets", "Power.SchDoc"), hierarchy.Sheets.Single(s => s.RoomName == "PA").DocumentKey, ignoreCase: true);
        Assert.Equal(Path.Combine(folder.Root, "Amp.SchDoc"), hierarchy.Sheets.Single(s => s.RoomName == "PB").DocumentKey, ignoreCase: true);
    }

    [Fact]
    public void FilesAreFoundIgnoringCase_SegmentBySegment()
    {
        using var folder = new Folder((@"Sheets\Filter.SchDoc", Sheet("Filter.SchDoc")));

        var found = ProjectHierarchy.FindIgnoringCase(Path.Combine(folder.Root, "SHEETS", "filter.schdoc"));

        Assert.NotNull(found);
        Assert.Equal(Path.Combine(folder.Root, "Sheets", "Filter.SchDoc"), found, ignoreCase: true);
        Assert.Null(ProjectHierarchy.FindIgnoringCase(Path.Combine(folder.Root, "Other", "filter.schdoc")));
    }
}

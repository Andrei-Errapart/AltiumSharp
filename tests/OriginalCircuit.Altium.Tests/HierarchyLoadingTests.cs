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

    // ---- the stream loader that is told which sheet asks ----

    // An upload: paths from its root, looked up next to the asking sheet, then at the root.
    private sealed class Upload(Dictionary<string, byte[]> files)
    {
        public List<string> Asked { get; } = new();
        public List<string> AskedByName { get; } = new();
        public int Opened { get; private set; }

        public (Stream? Stream, string? Location) Open(string path, string? usedBy)
        {
            Asked.Add(usedBy is null ? path : $"{path} <- {usedBy}");
            var folder = usedBy is null ? null : usedBy[..(usedBy.LastIndexOf('\\') + 1)];
            foreach (var candidate in new[] { folder is null ? null : folder + path, path })
            {
                if (candidate is not null && ProjectHierarchy.NormalizePath(candidate) is { } at && files.TryGetValue(at, out var bytes))
                {
                    Opened++;
                    return (new MemoryStream(bytes), at);
                }
            }
            return (null, null);
        }

        public (Stream? Stream, string? Location) OpenByName(string name, string path, string? usedBy)
        {
            AskedByName.Add(name);
            var at = files.Keys.FirstOrDefault(k => string.Equals(ProjectHierarchy.NormalizeFileName(k), name, StringComparison.OrdinalIgnoreCase));
            return at is null ? (null, null) : (new MemoryStream(files[at]), at);
        }
    }

    [Fact]
    public async Task ASymbolsPath_IsTakenFromTheSheetThatHoldsIt()
    {
        var upload = new Upload(Files(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("SUB", @"Sheets\Sub.SchDoc", "SYMSUBAA"))),
            (@"Sheets\Sub.SchDoc", Sheet("Sub.SchDoc", Symbol("PWR", "Power.SchDoc", "SYMPOWER"))),
            (@"Sheets\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))),
            ("Power.SchDoc", Sheet("Power.SchDoc", Part("C9", "COLDCOPY")))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc", @"Sheets\Sub.SchDoc"), upload.Open);

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Contains(@"Power.SchDoc <- Sheets\Sub.SchDoc", upload.Asked);
        Assert.Equal(new[] { "Top.SchDoc", @"Sheets\Sub.SchDoc", @"Sheets\Power.SchDoc" }, hierarchy.Sheets.Select(s => s.DocumentKey).ToArray());
        Assert.Equal("Power.SchDoc", hierarchy.Sheets[2].FileName);
    }

    [Fact]
    public async Task TwoPathsToOneLocation_ReadItOnce()
    {
        var upload = new Upload(Files(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("SUB", @"Sub\Sub.SchDoc", "SYMSUBAA", xMils: 1000),
                Symbol("PA", @"Sheets\Power.SchDoc", "SYMPOWRA", xMils: 3000))),
            (@"Sub\Sub.SchDoc", Sheet("Sub.SchDoc", Symbol("PB", @"..\Sheets\Power.SchDoc", "SYMPOWRB"))),
            (@"Sheets\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA")))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), upload.Open);

        Assert.Equal(new[] { "R1_PA", "R1_PB" }, Designators(hierarchy));
        Assert.Equal(3, hierarchy.DocumentsByKey.Count);
        Assert.Equal(4, upload.Opened);
    }

    [Fact]
    public async Task ANameNoneOfWhosePathsOpened_IsLookedUpByFileName_OnceNoPathIsLeft()
    {
        var upload = new Upload(Files(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PWR", @"Gone\Power.SchDoc", "SYMPOWER", xMils: 1000),
                Symbol("AMP", "Amp.SchDoc", "SYMAMPAA", xMils: 3000))),
            ("Amp.SchDoc", Sheet("Amp.SchDoc", Part("C1", "CONEAAAA"))),
            (@"Elsewhere\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"), Symbol("FLT", "Filter.SchDoc", "SYMFILTR"))),
            (@"Elsewhere\Filter.SchDoc", Sheet("Filter.SchDoc", Part("L1", "LONEAAAA")))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), upload.Open, upload.OpenByName);

        Assert.Equal(new[] { "C1", "L1", "R1" }, Designators(hierarchy));
        Assert.Equal(new[] { "Power.SchDoc" }, upload.AskedByName);
        Assert.Equal(new[] { "Top.SchDoc", @"Gone\Power.SchDoc <- Top.SchDoc", "Amp.SchDoc <- Top.SchDoc", @"Filter.SchDoc <- Elsewhere\Power.SchDoc" },
            upload.Asked);
        var byName = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetFoundByFileName);
        Assert.Equal("PWR", byName.SheetSymbol!.SheetName);
        Assert.Equal(@"Gone\Power.SchDoc", byName.DocumentName);
        Assert.Empty(hierarchy.MissingDocuments);
    }

    [Fact]
    public async Task ANameWithAPathThatOpened_IsNotLookedUpByFileName()
    {
        var upload = new Upload(Files(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PA", @"Gone\Power.SchDoc", "SYMPOWRA", xMils: 1000),
                Symbol("PB", @"B\Power.SchDoc", "SYMPOWRB", xMils: 3000))),
            (@"B\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))),
            (@"Elsewhere\Power.SchDoc", Sheet("Power.SchDoc", Part("C9", "COLDCOPY")))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), upload.Open, upload.OpenByName);

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Empty(upload.AskedByName);
        Assert.Equal("PA", Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SubSheetNotSupplied).SheetSymbol!.SheetName);
    }

    [Fact]
    public async Task AProjectDocumentFoundByFileName_IsNotMissing()
    {
        var upload = new Upload(Files(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("PWR", @"Sheets\Power.SchDoc", "SYMPOWER"))),
            (@"Elsewhere\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA")))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc", @"Sheets\Power.SchDoc", "Gone.SchDoc"),
            upload.Open, upload.OpenByName);

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Equal(new[] { "Gone.SchDoc" }, hierarchy.MissingDocuments);
        Assert.Equal("Gone.SchDoc", Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.DocumentNotSupplied).DocumentName);
        var byName = hierarchy.Diagnostics.Where(d => d.Code == AltiumDiagnosticCode.SheetFoundByFileName).ToList();
        Assert.Equal(2, byName.Count);
        Assert.Null(byName[0].SheetSymbol);
        Assert.Equal(@"Sheets\Power.SchDoc", byName[0].DocumentName);
        Assert.Equal("PWR", byName[1].SheetSymbol!.SheetName);
        Assert.Equal(new[] { "Power.SchDoc", "Gone.SchDoc" }, upload.AskedByName);
    }

    [Fact]
    public async Task WithoutALocation_ADocumentIsKnownByItsFileName()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PA", @"A\Power.SchDoc", "SYMPOWRA", xMils: 1000),
                Symbol("PB", @"B\Power.SchDoc", "SYMPOWRB", xMils: 3000))),
            (@"A\Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"),
            (path, _) => files.TryGetValue(ProjectHierarchy.NormalizePath(path)!, out var bytes) ? (new MemoryStream(bytes), null) : (null, null));

        Assert.Equal(new[] { "R1_PA", "R1_PB" }, Designators(hierarchy));
        Assert.Equal(new[] { "Top.SchDoc", "Power.SchDoc" }, hierarchy.DocumentsByKey.Keys.ToArray());
    }

    // ---- each sheet placed once (flat and global scope) ----

    private static readonly ProjectHierarchyOptions PlacedOnce = new() { PlaceSheetsOnce = true };

    [Fact]
    public void PlaceSheetsOnce_SkipsASecondPlainSymbolOfTheSameSheet()
    {
        // An overview page placing the working sheet a second time for navigation.
        var top = Sheet("Top.SchDoc",
            Symbol("PWR", "Power.SchDoc", "SYMPWRAA", xMils: 1000),
            Symbol("PWR_OVERVIEW", "Power.SchDoc", "SYMPWRBB", xMils: 3000));
        var power = Sheet("Power.SchDoc", Part("R1", "RONEAAAA"));

        var cloned = ProjectHierarchy.Build(Project("Top.SchDoc", "Power.SchDoc"), new[] { top, power });
        Assert.Equal(new[] { "R1_PWR", "R1_PWR_OVERVIEW" }, Designators(cloned));

        var once = ProjectHierarchy.Build(Project("Top.SchDoc", "Power.SchDoc"), new[] { top, power }, PlacedOnce);

        Assert.Equal(new[] { "R1" }, Designators(once));
        Assert.Equal(2, once.Sheets.Count);
        Assert.False(once.Sheets[1].IsMultiChannel);
        var skipped = Assert.Single(once.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetReferencedAgain);
        Assert.Equal(DiagnosticSeverity.Info, skipped.Severity);
        Assert.Equal("PWR_OVERVIEW", skipped.SheetSymbol!.SheetName);
    }

    [Fact]
    public void PlaceSheetsOnce_LeavesRepeatChannelsAlone()
    {
        var top = Sheet("Top.SchDoc", Symbol("Repeat(CH,1,3)", "Channel.SchDoc", "SYMCHANL"));
        var channel = Sheet("Channel.SchDoc", Part("R1", "RONEAAAA"));

        var once = ProjectHierarchy.Build(Project("Top.SchDoc", "Channel.SchDoc"), new[] { top, channel }, PlacedOnce);

        Assert.Equal(new[] { "R1_CH1", "R1_CH2", "R1_CH3" }, Designators(once));
        Assert.DoesNotContain(once.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetReferencedAgain);
    }

    // ---- symbols the caller does not follow ----

    private static readonly ProjectHierarchyOptions NotEndingInASpace = new()
    {
        FollowSheetSymbol = symbol => symbol.FileName is not { } path || !path.EndsWith(' '),
    };

    [Fact]
    public void ASymbolNotFollowed_PlacesNothing_AndDoesNotMakeItsSheetAChild()
    {
        var top = Sheet("Top.SchDoc", Part("R9", "RNINEAAA"), Symbol("PWR", "Power.SchDoc ", "SYMPOWER"));
        var power = Sheet("Power.SchDoc", Part("R1", "RONEAAAA"));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "Power.SchDoc"), new[] { top, power }, NotEndingInASpace);

        Assert.Equal(new[] { "Top.SchDoc", "Power.SchDoc" }, hierarchy.Roots.Select(r => r.FileName).ToArray());
        Assert.Equal(new[] { "R1", "R9" }, Designators(hierarchy));
        var skipped = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetSymbolNotFollowed);
        Assert.Equal("PWR", skipped.SheetSymbol!.SheetName);
        Assert.Equal("Power.SchDoc ", skipped.DocumentName);
        Assert.DoesNotContain(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SubSheetNotSupplied);
        Assert.Empty(hierarchy.MissingDocuments);

        var followed = ProjectHierarchy.Build(Project("Top.SchDoc", "Power.SchDoc"), new[] { top, power });
        Assert.Equal("PWR", followed.FindComponent("R1")!.Sheet.RoomName);
    }

    [Fact]
    public async Task ASymbolNotFollowed_IsNotAskedFor()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc",
                Symbol("PWR", "Power.SchDoc ", "SYMPOWER", xMils: 1000),
                Symbol("AMP", "Amp.SchDoc", "SYMAMPAA", xMils: 3000))),
            ("Power.SchDoc", Sheet("Power.SchDoc", Part("R1", "RONEAAAA"))),
            ("Amp.SchDoc", Sheet("Amp.SchDoc", Part("C1", "CONEAAAA"))));
        var asked = new List<string>();

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), Open(files, asked), NotEndingInASpace);

        Assert.Equal(new[] { "Top.SchDoc", "Amp.SchDoc" }, asked);
        Assert.Equal(new[] { "C1" }, Designators(hierarchy));
        Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetSymbolNotFollowed);
    }

    // ---- diagnostics name their document the same way ----

    [Fact]
    public void DiagnosticsAboutOneDocument_ShareItsKey()
    {
        var c1 = Sheet("C1.SchDoc",
            Symbol("A", "c1.schdoc", "SYMSELFA", xMils: 1000),
            Symbol("B", "C1.SchDoc", "SYMSELFB", xMils: 3000));

        var hierarchy = ProjectHierarchy.Build(null, new[] { c1 });

        var cycles = hierarchy.Diagnostics.Where(d => d.Code == AltiumDiagnosticCode.SheetCycle).ToList();
        Assert.Equal(new[] { "c1.schdoc", "C1.SchDoc" }, cycles.Select(d => d.DocumentName).ToArray());
        Assert.Equal(new[] { "c1.schdoc", "C1.SchDoc" }, cycles.Select(d => d.DocumentPath).ToArray());
        Assert.All(cycles, d => Assert.Equal("C1.SchDoc", d.DocumentKey));
        Assert.Equal(hierarchy.Sheets[0].DocumentKey, cycles[0].DocumentKey);
    }

    [Fact]
    public void DiagnosticsAboutOneMissingDocument_ShareItsFirstSpelling()
    {
        var top = Sheet("Top.SchDoc",
            Symbol("A", @"Sub\Gone.SchDoc", "SYMGONEA", xMils: 1000),
            Symbol("B", "gone.schdoc ", "SYMGONEB", xMils: 3000));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "GONE.SchDoc"), new[] { top });

        var about = hierarchy.Diagnostics.Where(d => d.Code is AltiumDiagnosticCode.DocumentNotSupplied or AltiumDiagnosticCode.SubSheetNotSupplied).ToList();
        Assert.Equal(3, about.Count);
        Assert.All(about, d => Assert.Equal("GONE.SchDoc", d.DocumentKey));
        Assert.Equal(new[] { "GONE.SchDoc", @"Sub\Gone.SchDoc", "gone.schdoc " }, about.Select(d => d.DocumentPath).ToArray());
    }

    [Fact]
    public async Task DiagnosticsFromTheDiskLoader_UseTheFilesKey()
    {
        using var folder = new Folder(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("SUB", @"Sheets\Sub.SchDoc", "SYMSUBAA"))),
            (@"Sheets\Sub.SchDoc", Sheet("Sub.SchDoc", Symbol("UP", @"..\Top.SchDoc", "SYMUPAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(folder.Project("Top.SchDoc"));

        var cycle = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetCycle);
        Assert.Equal(hierarchy.Roots[0].DocumentKey, cycle.DocumentKey);
        Assert.Equal(@"..\Top.SchDoc", cycle.DocumentPath);
        var unlisted = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.UnlistedSheetLoaded);
        Assert.Equal(hierarchy.Sheets[1].DocumentKey, unlisted.DocumentKey);
        Assert.Equal(@"Sheets\Sub.SchDoc", unlisted.DocumentPath);
    }

    [Fact]
    public void Siblings_AreOrderedBottomToTopThenLeftToRight()
    {
        var a = Symbol("A", "A.SchDoc", "SYMAAAAA", xMils: 3000, yMils: 1000);
        var b = Symbol("B", "B.SchDoc", "SYMBBBBB", xMils: 1000, yMils: 2000);
        var c = Symbol("C", "C.SchDoc", "SYMCCCCC", xMils: 1000, yMils: 1000);

        Assert.Equal(new[] { c, a, b }, ProjectHierarchy.OrderSiblings(new[] { a, b, c }));
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

using System.Text;
using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Altium.Models.Project;
using static OriginalCircuit.Altium.Tests.HierarchyDesigns;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests that document paths are resolved as Windows (and so Altium) resolves them: <c>.</c> and
/// <c>..</c> segments, trailing periods and spaces, on every platform.
/// </summary>
public sealed class DocumentPathTests
{
    [Theory]
    [InlineData(@"Main.SchDoc\.", "Main.SchDoc")]
    [InlineData(@"Sub\Main.SchDoc\X\..", "Main.SchDoc")]
    [InlineData("Main.SchDoc.", "Main.SchDoc")]
    [InlineData("Main.SchDoc . .", "Main.SchDoc")]
    [InlineData("Power.SchDoc ", "Power.SchDoc")]
    [InlineData(@"Sheets\ Power.SchDoc", "Power.SchDoc")]
    [InlineData(@"Sheets.\Power.SchDoc", "Power.SchDoc")]
    [InlineData(@"Sheets\C:Power.SchDoc", "Power.SchDoc")]
    [InlineData(@"C:\Sheets\Power.SchDoc", "Power.SchDoc")]
    [InlineData("Sheets/Power.SchDoc", "Power.SchDoc")]
    [InlineData(@"..\Other\Power.SchDoc", "Power.SchDoc")]
    [InlineData("...Power.SchDoc", "...Power.SchDoc")]
    public void FileNames_AreTakenAsWindowsOpensThePath(string path, string expected)
    {
        Assert.Equal(expected, ProjectHierarchy.NormalizeFileName(path));
        Assert.Equal(expected, ProjectHierarchy.NormalizeFileName(expected));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"X\..")]
    [InlineData(@"Main.SchDoc\..")]
    [InlineData(@"Sheets\")]
    [InlineData(@"Sheets\...")]
    [InlineData(" . ")]
    [InlineData("C:")]
    public void PathsThatEndAtAFolder_NameNoFile(string path) =>
        Assert.Null(ProjectHierarchy.NormalizeFileName(path));

    [Theory]
    [InlineData(@"Sheets.\Power.SchDoc", @"Sheets\Power.SchDoc")]
    [InlineData(@"Sheets..\Power.SchDoc", @"Sheets..\Power.SchDoc")]
    [InlineData(@"Sheets \Power.SchDoc", @"Sheets \Power.SchDoc")]
    [InlineData(@"a//b\.\c", @"a\b\c")]
    [InlineData(@"..\..\a\b\..\c.SchDoc.", @"..\..\a\c.SchDoc")]
    [InlineData(@"C:\..\x", @"C:\x")]
    [InlineData(@"\\server\share\..\x", @"\\server\share\x")]
    [InlineData(@"Sheets\", @"Sheets\")]
    [InlineData(".", "")]
    public void Paths_AreResolvedAsWindowsResolvesThem(string path, string expected) =>
        Assert.Equal(expected, ProjectHierarchy.NormalizePath(path));

    [Fact]
    public void NormalizedPaths_MatchGetFullPath_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        string[] atoms =
        {
            "a", "B", "x.", "x..", "x...", ".", "..", "...", " ", "x ", " x", "x .", "x. ", ". .", ".. ", " ..",
            "Main.SchDoc", "y.SchDoc.", "z.SchDoc ", "q. . ", ".x.", "..x", "x..y",
        };
        const string Base = @"C:\d0\d1\d2\d3\d4\d5\d6\d7\d8\";
        var random = new Random(20261105);
        for (var i = 0; i < 20_000; i++)
        {
            var sb = new StringBuilder();
            var count = random.Next(1, 7);
            for (var j = 0; j < count; j++)
            {
                if (j > 0)
                    sb.Append(random.Next(6) switch { 0 => "/", 1 => @"\\", _ => @"\" });
                sb.Append(atoms[random.Next(atoms.Length)]);
            }
            if (random.Next(5) == 0)
                sb.Append('\\');
            var path = sb.ToString();
            var expected = Path.GetFullPath(Base + path);

            Assert.Equal(expected, ProjectHierarchy.NormalizePath(Base + path));
            var relative = ProjectHierarchy.NormalizePath(path)!;
            var viaRelative = relative.Length == 0 ? Path.GetFullPath(Base.TrimEnd('\\')) : Path.GetFullPath(Base + relative);
            Assert.Equal(expected.TrimEnd('\\'), viaRelative.TrimEnd('\\'));
        }
    }

    // ---- the hierarchy ----

    private static Func<string, Stream?> Open(Dictionary<string, byte[]> files, List<string>? asked = null) => path =>
    {
        asked?.Add(path);
        return files.TryGetValue(ProjectHierarchy.NormalizePath(path)!, out var bytes) ? new MemoryStream(bytes) : null;
    };

    [Fact]
    public async Task SymbolPathsThatEndInADotFolder_PlaceTheirOwnSheets()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc", Part("R9", "RNINEAAA"),
                Symbol("MAIN", @"Main.SchDoc\.", "SYMMAINA", xMils: 1000),
                Symbol("OTHER", @"Other.SchDoc\.", "SYMOTHER", xMils: 3000))),
            ("Main.SchDoc", Sheet("Main.SchDoc", Part("R1", "RONEAAAA"))),
            ("Other.SchDoc", Sheet("Other.SchDoc", Part("C1", "CONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), Open(files));

        Assert.Equal(new[] { "C1", "R1", "R9" }, Designators(hierarchy));
        Assert.Equal(new[] { "Top.SchDoc", "Main.SchDoc", "Other.SchDoc" }, hierarchy.Documents.Keys.ToArray());
        Assert.Empty(hierarchy.MissingDocuments);
    }

    [Fact]
    public async Task ASymbolPathToAListedSheet_ReadsItOnce()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("MAIN", @"Main.SchDoc\.", "SYMMAINA"))),
            ("Main.SchDoc", Sheet("Main.SchDoc", Part("R1", "RONEAAAA"))));
        var asked = new List<string>();

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc", "Main.SchDoc"), Open(files, asked));

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Equal(new[] { "Top.SchDoc", "Main.SchDoc" }, asked);
    }

    [Fact]
    public async Task ASymbolPathThatEndsInAPeriod_PlacesTheSheetWithoutIt()
    {
        var files = Files(
            ("Top.SchDoc", Sheet("Top.SchDoc", Symbol("MAIN", "Main.SchDoc.", "SYMMAINA"))),
            ("Main.SchDoc", Sheet("Main.SchDoc", Part("R1", "RONEAAAA"))));

        var hierarchy = await ProjectHierarchy.LoadAsync(Project("Top.SchDoc"), Open(files));

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Empty(hierarchy.MissingDocuments);
    }

    [Fact]
    public void ASymbolPathThatEndsAtAFolder_NamesNoSheet()
    {
        var top = Sheet("Top.SchDoc", Symbol("GONE", @"Main.SchDoc\..", "SYMGONEA"));

        var hierarchy = ProjectHierarchy.Build(null, new[] { top, Sheet("Main.SchDoc", Part("R1", "RONEAAAA")) });

        var diagnostic = Assert.Single(hierarchy.Diagnostics, d => d.Code == AltiumDiagnosticCode.SheetSymbolWithoutFile);
        Assert.Equal("GONE", diagnostic.SheetSymbol!.SheetName);
    }
}

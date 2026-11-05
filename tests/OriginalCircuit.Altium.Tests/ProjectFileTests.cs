using System.Text;
using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Serialization;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for the Altium project (.PrjPcb) reader, writer and data model. A project file is plain
/// UTF-8 INI-style text, so these tests use synthetic fixtures built inline (the real customer
/// projects are never checked into the repo).
/// </summary>
public sealed class ProjectFileTests
{
    // A representative project covering the section variety: design settings, two documents of
    // different kinds, a variant with component variations, a project parameter, a configuration and
    // an output group. Uses CRLF and the blank-line-after-each-section layout Altium writes.
    private const string SampleProject =
        "[Design]\r\n" +
        "Version=1.0\r\n" +
        "HierarchyMode=0\r\n" +
        "DefaultConfiguration=Sources\r\n" +
        "ManagedProjectGUID=8082E159-47AC-400F-8FB5-7210BE66232B\r\n" +
        "ReleaseVaultName=Original Circuit\r\n" +
        "\r\n" +
        "[Document1]\r\n" +
        "DocumentPath=Board.SchDoc\r\n" +
        "AnnotateOrder=0\r\n" +
        "DocumentUniqueId=HSSKEIYU\r\n" +
        "\r\n" +
        "[Document2]\r\n" +
        "DocumentPath=Board.PcbDoc\r\n" +
        "AnnotateOrder=-1\r\n" +
        "DocumentUniqueId=YVFHVNGI\r\n" +
        "\r\n" +
        "[ProjectVariant1]\r\n" +
        "UniqueId=58ba1377-484a-4829-9ab9-df57b90b5cef\r\n" +
        "Description=MALE\r\n" +
        "AllowFabrication=0\r\n" +
        "ParameterCount=0\r\n" +
        "VariationCount=2\r\n" +
        "Variation1=Designator=J3|UniqueId=\\CWVLSYAN|Kind=1|AlternatePart=\r\n" +
        "Variation2=Designator=J4|UniqueId=\\EPKUPTFF|Kind=2|AlternatePart==Value\r\n" +
        "ParamVariationCount=1\r\n" +
        "ParamVariation1=ParameterName=Comment|VariantValue==Value\r\n" +
        "ParamDesignator1=J4\r\n" +
        "\r\n" +
        "[Parameter1]\r\n" +
        "Name=Revision\r\n" +
        "Value=A.1\r\n" +
        "\r\n" +
        "[Configuration1]\r\n" +
        "Name=Sources\r\n" +
        "Variant=[No Variations]\r\n" +
        "ConfigurationType=Source\r\n" +
        "\r\n" +
        "[OutputGroup1]\r\n" +
        "Name=Fabrication Outputs\r\n" +
        "Description=\r\n" +
        "OutputType1=Gerber\r\n" +
        "OutputName1=Gerber Files\r\n" +
        "OutputDocumentPath1=\r\n" +
        "OutputVariantName1=[No Variations]\r\n" +
        "OutputDefault1=0\r\n" +
        "OutputType2=NC Drill\r\n" +
        "OutputName2=NC Drill Files\r\n" +
        "OutputDocumentPath2=\r\n" +
        "OutputVariantName2=\r\n" +
        "OutputDefault2=0\r\n" +
        "\r\n";

    private const string SampleStructure =
        "Record=TopLevelDocument|FileName=Board.SchDoc|SheetNumber=1\r\n" +
        "Record=SheetSymbol|SourceDocument=Board.SchDoc|Designator=U_Power|SchDesignator=U_Power|FileName=Power.SchDoc|SheetNumber=2|SymbolType=Normal|RawFileName=Power.SchDoc|DesignItemId= |SourceLibraryName= |ObjectKind=Sheet Symbol|RevisionGUID= |ItemGUID= |VaultGUID= \r\n" +
        "Record=SheetSymbol|SourceDocument=Board.SchDoc|Designator=U_IO|SchDesignator=U_IO|FileName=IO.SchDoc|SheetNumber=3|SymbolType=Normal|RawFileName=IO.SchDoc|DesignItemId= |SourceLibraryName= |ObjectKind=Sheet Symbol|RevisionGUID= |ItemGUID= |VaultGUID= \r\n" +
        "Record=SheetSymbol|SourceDocument=Power.SchDoc|Designator=U_LDO|SchDesignator=U_LDO|FileName=LDO.SchDoc|SheetNumber=4|SymbolType=Normal|RawFileName=LDO.SchDoc|DesignItemId= |SourceLibraryName= |ObjectKind=Sheet Symbol|RevisionGUID= |ItemGUID= |VaultGUID= \r\n";

    private static byte[] WithBom(string text) =>
        new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    private static AltiumProject ReadSample() => new PrjPcbReader().Read(SampleProject);

    // ---- Byte-fidelity round-trip --------------------------------------------------------------

    [Fact]
    public void RoundTrip_WithBomAndCrlf_IsByteIdentical()
    {
        var original = WithBom(SampleProject);
        using var ms = new MemoryStream(original);
        var project = new PrjPcbReader().Read(ms);

        Assert.True(project.HasByteOrderMark);
        Assert.Equal("\r\n", project.NewLine);

        var written = new PrjPcbWriter().SerializeToBytes(project);
        Assert.Equal(original, written);
    }

    [Fact]
    public void RoundTrip_NoBom_IsByteIdentical()
    {
        var original = Encoding.UTF8.GetBytes(SampleProject);
        using var ms = new MemoryStream(original);
        var project = new PrjPcbReader().Read(ms);

        Assert.False(project.HasByteOrderMark);
        var written = new PrjPcbWriter().SerializeToBytes(project);
        Assert.Equal(original, written);
    }

    [Fact]
    public void RoundTrip_LfLineEndings_AreDetectedAndPreserved()
    {
        var lfText = SampleProject.Replace("\r\n", "\n");
        var project = new PrjPcbReader().Read(lfText);

        Assert.Equal("\n", project.NewLine);
        Assert.Equal(lfText, new PrjPcbWriter().SerializeText(project));
    }

    // ---- Typed model parsing -------------------------------------------------------------------

    [Fact]
    public void Design_ExposesTypedSettings()
    {
        var p = ReadSample();
        Assert.Equal("1.0", p.Design.Version);
        Assert.Equal(0, p.Design.HierarchyMode);
        Assert.Equal("Sources", p.Design.DefaultConfiguration);
        Assert.Equal(Guid.Parse("8082E159-47AC-400F-8FB5-7210BE66232B"), p.Design.ManagedProjectGuid);
        Assert.Equal("Original Circuit", p.Design.ReleaseVaultName);
    }

    [Fact]
    public void Documents_AreParsedWithKindsAndMetadata()
    {
        var p = ReadSample();
        Assert.Equal(2, p.Documents.Count);

        var sch = p.Documents[0];
        Assert.Equal("Board.SchDoc", sch.DocumentPath);
        Assert.Equal(ProjectDocumentKind.Schematic, sch.Kind);
        Assert.Equal("HSSKEIYU", sch.DocumentUniqueId);
        Assert.Equal(0, sch.AnnotateOrder);

        var pcb = p.Documents[1];
        Assert.Equal(ProjectDocumentKind.Pcb, pcb.Kind);
        Assert.Equal("Board.PcbDoc", pcb.DocumentPath);

        Assert.Single(p.SchematicDocuments);
        Assert.Single(p.PcbDocuments);
    }

    [Theory]
    [InlineData("x.SchDoc", ProjectDocumentKind.Schematic)]
    [InlineData("x.PcbDoc", ProjectDocumentKind.Pcb)]
    [InlineData("x.SchLib", ProjectDocumentKind.SchematicLibrary)]
    [InlineData("x.PcbLib", ProjectDocumentKind.PcbLibrary)]
    [InlineData("x.BomDoc", ProjectDocumentKind.BillOfMaterials)]
    [InlineData("x.OutJob", ProjectDocumentKind.OutputJob)]
    [InlineData("x.Harness", ProjectDocumentKind.Harness)]
    [InlineData("x.PcbDwf", ProjectDocumentKind.Draftsman)]
    [InlineData("Lib\\sub\\X.SCHDOC", ProjectDocumentKind.Schematic)]
    [InlineData("x.weird", ProjectDocumentKind.Other)]
    [InlineData("", ProjectDocumentKind.Unknown)]
    [InlineData("SchDoc", ProjectDocumentKind.Schematic)]
    [InlineData(".PcbDoc", ProjectDocumentKind.Pcb)]
    [InlineData("Main.SchDoc ", ProjectDocumentKind.Schematic)]
    [InlineData("Main.SchDoc.", ProjectDocumentKind.Schematic)]
    [InlineData("Main.SchDoc\\.", ProjectDocumentKind.Schematic)]
    [InlineData("Main.SchDoc\\Old\\..", ProjectDocumentKind.Schematic)]
    [InlineData("Main.BomDoc ", ProjectDocumentKind.BillOfMaterials)]
    [InlineData("Main.BomDoc.", ProjectDocumentKind.BillOfMaterials)]
    [InlineData("Sheets\\", ProjectDocumentKind.Unknown)]
    [InlineData(".", ProjectDocumentKind.Unknown)]
    public void DocumentKind_IsClassifiedByExtension(string path, ProjectDocumentKind expected)
    {
        Assert.Equal(expected, ProjectDocumentKinds.FromPath(path));
    }

    [Theory]
    [InlineData("Main.SchDoc\\.")]
    [InlineData("Main.SchDoc ")]
    [InlineData("Main.SchDoc.")]
    public void ListedSchematics_WhosePathsWindowsOpensAsASchDoc_AreSchematicDocuments(string path)
    {
        var project = AltiumLibrary.CreateProject();
        project.AddDocument(path);
        project.AddDocument("Main.BomDoc ");

        Assert.Equal(path, Assert.Single(project.SchematicDocuments).DocumentPath);
        Assert.Single(project.DocumentsOfKind(ProjectDocumentKind.BillOfMaterials));
    }

    [Fact]
    public async Task AListedSchematicWhosePathEndsInASpace_IsReadAndPlaced()
    {
        var files = HierarchyDesigns.Files(
            ("Top.SchDoc", HierarchyDesigns.Sheet("Top.SchDoc", HierarchyDesigns.Symbol("PWR", "Power.SchDoc", "SYMPOWER"))),
            ("Power.SchDoc", HierarchyDesigns.Sheet("Power.SchDoc", HierarchyDesigns.Part("R1", "RONEAAAA"))));
        var project = HierarchyDesigns.Project("Top.SchDoc", "Power.SchDoc ");

        var hierarchy = await ProjectHierarchy.LoadAsync(project,
            path => files.TryGetValue(ProjectHierarchy.NormalizePath(path)!, out var bytes) ? new MemoryStream(bytes) : null);

        Assert.Equal(new[] { "R1" }, HierarchyDesigns.Designators(hierarchy));
        Assert.Empty(hierarchy.MissingDocuments);
        Assert.DoesNotContain(hierarchy.Diagnostics, d => d.Code == Diagnostics.AltiumDiagnosticCode.UnlistedSheetLoaded);
    }

    [Fact]
    public void Variant_ParsesVariationsAndParameterOverrides()
    {
        var p = ReadSample();
        var variant = Assert.Single(p.Variants);
        Assert.Equal("MALE", variant.Description);
        Assert.False(variant.AllowFabrication);

        Assert.Equal(2, variant.Variations.Count);
        Assert.Equal("J3", variant.Variations[0].Designator);
        Assert.Equal(VariationKind.NotFitted, variant.Variations[0].Kind);
        Assert.Equal(VariationKind.Alternate, variant.Variations[1].Kind);
        // AlternatePart value itself contains '=' — the field split must keep it intact.
        Assert.Equal("=Value", variant.Variations[1].AlternatePart);

        var pv = Assert.Single(variant.ParameterVariations);
        Assert.Equal("J4", pv.Designator);
        Assert.Equal("Comment", pv.ParameterName);
        Assert.Equal("=Value", pv.VariantValue);
    }

    [Fact]
    public void Parameters_AndConfigurations_AndOutputGroups_AreParsed()
    {
        var p = ReadSample();

        var param = Assert.Single(p.Parameters);
        Assert.Equal("Revision", param.Name);
        Assert.Equal("A.1", param.Value);

        var config = Assert.Single(p.Configurations);
        Assert.Equal("Sources", config.Name);
        Assert.Equal("[No Variations]", config.Variant);
        Assert.Equal("Source", config.ConfigurationType);

        var group = Assert.Single(p.OutputGroups);
        Assert.Equal("Fabrication Outputs", group.Name);
        Assert.Equal(2, group.Outputs.Count);
        Assert.Equal("Gerber", group.Outputs[0].Type);
        Assert.Equal("Gerber Files", group.Outputs[0].Name);
        Assert.Equal("[No Variations]", group.Outputs[0].VariantName);
        Assert.Equal("NC Drill", group.Outputs[1].Type);
    }

    // ---- Structure file ------------------------------------------------------------------------

    [Fact]
    public void Structure_RoundTripsByteIdentical()
    {
        var structure = ProjectStructure.Parse(SampleStructure);
        Assert.Equal(4, structure.Records.Count);
        Assert.Equal("Board.SchDoc", structure.TopLevelDocument);
        Assert.Equal(SampleStructure, structure.Serialize());
    }

    [Fact]
    public void Structure_PreservesTrailingSpaceValues()
    {
        var structure = ProjectStructure.Parse(SampleStructure);
        // The customer corpus stores blank GUID fields as a single space; that must round-trip.
        Assert.Equal(" ", structure.Records[1].Field("VaultGUID"));
    }

    [Fact]
    public void Structure_BuildsHierarchyTree()
    {
        var structure = ProjectStructure.Parse(SampleStructure);
        var root = structure.BuildTree();

        Assert.NotNull(root);
        Assert.Equal("Board.SchDoc", root!.FileName);
        Assert.Null(root.Designator);
        Assert.Equal(2, root.Children.Count);

        var power = root.Children.Single(c => c.FileName == "Power.SchDoc");
        Assert.Equal("U_Power", power.Designator);
        Assert.Equal(2, power.SheetNumber);
        var ldo = Assert.Single(power.Children);
        Assert.Equal("LDO.SchDoc", ldo.FileName);
        Assert.Equal("U_LDO", ldo.Designator);
    }

    [Fact]
    public void Structure_BreaksCycles()
    {
        // A document that references itself (directly) must not recurse forever.
        var cyclic =
            "Record=TopLevelDocument|FileName=A.SchDoc|SheetNumber=1\r\n" +
            "Record=SheetSymbol|SourceDocument=A.SchDoc|Designator=U_A|FileName=A.SchDoc|SheetNumber=2\r\n";
        var root = ProjectStructure.Parse(cyclic).BuildTree();

        Assert.NotNull(root);
        var child = Assert.Single(root!.Children);
        Assert.True(child.IsCycle);
        Assert.Empty(child.Children);
    }

    [Fact]
    public void Structure_BuildsADeepChainWithoutRecursion()
    {
        // Deep enough that one stack frame per level would overflow the stack.
        const int levels = 50_000;
        var text = new StringBuilder("Record=TopLevelDocument|FileName=S0.SchDoc\r\n");
        for (var i = 0; i < levels; i++)
            text.Append($"Record=SheetSymbol|SourceDocument=S{i}.SchDoc|Designator=U{i + 1}|FileName=S{i + 1}.SchDoc\r\n");
        text.Append($"Record=SheetSymbol|SourceDocument=S{levels}.SchDoc|Designator=U_Back|FileName=S0.SchDoc\r\n");

        var root = ProjectStructure.Parse(text.ToString()).BuildTree()!;

        var depth = 0;
        var node = root;
        while (node.Children.Count == 1 && !node.Children[0].IsCycle)
        {
            node = node.Children[0];
            depth++;
        }
        Assert.Equal(levels, depth);
        Assert.Equal($"S{levels}.SchDoc", node.FileName);
        Assert.True(Assert.Single(node.Children).IsCycle);
        Assert.False(root.IsTruncated);
    }

    [Fact]
    public void Structure_StopsAtTheNodeLimit_AndFlagsWhereItWasCut()
    {
        // Each of 20 levels places the next 10 times: 10^20 instances from a few kilobytes.
        var text = new StringBuilder("Record=TopLevelDocument|FileName=L0.SchDoc\r\n");
        for (var level = 0; level < 20; level++)
            for (var k = 0; k < 10; k++)
                text.Append($"Record=SheetSymbol|SourceDocument=L{level}.SchDoc|Designator=U{k}|FileName=L{level + 1}.SchDoc\r\n");
        var structure = ProjectStructure.Parse(text.ToString());

        var root = structure.BuildTree(maxNodes: 1000)!;

        static IEnumerable<ProjectSheetNode> All(ProjectSheetNode n) => n.Children.SelectMany(All).Prepend(n);
        Assert.Equal(1000, All(root).Count());
        // The flagged nodes are the path from the root to where the tree was cut ...
        var path = new List<ProjectSheetNode> { root };
        while (path[^1].Children.LastOrDefault() is { IsTruncated: true } next)
            path.Add(next);
        Assert.Equal(path.Count, All(root).Count(n => n.IsTruncated));
        // ... and a subtree finished before the cut is complete: a level-18 sheet, 1 + 10 x (1 + 10) nodes.
        var finished = path[17].Children[0];
        Assert.False(finished.IsTruncated);
        Assert.Equal(111, All(finished).Count());
        Assert.Equal(ProjectStructure.DefaultMaxTreeNodes, All(structure.BuildTree()!).Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => structure.BuildTree(0));
    }

    // ---- Authoring -----------------------------------------------------------------------------

    [Fact]
    public void CreateProject_FromScratch_RoundTrips()
    {
        var project = AltiumLibrary.CreateProject();
        var doc = project.AddDocument("MyBoard.SchDoc");
        doc.DocumentUniqueId = "ABCDEFGH";
        project.AddDocument("MyBoard.PcbDoc");

        Assert.Equal("1.0", project.Design.Version);
        Assert.Equal(2, project.Documents.Count);

        // Serialize and reparse — the typed views survive a write/read cycle.
        var bytes = new PrjPcbWriter().SerializeToBytes(project);
        using var ms = new MemoryStream(bytes);
        var reloaded = new PrjPcbReader().Read(ms);

        Assert.Equal(2, reloaded.Documents.Count);
        Assert.Equal("MyBoard.SchDoc", reloaded.Documents[0].DocumentPath);
        Assert.Equal("ABCDEFGH", reloaded.Documents[0].DocumentUniqueId);
        Assert.Equal(ProjectDocumentKind.Pcb, reloaded.Documents[1].Kind);
    }

    [Fact]
    public void AddDocument_AssignsSequentialSectionNames()
    {
        var project = AltiumLibrary.CreateProject();
        project.AddDocument("A.SchDoc");
        project.AddDocument("B.SchDoc");

        Assert.NotNull(project.GetSection("Document1"));
        Assert.NotNull(project.GetSection("Document2"));
    }

    [Fact]
    public void EditingThroughTypedView_WritesBackToSection()
    {
        var p = ReadSample();
        p.Documents[0].DocumentPath = "Renamed.SchDoc";
        Assert.Equal("Renamed.SchDoc", p.GetSection("Document1")!.Get("DocumentPath"));
    }

    // ---- Path resolution -----------------------------------------------------------------------

    [Fact]
    public void ResolveDocumentPath_CombinesWithProjectDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PrjResolveTest");
        var p = ReadSample();
        p.FilePath = Path.Combine(dir, "Board.PrjPcb");

        var resolved = p.ResolveDocumentPath(p.Documents[0]);
        Assert.Equal(Path.GetFullPath(Path.Combine(dir, "Board.SchDoc")), resolved);
    }

    [Fact]
    public void ResolveDocumentPath_WithoutFilePath_ReturnsNull()
    {
        var p = ReadSample();
        Assert.Null(p.ResolveDocumentPath(p.Documents[0]));
    }

    // ---- Facade integration --------------------------------------------------------------------

    [Fact]
    public async Task OpenProjectAsync_LoadsProjectAndSiblingStructure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PrjOpenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prjPath = Path.Combine(dir, "Board.PrjPcb");
            await File.WriteAllBytesAsync(prjPath, WithBom(SampleProject));
            await File.WriteAllBytesAsync(
                Path.Combine(dir, "Board.PrjPcbStructure"),
                Encoding.UTF8.GetBytes(SampleStructure));

            var project = await AltiumLibrary.OpenProjectAsync(prjPath);

            Assert.Equal("Board", project.Name);
            Assert.Equal(2, project.Documents.Count);
            Assert.NotNull(project.Structure);
            Assert.Equal("Board.SchDoc", project.Structure!.TopLevelDocument);

            // Round-trip on disk stays byte-identical for both files.
            var savePath = Path.Combine(dir, "Saved.PrjPcb");
            await project.SaveAsync(savePath);
            Assert.Equal(
                await File.ReadAllBytesAsync(prjPath),
                await File.ReadAllBytesAsync(savePath));
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(dir, "Board.PrjPcbStructure")),
                await File.ReadAllBytesAsync(Path.Combine(dir, "Saved.PrjPcbStructure")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OpenProjectAsync_FromStreams_LoadsTheSuppliedStructure()
    {
        using var prj = new MemoryStream(WithBom(SampleProject), writable: false);
        using var structure = new MemoryStream(WithBom(SampleStructure), writable: false);

        var project = await AltiumLibrary.OpenProjectAsync(prj, structure);

        Assert.Equal(2, project.Documents.Count);
        Assert.NotNull(project.Structure);
        Assert.Equal("Board.SchDoc", project.Structure!.TopLevelDocument);
        Assert.Equal(4, project.Structure.Records.Count);
        Assert.Equal("Record", project.Structure.Records[0].Fields[0].Key); // the BOM is not part of the first key
        Assert.True(structure.CanRead); // left open
    }

    [Fact]
    public async Task OpenProjectAsync_FromStream_WithoutStructure_HasNone()
    {
        using var prj = new MemoryStream(WithBom(SampleProject), writable: false);

        Assert.Null((await AltiumLibrary.OpenProjectAsync(prj)).Structure);

        prj.Position = 0;
        Assert.Null((await AltiumLibrary.OpenProjectAsync(prj, structureStream: null)).Structure);
    }

    [Fact]
    public void Structure_Parse_IgnoresByteOrderMark_InStreamAndText()
    {
        using var stream = new MemoryStream(WithBom(SampleStructure));
        var fromStream = ProjectStructure.Parse(stream);
        var fromText = ProjectStructure.Parse('﻿' + SampleStructure);

        Assert.Equal("Board.SchDoc", fromStream.TopLevelDocument);
        Assert.Equal("Board.SchDoc", fromText.TopLevelDocument);
        Assert.Equal(SampleStructure, fromStream.Serialize());
        Assert.Equal(SampleStructure, fromText.Serialize());
    }

    private const string AnsiStructure = "Record=TopLevelDocument|FileName=Übersicht.SchDoc\r\n";

    [Fact]
    public void Structure_Parse_ReadsWindows1252_WhenTheBytesAreNotUtf8()
    {
        using var ansi = new MemoryStream(AltiumEncoding.Windows1252.GetBytes(AnsiStructure));
        using var utf8 = new MemoryStream(Encoding.UTF8.GetBytes(AnsiStructure));
        using var utf8WithBom = new MemoryStream(WithBom(AnsiStructure));

        Assert.Equal("Übersicht.SchDoc", ProjectStructure.Parse(ansi).TopLevelDocument);
        Assert.Equal("Übersicht.SchDoc", ProjectStructure.Parse(utf8).TopLevelDocument);
        Assert.Equal("Übersicht.SchDoc", ProjectStructure.Parse(utf8WithBom).TopLevelDocument);
    }

    [Fact]
    public async Task OpenProjectAsync_FromStreams_ReadsAWindows1252ProjectAndStructure()
    {
        var prjText = "[Document1]\r\nDocumentPath=Übersicht.SchDoc\r\n\r\n";
        var prjBytes = AltiumEncoding.Windows1252.GetBytes(prjText);
        using var prj = new MemoryStream(prjBytes);
        using var structure = new MemoryStream(AltiumEncoding.Windows1252.GetBytes(AnsiStructure));

        var project = await AltiumLibrary.OpenProjectAsync(prj, structure);

        Assert.Equal("Übersicht.SchDoc", project.Structure!.TopLevelDocument);
        Assert.Equal("Übersicht.SchDoc", Assert.Single(project.SchematicDocuments).DocumentPath);
        Assert.Equal(prjBytes, new PrjPcbWriter().SerializeToBytes(project)); // written back as it was read
        var hierarchy = ProjectHierarchy.Build(project, new[] { new Models.Sch.SchDocument { FileName = "Übersicht.SchDoc" } });
        Assert.Equal("Übersicht.SchDoc", Assert.Single(hierarchy.Roots).FileName);
    }

    [Fact]
    public async Task OpenProjectAsync_FromDisk_ReadsAndWritesBackAWindows1252Structure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PrjAnsiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prjPath = Path.Combine(dir, "Board.PrjPcb");
            await File.WriteAllBytesAsync(prjPath, WithBom(SampleProject));
            var structureBytes = AltiumEncoding.Windows1252.GetBytes(AnsiStructure);
            await File.WriteAllBytesAsync(Path.Combine(dir, "Board.PrjPcbStructure"), structureBytes);

            var project = await AltiumLibrary.OpenProjectAsync(prjPath);
            Assert.Equal("Übersicht.SchDoc", project.Structure!.TopLevelDocument);

            await project.SaveAsync(Path.Combine(dir, "Saved.PrjPcb"));
            Assert.Equal(structureBytes, await File.ReadAllBytesAsync(Path.Combine(dir, "Saved.PrjPcbStructure")));

            // A character Windows-1252 cannot hold is written as UTF-8 instead of being lost.
            project.Structure.Records[0].Fields[1] = new("FileName", "Übersicht-日本.SchDoc");
            await project.SaveAsync(Path.Combine(dir, "Edited.PrjPcb"));
            await using var edited = File.OpenRead(Path.Combine(dir, "Edited.PrjPcbStructure"));
            Assert.Equal("Übersicht-日本.SchDoc", ProjectStructure.Parse(edited).TopLevelDocument);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_OnProject_ThrowsWithHelpfulMessage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PrjOpenAsyncTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var prjPath = Path.Combine(dir, "Board.PrjPcb");
            await File.WriteAllBytesAsync(prjPath, WithBom(SampleProject));

            var ex = await Assert.ThrowsAsync<NotSupportedException>(() => AltiumLibrary.OpenAsync(prjPath).AsTask());
            Assert.Contains("OpenProjectAsync", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

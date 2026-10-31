using OriginalCircuit.Altium.Models;
using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Writers;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests for <see cref="ProjectHierarchy"/>: sheet instances, Altium room names and physical
/// designators, multi-part merging, and building from in-memory documents. All designs are synthetic.
/// </summary>
public sealed class ProjectHierarchyTests
{
    // ---- builders ----

    private static SchDocument Sheet(string fileName, params object[] content)
    {
        var doc = new SchDocument { FileName = fileName };
        foreach (var item in content)
        {
            switch (item)
            {
                case SchComponent component: doc.AddComponent(component); break;
                default: doc.AddPrimitive(item); break;
            }
        }
        return doc;
    }

    private static SchComponent Part(string designator, string uid, int partId = 1, int partCount = 1,
        params (string Name, string Value)[] parameters)
    {
        var component = new SchComponent
        {
            Name = "PART",
            UniqueId = uid,
            CurrentPartId = partId,
            PartCount = partCount,
        };
        component.AddParameter(new SchParameter { Name = "Designator", Value = designator });
        foreach (var (name, value) in parameters)
            component.AddParameter(new SchParameter { Name = name, Value = value });
        return component;
    }

    private static SchSheetSymbol Symbol(string designator, string fileName, string uid, int xMils = 1000, int yMils = 1000) => new()
    {
        Location = new CoordPoint(Coord.FromMils(xMils), Coord.FromMils(yMils)),
        XSize = Coord.FromMils(500),
        YSize = Coord.FromMils(400),
        FileName = fileName,
        SheetName = designator,
        NameLabel = new SchLabel { Text = designator },
        UniqueId = uid,
    };

    private static AltiumProject Project(params string[] documents)
    {
        var project = AltiumLibrary.CreateProject();
        foreach (var document in documents)
            project.AddDocument(document);
        return project;
    }

    private static string[] Designators(ProjectHierarchy hierarchy) =>
        hierarchy.Components.Select(c => c.Designator).OrderBy(d => d, StringComparer.Ordinal).ToArray();

    // ---- flat and single-instance designs ----

    [Fact]
    public void FlatSheet_KeepsLogicalDesignators()
    {
        var top = Sheet("Board.SchDoc", Part("R1", "AAAAAAAA"), Part("C1", "BBBBBBBB"));

        var hierarchy = ProjectHierarchy.Build(null, new[] { top });

        var root = Assert.Single(hierarchy.Roots);
        Assert.Same(root, Assert.Single(hierarchy.Sheets));
        Assert.Null(root.RoomName);
        Assert.False(root.IsMultiChannel);
        Assert.Equal("", root.UniqueIdPath);
        Assert.Equal("Board", root.HierarchicalPath);
        Assert.Equal(new[] { "C1", "R1" }, Designators(hierarchy));
        Assert.Equal("\\AAAAAAAA", hierarchy.FindComponent("R1")!.Parts[0].UniqueIdPath);
        Assert.Empty(hierarchy.Diagnostics);
    }

    [Fact]
    public void SheetUsedOnce_KeepsLogicalDesignators_AndGetsSymbolRoom()
    {
        var top = Sheet("Top.SchDoc", Symbol("U_Power", "Power.SchDoc", "SYMPOWER"), Part("J1", "JJJJJJJJ"));
        var power = Sheet("Power.SchDoc", Part("R10", "RRRRRRRR"));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "Power.SchDoc"), new[] { top, power });

        var child = hierarchy.Sheets.Single(s => s.FileName == "Power.SchDoc");
        Assert.Equal("U_Power", child.RoomName);
        Assert.Null(child.ChannelIndex);
        Assert.False(child.IsMultiChannel);
        Assert.Equal("\\SYMPOWER", child.UniqueIdPath);
        Assert.Equal("Top\\U_Power", child.HierarchicalPath);
        Assert.Equal(new[] { "J1", "R10" }, Designators(hierarchy));
        Assert.Equal("\\SYMPOWER\\RRRRRRRR", hierarchy.FindComponent("R10")!.Parts[0].UniqueIdPath);
    }

    // ---- multi-channel ----

    [Fact]
    public void Repeat_CreatesOneChannelPerIndex_WithAltiumDesignatorsAndPaths()
    {
        var top = Sheet("Top.SchDoc", Symbol("Repeat(CH,1,3)", "Channel.SchDoc", "SYMCHANL"));
        var channel = Sheet("Channel.SchDoc", Part("R1", "RESISTOR"), Part("C1", "CAPACITR"));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "Channel.SchDoc"), new[] { top, channel });

        var channels = hierarchy.Sheets.Where(s => s.FileName == "Channel.SchDoc").ToList();
        Assert.Equal(new int?[] { 1, 2, 3 }, channels.Select(c => c.RepeatIndex).ToArray());
        Assert.All(channels, c => Assert.True(c.IsMultiChannel));
        Assert.All(channels, c => Assert.Equal("CH", c.ChannelName));
        Assert.Equal(new[] { "CH1", "CH2", "CH3" }, channels.Select(c => c.RoomName).ToArray());
        Assert.Equal("\\2SYMCHANL", channels[1].UniqueIdPath);
        Assert.Equal("Top\\CH2", channels[1].HierarchicalPath);
        Assert.Equal(new[] { "C1_CH1", "C1_CH2", "C1_CH3", "R1_CH1", "R1_CH2", "R1_CH3" }, Designators(hierarchy));

        var r2 = hierarchy.FindComponentByUniqueIdPath("\\2SYMCHANL\\RESISTOR");
        Assert.Equal("R1_CH2", r2!.Designator);
        Assert.Equal("R1", r2.LogicalDesignator);
        Assert.Same(channels[1], hierarchy.FindSheetByUniqueIdPath("\\2SYMCHANL"));
    }

    [Fact]
    public void DuplicateSymbols_AreNumberedBottomToTopThenLeftToRight_NotByRecordOrder()
    {
        // Record order: top-left, bottom-right, bottom-left. Altium numbers from the bottom up.
        var top = Sheet("Top.SchDoc",
            Symbol("U_Amp", "Amp.SchDoc", "SYMTOPLF", xMils: 1000, yMils: 6000),
            Symbol("U_Amp", "Amp.SchDoc", "SYMBOTRT", xMils: 4000, yMils: 2000),
            Symbol("U_Amp", "Amp.SchDoc", "SYMBOTLF", xMils: 1000, yMils: 2000));
        var amp = Sheet("Amp.SchDoc", Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "Amp.SchDoc"), new[] { top, amp });

        Assert.Equal("R1_U_Amp1", hierarchy.FindComponentByUniqueIdPath("\\SYMBOTLF\\RESISTOR")!.Designator);
        Assert.Equal("R1_U_Amp2", hierarchy.FindComponentByUniqueIdPath("\\SYMBOTRT\\RESISTOR")!.Designator);
        Assert.Equal("R1_U_Amp3", hierarchy.FindComponentByUniqueIdPath("\\SYMTOPLF\\RESISTOR")!.Designator);
        var sheets = hierarchy.Sheets.Where(s => s.Parent is not null).ToList();
        Assert.Equal(new int?[] { 1, 2, 3 }, sheets.Select(s => s.ChannelIndex).ToArray());
        Assert.All(sheets, s => Assert.Equal("Top\\U_Amp", s.HierarchicalPath)); // the PCB path carries no number
    }

    [Fact]
    public void SharedSymbolDesignators_AreNumberedAcrossTheWholeDesign_DepthFirst()
    {
        var top = Sheet("Top.SchDoc",
            Symbol("U_Board", "Board.SchDoc", "SYMBRDLO", yMils: 1000),
            Symbol("U_Board", "Board.SchDoc", "SYMBRDHI", yMils: 5000));
        var board = Sheet("Board.SchDoc",
            Symbol("U_Driver", "Driver.SchDoc", "SYMDRVLO", yMils: 1000),
            Symbol("U_Driver", "Driver.SchDoc", "SYMDRVHI", yMils: 5000),
            Part("C1", "CAPACITR"));
        var driver = Sheet("Driver.SchDoc", Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "Board.SchDoc", "Driver.SchDoc"), new[] { top, board, driver });

        Assert.Equal(new[]
        {
            "C1_U_Board1", "C1_U_Board2",
            "R1_U_Driver1", "R1_U_Driver2", "R1_U_Driver3", "R1_U_Driver4",
        }, Designators(hierarchy));
        Assert.Equal("R1_U_Driver3", hierarchy.FindComponentByUniqueIdPath("\\SYMBRDHI\\SYMDRVLO\\RESISTOR")!.Designator);
        Assert.Equal("Top\\U_Board\\U_Driver",
            hierarchy.FindSheetByUniqueIdPath("\\SYMBRDHI\\SYMDRVLO")!.HierarchicalPath);
    }

    [Fact]
    public void SheetUsedByDifferentlyNamedSymbols_UsesEachSymbolName()
    {
        var top = Sheet("Top.SchDoc",
            Symbol("U_Left", "Shifter.SchDoc", "SYMLEFTS", xMils: 1000),
            Symbol("U_Right", "Shifter.SchDoc", "SYMRIGHT", xMils: 5000));
        var shifter = Sheet("Shifter.SchDoc", Part("IC5", "ICFIVEAA"));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "Shifter.SchDoc"), new[] { top, shifter });

        Assert.Equal(new[] { "IC5_U_Left", "IC5_U_Right" }, Designators(hierarchy));
    }

    [Fact]
    public void ChannelDesignatorFormat_ComesFromTheProject()
    {
        var project = Project("Top.SchDoc", "Channel.SchDoc");
        project.Design.Section.Set("ChannelDesignatorFormatString", "$Component$RoomName");
        var top = Sheet("Top.SchDoc", Symbol("Repeat(A,1,2)", "Channel.SchDoc", "SYMCHANL"));
        var channel = Sheet("Channel.SchDoc", Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(project, new[] { top, channel });

        Assert.Equal("$Component$RoomName", hierarchy.DesignatorFormat);
        Assert.Equal(new[] { "R1A1", "R1A2" }, Designators(hierarchy));
        Assert.Empty(hierarchy.Diagnostics);
    }

    [Fact]
    public void FormatDesignator_ExpandsComponentAndRoomName_AndKeepsOtherTokens()
    {
        Assert.Equal("R2_CH3", ProjectHierarchy.FormatDesignator("$Component_$RoomName", "R2", "CH3"));
        Assert.Equal("R2-ch3", ProjectHierarchy.FormatDesignator("$component-$roomname", "R2", "ch3"));
        Assert.Equal("R2$ChannelAlpha", ProjectHierarchy.FormatDesignator("$Component$ChannelAlpha", "R2", "CH3"));
        Assert.Equal("$R2", ProjectHierarchy.FormatDesignator("$$Component", "R2", "CH3"));
    }

    [Fact]
    public void UnsupportedFormatTokens_AndRoomStyles_AreReported()
    {
        var project = Project("Top.SchDoc");
        project.Design.Section.Set("ChannelDesignatorFormatString", "$Component$ChannelAlpha");
        project.Design.Section.Set("ChannelRoomNamingStyle", "2");

        var hierarchy = ProjectHierarchy.Build(project, new[] { Sheet("Top.SchDoc", Part("R1", "RESISTOR")) });

        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("$ChannelAlpha", StringComparison.Ordinal));
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("naming style 2", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedRoomNames_AreReported()
    {
        // A Repeat inside a duplicated sheet gives CH1/CH2 under both copies.
        var top = Sheet("Top.SchDoc",
            Symbol("U_Bank", "Bank.SchDoc", "SYMBANKA", yMils: 1000),
            Symbol("U_Bank", "Bank.SchDoc", "SYMBANKB", yMils: 5000));
        var bank = Sheet("Bank.SchDoc", Symbol("Repeat(CH,1,2)", "Channel.SchDoc", "SYMCHANL"));
        var channel = Sheet("Channel.SchDoc", Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(null, new[] { top, bank, channel });

        Assert.Equal(4, hierarchy.Sheets.Count(s => s.FileName == "Channel.SchDoc"));
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("'CH1'", StringComparison.Ordinal));
        Assert.Equal("\\SYMBANKB\\2SYMCHANL", hierarchy.Sheets.Last().UniqueIdPath);
    }

    // ---- multi-part components ----

    [Fact]
    public void MultiPartComponent_AcrossSheets_IsOneComponent()
    {
        var top = Sheet("Top.SchDoc",
            Symbol("U_Io", "Io.SchDoc", "SYMIOIOO"),
            Part("U1", "UONEPRTB", partId: 2, partCount: 2, ("Comment", "=Part Number"), ("Part Number", "MCU-100")));
        var io = Sheet("Io.SchDoc", Part("U1", "UONEPRTA", partId: 1, partCount: 2, ("Comment", "=Part Number"), ("Part Number", "MCU-100")));

        var hierarchy = ProjectHierarchy.Build(Project("Top.SchDoc", "Io.SchDoc"), new[] { top, io });

        var u1 = Assert.Single(hierarchy.Components);
        Assert.Equal("U1", u1.Designator);
        Assert.Equal(new[] { 1, 2 }, u1.Parts.Select(p => p.PartId).ToArray());
        Assert.Equal("MCU-100", u1.Comment);
        Assert.Same(u1, hierarchy.FindComponentByUniqueIdPath("\\SYMIOIOO\\UONEPRTA"));
        Assert.Same(u1, hierarchy.FindComponentByUniqueIdPath("\\UONEPRTB"));
        // The PCB can pair one part's sheet path with another part's id.
        Assert.Same(u1, hierarchy.FindComponentByUniqueIdPath("\\SYMIOIOO\\UONEPRTB"));
    }

    [Fact]
    public void MultiPartComponent_InAChannel_MergesPerChannel()
    {
        var top = Sheet("Top.SchDoc", Symbol("Repeat(CH,1,2)", "Channel.SchDoc", "SYMCHANL"));
        var channel = Sheet("Channel.SchDoc",
            Part("U2", "UTWOPRTA", partId: 1, partCount: 2),
            Part("U2", "UTWOPRTB", partId: 2, partCount: 2));

        var hierarchy = ProjectHierarchy.Build(null, new[] { top, channel });

        Assert.Equal(new[] { "U2_CH1", "U2_CH2" }, Designators(hierarchy));
        Assert.All(hierarchy.Components, c => Assert.Equal(2, c.Parts.Count));
        Assert.Equal(4, hierarchy.Parts.Count);
    }

    [Fact]
    public void DuplicateDesignators_AndUnannotatedParts_AreReported()
    {
        var top = Sheet("Top.SchDoc",
            Part("R1", "RESONEAA"), Part("R1", "RESONEBB"),
            Part("R?", "UNANNOTA"), Part("R?", "UNANNOTB"));

        var hierarchy = ProjectHierarchy.Build(null, new[] { top });

        Assert.Equal(3, hierarchy.Components.Count); // R1 once, each R? apart
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("'R1'", StringComparison.Ordinal));
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("not annotated", StringComparison.Ordinal));
        Assert.Null(hierarchy.FindComponent("R?"));
    }

    [Fact]
    public void ComponentsWithoutDesignator_AreLeftOut()
    {
        var logo = new SchComponent { Name = "LOGO", UniqueId = "LOGOLOGO" };
        var hierarchy = ProjectHierarchy.Build(null, new[] { Sheet("Top.SchDoc", logo, Part("R1", "RESISTOR")) });

        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("no designator", StringComparison.Ordinal));
    }

    [Fact]
    public void PhysicalComponent_ExposesTypeAndResolvedParameters()
    {
        var project = Project("Top.SchDoc");
        var section = new ProjectSection("Parameter1");
        section.Add("Name", "Assembly");
        section.Add("Value", "PCA-7");
        project.Sections.Add(section);
        var testPoint = Part("TP1", "TESTPNTA", 1, 1, ("Comment", "TestPoint"));
        testPoint.ComponentKindVersion2 = (int)ComponentType.StandardNoBom;
        var top = Sheet("Top.SchDoc", testPoint, Part("R1", "RESISTOR", 1, 1, ("Comment", "=Value"), ("Value", "10k"), ("Note", "=Assembly")));

        var hierarchy = ProjectHierarchy.Build(project, new[] { top });

        var tp1 = hierarchy.FindComponent("tp1")!;
        Assert.Equal(ComponentType.StandardNoBom, tp1.ComponentType);
        Assert.False(tp1.IsInBom);
        var r1 = hierarchy.FindComponent("R1")!;
        Assert.True(r1.IsInBom);
        Assert.Equal("10k", r1.Comment);
        Assert.Equal("PCA-7", r1.GetParameterValue("Note"));
        Assert.Null(r1.GetParameterValue("Missing"));
    }

    // ---- roots and problems ----

    [Fact]
    public void Roots_PreferTheStructureTopLevelDocument()
    {
        var project = Project("Spare.SchDoc", "Top.SchDoc", "Child.SchDoc");
        project.Structure = ProjectStructure.Parse("Record=TopLevelDocument|FileName=Top.SchDoc\r\n");
        var spare = Sheet("Spare.SchDoc", Part("R9", "SPARERES"));
        var top = Sheet("Top.SchDoc", Symbol("U_Child", "Child.SchDoc", "SYMCHILD"));
        var child = Sheet("Child.SchDoc", Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(project, new[] { spare, top, child });

        Assert.Equal("Top.SchDoc", Assert.Single(hierarchy.Roots).FileName);
        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("'Spare.SchDoc' is not part of the hierarchy", StringComparison.Ordinal));
    }

    [Fact]
    public void Roots_IgnoreAStaleStructure_AndOtherwiseAreTheUnreferencedSheets()
    {
        var project = Project("Top.SchDoc", "Child.SchDoc");
        project.Structure = ProjectStructure.Parse("Record=TopLevelDocument|FileName=Child.SchDoc\r\n");
        var top = Sheet("Top.SchDoc", Symbol("U_Child", "Child.SchDoc", "SYMCHILD"));
        var child = Sheet("Child.SchDoc", Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(project, new[] { top, child });

        Assert.Equal("Top.SchDoc", Assert.Single(hierarchy.Roots).FileName);
        Assert.Equal(2, hierarchy.Sheets.Count);
    }

    [Fact]
    public void MissingSubSheets_AndCycles_AreReported()
    {
        var top = Sheet("Top.SchDoc",
            Symbol("U_Gone", "Gone.SchDoc", "SYMGONEE", xMils: 1000),
            Symbol("U_Loop", "Loop.SchDoc", "SYMLOOPP", xMils: 5000));
        var loop = Sheet("Loop.SchDoc", Symbol("U_Back", "Loop.SchDoc", "SYMBACKK"), Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(null, new[] { top, loop });

        Assert.Equal(2, hierarchy.Sheets.Count);
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("'Gone.SchDoc', which was not supplied", StringComparison.Ordinal));
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("one of its own parents", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Repeat(CH,1,99999999999)")]
    [InlineData("Repeat(CH,١,٤)")]
    public void UnreadableRepeat_IsOneInstance_AndReported(string designator)
    {
        var top = Sheet("Top.SchDoc", Symbol(designator, "Channel.SchDoc", "SYMCHANL"));
        var channel = Sheet("Channel.SchDoc", Part("R1", "RESISTOR"));

        var hierarchy = ProjectHierarchy.Build(null, new[] { top, channel });

        Assert.Equal(2, hierarchy.Sheets.Count);
        Assert.Equal(new[] { "R1" }, Designators(hierarchy));
        Assert.Contains(hierarchy.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("cannot be read", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentsWithoutAFileName_AreSkipped()
    {
        var hierarchy = ProjectHierarchy.Build(null, new[] { new SchDocument() });

        Assert.Empty(hierarchy.Sheets);
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("without a file name", StringComparison.Ordinal));
    }

    // ---- loading ----

    private static byte[] Bytes(SchDocument document)
    {
        using var ms = new MemoryStream();
        new SchDocWriter().Write(document, ms);
        return ms.ToArray();
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task LoadAsync_FromStreams_BuildsTheSameHierarchy()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["Top.SchDoc"] = Bytes(Sheet("Top.SchDoc", Symbol("Repeat(CH,1,2)", "Channel.SchDoc", "SYMCHANL"))),
            ["sheets/Channel.SchDoc"] = Bytes(Sheet("Channel.SchDoc", Part("R1", "RESISTOR", 1, 1, ("Comment", "=Value"), ("Value", "4k7")))),
        };
        var prjText = "[Design]\r\nChannelDesignatorFormatString=$Component_$RoomName\r\n\r\n" +
                      "[Document1]\r\nDocumentPath=Top.SchDoc\r\n\r\n" +
                      "[Document2]\r\nDocumentPath=sheets\\Channel.SchDoc\r\n\r\n" +
                      "[Document3]\r\nDocumentPath=Missing.SchDoc\r\n\r\n";
        using var prj = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(prjText));
        using var structure = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Record=TopLevelDocument|FileName=Top.SchDoc\r\n"));
        var project = await AltiumLibrary.OpenProjectAsync(prj, structure);

        var opened = new List<ForwardOnlyStream>();
        var hierarchy = await ProjectHierarchy.LoadAsync(project, document =>
        {
            var key = document.DocumentPath!.Replace('\\', '/');
            if (!files.TryGetValue(key, out var bytes))
                return null;
            var stream = new ForwardOnlyStream(bytes);
            opened.Add(stream);
            return stream;
        });

        Assert.Equal(new[] { "R1_CH1", "R1_CH2" }, Designators(hierarchy));
        Assert.Equal("4k7", hierarchy.FindComponent("R1_CH2")!.Comment);
        Assert.Equal(new[] { "Top.SchDoc", "Channel.SchDoc" }, hierarchy.Documents.Keys.ToArray());
        Assert.All(opened, s => Assert.True(s.Disposed));
        Assert.Contains(hierarchy.Diagnostics, d => d.Message.Contains("'Missing.SchDoc' was not supplied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_NamesTheDocument_WhenASheetIsCorrupt()
    {
        var project = Project("Top.SchDoc");

        var ex = await Assert.ThrowsAsync<AltiumCorruptFileException>(async () =>
            await ProjectHierarchy.LoadAsync(project, _ => new MemoryStream(new byte[1024])));

        Assert.Equal("Top.SchDoc", ex.FilePath);
    }

    [Fact]
    public async Task LoadAsync_FromDisk_ReadsTheProjectFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hier_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "Top.SchDoc"),
                Bytes(Sheet("Top.SchDoc", Symbol("U_A", "Amp.SchDoc", "SYMAMPAA", xMils: 1000), Symbol("U_A", "Amp.SchDoc", "SYMAMPBB", xMils: 3000))));
            await File.WriteAllBytesAsync(Path.Combine(dir, "Amp.SchDoc"), Bytes(Sheet("Amp.SchDoc", Part("Q1", "TRANSIST"))));
            var project = Project("Top.SchDoc", "Amp.SchDoc");
            project.FilePath = Path.Combine(dir, "Board.PrjPcb");

            var hierarchy = await ProjectHierarchy.LoadAsync(project);

            Assert.Equal(new[] { "Q1_U_A1", "Q1_U_A2" }, Designators(hierarchy));
            Assert.Equal("Q1_U_A2", hierarchy.FindComponentByUniqueIdPath("\\SYMAMPBB\\TRANSIST")!.Designator);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

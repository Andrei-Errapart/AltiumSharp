using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Connectivity;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Enums;
using Xunit;

namespace OriginalCircuit.Altium.Tests.Connectivity;

/// <summary>
/// Integration tests for the project-wide hierarchical netlist solver. Each test writes a couple of
/// tiny schematic documents to a temp directory, wires up a project, and asserts cross-sheet merging.
/// </summary>
public class ProjectNetlistTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "conn_" + Guid.NewGuid().ToString("N"));

    public ProjectNetlistTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- builders ----

    private static SchComponent Comp(string designator, string pin, int x, int y)
    {
        var c = new SchComponent { Name = designator, PartCount = 1, CurrentPartId = 1 };
        c.AddParameter(new SchParameter { Name = "Designator", Value = designator });
        c.AddPin(SchPin.Create(pin).At(Coord.FromMils(x), Coord.FromMils(y))
            .Length(Coord.FromMils(0)).Orient(PinOrientation.Right).Build());
        return c;
    }

    private static SchWire Wire(int x1, int y1, int x2, int y2)
    {
        var w = new SchWire();
        w.AddVertex(new CoordPoint(Coord.FromMils(x1), Coord.FromMils(y1)));
        w.AddVertex(new CoordPoint(Coord.FromMils(x2), Coord.FromMils(y2)));
        return w;
    }

    private async Task WriteDoc(string name, Action<SchDocument> build)
    {
        var doc = new SchDocument();
        build(doc);
        await doc.SaveAsync(Path.Combine(_dir, name));
    }

    private async Task<AltiumProject> Project(params string[] schDocNames)
    {
        var project = AltiumLibrary.CreateProject();
        project.FilePath = Path.Combine(_dir, "test.PrjPcb");
        foreach (var n in schDocNames)
            project.AddDocument(n);
        await Task.CompletedTask;
        return project;
    }

    // ---- tests ----

    [Fact]
    public async Task Port_And_SheetEntry_Merge_Across_Boundary()
    {
        // Parent: pin P1.1 -- wire -- sheet entry "NETX" (right edge of a sheet symbol -> child.SchDoc).
        await WriteDoc("parent.SchDoc", doc =>
        {
            var sym = new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = "child.SchDoc",
                SheetName = "U_CHILD",
            };
            sym.AddEntry(new SchSheetEntry { Side = 1, DistanceFromTop = Coord.FromMils(100), Name = "NETX" });
            doc.AddPrimitive(sym);
            // Entry connection point = (right=1500, top-100 = 1900).
            doc.AddPrimitive(Wire(1500, 1900, 2000, 1900));
            doc.AddComponent(Comp("P1", "1", 2000, 1900));
        });

        // Child: pin C1.1 -- wire -- port "NETX".
        await WriteDoc("child.SchDoc", doc =>
        {
            doc.AddPrimitive(new SchPort
            {
                Name = "NETX",
                Location = new CoordPoint(Coord.FromMils(500), Coord.FromMils(500)),
                Width = Coord.FromMils(200),
                ConnectedEnd = 1, // left end = Location
            });
            doc.AddPrimitive(Wire(500, 500, 1000, 500));
            doc.AddComponent(Comp("C1", "1", 1000, 500));
        });

        var project = await Project("parent.SchDoc", "child.SchDoc");
        var pnl = await ProjectNetlistBuilder.BuildAsync(project);

        var pNet = pnl.NetForPin("P1", "1");
        var cNet = pnl.NetForPin("C1", "1");
        Assert.NotNull(pNet);
        Assert.NotNull(cNet);
        Assert.Equal(pNet, cNet); // merged across the port <-> sheet-entry boundary
    }

    [Fact]
    public async Task Power_Net_Unifies_Globally_Across_Sheets()
    {
        await WriteDoc("parent.SchDoc", doc =>
        {
            var sym = new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = "child.SchDoc",
                SheetName = "U_CHILD",
            };
            doc.AddPrimitive(sym);
            doc.AddPrimitive(Wire(3000, 3000, 3200, 3000));
            doc.AddComponent(Comp("P1", "1", 3000, 3000));
            doc.AddPrimitive(new SchPowerObject { Text = "GND", Location = new CoordPoint(Coord.FromMils(3200), Coord.FromMils(3000)) });
        });

        await WriteDoc("child.SchDoc", doc =>
        {
            doc.AddPrimitive(Wire(500, 500, 700, 500));
            doc.AddComponent(Comp("C1", "1", 500, 500));
            doc.AddPrimitive(new SchPowerObject { Text = "GND", Location = new CoordPoint(Coord.FromMils(700), Coord.FromMils(500)) });
        });

        var project = await Project("parent.SchDoc", "child.SchDoc");
        var pnl = await ProjectNetlistBuilder.BuildAsync(project);

        var pNet = pnl.NetForPin("P1", "1");
        Assert.NotNull(pNet);
        Assert.Equal("GND", pNet!.Name);
        Assert.Equal(pNet, pnl.NetForPin("C1", "1"));
    }

    [Fact]
    public async Task Two_Sheets_Of_One_File_Name_In_Two_Folders_Are_Not_Channels()
    {
        // A\Power.SchDoc and B\Power.SchDoc are two different sheets, each placed once. Taken by
        // file name for one sheet placed twice, they became two channels with channel-private power,
        // and GND fell apart into one net per sheet.
        Directory.CreateDirectory(Path.Combine(_dir, "A"));
        Directory.CreateDirectory(Path.Combine(_dir, "B"));
        await WriteDoc("parent.SchDoc", doc =>
        {
            doc.AddPrimitive(new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = @"A\Power.SchDoc",
                SheetName = "U_A",
            });
            doc.AddPrimitive(new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(2000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = @"B\Power.SchDoc",
                SheetName = "U_B",
            });
        });
        foreach (var (folder, designator) in new[] { ("A", "R1"), ("B", "R2") })
        {
            await WriteDoc(Path.Combine(folder, "Power.SchDoc"), doc =>
            {
                doc.AddPrimitive(Wire(500, 500, 700, 500));
                doc.AddComponent(Comp(designator, "1", 500, 500));
                doc.AddPrimitive(new SchPowerObject { Text = "GND", Location = new CoordPoint(Coord.FromMils(700), Coord.FromMils(500)) });
            });
        }

        var project = await Project("parent.SchDoc", @"A\Power.SchDoc", @"B\Power.SchDoc");
        var pnl = await ProjectNetlistBuilder.BuildAsync(project);

        var gnd = pnl.NetForPin("R1", "1");
        Assert.NotNull(gnd);
        Assert.Equal("GND", gnd!.Name);
        Assert.Same(gnd, pnl.NetForPin("R2", "1"));
        Assert.All(pnl.Sheets.Where(s => s.FileName == "Power.SchDoc"), s => Assert.False(s.IsRepeated));
        Assert.DoesNotContain(pnl.Diagnostics, d => d.Message.Contains("multi-channel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NetLabel_Scope_Flat_Merges_But_Hierarchical_Does_Not()
    {
        // Two sheets (parent references child), each with a wire+pin labelled "SIG", not otherwise joined.
        await WriteDoc("parent.SchDoc", doc =>
        {
            var sym = new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = "child.SchDoc",
                SheetName = "U_CHILD",
            };
            doc.AddPrimitive(sym);
            doc.AddPrimitive(Wire(3000, 3000, 3200, 3000));
            doc.AddComponent(Comp("P1", "1", 3000, 3000));
            doc.AddPrimitive(new SchNetLabel { Text = "SIG", Location = new CoordPoint(Coord.FromMils(3000), Coord.FromMils(3000)) });
        });

        await WriteDoc("child.SchDoc", doc =>
        {
            doc.AddPrimitive(Wire(500, 500, 700, 500));
            doc.AddComponent(Comp("C1", "1", 500, 500));
            doc.AddPrimitive(new SchNetLabel { Text = "SIG", Location = new CoordPoint(Coord.FromMils(500), Coord.FromMils(500)) });
        });

        var project = await Project("parent.SchDoc", "child.SchDoc");

        var flat = await ProjectNetlistBuilder.BuildAsync(project,
            new NetlistOptions { Scope = NetIdentifierScope.Flat, ScopeIsExplicit = true });
        Assert.Equal(flat.NetForPin("P1", "1"), flat.NetForPin("C1", "1")); // global labels merge

        var hier = await ProjectNetlistBuilder.BuildAsync(project,
            new NetlistOptions { Scope = NetIdentifierScope.Hierarchical, ScopeIsExplicit = true });
        Assert.NotNull(hier.NetForPin("P1", "1"));
        Assert.NotEqual(hier.NetForPin("P1", "1"), hier.NetForPin("C1", "1")); // sheet-local labels stay apart
    }

    [Fact]
    public async Task NetLabel_Joins_The_Power_Net_Of_Its_Name()
    {
        // TE0965: VCC_FSIO_BANK1 is a net label on two sheets and a power port on a third; Altium's
        // board has one net. Flat scope joins the label to the rail anywhere; hierarchical scope only
        // through a power port of that name on the label's own sheet.
        await WriteDoc("parent.SchDoc", doc =>
        {
            doc.AddPrimitive(new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = "child.SchDoc",
                SheetName = "U_CHILD",
            });
            doc.AddPrimitive(Wire(3000, 3000, 3200, 3000));
            doc.AddComponent(Comp("P1", "1", 3000, 3000));
            doc.AddPrimitive(new SchNetLabel { Text = "VCC_IO", Location = new CoordPoint(Coord.FromMils(3000), Coord.FromMils(3000)) });
            doc.AddPrimitive(Wire(4000, 3000, 4200, 3000));
            doc.AddComponent(Comp("P2", "1", 4000, 3000));
            doc.AddPrimitive(new SchNetLabel { Text = "VCC_LOCAL", Location = new CoordPoint(Coord.FromMils(4000), Coord.FromMils(3000)) });
            doc.AddPrimitive(Wire(5000, 3000, 5200, 3000));
            doc.AddComponent(Comp("P3", "1", 5000, 3000));
            doc.AddPrimitive(new SchPowerObject { Text = "vcc_local", Location = new CoordPoint(Coord.FromMils(5200), Coord.FromMils(3000)) });
        });

        await WriteDoc("child.SchDoc", doc =>
        {
            doc.AddPrimitive(Wire(500, 500, 700, 500));
            doc.AddComponent(Comp("C1", "1", 500, 500));
            doc.AddPrimitive(new SchPowerObject { Text = "VCC_IO", Location = new CoordPoint(Coord.FromMils(700), Coord.FromMils(500)) });
        });

        var project = await Project("parent.SchDoc", "child.SchDoc");

        var flat = await ProjectNetlistBuilder.BuildAsync(project,
            new NetlistOptions { Scope = NetIdentifierScope.Flat, ScopeIsExplicit = true });
        Assert.Equal(flat.NetForPin("P1", "1"), flat.NetForPin("C1", "1"));
        Assert.Equal(flat.NetForPin("P2", "1"), flat.NetForPin("P3", "1"));

        var hier = await ProjectNetlistBuilder.BuildAsync(project,
            new NetlistOptions { Scope = NetIdentifierScope.Hierarchical, ScopeIsExplicit = true });
        Assert.NotEqual(hier.NetForPin("P1", "1"), hier.NetForPin("C1", "1"));
        Assert.Equal(hier.NetForPin("P2", "1"), hier.NetForPin("P3", "1"));
    }

    [Fact]
    public async Task Repeat_Sheet_Symbol_Instantiates_Distinct_Channels()
    {
        // Child sheet with one component pin on a sheet-local net "SIG".
        await WriteDoc("rchild.SchDoc", doc =>
        {
            doc.AddPrimitive(Wire(0, 0, 100, 0));
            doc.AddComponent(Comp("X1", "1", 0, 0));
            doc.AddPrimitive(new SchNetLabel { Text = "SIG", Location = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)) });
        });

        // Parent instantiates the child three times via a Repeat() directive on the sheet symbol.
        await WriteDoc("rparent.SchDoc", doc =>
        {
            doc.AddPrimitive(new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = "rchild.SchDoc",
                SheetName = "Repeat(CH,1,3)",
                NameLabel = new SchLabel { Text = "Repeat(CH,1,3)" },
            });
        });

        var project = await Project("rparent.SchDoc", "rchild.SchDoc");
        var pnl = await ProjectNetlistBuilder.BuildAsync(project);

        var channels = pnl.Sheets.Where(s => s.FileName == "rchild.SchDoc").ToList();
        Assert.Equal(3, channels.Count);
        Assert.All(channels, c => Assert.True(c.IsRepeated));
        Assert.Equal(new[] { 1, 2, 3 }, channels.Select(c => c.ChannelIndex!.Value).OrderBy(i => i).ToArray());
        Assert.All(channels, c => Assert.Equal("CH", c.ChannelName));

        // Each channel's "SIG" is a distinct net (X1.1 resolves to a different net per channel).
        var nets = channels.Select(c => pnl.NetForPin(c.Id, "X1", "1")).ToList();
        Assert.All(nets, n => Assert.NotNull(n));
        Assert.Equal(3, nets.Distinct().Count());
    }

    [Fact]
    public async Task A_Channel_Pin_Carries_The_Channels_Physical_Designator()
    {
        await WriteDoc("rchild.SchDoc", doc =>
        {
            doc.AddPrimitive(Wire(0, 0, 100, 0));
            doc.AddComponent(Comp("X1", "1", 0, 0));
            doc.AddPrimitive(new SchNetLabel { Text = "SIG", Location = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)) });
        });
        await WriteDoc("rparent.SchDoc", doc =>
        {
            doc.AddComponent(Comp("P1", "1", 3000, 0));
            doc.AddPrimitive(new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = "rchild.SchDoc",
                SheetName = "Repeat(CH,1,3)",
                NameLabel = new SchLabel { Text = "Repeat(CH,1,3)" },
            });
        });

        var project = await Project("rparent.SchDoc", "rchild.SchDoc");
        var pnl = await ProjectNetlistBuilder.BuildAsync(project);

        Assert.NotNull(pnl.Hierarchy);
        Assert.Equal(pnl.Sheets.Select(s => s.Id), pnl.Hierarchy!.Sheets.Select(s => s.Index));

        // One X1.1 per channel, told apart by the designator the board carries.
        var channelPins = pnl.Nets.SelectMany(n => n.Pins).Where(p => p.ComponentDesignator == "X1").ToList();
        Assert.Equal(new[] { "X1_CH1", "X1_CH2", "X1_CH3" }, channelPins.Select(pnl.PhysicalDesignatorOf).OrderBy(d => d, StringComparer.Ordinal));

        // A component on a sheet placed once keeps its logical designator.
        var parentPin = pnl.Nets.SelectMany(n => n.Pins).Concat(pnl.UnconnectedPins).Single(p => p.ComponentDesignator == "P1");
        Assert.Equal("P1", pnl.PhysicalDesignatorOf(parentPin));
    }

    [Fact]
    public async Task Repeat_Channel_UidPath_Matches_Altiums_Pcb_SourceUniqueId()
    {
        await WriteDoc("rchild.SchDoc", doc => doc.AddComponent(Comp("X1", "1", 0, 0)));
        await WriteDoc("rparent.SchDoc", doc => doc.AddPrimitive(new SchSheetSymbol
        {
            Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
            XSize = Coord.FromMils(500),
            YSize = Coord.FromMils(400),
            FileName = "rchild.SchDoc",
            SheetName = "Repeat(CH,1,3)",
            UniqueId = "SYMREPTA",
        }));

        var project = await Project("rparent.SchDoc", "rchild.SchDoc");
        var pnl = await ProjectNetlistBuilder.BuildAsync(project);

        // Altium's PCB names channel k of a Repeat symbol "{k}{symbolUid}", e.g. \2SYMREPTA\<component uid>.
        var channel2 = pnl.FindInstanceByUidPath(new[] { "2SYMREPTA" });
        Assert.NotNull(channel2);
        Assert.Equal(2, channel2!.ChannelIndex);
        Assert.Equal(new[] { "2SYMREPTA" }, channel2.SymbolUidPath);
    }

    [Fact]
    public async Task Build_FromInMemoryHierarchy_MatchesBuildAsync()
    {
        SchDocument parent = null!, child = null!;
        await WriteDoc("parent.SchDoc", doc =>
        {
            var sym = new SchSheetSymbol
            {
                Location = new CoordPoint(Coord.FromMils(1000), Coord.FromMils(2000)),
                XSize = Coord.FromMils(500),
                YSize = Coord.FromMils(400),
                FileName = "child.SchDoc",
                SheetName = "U_CHILD",
            };
            sym.AddEntry(new SchSheetEntry { Side = 1, DistanceFromTop = Coord.FromMils(100), Name = "NETX" });
            doc.AddPrimitive(sym);
            doc.AddPrimitive(Wire(1500, 1900, 2000, 1900));
            doc.AddComponent(Comp("P1", "1", 2000, 1900));
            parent = doc;
        });
        await WriteDoc("child.SchDoc", doc =>
        {
            doc.AddPrimitive(new SchPort
            {
                Name = "NETX",
                Location = new CoordPoint(Coord.FromMils(500), Coord.FromMils(500)),
                Width = Coord.FromMils(200),
                ConnectedEnd = 1,
            });
            doc.AddPrimitive(Wire(500, 500, 1000, 500));
            doc.AddComponent(Comp("C1", "1", 1000, 500));
            child = doc;
        });
        parent.FileName = "parent.SchDoc";
        child.FileName = "child.SchDoc";

        var project = await Project("parent.SchDoc", "child.SchDoc");
        var pnl = ProjectNetlistBuilder.Build(ProjectHierarchy.Build(project, new[] { parent, child }));

        Assert.NotNull(pnl.NetForPin("P1", "1"));
        Assert.Equal(pnl.NetForPin("P1", "1"), pnl.NetForPin("C1", "1"));
        Assert.Equal(2, pnl.Sheets.Count);
    }
}

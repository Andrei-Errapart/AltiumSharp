using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Tests that embedded payloads (schematic images, PCB 3D models) inflate only as far as
/// <see cref="AltiumReaderOptions"/> allows, and can be skipped.
/// </summary>
public sealed class EmbeddedPayloadTests
{
    private static SchImage Image(int bytes, byte fill, int x) => new()
    {
        Corner1 = new CoordPoint(Coord.FromMils(x), Coord.FromMils(100)),
        Corner2 = new CoordPoint(Coord.FromMils(x + 200), Coord.FromMils(300)),
        EmbedImage = true,
        Filename = $"logo{x}.bmp",
        ImageData = Enumerable.Repeat(fill, bytes).ToArray(),
    };

    private static byte[] SheetWith(params SchImage[] images)
    {
        var sheet = new SchDocument();
        foreach (var image in images)
            sheet.AddPrimitive(image);
        using var ms = new MemoryStream();
        new SchDocWriter().Write(sheet, ms);
        return ms.ToArray();
    }

    private static SchImage[] Images(SchDocument sheet) => sheet.Images.Cast<SchImage>().ToArray();

    [Fact]
    public void Images_AreReadByDefault()
    {
        var sheet = new SchDocReader().Read(new MemoryStream(SheetWith(Image(4_000_000, 1, 100), Image(1000, 2, 1000))));

        Assert.Equal(4_000_000, Images(sheet)[0].ImageData!.Length);
        Assert.Equal(1000, Images(sheet)[1].ImageData!.Length);
        Assert.Empty(sheet.Diagnostics);
    }

    [Fact]
    public void AnImagePastTheLimit_IsLeftOut_AndTheNextKeepsItsOwnData()
    {
        var options = new AltiumReaderOptions { MaxInflatedPayloadBytes = 1_000_000 };
        var bytes = SheetWith(Image(4_000_000, 1, 100), Image(1000, 2, 1000));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sheet = new SchDocReader(options).Read(new MemoryStream(bytes));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Null(Images(sheet)[0].ImageData);
        Assert.Equal(Enumerable.Repeat((byte)2, 1000), Images(sheet)[1].ImageData!);
        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("MaxInflatedPayloadBytes", StringComparison.Ordinal));
        Assert.True(allocated < 4_000_000, $"allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ImagesPastTheDocumentsTotal_AreLeftOut()
    {
        var options = new AltiumReaderOptions { MaxInflatedTotalBytes = 1_000_000 };

        var sheet = new SchDocReader(options).Read(new MemoryStream(SheetWith(Image(600_000, 1, 100), Image(600_000, 2, 1000))));

        Assert.NotNull(Images(sheet)[0].ImageData);
        Assert.Null(Images(sheet)[1].ImageData);
        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("MaxInflatedTotalBytes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Images_CanBeSkipped()
    {
        var bytes = SheetWith(Image(4_000_000, 1, 100));
        var skip = new AltiumReaderOptions { ReadEmbeddedPayloads = false };

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sheet = (SchDocument)await AltiumLibrary.OpenSchDocAsync(new MemoryStream(bytes), skip);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Null(Images(sheet)[0].ImageData);
        Assert.Equal("logo100.bmp", Images(sheet)[0].Filename);
        Assert.Empty(sheet.Diagnostics);
        Assert.True(allocated < 4_000_000, $"allocated {allocated:N0} bytes");
    }

    [Fact]
    public async Task TheHierarchysLoaders_ReadSheetsWithItsReaderOptions()
    {
        var bytes = SheetWith(Image(1000, 1, 100));
        var project = AltiumLibrary.CreateProject();
        project.AddDocument("Top.SchDoc");
        var options = new ProjectHierarchyOptions { ReaderOptions = new AltiumReaderOptions { ReadEmbeddedPayloads = false } };

        var hierarchy = await ProjectHierarchy.LoadAsync(project, _ => new MemoryStream(bytes), options);

        Assert.Null(Images(hierarchy.Sheets[0].Document)[0].ImageData);
    }

    private static byte[] BoardWith(params PcbModel[] models)
    {
        var board = new PcbDocument();
        board.Models.AddRange(models);
        using var ms = new MemoryStream();
        new PcbDocWriter().Write(board, ms);
        return ms.ToArray();
    }

    private static PcbModel Model(string name, int characters) => new()
    {
        Id = "{MODEL-" + name + "}",
        Name = name,
        IsEmbedded = true,
        StepData = new string('A', characters),
    };

    [Fact]
    public void ModelsPastTheLimit_AreLeftOut_AndTheirOthersKept()
    {
        var bytes = BoardWith(Model("BIG.step", 4_000_000), Model("SMALL.step", 1000));
        var options = new AltiumReaderOptions { MaxInflatedPayloadBytes = 1_000_000 };

        var board = new PcbDocReader(options).Read(new MemoryStream(bytes));

        Assert.Equal(new[] { "BIG.step", "SMALL.step" }, board.Models.Select(m => m.Name).ToArray());
        Assert.Equal(string.Empty, board.Models[0].StepData);
        Assert.Equal(1000, board.Models[1].StepData.Length);
        Assert.Contains(board.Diagnostics, d => d.Message.Contains("3D model 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Models_CanBeSkipped()
    {
        var bytes = BoardWith(Model("BIG.step", 4_000_000));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var board = (PcbDocument)await AltiumLibrary.OpenPcbDocAsync(new MemoryStream(bytes), new AltiumReaderOptions { ReadEmbeddedPayloads = false });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal("BIG.step", Assert.Single(board.Models).Name);
        Assert.Equal(string.Empty, board.Models[0].StepData);
        Assert.True(allocated < 4_000_000, $"allocated {allocated:N0} bytes");
        Assert.Equal(4_000_000, new PcbDocReader().Read(new MemoryStream(bytes)).Models[0].StepData.Length);
    }

    [Fact]
    public void Options_RefuseLimitsThatAreNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AltiumReaderOptions { MaxInflatedPayloadBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AltiumReaderOptions { MaxInflatedTotalBytes = -1 });
    }
}

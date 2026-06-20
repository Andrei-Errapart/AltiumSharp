using OriginalCircuit.Altium.Barcodes;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Raster;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;
using SkiaSharp;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// Integration tests for rendering 2-D barcodes (PCB <c>Text</c> with <see cref="PcbBarCodeKind.DataMatrix"/>
/// or <see cref="PcbBarCodeKind.QrCode"/>). Mirrors the Coherent Digitiser: an inverted symbol on the top
/// solder-mask layer over a copper backing fill, which should read as a gold field with green data modules.
/// </summary>
public sealed class BarcodeRenderingTests
{
    // ENIG finish (gold) the realistic renderer reveals through a mask opening: RGB(0xBE,0x90,0x42).
    private static bool IsGold(byte r, byte g, byte b)
        => Math.Abs(r - 0xBE) < 45 && Math.Abs(g - 0x90) < 45 && Math.Abs(b - 0x42) < 45 && r > g && g > b;

    private static PcbDocument BuildBoard(PcbBarCodeKind? kind)
    {
        var board = new PcbDocument
        {
            BoardParameters = new Dictionary<string, string>
            {
                ["KIND0"] = "0", ["VX0"] = "0mil",   ["VY0"] = "0mil",
                ["KIND1"] = "0", ["VX1"] = "560mil", ["VY1"] = "0mil",
                ["KIND2"] = "0", ["VX2"] = "560mil", ["VY2"] = "560mil",
                ["KIND3"] = "0", ["VX3"] = "0mil",   ["VY3"] = "560mil",
                ["KIND4"] = "0", ["VX4"] = "0mil",   ["VY4"] = "0mil",
            },
        };

        // Copper backing fill (layer 1) under the barcode, so the mask openings reveal gold finish.
        board.AddFill(new PcbFill
        {
            Layer = 1,
            Corner1 = new CoordPoint(Coord.FromMm(3), Coord.FromMm(3)),
            Corner2 = new CoordPoint(Coord.FromMm(11), Coord.FromMm(11)),
        });

        if (kind is { } k)
        {
            board.AddText(new PcbText
            {
                Text = "8CHDIG01-V1R0",
                Location = new CoordPoint(Coord.FromMm(3.25), Coord.FromMm(3.25)),
                Layer = 37,                          // top solder mask
                TextKind = PcbTextKind.BarCode,
                BarCodeType = k,
                BarCodeInverted = true,
                InvertedRectWidth = Coord.FromMm(7.5),  // Altium stores the 2-D barcode box here
                InvertedRectHeight = Coord.FromMm(7.5),
                BarCodeXMargin = Coord.FromMm(0.5),
                BarCodeYMargin = Coord.FromMm(0.5),
            });
        }

        return board;
    }

    private static async Task<(int gold, int green)> RenderAndCountAsync(PcbDocument board)
    {
        var renderer = new RasterRenderer();
        using var ms = new MemoryStream();
        await renderer.RenderRealisticAsync(board, ms, new RenderOptions { Width = 500, Height = 500 },
            PcbRealisticStyle.GreenEnig.For(PcbViewSide.Top));

        ms.Position = 0;
        using var bitmap = SKBitmap.Decode(ms);
        Assert.NotNull(bitmap);
        int gold = 0, green = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (IsGold(p.Red, p.Green, p.Blue)) gold++;
                else if (p.Green > p.Red && p.Green > p.Blue) green++;
            }
        return (gold, green);
    }

    [Theory]
    [InlineData(PcbBarCodeKind.DataMatrix)]
    [InlineData(PcbBarCodeKind.QrCode)]
    public async Task InvertedBarcode_OnSolderMask_RevealsGoldField(PcbBarCodeKind kind)
    {
        // Without the barcode the copper stays fully masked (no gold); the inverted symbol opens the mask over
        // its field, exposing a large gold area while the dark data modules stay green-masked.
        var (goldWithout, _) = await RenderAndCountAsync(BuildBoard(null));
        var (goldWith, greenWith) = await RenderAndCountAsync(BuildBoard(kind));

        Assert.True(goldWith > goldWithout + 1000,
            $"{kind}: inverted symbol should expose gold finish (with={goldWith}, without={goldWithout}).");
        Assert.True(greenWith > 0, $"{kind}: data modules should remain green-masked.");
    }
}

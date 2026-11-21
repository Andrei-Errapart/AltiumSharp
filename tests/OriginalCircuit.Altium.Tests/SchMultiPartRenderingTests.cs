using System.Xml.Linq;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Altium.Tests;

/// <summary>
/// A multi-part symbol placed on a sheet stores every part's pins and body graphics (tagged with
/// OwnerPartId); each placed instance must draw only the part it represents (CurrentPartId), not every
/// part stacked on top of one another, while keeping its component-level (OwnerPartId -1) designator.
/// </summary>
public sealed class SchMultiPartRenderingTests
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    private static SchComponent MultiPartComponent(int currentPart)
    {
        var component = new SchComponent
        {
            Name = "MCU",
            PartCount = 3, // Altium stores parts + 1
            CurrentPartId = currentPart,
            Location = new CoordPoint(Coord.FromMils(2000), Coord.FromMils(2000)),
        };
        component.AddParameter(new SchParameter
        {
            Name = "Designator",
            Value = "U7",
            OwnerPartId = -1,
            Location = new CoordPoint(Coord.FromMils(2000), Coord.FromMils(2600)),
        });
        for (var part = 1; part <= 2; part++)
        {
            component.AddRectangle(new SchRectangle
            {
                OwnerPartId = part,
                Corner1 = new CoordPoint(Coord.FromMils(2000), Coord.FromMils(2000)),
                Corner2 = new CoordPoint(Coord.FromMils(2800), Coord.FromMils(2500)),
            });
            component.AddPin(new SchPin
            {
                OwnerPartId = part,
                Name = $"PARTPIN{part}",
                Designator = $"P{part}",
                Location = new CoordPoint(Coord.FromMils(1800), Coord.FromMils(2200)),
                Length = Coord.FromMils(200),
                Orientation = PinOrientation.Left,
            });
        }
        return component;
    }

    private static async Task<List<string>> RenderSheetTexts(SchDocument document)
    {
        using var ms = new MemoryStream();
        await new SvgRenderer().RenderAsync(document, ms, new RenderOptions { Width = 1600, Height = 1200 });
        ms.Position = 0;
        return XDocument.Load(ms).Descendants(Ns + "text").Select(t => t.Value).ToList();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PlacedMultiPartComponent_RendersOnlyItsCurrentPart(int currentPart)
    {
        var document = new SchDocument();
        document.AddComponent(MultiPartComponent(currentPart));

        var texts = await RenderSheetTexts(document);

        var otherPart = currentPart == 1 ? 2 : 1;
        Assert.Contains($"PARTPIN{currentPart}", texts);
        Assert.DoesNotContain($"PARTPIN{otherPart}", texts);
        // The component-level designator (OwnerPartId -1) stays visible on the placed instance.
        Assert.Contains(texts, t => t.StartsWith("U7", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task McuSheets_EachPlacedPartDrawsOnlyItsOwnPins()
    {
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "PrivateTestData", "MCU - GPIO1.SchDoc");
        Skip.IfNot(File.Exists(path), "MCU - GPIO1.SchDoc not available");

        var document = new SchDocReader().Read(File.OpenRead(path));
        var placed = document.Components.Cast<SchComponent>().Where(c => c.PartCount > 2).ToList();
        Assert.NotEmpty(placed);

        var texts = new HashSet<string>(await RenderSheetTexts(document), StringComparer.Ordinal);

        // Pin names that exist only on parts NOT placed on this sheet must not be drawn.
        var placedParts = placed.Select(c => c.CurrentPartId).ToHashSet();
        var pins = placed[0].Pins.Cast<SchPin>().Where(p => !string.IsNullOrEmpty(p.Name)).ToList();
        var placedNames = pins.Where(p => placedParts.Contains(p.OwnerPartId)).Select(p => p.Name!).ToHashSet();
        var foreignOnly = pins.Where(p => !placedParts.Contains(p.OwnerPartId) && !placedNames.Contains(p.Name!))
            .Select(p => p.Name!).Distinct().ToList();
        Assert.NotEmpty(foreignOnly);
        Assert.DoesNotContain(foreignOnly, texts.Contains);
    }
}

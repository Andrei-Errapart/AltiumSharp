using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// A PcbDoc stores every region twice — Regions6 (arcs flattened) and ShapeBasedRegions6 (arc-exact) —
/// as two parallel collections. These tests verify an edit to the Regions6 copy mirrors onto its
/// shape-based twin so the file cannot end up holding two different outlines for one region, and that a
/// component move (which already moves the twin as an owned primitive) does not move it twice.
/// </summary>
public sealed class PcbDocRegionTwinSyncTests
{
    [Fact]
    public void SetOutline_MirrorsStraightSegmentsOntoTwin_DroppingArcs()
    {
        var region = PcbRegion.Create()
            .AddPoint(Coord.Zero, Coord.Zero)
            .AddPoint(Coord.OneMm, Coord.Zero)
            .AddPoint(Coord.OneMm, Coord.OneMm)
            .Build();
        var twin = new PcbShapeBasedRegion();
        twin.Outline.Add(new PcbExtendedVertex { X = 999, Y = 999, IsRoundRaw = 1, CenterX = 5, CenterY = 6, Radius = 7 });
        region.ShapeBasedTwin = twin;

        var pts = new[]
        {
            new CoordPoint(Coord.FromMils(10), Coord.FromMils(10)),
            new CoordPoint(Coord.FromMils(20), Coord.FromMils(10)),
            new CoordPoint(Coord.FromMils(20), Coord.FromMils(20)),
            new CoordPoint(Coord.FromMils(10), Coord.FromMils(20)),
        };
        region.SetOutline(pts);

        Assert.Equal(pts.Length, twin.Outline.Count);
        for (var i = 0; i < pts.Length; i++)
        {
            Assert.Equal(pts[i].X.ToRaw(), twin.Outline[i].X);
            Assert.Equal(pts[i].Y.ToRaw(), twin.Outline[i].Y);
            Assert.Equal(0, twin.Outline[i].IsRoundRaw); // arc data dropped — the new outline is a polygon
        }
    }

    [Fact]
    public void Translate_MovesTwin_PreservingArcCenters()
    {
        var region = PcbRegion.Create().AddPoint(Coord.Zero, Coord.Zero).AddPoint(Coord.OneMm, Coord.OneMm).Build();
        var twin = new PcbShapeBasedRegion();
        twin.Outline.Add(new PcbExtendedVertex { X = 100, Y = 200, IsRoundRaw = 1, CenterX = 100, CenterY = 200, Radius = 50 });
        region.ShapeBasedTwin = twin;

        var dx = Coord.FromMils(5);
        var dy = Coord.FromMils(-3);
        region.Translate(dx, dy);

        Assert.Equal(100 + dx.ToRaw(), twin.Outline[0].X);
        Assert.Equal(200 + dy.ToRaw(), twin.Outline[0].Y);
        Assert.Equal(1, twin.Outline[0].IsRoundRaw);               // still an arc (preserved, not flattened)
        Assert.Equal(100 + dx.ToRaw(), twin.Outline[0].CenterX);   // arc centre moved with the vertex
        Assert.Equal(200 + dy.ToRaw(), twin.Outline[0].CenterY);
    }

    [Fact]
    public void ComponentMove_MovesLinkedTwinExactlyOnce_NotTwice()
    {
        var doc = new PcbDocument();
        var comp = PcbComponent.Create("U1").Build();
        doc.AddComponent(comp); // index 0

        var region = PcbRegion.Create().AddPoint(Coord.Zero, Coord.Zero).AddPoint(Coord.OneMm, Coord.OneMm).Build();
        region.ComponentIndex = 0;
        doc.AddRegion(region);

        var twin = new PcbShapeBasedRegion { ComponentIndex = 0 };
        twin.Outline.Add(new PcbExtendedVertex { X = 0, Y = 0 });
        twin.Outline.Add(new PcbExtendedVertex { X = 1000, Y = 1000 });
        doc.ShapeBasedRegions.Add(twin);
        region.ShapeBasedTwin = twin; // as the reader links a loaded board

        var dx = Coord.FromMils(10);
        comp.TranslateBy(dx, Coord.Zero);

        // The twin is both an owned primitive (moved by the component move) and the region's linked twin.
        // It must move exactly once — the component path suppresses the region's propagation.
        Assert.Equal(0 + dx.ToRaw(), twin.Outline[0].X);
        Assert.Equal(1000 + dx.ToRaw(), twin.Outline[1].X);
    }

    [SkippableFact]
    public void SetOutline_OnRealBoard_KeepsBothStoredCopiesConsistent()
    {
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "TestData", "SPI Isolator.PcbDoc");
        Skip.If(!File.Exists(path), "Test data not available");

        var doc = new PcbDocReader().Read(File.OpenRead(path));
        Skip.If(doc.Regions.Count == 0 || doc.Regions.Count != doc.ShapeBasedRegions.Count,
            "Board has no 1:1 region/shape-based-region pairing");

        var region = (PcbRegion)doc.Regions[0];
        var newPts = new[]
        {
            new CoordPoint(Coord.FromMils(1000), Coord.FromMils(1000)),
            new CoordPoint(Coord.FromMils(1100), Coord.FromMils(1000)),
            new CoordPoint(Coord.FromMils(1100), Coord.FromMils(1100)),
            new CoordPoint(Coord.FromMils(1000), Coord.FromMils(1100)),
        };
        region.SetOutline(newPts);

        using var ms = new MemoryStream();
        new PcbDocWriter().Write(doc, ms);
        ms.Position = 0;
        var rt = new PcbDocReader().Read(ms);

        // After the round-trip the two stored copies of region 0 describe the same new square — the twin
        // followed the edit instead of keeping the old outline.
        var rtRegion = (PcbRegion)rt.Regions[0];
        var rtTwin = rt.ShapeBasedRegions[0];
        Assert.Equal(Bbox(newPts), BboxRegion(rtRegion));
        Assert.Equal(Bbox(newPts), BboxShape(rtTwin));
    }

    private static (long, long, long, long) Bbox(IReadOnlyList<CoordPoint> pts)
    {
        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X.ToRaw()); maxX = Math.Max(maxX, p.X.ToRaw());
            minY = Math.Min(minY, p.Y.ToRaw()); maxY = Math.Max(maxY, p.Y.ToRaw());
        }
        return (minX, minY, maxX, maxY);
    }

    private static (long, long, long, long) BboxRegion(PcbRegion r) => Bbox(r.Outline);

    private static (long, long, long, long) BboxShape(PcbShapeBasedRegion r)
    {
        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        foreach (var v in r.Outline)
        {
            minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
        }
        return (minX, minY, maxX, maxY);
    }
}

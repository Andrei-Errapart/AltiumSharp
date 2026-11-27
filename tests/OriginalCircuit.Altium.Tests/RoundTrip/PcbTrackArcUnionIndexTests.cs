using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// A track's or arc's union index and subpolygon index sit where KiCad's importer reads them
/// (track bytes 33-34 and 36-39, arc 45-46 and 48-51). They used to be read as a "solder mask
/// expansion" spanning an unknown byte and three bytes of the union index, and written back with
/// the subpolygon index zeroed, so union membership survived a round trip only partly. On three
/// company boards every non-zero union index read this way names one of the board's unions.
/// </summary>
public sealed class PcbTrackArcUnionIndexTests
{
    private static PcbDocument RoundTrip(PcbDocument document)
    {
        using var ms = new MemoryStream();
        new PcbDocWriter().Write(document, ms);
        ms.Position = 0;
        return new PcbDocReader().Read(ms);
    }

    [Theory]
    [InlineData(7, 3)]
    [InlineData(0x01020304, 0xFFFE)]   // every byte of the index carries something
    public void TrackAndArcKeepTheirUnionAndSubpolygonIndex(int union, int subpoly)
    {
        var document = new PcbDocument();
        document.AddTrack(new PcbTrack
        {
            Start = new CoordPoint(Coord.FromMils(0), Coord.FromMils(0)),
            End = new CoordPoint(Coord.FromMils(100), Coord.FromMils(0)),
            Width = Coord.FromMils(5),
            Layer = 1,
            UnionIndex = union,
            SubPolyIndex = subpoly,
        });
        document.AddArc(new PcbArc
        {
            Center = new CoordPoint(Coord.FromMils(50), Coord.FromMils(50)),
            Radius = Coord.FromMils(20),
            StartAngle = 0,
            EndAngle = 90,
            Width = Coord.FromMils(5),
            Layer = 1,
            UnionIndex = union + 1,
            SubPolyIndex = subpoly - 1,
        });

        var read = RoundTrip(document);

        var track = Assert.IsType<PcbTrack>(Assert.Single(read.Tracks));
        Assert.Equal((union, subpoly), (track.UnionIndex, track.SubPolyIndex));
        var arc = Assert.IsType<PcbArc>(Assert.Single(read.Arcs));
        Assert.Equal((union + 1, subpoly - 1), (arc.UnionIndex, arc.SubPolyIndex));

        // And again, from what was read.
        var again = RoundTrip(read);
        Assert.Equal(union, Assert.IsType<PcbTrack>(Assert.Single(again.Tracks)).UnionIndex);
        Assert.Equal(union + 1, Assert.IsType<PcbArc>(Assert.Single(again.Arcs)).UnionIndex);
    }
}

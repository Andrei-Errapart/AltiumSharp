using System.Globalization;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Regression tests for culture-sensitive numeric formatting in the Sch/PcbDoc writers.
/// The read path parses numbers with <see cref="CultureInfo.InvariantCulture"/>, so the
/// write path must format with it too. Under cultures whose negative sign or digits are
/// non-ASCII — e.g. sv-SE formats a minus as U+2212 MINUS SIGN, fa-IR uses Persian digits,
/// de-DE uses a decimal comma — ambient-culture formatting of negative coordinates (and
/// fractional values like rotation/scale) would previously round-trip silently to 0.
/// </summary>
public sealed class CultureRoundTripTests
{
    public static IEnumerable<object[]> HostileCultures =>
    [
        ["sv-SE"], // minus sign is U+2212, decimal comma
        ["fa-IR"], // Persian digits / RTL marks
        ["de-DE"], // decimal comma (catches double-formatting holes)
    ];

    private static T UnderCulture<T>(string cultureName, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void SchLib_NegativeCoordinates_RoundTripUnderHostileCulture(string culture)
    {
        var readBack = UnderCulture(culture, () =>
        {
            var library = new SchLibrary();
            var component = new SchComponent { Name = "RES", PartCount = 1 };
            component.AddPin(SchPin.Create("1")
                .WithName("A")
                .At(Coord.FromMils(-200), Coord.FromMils(-50))
                .Length(Coord.FromMils(200))
                .Orient(PinOrientation.Right)
                .Build());
            library.Add(component);

            using var ms = new MemoryStream();
            new SchLibWriter().Write(library, ms);
            ms.Position = 0;
            return (SchLibrary)new SchLibReader().Read(ms);
        });

        var pin = (SchPin)readBack.Components.First().Pins[0];
        Assert.Equal(-200, pin.Location.X.ToMils(), 1);
        Assert.Equal(-50, pin.Location.Y.ToMils(), 1);
    }

    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void SchDoc_NegativeCoordinates_RoundTripUnderHostileCulture(string culture)
    {
        var readBack = UnderCulture(culture, () =>
        {
            var doc = new SchDocument();
            var wire = new SchWire { Color = 128 };
            wire.AddVertex(new CoordPoint(Coord.FromMils(-300), Coord.FromMils(-100)));
            wire.AddVertex(new CoordPoint(Coord.FromMils(300), Coord.FromMils(100)));
            doc.AddPrimitive(wire);

            using var ms = new MemoryStream();
            new SchDocWriter().Write(doc, ms);
            ms.Position = 0;
            return new SchDocReader().Read(ms);
        });

        var v0 = readBack.Wires.First().Vertices[0];
        Assert.Equal(-300, v0.X.ToMils(), 1);
        Assert.Equal(-100, v0.Y.ToMils(), 1);
    }

    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void PcbDoc_NegativeCoordinates_RoundTripUnderHostileCulture(string culture)
    {
        var readBack = UnderCulture(culture, () =>
        {
            var doc = new PcbDocument();
            doc.AddTrack(new PcbTrack
            {
                Start = new CoordPoint(Coord.FromMils(-500), Coord.FromMils(-250)),
                End = new CoordPoint(Coord.FromMils(500), Coord.FromMils(250)),
                Width = Coord.FromMils(10),
                Layer = 1
            });

            using var ms = new MemoryStream();
            new PcbDocWriter().Write(doc, ms);
            ms.Position = 0;
            return new PcbDocReader().Read(ms);
        });

        var track = readBack.Tracks.First();
        Assert.Equal(-500, track.Start.X.ToMils(), 1);
        Assert.Equal(-250, track.Start.Y.ToMils(), 1);
    }
}

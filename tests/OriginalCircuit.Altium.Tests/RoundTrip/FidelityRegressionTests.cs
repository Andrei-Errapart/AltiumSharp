using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Targeted round-trip regression tests for individual fidelity fixes.
/// </summary>
public sealed class FidelityRegressionTests
{
    [Fact]
    public void SchLib_BinaryPin_SwapIdPart_RoundTrips()
    {
        var library = new SchLibrary();
        var component = new SchComponent { Name = "U1", PartCount = 1 };
        var pin = SchPin.Create("1")
            .WithName("A")
            .At(Coord.FromMils(0), Coord.FromMils(0))
            .Length(Coord.FromMils(100))
            .Orient(PinOrientation.Right)
            .Build();
        pin.SwapIdPart = "5"; // non-default; binary pins previously always read back "0"
        component.AddPin(pin);
        library.Add(component);

        using var ms = new MemoryStream();
        new SchLibWriter().Write(library, ms);
        ms.Position = 0;
        var readBack = (SchLibrary)new SchLibReader().Read(ms);

        var roundTripped = (SchPin)readBack.Components.First().Pins[0];
        Assert.Equal("5", roundTripped.SwapIdPart);
    }

    [Fact]
    public void SchLib_UnmappedRecord_RoundTripsAsOpaque()
    {
        var library = new SchLibrary();
        var component = new SchComponent { Name = "U1", PartCount = 1 };
        // RECORD=209 (Note) is not modelled by the SchLib reader. Place it on the ordered-record list
        // so the writer's byte-fidelity path emits it; the reader must read it back as an opaque record.
        component.ReadOrderedPrimitives.Add(new SchOpaqueRecord(new Dictionary<string, string>
        {
            ["RECORD"] = "209",
            ["OWNERINDEX"] = "0",
            ["LOCATION.X"] = "100",
            ["LOCATION.Y"] = "200",
            ["TEXT"] = "hello",
        }));
        library.Add(component);

        using var ms = new MemoryStream();
        new SchLibWriter().Write(library, ms);
        ms.Position = 0;
        var readBack = (SchLibrary)new SchLibReader().Read(ms);

        var comp = (SchComponent)readBack.Components.First();
        var opaque = comp.ReadOrderedPrimitives.OfType<SchOpaqueRecord>().FirstOrDefault();
        Assert.NotNull(opaque);
        Assert.Equal("209", opaque!.Parameters["RECORD"]);
        Assert.Equal("hello", opaque.Parameters["TEXT"]);
    }
}

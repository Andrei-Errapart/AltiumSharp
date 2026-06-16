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
    public void SchLib_ReloadAfterRewrite_PreservesComponents()
    {
        // A from-scratch library is written with a FileHeader that carries no CompCount/LibRefN
        // params (only HEADER + Weight) followed by a binary count+name tail. Reading it captures
        // those params as HeaderParameters; rewriting must still let the reader rediscover the
        // component. Regression: the writer used to emit the captured params with no tail, so any
        // load -> save -> load of a library-authored SchLib reported zero components.
        var library = new SchLibrary();
        var component = new SchComponent { Name = "RES", PartCount = 1 };
        component.AddPin(SchPin.Create("1").WithName("A")
            .At(Coord.FromMils(0), Coord.FromMils(0))
            .Length(Coord.FromMils(100)).Orient(PinOrientation.Right).Build());
        library.Add(component);

        // First write + read simulates loading a previously-saved library.
        using var ms1 = new MemoryStream();
        new SchLibWriter().Write(library, ms1);
        ms1.Position = 0;
        var loaded = (SchLibrary)new SchLibReader().Read(ms1);
        Assert.Single(loaded.Components);

        // Rewrite the loaded library and read it again — the component must survive.
        using var ms2 = new MemoryStream();
        new SchLibWriter().Write(loaded, ms2);
        ms2.Position = 0;
        var reloaded = (SchLibrary)new SchLibReader().Read(ms2);

        var survivor = Assert.Single(reloaded.Components);
        Assert.Equal("RES", survivor.Name);
    }

    [Fact]
    public void SchLib_EditLoadedComponent_PreservesAddedPrimitive()
    {
        // Build a component, round-trip it (so it becomes a "loaded" component with a populated
        // ReadOrderedPrimitives byte-fidelity list), then add a new primitive and save again.
        // Regression: WriteComponent emitted only ReadOrderedPrimitives for a loaded component, so a
        // primitive added afterward (which lives only in the typed collection) was silently dropped.
        var library = new SchLibrary();
        var component = new SchComponent { Name = "U1", PartCount = 1 };
        component.AddPin(SchPin.Create("1").WithName("A")
            .At(Coord.FromMils(0), Coord.FromMils(0))
            .Length(Coord.FromMils(100)).Orient(PinOrientation.Right).Build());
        library.Add(component);

        using var ms1 = new MemoryStream();
        new SchLibWriter().Write(library, ms1);
        ms1.Position = 0;
        var loaded = (SchLibrary)new SchLibReader().Read(ms1);

        var loadedComp = (SchComponent)loaded.Components.First();
        Assert.NotEmpty(loadedComp.ReadOrderedPrimitives); // it is a "loaded" component
        var originalLabelCount = loadedComp.Labels.Count;
        loadedComp.AddLabel(new SchLabel { Text = "R", FontId = 1, Color = 128 });

        using var ms2 = new MemoryStream();
        new SchLibWriter().Write(loaded, ms2);
        ms2.Position = 0;
        var reloaded = (SchLibrary)new SchLibReader().Read(ms2);

        var reloadedComp = (SchComponent)reloaded.Components.First();
        Assert.Equal(originalLabelCount + 1, reloadedComp.Labels.Count);
        Assert.Equal(loadedComp.Pins.Count, reloadedComp.Pins.Count); // original primitives still present
        Assert.Contains(reloadedComp.Labels.Cast<SchLabel>(), l => l.Text == "R");
    }

    [Fact]
    public void SchDoc_EditLoadedDocument_PreservesAddedPrimitive()
    {
        // Round-trip a document so it becomes "loaded" (its RawRecords byte-fidelity fast path is
        // captured), then add a wire. Regression: the writer re-emitted only the captured RawRecords
        // and dropped primitives added after load. The added wire must survive the next save/reload.
        var doc = new SchDocument();
        doc.AddComponent(new SchComponent { Name = "U1", PartCount = 1 });

        using var ms1 = new MemoryStream();
        new SchDocWriter().Write(doc, ms1);
        ms1.Position = 0;
        var loaded = (SchDocument)new SchDocReader().Read(ms1);
        Assert.NotNull(loaded.RawRecords); // the byte-fidelity fast path is active for this document
        Assert.Empty(loaded.Wires);

        loaded.AddPrimitive(SchWire.Create()
            .From(Coord.FromMils(0), Coord.FromMils(0))
            .To(Coord.FromMils(100), Coord.FromMils(0))
            .Build());

        using var ms2 = new MemoryStream();
        new SchDocWriter().Write(loaded, ms2);
        ms2.Position = 0;
        var reloaded = (SchDocument)new SchDocReader().Read(ms2);

        Assert.Single(reloaded.Wires);
        Assert.Equal(loaded.Components.Count, reloaded.Components.Count); // component still present
    }

    [Fact]
    public void SchLib_NullComponentDescription_StaysNull()
    {
        // Regression: the writer unconditionally emitted ComponentDescription="" for a component with
        // no description, which read back as "" (not null) and perturbed the byte-faithful record.
        // A descriptionless component must round-trip as null; an explicit description must survive.
        var library = new SchLibrary();
        library.Add(new SchComponent { Name = "U1", PartCount = 1 });                 // Description == null
        library.Add(new SchComponent { Name = "U2", PartCount = 1, Description = "Op-amp" });

        using var ms = new MemoryStream();
        new SchLibWriter().Write(library, ms);
        ms.Position = 0;
        var reloaded = (SchLibrary)new SchLibReader().Read(ms);

        Assert.Null(((SchComponent)reloaded.Components[0]).Description);
        Assert.Equal("Op-amp", ((SchComponent)reloaded.Components[1]).Description);
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

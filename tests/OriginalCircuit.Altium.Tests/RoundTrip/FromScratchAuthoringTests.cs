using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Tests that objects authored from scratch (no source file to replay) serialize to valid,
/// re-readable files with sensible default identity/metadata — the half of fidelity that
/// raw-byte replay cannot cover.
/// </summary>
public sealed class FromScratchAuthoringTests
{
    [Fact]
    public void SchLib_FromScratch_EmitsDefaultFontTable()
    {
        var library = new SchLibrary();
        var component = new SchComponent { Name = "U1", PartCount = 1 };
        component.AddPin(SchPin.Create("1").WithName("A")
            .At(Coord.FromMils(0), Coord.FromMils(0))
            .Length(Coord.FromMils(100)).Orient(PinOrientation.Right).Build());
        library.Add(component);

        using var ms = new MemoryStream();
        new SchLibWriter().Write(library, ms);
        ms.Position = 0;
        var reloaded = (SchLibrary)new SchLibReader().Read(ms);

        // A from-scratch library must still carry a font table so text referencing FontID 1 resolves.
        Assert.NotEmpty(reloaded.Fonts);
        Assert.Contains(reloaded.Fonts, f => f.Name == "Times New Roman");
        Assert.Single(reloaded.Components);
    }

    [Fact]
    public void PcbLib_FromScratch_PadsGetDistinctIdentityGuids()
    {
        var library = new PcbLibrary();
        var component = PcbComponent.Create("R0402")
            .AddPad(p => p.At(Coord.FromMils(-25), Coord.FromMils(0)).Size(Coord.FromMils(30), Coord.FromMils(40)).WithDesignator("1").Layer(1))
            .AddPad(p => p.At(Coord.FromMils(25), Coord.FromMils(0)).Size(Coord.FromMils(30), Coord.FromMils(40)).WithDesignator("2").Layer(1))
            .Build();
        library.Add(component);

        using var ms = new MemoryStream();
        new PcbLibWriter().Write(library, ms);
        ms.Position = 0;
        var comp = (PcbComponent)new PcbLibReader().Read(ms).Components.First();

        Assert.Equal(2, comp.Pads.Count);
        var ids = comp.Pads.Cast<PcbPad>().Select(p => p.IdentityGuid).ToList();
        Assert.All(ids, g => Assert.NotEqual(Guid.Empty, g));
        Assert.NotEqual(ids[0], ids[1]); // each authored pad gets its own identity, not one shared template GUID
    }

    [Fact]
    public void PcbModel_Checksum_MatchesKnownVector()
    {
        // Reverse-engineered position-weighted byte sum: weight(0)=1, weight(i)=i.
        Assert.Equal(410u, PcbModel.ComputeChecksum(new byte[] { 10, 20, 30, 40, 50 }));

        var model = new PcbModel { StepData = "ISO-10303-21;\nENDSEC;" };
        model.RecomputeChecksum();
        Assert.Equal(unchecked((int)PcbModel.ComputeChecksum(System.Text.Encoding.UTF8.GetBytes(model.StepData))), model.Checksum);
    }
}

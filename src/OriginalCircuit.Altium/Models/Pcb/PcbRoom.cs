namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Represents a PCB room from the Rooms6 storage (a physical placement region for components).
/// Typed; the campaign corpus contains no Rooms6 records, so only the identifying fields are modeled
/// (enough for from-scratch authoring); there is no raw-parameter replay.
/// </summary>
public sealed class PcbRoom
{
    /// <summary>Room name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique identifier.</summary>
    public string UniqueId { get; set; } = string.Empty;
}

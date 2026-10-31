namespace OriginalCircuit.Altium.Models;

/// <summary>
/// The type of a placed component: Altium's <c>TComponentKind</c>, shown as the component's
/// "Type" property. Shared by schematic and PCB components.
/// </summary>
/// <remarks>
/// Altium stores the type in two keys. The legacy <c>COMPONENTKIND</c> only knows the original
/// kinds; newer versions write the full value to <c>COMPONENTKINDVERSION2</c> and leave
/// <c>COMPONENTKIND</c> absent (0). Read the effective value through
/// <see cref="Sch.SchComponent.ComponentType"/> or <see cref="Pcb.PcbComponent.ComponentType"/>.
/// </remarks>
public enum ComponentType
{
    /// <summary>A normal component: on the PCB and in the BOM.</summary>
    Standard = 0,

    /// <summary>A mechanical part (e.g. a heatsink or screw): in the BOM, need not be wired.</summary>
    Mechanical = 1,

    /// <summary>A graphical symbol (e.g. a logo): not on the PCB and not in the BOM.</summary>
    Graphical = 2,

    /// <summary>"Net Tie (In BOM)": a net tie that is listed in the BOM.</summary>
    NetTieInBom = 3,

    /// <summary>"Net Tie": a net tie that is not listed in the BOM.</summary>
    NetTieNoBom = 4,

    /// <summary>"Standard (No BOM)": on the PCB but not in the BOM, e.g. test points and fiducials.</summary>
    StandardNoBom = 5,

    /// <summary>A jumper.</summary>
    Jumper = 6,
}

/// <summary>Helpers for <see cref="ComponentType"/>.</summary>
public static class ComponentTypeExtensions
{
    /// <summary>
    /// Whether Altium lists a component of this type in a bill of materials. Graphical, Net Tie
    /// (no BOM) and Standard (No BOM) components are left out; every other type, including values
    /// this library does not know, is included.
    /// </summary>
    public static bool IsInBom(this ComponentType type) =>
        type is not (ComponentType.Graphical or ComponentType.NetTieNoBom or ComponentType.StandardNoBom);
}

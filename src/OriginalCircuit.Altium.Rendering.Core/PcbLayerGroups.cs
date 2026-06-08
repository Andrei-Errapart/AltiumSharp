namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Classifies Altium PCB layer IDs into groups, for layer-visibility filtering.
/// Layer IDs: 1=Top, 2-31=Mid1-30, 32=Bottom, 33=TopOverlay, 34=BottomOverlay,
/// 35=TopPaste, 36=BottomPaste, 37=TopSolder, 38=BottomSolder, 39-54=InternalPlane1-16,
/// 55=DrillGuide, 56=KeepOut, 57-72=Mechanical1-16, 73=DrillDrawing, 74=MultiLayer,
/// 81=PadHole, 82=ViaHole.
/// </summary>
public static class PcbLayerGroups
{
    /// <summary>Top signal/copper layer (1).</summary>
    public static bool IsTopCopper(int layer) => layer == 1;

    /// <summary>Bottom signal/copper layer (32).</summary>
    public static bool IsBottomCopper(int layer) => layer == 32;

    /// <summary>Internal mid-signal copper layers (2-31).</summary>
    public static bool IsMidCopper(int layer) => layer >= 2 && layer <= 31;

    /// <summary>Any signal/copper layer, top, bottom or mid (1-32).</summary>
    public static bool IsCopper(int layer) => layer >= 1 && layer <= 32;

    /// <summary>Internal power/ground plane layers (39-54).</summary>
    public static bool IsInternalPlane(int layer) => layer >= 39 && layer <= 54;

    /// <summary>Silkscreen overlay layers, top (33) or bottom (34).</summary>
    public static bool IsOverlay(int layer) => layer is 33 or 34;

    /// <summary>Solder-paste stencil layers, top (35) or bottom (36).</summary>
    public static bool IsPaste(int layer) => layer is 35 or 36;

    /// <summary>Solder-mask layers, top (37) or bottom (38).</summary>
    public static bool IsSolderMask(int layer) => layer is 37 or 38;

    /// <summary>Mechanical layers (57-72).</summary>
    public static bool IsMechanical(int layer) => layer >= 57 && layer <= 72;

    /// <summary>Multi-layer spanning objects: through-hole pads/vias and their holes (74, 81, 82).</summary>
    public static bool IsMultiLayer(int layer) => layer is 74 or 81 or 82;

    /// <summary>The keep-out layer (56).</summary>
    public static bool IsKeepout(int layer) => layer == 56;

    /// <summary>
    /// Electrically meaningful layers plus silkscreen — copper, internal planes, overlay and
    /// multi-layer objects — i.e. everything except mechanical, paste, drill and keep-out. Handy
    /// for a "show the routing" view that drops fabrication/documentation clutter.
    /// </summary>
    public static bool IsSignalOrSilk(int layer)
        => IsCopper(layer) || IsInternalPlane(layer) || IsOverlay(layer) || IsMultiLayer(layer);
}

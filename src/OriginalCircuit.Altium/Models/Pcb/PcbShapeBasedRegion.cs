namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// An outline vertex of a shape-based region — a point with optional arc data (37 bytes on disk:
/// isRound + X/Y/CenterX/CenterY/Radius as int32 + start/end angle as double). See
/// docs/decompile/feature-shapebased-regions.md.
/// </summary>
public sealed class PcbExtendedVertex
{
    /// <summary>Raw "is round" byte (0 = straight segment, non-zero = arc).</summary>
    public byte IsRoundRaw { get; set; }
    /// <summary>Vertex X (internal units).</summary>
    public int X { get; set; }
    /// <summary>Vertex Y (internal units).</summary>
    public int Y { get; set; }
    /// <summary>Arc center X.</summary>
    public int CenterX { get; set; }
    /// <summary>Arc center Y.</summary>
    public int CenterY { get; set; }
    /// <summary>Arc radius.</summary>
    public int Radius { get; set; }
    /// <summary>Arc start angle (degrees).</summary>
    public double StartAngle { get; set; }
    /// <summary>Arc end angle (degrees).</summary>
    public double EndAngle { get; set; }
}

/// <summary>
/// A shape-based region (ShapeBasedRegions6 storage). Like a region but with arc-capable extended
/// outline vertices. Modeled byte-exact by preserving the raw header/length bytes and the typed
/// geometry. See docs/decompile/feature-shapebased-regions.md.
/// </summary>
public sealed class PcbShapeBasedRegion
{
    /// <summary>Record type byte (0x0B = shape-based region, 0x0C = shape-based component body).</summary>
    internal byte TypeByte { get; set; } = 0x0B;

    /// <summary>Layer byte.</summary>
    public byte Layer { get; set; }
    /// <summary>Raw flags1 byte (bit 0x04 cleared = locked, 0x02 = polygon outline).</summary>
    internal byte Flags1 { get; set; } = 0x04;
    /// <summary>Raw flags2 byte (2 = keepout).</summary>
    internal byte Flags2 { get; set; }
    /// <summary>Net index (0xFFFF = none).</summary>
    public ushort NetIndex { get; set; } = 0xFFFF;
    /// <summary>Polygon index.</summary>
    public ushort PolygonIndex { get; set; } = 0xFFFF;
    /// <summary>Component index (0xFFFF = free).</summary>
    public ushort ComponentIndex { get; set; } = 0xFFFF;
    // The fixed header bytes after ComponentIndex (union-index-none "FF FF FF FF 00") and after the hole
    // count ("00 00"), and the SubRecord-1 length, are all constants/derived — written by the serializer,
    // not captured (verified across all 209 corpus shape-based records).
    /// <summary>
    /// The property block as an ordered, authorable key/value list (the <c>V7_LAYER</c>/<c>NAME</c>/
    /// <c>KIND</c>/<c>ISSHAPEBASED</c>… params), in source order. Reconstructed byte-for-byte by joining
    /// <c>KEY=VALUE</c> with <c>|</c> (a null value preserves a rare segment that has no <c>=</c>). This
    /// replaces the former opaque raw-byte capture. Authorable from scratch.
    /// </summary>
    public List<KeyValuePair<string, string?>> Properties { get; } = new();
    /// <summary>Count of NUL bytes that terminate the property block (inside its length-prefixed span).</summary>
    internal int PropsInnerNulls { get; set; }
    /// <summary>Whether an extra trailing NUL byte follows the property block's length-prefixed span.</summary>
    internal bool PropsHasTrailingNull { get; set; }

    /// <summary>Looks up a property value by key (case-insensitive); null if absent.</summary>
    public string? GetProperty(string key)
    {
        foreach (var p in Properties)
            if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }
    /// <summary>Outline vertices (the last is the closing vertex; <c>count</c> on disk is N-1).</summary>
    public List<PcbExtendedVertex> Outline { get; } = new();
    /// <summary>Hole contours (simple x/y double vertices).</summary>
    public List<List<(double X, double Y)>> Holes { get; } = new();
}

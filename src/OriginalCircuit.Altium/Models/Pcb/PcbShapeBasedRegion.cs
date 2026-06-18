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
    /// <summary>Raw 4-byte SubRecord-1 length field (preserved; some records use non-standard values).</summary>
    internal byte[] Sr1LengthBytes { get; set; } = new byte[4];
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
    /// <summary>5 preserved header bytes (union index + unknown).</summary>
    internal byte[] HeaderSkip5 { get; set; } = new byte[5];
    /// <summary>5 preserved header bytes after the hole count.</summary>
    internal byte[] HeaderSkip2 { get; set; } = new byte[2];
    /// <summary>The exact property-block bytes (parsed text is also exposed via <see cref="Parameters"/>).</summary>
    internal byte[] RawPropertyBytes { get; set; } = Array.Empty<byte>();
    /// <summary>Whether a trailing NUL byte follows the property block.</summary>
    internal bool PropsHasTrailingNull { get; set; }
    /// <summary>Parsed property parameters.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Outline vertices (the last is the closing vertex; <c>count</c> on disk is N-1).</summary>
    public List<PcbExtendedVertex> Outline { get; } = new();
    /// <summary>Hole contours (simple x/y double vertices).</summary>
    public List<List<(double X, double Y)>> Holes { get; } = new();
}

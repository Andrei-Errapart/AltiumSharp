using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Represents an embedded board / board-array object (the <c>EmbeddedBoards6</c> storage). Fully typed
/// so it round-trips byte-for-byte and is authored from scratch without replaying the raw block.
/// </summary>
public sealed class PcbEmbeddedBoard
{
    // Common primitive parameter prefix.
    /// <summary>Selection flag (transient; FALSE on disk).</summary>
    public bool Selection { get; set; }
    /// <summary>Layer token (e.g. <c>TOP</c>).</summary>
    public string Layer { get; set; } = "TOP";
    /// <summary>Whether the object is locked.</summary>
    public bool Locked { get; set; }
    /// <summary>Polygon-outline flag.</summary>
    public bool PolygonOutline { get; set; }
    /// <summary>User-routed flag.</summary>
    public bool UserRouted { get; set; } = true;
    /// <summary>Whether this is a keepout.</summary>
    public bool IsKeepout { get; set; }
    /// <summary>Union index.</summary>
    public int UnionIndex { get; set; }

    /// <summary>Bounding-box corner 1 X.</summary>
    public Coord X1Location { get; set; }
    /// <summary>Bounding-box corner 1 Y.</summary>
    public Coord Y1Location { get; set; }
    /// <summary>Bounding-box corner 2 X.</summary>
    public Coord X2Location { get; set; }
    /// <summary>Bounding-box corner 2 Y.</summary>
    public Coord Y2Location { get; set; }

    /// <summary>Rotation in degrees.</summary>
    public double Rotation { get; set; }

    /// <summary>Whether this object is a viewport.</summary>
    public bool IsViewport { get; set; }
    /// <summary>Viewport corner 1 X.</summary>
    public Coord ViewportX1 { get; set; }
    /// <summary>Viewport corner 1 Y.</summary>
    public Coord ViewportY1 { get; set; }
    /// <summary>Viewport corner 2 X.</summary>
    public Coord ViewportX2 { get; set; }
    /// <summary>Viewport corner 2 Y.</summary>
    public Coord ViewportY2 { get; set; }
    /// <summary>Viewport scale.</summary>
    public double ViewportScale { get; set; } = 1.0;
    /// <summary>Whether the viewport is visible.</summary>
    public bool ViewportVisible { get; set; } = true;
    /// <summary>Viewport title.</summary>
    public string ViewportTitle { get; set; } = "Title";

    /// <summary>Title font name.</summary>
    public string TitleFontName { get; set; } = "Arial";
    /// <summary>Title font size.</summary>
    public int TitleFontSize { get; set; }
    /// <summary>Title font color.</summary>
    public int TitleFontColor { get; set; }

    /// <summary>Visible-layers descriptor string.</summary>
    public string VisibleLayers { get; set; } = string.Empty;

    /// <summary>Path to the embedded board document.</summary>
    public string DocumentPath { get; set; } = string.Empty;

    /// <summary>Array origin X.</summary>
    public Coord X { get; set; }
    /// <summary>Array origin Y.</summary>
    public Coord Y { get; set; }
    /// <summary>Array row spacing.</summary>
    public Coord RowSpacing { get; set; }
    /// <summary>Array column spacing.</summary>
    public Coord ColSpacing { get; set; }
    /// <summary>Array row count.</summary>
    public int RowCount { get; set; } = 1;
    /// <summary>Array column count.</summary>
    public int ColCount { get; set; } = 1;
    /// <summary>Whether the array is mirrored.</summary>
    public bool MirrorFlag { get; set; }
    /// <summary>Origin mode.</summary>
    public int OriginMode { get; set; }
}

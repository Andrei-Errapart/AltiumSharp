using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Standard Altium schematic sheet sizes. The integer values match Altium's
/// <c>SHEETSTYLE</c> document parameter (the <c>TSheetStyle</c> enum order).
/// </summary>
public enum SchSheetStyle
{
    /// <summary>ISO A4 (Altium's default when no style is specified).</summary>
    A4 = 0,
    /// <summary>ISO A3.</summary>
    A3 = 1,
    /// <summary>ISO A2.</summary>
    A2 = 2,
    /// <summary>ISO A1.</summary>
    A1 = 3,
    /// <summary>ISO A0.</summary>
    A0 = 4,
    /// <summary>ANSI A.</summary>
    A = 5,
    /// <summary>ANSI B.</summary>
    B = 6,
    /// <summary>ANSI C.</summary>
    C = 7,
    /// <summary>ANSI D.</summary>
    D = 8,
    /// <summary>ANSI E.</summary>
    E = 9,
    /// <summary>US Letter.</summary>
    Letter = 10,
    /// <summary>US Legal.</summary>
    Legal = 11,
    /// <summary>US Tabloid.</summary>
    Tabloid = 12,
    /// <summary>OrCAD A.</summary>
    OrCadA = 13,
    /// <summary>OrCAD B.</summary>
    OrCadB = 14,
    /// <summary>OrCAD C.</summary>
    OrCadC = 15,
    /// <summary>OrCAD D.</summary>
    OrCadD = 16,
    /// <summary>OrCAD E.</summary>
    OrCadE = 17,
}

/// <summary>
/// Parsed schematic sheet (page) settings: paper size, orientation and which
/// frame graphics to draw. Derived from the RECORD=31 sheet-settings parameters.
/// </summary>
/// <remarks>
/// Altium stores sheet dimensions in "DXP" units where 1 unit = 10 mils, the same
/// units as schematic coordinates. The standard-style dimension table below uses the
/// same convention; values are converted to <see cref="Coord"/> on demand.
/// </remarks>
public sealed class SchSheetInfo
{
    // Standard Altium sheet sizes in DXP units (1 DXP = 10 mils), landscape (width, height).
    private static readonly IReadOnlyDictionary<SchSheetStyle, (int W, int H)> StandardSizesDxp =
        new Dictionary<SchSheetStyle, (int, int)>
        {
            [SchSheetStyle.A4] = (1150, 760),
            [SchSheetStyle.A3] = (1550, 1110),
            [SchSheetStyle.A2] = (2230, 1570),
            [SchSheetStyle.A1] = (3150, 2230),
            [SchSheetStyle.A0] = (4460, 3150),
            [SchSheetStyle.A] = (950, 750),
            [SchSheetStyle.B] = (1500, 950),
            [SchSheetStyle.C] = (2000, 1500),
            [SchSheetStyle.D] = (3200, 2000),
            [SchSheetStyle.E] = (4200, 3200),
            [SchSheetStyle.Letter] = (1100, 850),
            [SchSheetStyle.Legal] = (1400, 850),
            [SchSheetStyle.Tabloid] = (1700, 1100),
            [SchSheetStyle.OrCadA] = (990, 790),
            [SchSheetStyle.OrCadB] = (1540, 990),
            [SchSheetStyle.OrCadC] = (2060, 1560),
            [SchSheetStyle.OrCadD] = (3260, 2060),
            [SchSheetStyle.OrCadE] = (4280, 3280),
        };

    // Standard reference-zone layout per sheet style: (horizontal zones, vertical zones, margin band
    // width in DXP/10-mil units). Used for non-custom sheets; custom sheets carry their own counts.
    private static readonly IReadOnlyDictionary<SchSheetStyle, (int X, int Y, int MarginDxp)> StandardZones =
        new Dictionary<SchSheetStyle, (int, int, int)>
        {
            [SchSheetStyle.A4] = (4, 4, 20),
            [SchSheetStyle.A3] = (5, 4, 20),
            [SchSheetStyle.A2] = (6, 5, 30),
            [SchSheetStyle.A1] = (8, 6, 30),
            [SchSheetStyle.A0] = (10, 7, 30),
            [SchSheetStyle.A] = (4, 4, 20),
            [SchSheetStyle.B] = (6, 4, 20),
            [SchSheetStyle.C] = (6, 4, 30),
            [SchSheetStyle.D] = (8, 4, 30),
            [SchSheetStyle.E] = (16, 4, 40),
            [SchSheetStyle.Letter] = (4, 4, 20),
            [SchSheetStyle.Legal] = (5, 4, 20),
            [SchSheetStyle.Tabloid] = (6, 4, 30),
            [SchSheetStyle.OrCadA] = (4, 4, 20),
            [SchSheetStyle.OrCadB] = (6, 4, 30),
            [SchSheetStyle.OrCadC] = (6, 4, 30),
            [SchSheetStyle.OrCadD] = (8, 4, 30),
            [SchSheetStyle.OrCadE] = (10, 5, 30),
        };

    /// <summary>The standard sheet style (ignored when <see cref="UseCustomSheet"/> is true).</summary>
    public SchSheetStyle Style { get; init; } = SchSheetStyle.A4;

    /// <summary>When true, the sheet uses <see cref="CustomWidth"/>/<see cref="CustomHeight"/> instead of the standard style.</summary>
    public bool UseCustomSheet { get; init; }

    /// <summary>Custom sheet width (valid only when <see cref="UseCustomSheet"/> is true).</summary>
    public Coord CustomWidth { get; init; }

    /// <summary>Custom sheet height (valid only when <see cref="UseCustomSheet"/> is true).</summary>
    public Coord CustomHeight { get; init; }

    /// <summary>True for portrait orientation (workspace orientation = 1); standard sizes are swapped.</summary>
    public bool Portrait { get; init; }

    /// <summary>Whether the sheet border is drawn.</summary>
    public bool BorderOn { get; init; } = true;

    /// <summary>Whether the built-in title block is enabled.</summary>
    public bool TitleBlockOn { get; init; }

    /// <summary>
    /// Whether the alphanumeric reference zones (A/B/C… · 1/2/3…) are drawn in the border band.
    /// Defaults to on; Altium computes these for both standard and custom-template sheets.
    /// </summary>
    public bool ReferenceZonesOn { get; init; } = true;

    /// <summary>Whether template graphics (which include a title block) are shown.</summary>
    public bool ShowTemplateGraphics { get; init; }

    /// <summary>Referenced template file name (e.g. <c>A4.SchDot</c>), empty when none.</summary>
    public string TemplateFileName { get; init; } = string.Empty;

    // Custom-sheet reference-zone overrides (0 = not specified, fall back to the standard table).
    private int CustomZonesX { get; init; }
    private int CustomZonesY { get; init; }
    private int CustomMarginDxp { get; init; }

    /// <summary>The standard-table zone spec for this style (fallback when no custom values apply).</summary>
    private (int X, int Y, int MarginDxp) StandardZoneSpec =>
        StandardZones.TryGetValue(Style, out var z) ? z : (4, 4, 20);

    /// <summary>The number of reference-zone divisions along the X (horizontal) axis.</summary>
    public int ZonesX => UseCustomSheet && CustomZonesX > 0 ? CustomZonesX : StandardZoneSpec.X;

    /// <summary>The number of reference-zone divisions along the Y (vertical) axis.</summary>
    public int ZonesY => UseCustomSheet && CustomZonesY > 0 ? CustomZonesY : StandardZoneSpec.Y;

    /// <summary>The width of the reference-zone border band (a custom sheet may override the standard).</summary>
    public Coord MarginWidth =>
        Coord.FromMils((UseCustomSheet && CustomMarginDxp > 0 ? CustomMarginDxp : StandardZoneSpec.MarginDxp) * 10.0);

    /// <summary>Sheet width in world coordinates, honouring orientation and custom sizing.</summary>
    public Coord Width
    {
        get
        {
            if (UseCustomSheet) return Portrait ? CustomHeight : CustomWidth;
            var (w, h) = StandardSizesDxp.TryGetValue(Style, out var s) ? s : StandardSizesDxp[SchSheetStyle.A4];
            return Coord.FromMils((Portrait ? h : w) * 10);
        }
    }

    /// <summary>Sheet height in world coordinates, honouring orientation and custom sizing.</summary>
    public Coord Height
    {
        get
        {
            if (UseCustomSheet) return Portrait ? CustomWidth : CustomHeight;
            var (w, h) = StandardSizesDxp.TryGetValue(Style, out var s) ? s : StandardSizesDxp[SchSheetStyle.A4];
            return Coord.FromMils((Portrait ? w : h) * 10);
        }
    }

    /// <summary>The sheet rectangle in world coordinates, with its bottom-left corner at the origin.</summary>
    public CoordRect SheetRect =>
        new(CoordPoint.Zero, new CoordPoint(Width, Height));

    /// <summary>True when a title block (built-in or template) should be drawn.</summary>
    public bool HasTitleBlock => TitleBlockOn || ShowTemplateGraphics || !string.IsNullOrEmpty(TemplateFileName);

    /// <summary>
    /// Parses sheet settings from the RECORD=31 parameter dictionary. Returns a sensible
    /// A4 default when <paramref name="settings"/> is null or empty.
    /// </summary>
    public static SchSheetInfo Parse(IReadOnlyDictionary<string, string>? settings)
    {
        if (settings == null || settings.Count == 0)
            return new SchSheetInfo();

        int styleVal = GetInt(settings, "SHEETSTYLE", 0);
        var style = Enum.IsDefined(typeof(SchSheetStyle), styleVal) ? (SchSheetStyle)styleVal : SchSheetStyle.A4;

        return new SchSheetInfo
        {
            Style = style,
            UseCustomSheet = GetBool(settings, "USECUSTOMSHEET"),
            CustomWidth = DxpToCoord(GetInt(settings, "CUSTOMX", 1150)),
            CustomHeight = DxpToCoord(GetInt(settings, "CUSTOMY", 760)),
            Portrait = GetInt(settings, "WORKSPACEORIENTATION", 0) == 1,
            // Altium boolean keys are present only when true; absent means false.
            // BorderOn defaults to true (Altium draws the border unless explicitly off).
            BorderOn = !settings.ContainsKey("BORDERON") || GetBool(settings, "BORDERON"),
            TitleBlockOn = GetBool(settings, "TITLEBLOCKON"),
            // REFERENCEZONESON is stored INVERTED (present/"T" = zones OFF); absent = on.
            ReferenceZonesOn = !GetBool(settings, "REFERENCEZONESON"),
            ShowTemplateGraphics = GetBool(settings, "SHOWTEMPLATEGRAPHICS"),
            TemplateFileName = GetString(settings, "TEMPLATEFILENAME"),
            // Custom sheets carry their own reference-zone counts + margin; standard sheets fall back
            // to the per-style StandardZones table. (SHEETZONESX/Y store a zone *size*, not a count.)
            CustomZonesX = GetInt(settings, "CUSTOMXZONES", 0),
            CustomZonesY = GetInt(settings, "CUSTOMYZONES", 0),
            CustomMarginDxp = GetInt(settings, "CUSTOMMARGINWIDTH", 0),
        };
    }

    private static Coord DxpToCoord(int dxp) => Coord.FromMils(dxp * 10.0);

    private static string GetString(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var v) ? v : string.Empty;

    private static int GetInt(IReadOnlyDictionary<string, string> p, string key, int fallback) =>
        p.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var v) && (v == "T" || string.Equals(v, "TRUE", StringComparison.OrdinalIgnoreCase) || v == "1");
}

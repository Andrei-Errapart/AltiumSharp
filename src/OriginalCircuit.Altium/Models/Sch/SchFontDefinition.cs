namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// A single entry from a schematic document/library font table (the FontID table in the
/// FileHeader). Records reference these by 1-based <see cref="FontId"/>. The renderer uses the
/// name, point size and style to draw text faithfully.
/// </summary>
/// <param name="FontId">1-based font id referenced by records (<c>FontId</c>).</param>
/// <param name="Name">Font family name (e.g. "Times New Roman").</param>
/// <param name="Size">Font size in points.</param>
/// <param name="Bold">Whether the font is bold.</param>
/// <param name="Italic">Whether the font is italic.</param>
/// <param name="Underline">Whether the font is underlined.</param>
public sealed record SchFontDefinition(
    int FontId,
    string Name,
    double Size,
    bool Bold,
    bool Italic,
    bool Underline);

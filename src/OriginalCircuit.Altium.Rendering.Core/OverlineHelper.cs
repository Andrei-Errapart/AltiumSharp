namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Parses Altium's overline escape sequences in pin names and text.
/// A backslash (\) overlines the character that immediately precedes it, so an
/// active-low name like LDAC is stored as "L\D\A\C\" with every letter overlined.
/// </summary>
public static class OverlineHelper
{
    /// <summary>
    /// Represents a segment of text with an overline flag.
    /// </summary>
    public readonly record struct TextSegment(string Text, bool HasOverline);

    /// <summary>
    /// Parses a string containing backslash overline markers into segments. A character is
    /// overlined when it is immediately followed by a backslash; the backslashes are markers
    /// and are not displayed. Runs of equal overline state are grouped into one segment.
    /// </summary>
    public static List<TextSegment> Parse(string? text)
    {
        var segments = new List<TextSegment>();
        if (string.IsNullOrEmpty(text)) return segments;

        var sb = new System.Text.StringBuilder();
        bool runOverline = false;
        bool haveRun = false;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') continue; // markers are consumed, never displayed

            bool over = i + 1 < text.Length && text[i + 1] == '\\';
            if (!haveRun)
            {
                runOverline = over;
                haveRun = true;
            }
            else if (over != runOverline)
            {
                segments.Add(new TextSegment(sb.ToString(), runOverline));
                sb.Clear();
                runOverline = over;
            }
            sb.Append(text[i]);
        }

        if (sb.Length > 0)
            segments.Add(new TextSegment(sb.ToString(), runOverline));

        return segments;
    }

    /// <summary>
    /// Returns the display text (backslashes removed) from a string with overline markers.
    /// </summary>
    public static string GetDisplayText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\\", "");
    }
}

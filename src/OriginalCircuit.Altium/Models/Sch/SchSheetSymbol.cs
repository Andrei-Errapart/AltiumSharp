using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic sheet symbol (reference to a sub-sheet in hierarchical designs).
/// </summary>
public sealed class SchSheetSymbol : ISchSheet
{
    private readonly List<SchSheetEntry> _entries = new();

    /// <inheritdoc />
    public CoordPoint Location { get; set; }

    /// <summary>
    /// Width of the sheet symbol.
    /// </summary>
    public Coord XSize { get; set; }

    /// <summary>
    /// Height of the sheet symbol.
    /// </summary>
    public Coord YSize { get; set; }

    /// <summary>
    /// Whether the symbol is mirrored.
    /// </summary>
    public bool IsMirrored { get; set; }

    /// <summary>
    /// Referenced sheet file name.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Display name of the sheet.
    /// </summary>
    public string? SheetName { get; set; }

    /// <summary>
    /// The positioned label that draws the sheet name (designator). In Altium this is a separate
    /// child record (RECORD=32) with its own location, color and font, not an inline attribute.
    /// </summary>
    public SchLabel? NameLabel { get; set; }

    /// <summary>
    /// The positioned label that draws the referenced file name. In Altium this is a separate child
    /// record (RECORD=33) with its own location, color and font, not an inline attribute.
    /// </summary>
    public SchLabel? FileNameLabel { get; set; }

    /// <summary>
    /// Line width index (0=Small, 1=Medium, 2=Large).
    /// </summary>
    public int LineWidth { get; set; }

    /// <summary>
    /// Border color (RGB).
    /// </summary>
    public int Color { get; set; }

    /// <summary>
    /// Fill color (RGB).
    /// </summary>
    public int AreaColor { get; set; }

    /// <summary>
    /// Whether the symbol is filled.
    /// </summary>
    public bool IsSolid { get; set; }

    /// <summary>
    /// Whether hidden fields are shown.
    /// </summary>
    public bool ShowHiddenFields { get; set; }

    /// <summary>
    /// Symbol type identifier.
    /// </summary>
    public int SymbolType { get; set; }

    /// <summary>
    /// Design item ID for managed components.
    /// </summary>
    public string? DesignItemId { get; set; }

    /// <summary>
    /// Item GUID for vault/managed library reference.
    /// </summary>
    public string? ItemGuid { get; set; }

    /// <summary>
    /// Library identifier kind.
    /// </summary>
    public int LibIdentifierKind { get; set; }

    /// <summary>
    /// Library identifier string.
    /// </summary>
    public string? LibraryIdentifier { get; set; }

    /// <summary>
    /// Revision GUID for vault reference.
    /// </summary>
    public string? RevisionGuid { get; set; }

    /// <summary>
    /// Source library name.
    /// </summary>
    public string? SourceLibraryName { get; set; }

    /// <summary>
    /// Vault GUID.
    /// </summary>
    public string? VaultGuid { get; set; }

    /// <summary>
    /// Sheet entries (connection points on this symbol).
    /// </summary>
    public IReadOnlyList<SchSheetEntry> Entries => _entries;

    /// <inheritdoc />
    CoordPoint ISchSheet.Size => new(XSize, YSize);

    /// <inheritdoc />
    string ISchSheet.SheetName => SheetName ?? string.Empty;

    /// <inheritdoc />
    string ISchSheet.FileName => FileName ?? string.Empty;

    /// <inheritdoc />
    IReadOnlyList<ISchSheetPin> ISchSheet.Pins => _entries;

    /// <inheritdoc />
    EdaColor ISchSheet.Color => AltiumColorHelper.BgrToEdaColor(Color);

    /// <inheritdoc />
    EdaColor ISchSheet.FillColor => AltiumColorHelper.BgrToEdaColor(AreaColor);

    /// <inheritdoc />
    Coord ISchSheet.LineWidth => AltiumLineWidthHelper.IndexToCoord(LineWidth);

    /// <summary>
    /// Adds a sheet entry to this symbol.
    /// </summary>
    public void AddEntry(SchSheetEntry entry) => _entries.Add(entry);

    /// <summary>
    /// Index of the owning record in the schematic hierarchy.
    /// </summary>
    public int OwnerIndex { get; set; }

    /// <summary>
    /// Whether this primitive is not accessible for selection.
    /// </summary>
    public bool IsNotAccessible { get; set; }

    /// <summary>
    /// Index of this primitive within its parent sheet.
    /// </summary>
    public int IndexInSheet { get; set; }

    /// <summary>
    /// Part ID of the owning component (for multi-part components).
    /// </summary>
    public int OwnerPartId { get; set; }

    /// <summary>
    /// Display mode of the owning part.
    /// </summary>
    public int OwnerPartDisplayMode { get; set; }

    /// <summary>
    /// Whether this primitive is graphically locked.
    /// </summary>
    public bool GraphicallyLocked { get; set; }

    /// <summary>
    /// Whether this primitive is disabled.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Whether this primitive is dimmed in display.
    /// </summary>
    public bool Dimmed { get; set; }

    /// <summary>
    /// Unique identifier for this primitive.
    /// </summary>
    public string? UniqueId { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Altium anchors a sheet symbol by its <b>top-left</b> corner: <see cref="Location"/> is the top
    /// edge and the body extends downward by <see cref="YSize"/> (to <c>Location.Y - YSize</c>).
    /// </remarks>
    public CoordRect Bounds => new(
        new CoordPoint(Location.X, Location.Y - YSize),
        new CoordPoint(Location.X + XSize, Location.Y));

    /// <summary>
    /// The multi-channel <c>Repeat(...)</c> directive on this sheet symbol's designator, when present.
    /// A repeated sheet symbol instantiates its referenced sheet <see cref="RepeatInfo.InstanceCount"/>
    /// times (one channel per instance). Parsed from the designator <see cref="NameLabel"/> text
    /// (falling back to <see cref="SheetName"/>); <see cref="RepeatInfo.IsRepeated"/> is <c>false</c>
    /// when there is no <c>Repeat(...)</c>.
    /// </summary>
    public RepeatInfo Repeat => RepeatInfo.Parse(NameLabel?.Text ?? SheetName);
}

/// <summary>
/// A parsed Altium <c>Repeat(ChannelName, FirstInstance, LastInstance)</c> channel directive from a
/// sheet symbol's designator. When the designator has no <c>Repeat(...)</c>, <see cref="IsRepeated"/> is
/// <c>false</c>, <see cref="InstanceCount"/> is 1, and <see cref="ChannelName"/> is the plain designator.
/// </summary>
public readonly partial struct RepeatInfo
{
    private RepeatInfo(bool isRepeated, string? channelName, int firstInstance, int lastInstance, bool isUnreadable = false)
    {
        IsRepeated = isRepeated;
        ChannelName = channelName;
        FirstInstance = firstInstance;
        LastInstance = lastInstance;
        IsUnreadable = isUnreadable;
    }

    /// <summary>Whether the designator carries a <c>Repeat(...)</c> directive.</summary>
    public bool IsRepeated { get; }

    /// <summary>
    /// Whether the designator mentions <c>Repeat(</c> (see <see cref="MentionsRepeat"/>) but the
    /// directive cannot be read: its name is empty, its bounds are not plain ASCII whole numbers
    /// within range, it spans more than 100,000 channels, or it is not closed. <see cref="IsRepeated"/>
    /// is then <c>false</c>, so the symbol places its sheet once, as for a plain designator.
    /// </summary>
    public bool IsUnreadable { get; }

    /// <summary>The channel name (the <c>Repeat</c> first argument, or the plain designator).</summary>
    public string? ChannelName { get; }

    /// <summary>The first channel index (1 when not repeated).</summary>
    public int FirstInstance { get; }

    /// <summary>The last channel index (1 when not repeated).</summary>
    public int LastInstance { get; }

    /// <summary>The number of channel instances (<c>Last - First + 1</c>; 1 when not repeated).</summary>
    public int InstanceCount => Math.Max(1, LastInstance - FirstInstance + 1);

    // Each argument is matched as "anything but , ( )" and validated afterwards, so the pattern stays
    // linear on long input and an unreadable number cannot throw.
    [System.Text.RegularExpressions.GeneratedRegex(
        @"Repeat\s*\(([^,()]*),([^,()]*),([^,()]*)\)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex RepeatRegex();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"Repeat\s*\(",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex RepeatMentionRegex();

    /// <summary>
    /// Parses a sheet-symbol designator, tolerating any non-<c>Repeat</c> text. Never throws: a
    /// <c>Repeat(...)</c> whose name is empty or whose bounds are not plain ASCII integers within range
    /// (or span more than 100,000 channels) is treated as not repeated.
    /// </summary>
    public static RepeatInfo Parse(string? designator)
    {
        if (string.IsNullOrWhiteSpace(designator))
            return new RepeatInfo(false, designator, 1, 1);

        var plain = new RepeatInfo(false, designator.Trim(), 1, 1);
        var unreadable = new RepeatInfo(false, designator.Trim(), 1, 1, isUnreadable: true);
        var m = RepeatRegex().Match(designator);
        if (!m.Success)
            return MentionsRepeat(designator) ? unreadable : plain;

        var name = m.Groups[1].Value.Trim();
        if (name.Length == 0 || !TryParseIndex(m.Groups[2].Value, out var first) || !TryParseIndex(m.Groups[3].Value, out var last))
            return unreadable;
        if (last < first)
            (first, last) = (last, first);
        // Guard against absurd counts.
        if (last - first > 100_000)
            return unreadable;
        return new RepeatInfo(true, name, first, last);
    }

    /// <summary>
    /// Whether <paramref name="designator"/> contains <c>Repeat(</c> (case-insensitive, spaces allowed
    /// before the parenthesis), readable or not.
    /// </summary>
    public static bool MentionsRepeat(string? designator) =>
        designator is not null && RepeatMentionRegex().IsMatch(designator);

    // ASCII digits only: NumberStyles.None allows no sign, separators or whitespace, and int.TryParse
    // accepts no other digit characters.
    private static bool TryParseIndex(string text, out int value) =>
        int.TryParse(text.AsSpan().Trim(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out value);
}

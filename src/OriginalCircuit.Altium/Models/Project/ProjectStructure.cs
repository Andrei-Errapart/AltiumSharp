using System.Globalization;
using System.Text;

namespace OriginalCircuit.Altium.Models.Project;

/// <summary>
/// One pipe-delimited record from a <c>.PrjPcbStructure</c> file. The fields are kept in order
/// (so the file round-trips exactly) with typed accessors for the common keys.
/// </summary>
public sealed class ProjectStructureRecord
{
    /// <summary>Creates a record from its ordered fields.</summary>
    public ProjectStructureRecord(List<KeyValuePair<string, string>> fields)
    {
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    /// <summary>All <c>key=value</c> fields of the record, in file order, values stored exactly as written.</summary>
    public List<KeyValuePair<string, string>> Fields { get; }

    /// <summary>The record kind: the value of the leading <c>Record</c> field (e.g. <c>"TopLevelDocument"</c> or <c>"SheetSymbol"</c>).</summary>
    public string? Record => Field("Record");

    /// <summary>The document this record references (a sheet symbol's child sheet, or the top document).</summary>
    public string? FileName => Field("FileName");

    /// <summary>For a sheet symbol, the parent document that contains it.</summary>
    public string? SourceDocument => Field("SourceDocument");

    /// <summary>For a sheet symbol, its designator (instance name, e.g. <c>"U_Power"</c>).</summary>
    public string? Designator => Field("Designator");

    /// <summary>The assigned sheet number, when present.</summary>
    public int? SheetNumber =>
        int.TryParse(Field("SheetNumber"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Returns the first field with the given key (case-insensitive), or <c>null</c>.</summary>
    public string? Field(string key)
    {
        foreach (var f in Fields)
            if (string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))
                return f.Value;
        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Record}: {Designator ?? "(top)"} -> {FileName}";
}

/// <summary>
/// A node in a project's logical sheet hierarchy (built from the flat
/// <see cref="ProjectStructure.Records"/>). Each node is one sheet <em>instance</em>: the same
/// document may appear at several nodes if it is instantiated by more than one sheet symbol.
/// </summary>
public sealed class ProjectSheetNode
{
    internal ProjectSheetNode(string fileName, string? designator, int? sheetNumber)
    {
        FileName = fileName;
        Designator = designator;
        SheetNumber = sheetNumber;
    }

    /// <summary>The document this node represents (e.g. <c>"Power.SchDoc"</c>).</summary>
    public string FileName { get; }

    /// <summary>The instance designator of the sheet symbol that created this node, or <c>null</c> for the root.</summary>
    public string? Designator { get; }

    /// <summary>The sheet number assigned to this instance, when known.</summary>
    public int? SheetNumber { get; }

    /// <summary>The child sheets instantiated within this sheet.</summary>
    public List<ProjectSheetNode> Children { get; } = new();

    /// <summary><c>true</c> when this node was pruned because it would re-enter a document already on its ancestor chain.</summary>
    public bool IsCycle { get; internal set; }

    /// <summary>
    /// <c>true</c> when some of this node's descendants are missing because
    /// <see cref="ProjectStructure.BuildTree(int)"/> reached its node limit. The root and every node on
    /// the path to where the tree was cut are marked.
    /// </summary>
    public bool IsTruncated { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{(Designator is null ? "" : Designator + " -> ")}{FileName}" + (Children.Count > 0 ? $" ({Children.Count} children)" : "");
}

/// <summary>
/// The parsed contents of a <c>.PrjPcbStructure</c> file: the project's compiled logical document
/// structure (which schematic sheet symbols instantiate which child sheets). The raw
/// <see cref="Records"/> preserve the file byte-for-byte; <see cref="BuildTree()"/> turns them into a
/// navigable hierarchy.
/// </summary>
public sealed class ProjectStructure
{
    /// <summary>The records exactly as they appear in the file, in order.</summary>
    public List<ProjectStructureRecord> Records { get; } = new();

    // Read from bytes that were not UTF-8, so written back as Windows-1252.
    internal bool IsWindows1252 { get; set; }

    /// <summary>The top-level document file name (the <c>Record=TopLevelDocument</c> entry), or <c>null</c>.</summary>
    public string? TopLevelDocument
    {
        get
        {
            foreach (var r in Records)
                if (string.Equals(r.Record, "TopLevelDocument", StringComparison.OrdinalIgnoreCase))
                    return r.FileName;
            return null;
        }
    }

    /// <summary>The number of nodes <see cref="BuildTree()"/> builds at most.</summary>
    public const int DefaultMaxTreeNodes = 100_000;

    /// <summary>
    /// Builds the logical sheet hierarchy from <see cref="Records"/>, with at most
    /// <see cref="DefaultMaxTreeNodes"/> nodes (see <see cref="BuildTree(int)"/>).
    /// </summary>
    /// <returns>The root node, or <c>null</c> when there is no top-level document record.</returns>
    public ProjectSheetNode? BuildTree() => BuildTree(DefaultMaxTreeNodes);

    /// <summary>
    /// Builds the logical sheet hierarchy from <see cref="Records"/>. The root is the top-level
    /// document; each sheet's children are the sheet-symbol records whose <c>SourceDocument</c>
    /// equals that sheet's document. Re-entrant references (a document appearing on its own ancestor
    /// chain) are pruned and flagged via <see cref="ProjectSheetNode.IsCycle"/>. The tree is built
    /// without recursion, so any depth is safe, and stops after <paramref name="maxNodes"/> nodes
    /// (depth-first), because a small file whose sheets each place another several times describes
    /// an exponentially large tree; the nodes whose descendants were cut off are flagged via
    /// <see cref="ProjectSheetNode.IsTruncated"/>.
    /// </summary>
    /// <param name="maxNodes">The most nodes to build, including the root; at least 1.</param>
    /// <returns>The root node, or <c>null</c> when there is no top-level document record.</returns>
    public ProjectSheetNode? BuildTree(int maxNodes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        // Group sheet-symbol records by their parent document.
        var childrenByParent = new Dictionary<string, List<ProjectStructureRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Records)
        {
            if (!string.Equals(r.Record, "SheetSymbol", StringComparison.OrdinalIgnoreCase))
                continue;
            var parent = r.SourceDocument;
            if (string.IsNullOrEmpty(parent))
                continue;
            if (!childrenByParent.TryGetValue(parent, out var list))
                childrenByParent[parent] = list = new List<ProjectStructureRecord>();
            list.Add(r);
        }

        var topDoc = TopLevelDocument;
        if (string.IsNullOrEmpty(topDoc))
            return null;

        // Depth-first with an explicit stack of (node, index of its next child record), in the order
        // the records list the children.
        var root = new ProjectSheetNode(topDoc, null, TopSheetNumber());
        var nodes = 1;
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { topDoc };
        var stack = new Stack<(ProjectSheetNode Node, List<ProjectStructureRecord>? Children, int Next)>();
        stack.Push((root, childrenByParent.GetValueOrDefault(topDoc), 0));
        while (stack.Count > 0)
        {
            var (node, children, next) = stack.Pop();
            if (children is null || next >= children.Count)
            {
                if (stack.Count > 0)
                    ancestors.Remove(node.FileName);
                continue;
            }
            stack.Push((node, children, next + 1));

            var rec = children[next];
            var childDoc = rec.FileName;
            if (string.IsNullOrEmpty(childDoc))
                continue;
            if (nodes >= maxNodes)
            {
                foreach (var open in stack)
                    open.Node.IsTruncated = true;
                break;
            }

            var child = new ProjectSheetNode(childDoc, rec.Designator, rec.SheetNumber);
            node.Children.Add(child);
            nodes++;
            if (!ancestors.Add(childDoc))
            {
                child.IsCycle = true; // re-entrant reference: stop here
                continue;
            }
            stack.Push((child, childrenByParent.GetValueOrDefault(childDoc), 0));
        }
        return root;
    }

    private int? TopSheetNumber()
    {
        foreach (var r in Records)
            if (string.Equals(r.Record, "TopLevelDocument", StringComparison.OrdinalIgnoreCase))
                return r.SheetNumber;
        return null;
    }

    /// <summary>
    /// Reads a <c>.PrjPcbStructure</c> file from a stream of its bytes: UTF-8 (with or without a
    /// byte-order mark), or Windows-1252 when the bytes are not valid UTF-8, as for a file saved in
    /// the ANSI code page. The stream is read to the end and not closed. Use this, or
    /// <see cref="OriginalCircuit.Altium.AltiumLibrary.OpenProjectAsync(Stream, Stream?, CancellationToken)"/>,
    /// to give a project opened from a stream its structure:
    /// <c>project.Structure = ProjectStructure.Parse(structureStream)</c>.
    /// </summary>
    public static ProjectStructure Parse(Stream stream) => Parse(stream, CancellationToken.None);

    /// <summary>
    /// Reads a <c>.PrjPcbStructure</c> file from a stream, as <see cref="Parse(Stream)"/>, checking
    /// <paramref name="cancellationToken"/> while it parses.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static ProjectStructure Parse(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Parse(Serialization.AltiumEncoding.ReadToEnd(stream, cancellationToken), cancellationToken);
    }

    internal static ProjectStructure Parse(byte[] bytes) => Parse(bytes, CancellationToken.None);

    internal static ProjectStructure Parse(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        var structure = Parse(Serialization.AltiumEncoding.DecodeText(bytes, out _, out var isWindows1252), cancellationToken);
        structure.IsWindows1252 = isWindows1252;
        return structure;
    }

    internal static ProjectStructure Parse(ArraySegment<byte> bytes, CancellationToken cancellationToken) =>
        Parse(bytes.AsSpan(), cancellationToken);

    internal static ProjectStructure Parse(byte[] bytes, CancellationToken cancellationToken) =>
        Parse(bytes.AsSpan(), cancellationToken);

    /// <summary>
    /// Parses the text of a <c>.PrjPcbStructure</c> file into a <see cref="ProjectStructure"/>.
    /// A leading byte-order mark is ignored, blank lines are ignored, and each other line is one
    /// pipe-delimited record.
    /// </summary>
    public static ProjectStructure Parse(string text) => Parse(text, CancellationToken.None);

    // Line by line and field by field over the text, so only the keys and values are allocated.
    private static ProjectStructure Parse(string text, CancellationToken cancellationToken)
    {
        var structure = new ProjectStructure();
        if (string.IsNullOrEmpty(text))
            return structure;
        var start = text[0] == '\ufeff' ? 1 : 0;

        var keys = new Serialization.AltiumEncoding.KeyPool();
        var lines = 0;
        while (start <= text.Length)
        {
            if ((++lines & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;
            var line = text.AsSpan(start, end - start).TrimEnd('\r');
            start = end + 1;
            if (line.Length == 0)
                continue;

            var fields = new List<KeyValuePair<string, string>>();
            foreach (var range in line.Split('|'))
            {
                var segment = line[range];
                var eq = segment.IndexOf('=');
                fields.Add(eq < 0
                    ? new KeyValuePair<string, string>(segment.ToString(), string.Empty)
                    : new KeyValuePair<string, string>(keys.Get(segment[..eq]), segment[(eq + 1)..].ToString()));
            }
            structure.Records.Add(new ProjectStructureRecord(fields));
        }

        return structure;
    }

    /// <summary>
    /// Serialises this structure back to <c>.PrjPcbStructure</c> text. Each record is emitted as
    /// <c>key=value</c> fields joined by <c>|</c>, one record per line terminated by
    /// <paramref name="newLine"/> (Altium uses CRLF, with no BOM and no trailing blank line).
    /// </summary>
    public string Serialize(string newLine = "\r\n")
    {
        var sb = new StringBuilder();
        foreach (var record in Records)
        {
            for (var i = 0; i < record.Fields.Count; i++)
            {
                if (i > 0)
                    sb.Append('|');
                sb.Append(record.Fields[i].Key).Append('=').Append(record.Fields[i].Value);
            }
            sb.Append(newLine);
        }
        return sb.ToString();
    }
}

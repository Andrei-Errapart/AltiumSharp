using System.Globalization;
using System.Text;
using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;

namespace OriginalCircuit.Altium.Models.Project;

/// <summary>
/// A project's compiled schematic hierarchy: every sheet instance (one per sheet-symbol use,
/// including each <c>Repeat(...)</c> channel), every placed component part with its physical
/// designator, and the physical components those parts form. Built from documents already in
/// memory, so it works for uploads as well as files on disk.
/// </summary>
/// <remarks>
/// <para>
/// The rules follow what Altium compiles, checked against real multi-channel boards:
/// </para>
/// <list type="bullet">
/// <item>The root is the structure file's top-level document when it is supplied and not itself
/// referenced; otherwise every sheet no sheet symbol references.</item>
/// <item>Each sheet symbol creates one instance of its sheet; <c>Repeat(name, first, last)</c> creates
/// one per index. Sibling symbols are visited in sheet-position order.</item>
/// <item>Room names: a <c>Repeat</c> channel is the channel name plus its index (<c>CH3</c>); any
/// other instance is its symbol designator, numbered 1..n across the whole design in depth-first
/// order when several instances share that designator (<c>U_Driver1</c> .. <c>U_Driver4</c>).
/// This is the "flat numbered" room naming style (<c>ChannelRoomNamingStyle=0</c>).</item>
/// <item>A component on a sheet instantiated more than once gets the physical designator
/// <see cref="DesignatorFormat"/> (Altium's <c>ChannelDesignatorFormatString</c>, by default
/// <c>$Component_$RoomName</c>, e.g. <c>R2_CH3</c>); elsewhere it keeps its logical designator.</item>
/// <item>Component records with the same physical designator are the parts of one
/// <see cref="PhysicalComponent"/>, even when the parts sit on different sheets.</item>
/// </list>
/// <para>Problems such as missing sub-sheets or duplicate designators are reported in
/// <see cref="Diagnostics"/>; nothing throws for design content.</para>
/// </remarks>
public sealed class ProjectHierarchy
{
    /// <summary>Altium's default <c>ChannelDesignatorFormatString</c>.</summary>
    public const string DefaultDesignatorFormat = "$Component_$RoomName";

    private readonly ProjectHierarchyOptions _options;
    private readonly Dictionary<string, SchDocument> _documents;
    private readonly List<HierarchySheetInstance> _roots = new();
    private readonly List<HierarchySheetInstance> _sheets = new();
    private readonly List<PhysicalComponentPart> _parts = new();
    private readonly List<PhysicalComponent> _components = new();
    private readonly List<AltiumDiagnostic> _diagnostics;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PhysicalComponent> _byDesignator = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PhysicalComponentPart> _partByUniqueIdPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PhysicalComponentPart>> _partsByComponentUid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HierarchySheetInstance> _sheetByUniqueIdPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SchDocument, int> _designatedCounts = new(ReferenceEqualityComparer.Instance);
    private long _plannedParts;
    private bool _stopped;

    private ProjectHierarchy(AltiumProject? project, Dictionary<string, SchDocument> documents,
        List<AltiumDiagnostic> diagnostics, ProjectHierarchyOptions options)
    {
        Project = project;
        _documents = documents;
        _diagnostics = diagnostics;
        _options = options;
        var design = project?.GetSection("Design");
        DesignatorFormat = design?.Get("ChannelDesignatorFormatString") is { Length: > 0 } format ? format : DefaultDesignatorFormat;
        RoomNamingStyle = design?.Get("ChannelRoomNamingStyle") is { Length: > 0 } style ? style : "0";
    }

    /// <summary>The project the hierarchy was built for, or <c>null</c> when built from sheets alone.</summary>
    public AltiumProject? Project { get; }

    /// <summary>
    /// The format of a channel component's physical designator: the project's
    /// <c>ChannelDesignatorFormatString</c>, or <see cref="DefaultDesignatorFormat"/>. <c>$Component</c>
    /// is replaced by the logical designator and <c>$RoomName</c> by the sheet instance's room name.
    /// </summary>
    public string DesignatorFormat { get; }

    /// <summary>The project's <c>ChannelRoomNamingStyle</c> (<c>"0"</c> when absent). Only style 0 is modelled.</summary>
    public string RoomNamingStyle { get; }

    /// <summary>The schematic documents the hierarchy was built from, keyed by file name.</summary>
    public IReadOnlyDictionary<string, SchDocument> Documents => _documents;

    /// <summary>The top-level sheet instances (usually one).</summary>
    public IReadOnlyList<HierarchySheetInstance> Roots => _roots;

    /// <summary>Every sheet instance, depth-first, each parent before its children.</summary>
    public IReadOnlyList<HierarchySheetInstance> Sheets => _sheets;

    /// <summary>Every placed component part, in <see cref="Sheets"/> order then document order.</summary>
    public IReadOnlyList<PhysicalComponentPart> Parts => _parts;

    /// <summary>The physical components, in the order their first part appears.</summary>
    public IReadOnlyList<PhysicalComponent> Components => _components;

    /// <summary>Problems found while building: missing sheets, duplicate designators and the like.</summary>
    public IReadOnlyList<AltiumDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Whether a <see cref="ProjectHierarchyOptions"/> limit was reached, so part of the design is
    /// missing from <see cref="Sheets"/>, <see cref="Parts"/> and <see cref="Components"/>. An
    /// error in <see cref="Diagnostics"/> says which limit.
    /// </summary>
    public bool IsTruncated { get; private set; }

    /// <summary>Returns the physical component with the given physical designator (case-insensitive), or <c>null</c>.</summary>
    public PhysicalComponent? FindComponent(string designator)
    {
        ArgumentNullException.ThrowIfNull(designator);
        return _byDesignator.GetValueOrDefault(designator.Trim());
    }

    /// <summary>
    /// Returns the physical component for an instance path such as a PCB component's
    /// <c>SourceUniqueId</c> or a variant's <c>UniqueId</c>, or <c>null</c>. A path that names a
    /// sheet instance and a component on another sheet (as the PCB can for a multi-part component
    /// whose parts are on different sheets) is matched to the component with a part on that instance.
    /// </summary>
    public PhysicalComponent? FindComponentByUniqueIdPath(string uniqueIdPath)
    {
        ArgumentNullException.ThrowIfNull(uniqueIdPath);
        if (_partByUniqueIdPath.TryGetValue(uniqueIdPath, out var part))
            return part.Owner;

        var split = uniqueIdPath.LastIndexOf('\\');
        if (split < 0)
            return null;
        var componentUid = uniqueIdPath[(split + 1)..];
        if (!_partsByComponentUid.TryGetValue(componentUid, out var candidates))
            return null;
        if (!_sheetByUniqueIdPath.TryGetValue(uniqueIdPath[..split], out var sheet))
            return candidates.Count == 1 ? candidates[0].Owner : null;
        foreach (var candidate in candidates)
        {
            if (candidate.Owner.Parts.Any(p => ReferenceEquals(p.Sheet, sheet)))
                return candidate.Owner;
        }
        return null;
    }

    /// <summary>Returns the sheet instance with the given <see cref="HierarchySheetInstance.UniqueIdPath"/>, or <c>null</c>.</summary>
    public HierarchySheetInstance? FindSheetByUniqueIdPath(string uniqueIdPath)
    {
        ArgumentNullException.ThrowIfNull(uniqueIdPath);
        return _sheetByUniqueIdPath.GetValueOrDefault(uniqueIdPath);
    }

    // ---- Construction ------------------------------------------------------------------------------

    /// <summary>
    /// Builds the hierarchy from schematic documents that are already open. Each document is
    /// identified by its <see cref="SchDocument.FileName"/> (or the file name of
    /// <see cref="SchDocument.FilePath"/>), which must match the name its sheet symbols use; set it
    /// for documents opened from a stream.
    /// </summary>
    /// <param name="project">The project, for its structure file and channel settings; may be <c>null</c>.</param>
    /// <param name="sheets">The project's schematic documents, in project order.</param>
    /// <param name="options">Limits on the walk; <see cref="ProjectHierarchyOptions.Default"/> when <c>null</c>.</param>
    public static ProjectHierarchy Build(AltiumProject? project, IEnumerable<SchDocument> sheets, ProjectHierarchyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        return Build(project, sheets, new List<AltiumDiagnostic>(), options ?? ProjectHierarchyOptions.Default);
    }

    /// <summary>
    /// Opens the project's schematic documents through <paramref name="openDocument"/> (for example
    /// from the entries of an uploaded zip) and builds the hierarchy. The callback returns a stream of
    /// a document's bytes, or <c>null</c> when it is not available (reported in
    /// <see cref="Diagnostics"/>). Each returned stream is read and then disposed; it need not be
    /// seekable.
    /// </summary>
    /// <exception cref="AltiumCorruptFileException">A supplied document is not a readable SchDoc.</exception>
    public static async ValueTask<ProjectHierarchy> LoadAsync(
        AltiumProject project, Func<ProjectDocument, Stream?> openDocument,
        ProjectHierarchyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(openDocument);

        var diagnostics = new List<AltiumDiagnostic>();
        var sheets = new List<SchDocument>();
        foreach (var document in project.SchematicDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = openDocument(document);
            if (stream is null)
            {
                diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning,
                    $"Schematic document '{document.DocumentPath}' was not supplied."));
                continue;
            }

            SchDocument sheet;
            await using (stream.ConfigureAwait(false))
            {
                var seekable = stream;
                if (!stream.CanSeek)
                {
                    seekable = new MemoryStream();
                    await stream.CopyToAsync(seekable, cancellationToken).ConfigureAwait(false);
                    seekable.Position = 0;
                }
                try
                {
                    sheet = new SchDocReader().Read(seekable);
                }
                catch (AltiumCorruptFileException ex) when (ex.FilePath is null)
                {
                    throw new AltiumCorruptFileException(ex.Message, ex.StreamName, document.DocumentPath, ex);
                }
            }
            sheet.FileName = NormalizeFileName(document.DocumentPath);
            sheets.Add(sheet);
        }

        return Build(project, sheets, diagnostics, options ?? ProjectHierarchyOptions.Default);
    }

    /// <summary>
    /// Opens the project's schematic documents from disk, next to the project file
    /// (<see cref="AltiumProject.FilePath"/> must be set), and builds the hierarchy. Documents that
    /// are not found are reported in <see cref="Diagnostics"/>.
    /// </summary>
    public static async ValueTask<ProjectHierarchy> LoadAsync(
        AltiumProject project, ProjectHierarchyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<AltiumDiagnostic>();
        var sheets = new List<SchDocument>();
        foreach (var document in project.SchematicDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = project.ResolveDocumentPath(document);
            if (path is null || !File.Exists(path))
            {
                diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning,
                    $"Schematic document not found: {document.DocumentPath}"));
                continue;
            }
            var sheet = (SchDocument)await AltiumLibrary.OpenSchDocAsync(path, cancellationToken).ConfigureAwait(false);
            sheet.FileName = NormalizeFileName(document.DocumentPath) ?? Path.GetFileName(path);
            sheets.Add(sheet);
        }

        return Build(project, sheets, diagnostics, options ?? ProjectHierarchyOptions.Default);
    }

    private static ProjectHierarchy Build(AltiumProject? project, IEnumerable<SchDocument> sheets,
        List<AltiumDiagnostic> diagnostics, ProjectHierarchyOptions options)
    {
        var documents = new Dictionary<string, SchDocument>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var sheet in sheets)
        {
            if (sheet is null)
                continue;
            var name = NormalizeFileName(sheet.FileName) ?? NormalizeFileName(sheet.FilePath);
            if (name is null)
            {
                diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning,
                    "A schematic document without a file name was skipped; set SchDocument.FileName to the name its sheet symbols use."));
                continue;
            }
            if (!documents.TryAdd(name, sheet))
            {
                diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning,
                    $"More than one schematic document is named '{name}'; only the first is used."));
                continue;
            }
            order.Add(name);
        }

        var hierarchy = new ProjectHierarchy(project, documents, diagnostics, options);
        hierarchy.Compile(order);
        return hierarchy;
    }

    private void Compile(IReadOnlyList<string> documentOrder)
    {
        if (RoomNamingStyle != "0")
            Report(DiagnosticSeverity.Warning,
                $"Channel room naming style {RoomNamingStyle} is not modelled; rooms are named as for style 0 (flat numbered).");
        var unknownTokens = DesignatorTokens(DesignatorFormat).Where(t => !IsKnownToken(t)).ToList();
        if (unknownTokens.Count > 0)
            Report(DiagnosticSeverity.Warning,
                $"The channel designator format '{DesignatorFormat}' uses {string.Join(", ", unknownTokens.Select(t => "$" + t))}, " +
                "which are left as written; only $Component and $RoomName are expanded.");

        foreach (var root in DetermineRoots(documentOrder))
        {
            if (_stopped)
                break;
            Walk(root);
        }

        var instantiated = new HashSet<string>(_sheets.Select(s => s.FileName), StringComparer.OrdinalIgnoreCase);
        foreach (var name in documentOrder.Where(n => !instantiated.Contains(n)))
            Report(DiagnosticSeverity.Info, $"Sheet '{name}' is not part of the hierarchy and was left out.");

        AssignRooms();
        CollectParts();
        MergeComponents();
    }

    private List<string> DetermineRoots(IReadOnlyList<string> documentOrder)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in documentOrder)
        {
            foreach (var symbol in _documents[name].SheetSymbols)
            {
                if (NormalizeFileName(symbol.FileName) is { } child && !string.Equals(child, name, StringComparison.OrdinalIgnoreCase))
                    referenced.Add(child);
            }
        }

        // The compiled top-level document, unless the structure file is stale and another sheet now uses it.
        var top = NormalizeFileName(Project?.Structure?.TopLevelDocument);
        if (top is not null && _documents.ContainsKey(top) && !referenced.Contains(top))
            return new List<string> { top };
        if (top is not null)
            Report(DiagnosticSeverity.Info,
                $"The structure file's top-level document '{top}' is {(_documents.ContainsKey(top) ? "used by a sheet symbol" : "not supplied")}; the roots are the sheets no sheet symbol uses.");

        var roots = documentOrder.Where(n => !referenced.Contains(n)).ToList();
        if (roots.Count == 0 && documentOrder.Count > 0)
        {
            Report(DiagnosticSeverity.Warning,
                $"Every sheet is used by a sheet symbol (a cycle); the hierarchy starts from '{documentOrder[0]}'.");
            roots.Add(documentOrder[0]);
        }
        return roots;
    }

    // A sheet instance whose sheet symbols are being expanded. The walk keeps these on an explicit
    // stack rather than recursing, so a crafted chain of thousands of sheets cannot overflow the
    // call stack (which would end the process).
    private sealed class WalkFrame(HierarchySheetInstance instance, List<SchSheetSymbol> symbols)
    {
        public HierarchySheetInstance Instance { get; } = instance;
        public List<SchSheetSymbol> Symbols { get; } = symbols;
        public int NextSymbol { get; set; }
        public SchSheetSymbol? Symbol { get; set; }
        public string ChildName { get; set; } = string.Empty;
        public string SymbolUid { get; set; } = string.Empty;
        public RepeatInfo Repeat { get; set; }
        public int NextIndex { get; set; }
    }

    // Depth-first, each instance before its children and siblings in OrderSiblings order.
    private void Walk(string rootName)
    {
        var root = Enter(rootName, parent: null, symbol: null, repeatIndex: null, Array.Empty<string>());
        if (root is null)
            return;
        _roots.Add(root);

        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootName };
        var stack = new Stack<WalkFrame>();
        stack.Push(new WalkFrame(root, OrderSiblings(root.Document.SheetSymbols).ToList()));
        while (stack.Count > 0 && !_stopped)
        {
            var frame = stack.Peek();
            if (frame.Symbol is null && !StartNextSymbol(frame, ancestors))
            {
                stack.Pop();
                if (frame.Instance.Parent is not null)
                    ancestors.Remove(frame.Instance.FileName);
                continue;
            }

            var repeat = frame.Repeat;
            var k = frame.NextIndex++;
            if (k > repeat.LastInstance)
            {
                frame.Symbol = null;
                continue;
            }

            // Altium names a Repeat channel by its index followed by the symbol's unique id ("5ABCDEFGH").
            var segment = repeat.IsRepeated ? k.ToString(CultureInfo.InvariantCulture) + frame.SymbolUid : frame.SymbolUid;
            var child = Enter(frame.ChildName, frame.Instance, frame.Symbol, repeat.IsRepeated ? k : null,
                Append(frame.Instance.SymbolUniqueIds, segment));
            if (child is null)
                break;
            ancestors.Add(child.FileName);
            stack.Push(new WalkFrame(child, OrderSiblings(child.Document.SheetSymbols).ToList()));
        }
    }

    // Moves the frame to its next sheet symbol that can be followed, reporting those that cannot.
    private bool StartNextSymbol(WalkFrame frame, HashSet<string> ancestors)
    {
        var fileName = frame.Instance.FileName;
        while (frame.NextSymbol < frame.Symbols.Count)
        {
            var symbol = frame.Symbols[frame.NextSymbol++];
            var childName = NormalizeFileName(symbol.FileName);
            if (childName is null)
            {
                Report(DiagnosticSeverity.Warning, $"A sheet symbol on '{fileName}' names no sheet file.");
                continue;
            }
            if (!_documents.ContainsKey(childName))
            {
                Report(DiagnosticSeverity.Warning, $"Sheet '{fileName}' uses sub-sheet '{symbol.FileName}', which was not supplied.");
                continue;
            }
            if (ancestors.Contains(childName))
            {
                Report(DiagnosticSeverity.Warning, $"Sheet '{fileName}' uses '{childName}', which is one of its own parents; the cycle was skipped.");
                continue;
            }
            if (frame.Instance.Depth >= _options.MaxDepth)
            {
                IsTruncated = true;
                Report(DiagnosticSeverity.Error,
                    $"The hierarchy is deeper than {_options.MaxDepth} levels (ProjectHierarchyOptions.MaxDepth); sheets below that depth were left out.");
                continue;
            }

            var designator = symbol.NameLabel?.Text ?? symbol.SheetName;
            var repeat = symbol.Repeat;
            if (!repeat.IsRepeated && RepeatInfo.MentionsRepeat(designator))
                Report(DiagnosticSeverity.Warning,
                    $"Sheet symbol '{designator}' on '{fileName}' has a Repeat(...) that cannot be read " +
                    "(it needs a name and two whole numbers at most 100,000 apart); it is treated as one instance.");

            frame.Symbol = symbol;
            frame.ChildName = childName;
            frame.SymbolUid = string.IsNullOrEmpty(symbol.UniqueId) ? (symbol.SheetName ?? childName) : symbol.UniqueId;
            frame.Repeat = repeat;
            frame.NextIndex = repeat.FirstInstance;
            return true;
        }
        return false;
    }

    // Creates a sheet instance, or stops the walk (returning null) once a limit is reached.
    private HierarchySheetInstance? Enter(string fileName, HierarchySheetInstance? parent, SchSheetSymbol? symbol,
        int? repeatIndex, IReadOnlyList<string> symbolUniqueIds)
    {
        if (_sheets.Count >= _options.MaxSheetInstances)
        {
            Stop($"The hierarchy has more than {_options.MaxSheetInstances} sheet instances (ProjectHierarchyOptions.MaxSheetInstances); " +
                 "the walk stopped there.");
            return null;
        }
        var document = _documents[fileName];
        var parts = DesignatedComponentCount(document);
        if (_plannedParts + parts > _options.MaxComponentParts)
        {
            Stop($"The design places more than {_options.MaxComponentParts} component parts (ProjectHierarchyOptions.MaxComponentParts); " +
                 "the walk stopped there.");
            return null;
        }
        _plannedParts += parts;

        var instance = new HierarchySheetInstance(_sheets.Count, document, fileName, parent, symbol, repeatIndex, symbolUniqueIds);
        _sheets.Add(instance);
        parent?.AddChild(instance);
        if (instance.UniqueIdPath.Length > 0)
            _sheetByUniqueIdPath.TryAdd(instance.UniqueIdPath, instance);
        return instance;
    }

    // The number of parts CollectParts takes from one instance of the document.
    private int DesignatedComponentCount(SchDocument document)
    {
        if (!_designatedCounts.TryGetValue(document, out var count))
        {
            count = document.Components.OfType<SchComponent>().Count(c => HasDesignator(c));
            _designatedCounts[document] = count;
        }
        return count;
    }

    private static bool HasDesignator(SchComponent component) => !string.IsNullOrWhiteSpace(component.Designator);

    private void Stop(string message)
    {
        _stopped = true;
        IsTruncated = true;
        Report(DiagnosticSeverity.Error, message);
    }

    // Altium numbers channels by where their sheet symbols sit, not by record order: bottom to top
    // (ascending Y), then left to right. On real boards record order misnumbers about a third of
    // the channels while this order matches every PCB designator. (Those boards cannot tell it
    // apart from left-to-right first, then bottom to top.)
    internal static IEnumerable<SchSheetSymbol> OrderSiblings(IReadOnlyList<SchSheetSymbol> symbols) =>
        symbols.Select((symbol, index) => (symbol, index))
            .OrderBy(s => s.symbol.Location.Y.ToRaw())
            .ThenBy(s => s.symbol.Location.X.ToRaw())
            .ThenBy(s => s.index)
            .Select(s => s.symbol);

    private void AssignRooms()
    {
        var instancesPerFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var namedCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in _sheets)
        {
            instancesPerFile[sheet.FileName] = instancesPerFile.GetValueOrDefault(sheet.FileName) + 1;
            if (sheet.Parent is null || sheet.SheetSymbol is not { } symbol)
            {
                sheet.HierarchicalPath = Path.GetFileNameWithoutExtension(sheet.FileName);
                continue;
            }
            var designator = sheet.SymbolDesignator?.Trim();
            sheet.ChannelName = sheet.RepeatIndex is null
                ? (string.IsNullOrEmpty(designator) ? Path.GetFileNameWithoutExtension(sheet.FileName) : designator)
                : symbol.Repeat.ChannelName;
            if (sheet.RepeatIndex is null)
                namedCount[sheet.ChannelName!] = namedCount.GetValueOrDefault(sheet.ChannelName!) + 1;
            // Parents precede children in _sheets, so the parent's path is already set.
            sheet.HierarchicalPath = sheet.Parent.HierarchicalPath + "\\" + sheet.ChannelName
                + (sheet.RepeatIndex is { } k ? k.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }

        var numbered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in _sheets)
        {
            sheet.IsMultiChannel = instancesPerFile[sheet.FileName] > 1;
            if (sheet.ChannelName is not { } name)
                continue;
            if (sheet.RepeatIndex is { } k)
            {
                sheet.ChannelIndex = k;
            }
            else if (namedCount[name] > 1)
            {
                sheet.ChannelIndex = numbered[name] = numbered.GetValueOrDefault(name) + 1;
            }
            sheet.RoomName = sheet.ChannelIndex is { } index ? name + index.ToString(CultureInfo.InvariantCulture) : name;
        }

        foreach (var shared in _sheets.Where(s => s.IsMultiChannel && s.RoomName is not null)
                     .GroupBy(s => s.RoomName!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            Report(DiagnosticSeverity.Warning,
                $"Room name '{shared.Key}' is shared by {shared.Count()} sheet instances, so their physical designators can collide.");
        }
    }

    private void CollectParts()
    {
        foreach (var sheet in _sheets)
        {
            var skipped = 0;
            foreach (var component in sheet.Document.Components.OfType<SchComponent>())
            {
                var logical = component.Designator?.Trim();
                if (string.IsNullOrEmpty(logical))
                {
                    skipped++;
                    continue;
                }
                var physical = sheet.IsMultiChannel && sheet.RoomName is { } room
                    ? FormatDesignator(DesignatorFormat, logical, room)
                    : logical;
                var part = new PhysicalComponentPart(component, sheet, logical, physical);
                sheet.AddPart(part);
                _parts.Add(part);
                _partByUniqueIdPath.TryAdd(part.UniqueIdPath, part);
                if (!string.IsNullOrEmpty(component.UniqueId))
                {
                    if (!_partsByComponentUid.TryGetValue(component.UniqueId, out var list))
                        _partsByComponentUid[component.UniqueId] = list = new List<PhysicalComponentPart>();
                    list.Add(part);
                }
            }
            if (skipped > 0)
                Report(DiagnosticSeverity.Info, $"{skipped} component(s) on '{sheet.FileName}' have no designator and were left out.");
        }
    }

    private void MergeComponents()
    {
        var groups = new Dictionary<string, List<PhysicalComponentPart>>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<List<PhysicalComponentPart>>();
        var unannotated = 0;
        foreach (var part in _parts)
        {
            // An unannotated designator ("R?") names no particular component: keep each record apart.
            if (part.LogicalDesignator.Contains('?', StringComparison.Ordinal))
            {
                unannotated++;
                ordered.Add(new List<PhysicalComponentPart> { part });
                continue;
            }
            if (!groups.TryGetValue(part.PhysicalDesignator, out var group))
            {
                groups[part.PhysicalDesignator] = group = new List<PhysicalComponentPart>();
                ordered.Add(group);
            }
            group.Add(part);
        }

        foreach (var group in ordered)
        {
            var parts = group.OrderBy(p => p.PartId).ToList(); // stable: walk order within a part number
            var component = new PhysicalComponent(this, parts);
            _components.Add(component);
            if (parts[0].LogicalDesignator.Contains('?', StringComparison.Ordinal))
                continue;
            _byDesignator[component.Designator] = component;
            if (parts.GroupBy(p => p.PartId).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
                Report(DiagnosticSeverity.Warning,
                    $"Designator '{component.Designator}' is used by {duplicate.Count()} component records for the same part; they are counted as one component.");
        }

        if (unannotated > 0)
            Report(DiagnosticSeverity.Warning,
                $"{unannotated} component(s) are not annotated (their designator contains '?'); each is kept as a separate component.");
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// Expands a channel designator format: <c>$Component</c> becomes <paramref name="component"/> and
    /// <c>$RoomName</c> becomes <paramref name="roomName"/> (case-insensitively); other text and other
    /// <c>$</c> tokens are kept as written.
    /// </summary>
    public static string FormatDesignator(string format, string component, string roomName)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(roomName);
        var sb = new StringBuilder(format.Length + component.Length + roomName.Length);
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '$')
            {
                sb.Append(format[i]);
                continue;
            }
            var end = TokenEnd(format, i + 1);
            var token = format.AsSpan(i + 1, end - i - 1);
            if (token.Equals("Component", StringComparison.OrdinalIgnoreCase))
                sb.Append(component);
            else if (token.Equals("RoomName", StringComparison.OrdinalIgnoreCase))
                sb.Append(roomName);
            else
                sb.Append(format, i, end - i);
            i = end - 1;
        }
        return sb.ToString();
    }

    private static IEnumerable<string> DesignatorTokens(string format)
    {
        for (var i = format.IndexOf('$'); i >= 0 && i < format.Length; i = format.IndexOf('$', i + 1))
        {
            var end = TokenEnd(format, i + 1);
            if (end > i + 1)
                yield return format[(i + 1)..end];
        }
    }

    private static int TokenEnd(string format, int start)
    {
        var end = start;
        while (end < format.Length && char.IsLetterOrDigit(format[end]))
            end++;
        return end;
    }

    private static bool IsKnownToken(string token) =>
        token.Equals("Component", StringComparison.OrdinalIgnoreCase) || token.Equals("RoomName", StringComparison.OrdinalIgnoreCase);

    private void Report(DiagnosticSeverity severity, string message)
    {
        if (_reported.Add(message))
            _diagnostics.Add(new AltiumDiagnostic(severity, message));
    }

    private static IReadOnlyList<string> Append(IReadOnlyList<string> path, string entry)
    {
        var list = new List<string>(path.Count + 1);
        list.AddRange(path);
        list.Add(entry);
        return list;
    }

    internal static string? NormalizeFileName(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName.Trim().Replace('\\', '/'));
}

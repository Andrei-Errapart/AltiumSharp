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
/// <c>$Component_$RoomName</c>, e.g. <c>R2_CH3</c>; see <see cref="FormatDesignator(string, string, string, string?, int?)"/>
/// for the keywords); elsewhere it keeps its logical designator.</item>
/// <item>Component records with the same physical designator and different part numbers (U1A,
/// U1B, ...) from the same library symbol are the parts of one <see cref="PhysicalComponent"/>,
/// even when the parts sit on different sheets. Records that cannot be parts of one component
/// (two R5s, or a second U1A) are separate components with the same designator, listed in
/// <see cref="DuplicateDesignators"/>.</item>
/// </list>
/// <para>Problems such as missing sub-sheets or duplicate designators are reported in
/// <see cref="Diagnostics"/>; nothing throws for design content.</para>
/// </remarks>
public sealed class ProjectHierarchy
{
    /// <summary>Altium's default <c>ChannelDesignatorFormatString</c>.</summary>
    public const string DefaultDesignatorFormat = "$Component_$RoomName";

    private readonly ProjectHierarchyOptions _options;
    private readonly List<HeldDocument> _held;
    private readonly Dictionary<string, HeldDocument> _heldByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly SheetFinder _finder;
    private readonly Dictionary<string, SchDocument> _documentsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SchDocument> _documentsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HierarchySheetInstance> _roots = new();
    private readonly List<HierarchySheetInstance> _sheets = new();
    private readonly List<PhysicalComponentPart> _parts = new();
    private readonly List<PhysicalComponent> _components = new();
    private readonly List<AltiumDiagnostic> _diagnostics;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly HashSet<(AltiumDiagnosticCode, SchSheetSymbol)> _reportedSymbols = new();
    private readonly Dictionary<string, List<PhysicalComponent>> _byDesignator = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _duplicateDesignators = new();
    private readonly Dictionary<string, PhysicalComponentPart> _partByUniqueIdPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PhysicalComponentPart>> _partsByComponentUid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HierarchySheetInstance> _sheetByUniqueIdPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SchDocument, int> _designatedCounts = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _missing = new();
    private readonly HashSet<string> _missingNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unfilledTokens = new(StringComparer.OrdinalIgnoreCase);
    private bool _formatHasUnknownTokens;
    private long _plannedParts;
    private bool _stopped;
    private CancellationToken _cancellationToken;

    private ProjectHierarchy(AltiumProject? project, List<HeldDocument> held, SheetFinder finder,
        List<AltiumDiagnostic> diagnostics, List<string> missing, ProjectHierarchyOptions options)
    {
        Project = project;
        _held = held;
        _finder = finder;
        foreach (var document in held)
        {
            _heldByKey[document.Key] = document;
            _documentsByKey[document.Key] = document.Document;
            _documentsByName.TryAdd(document.Name, document.Document);
        }
        _diagnostics = diagnostics;
        _options = options;
        foreach (var path in missing)
            AddMissing(path);
        var design = project?.GetSection("Design");
        // Trimmed, and blank counts as absent: a designator never starts or ends with a space.
        DesignatorFormat = design?.Get("ChannelDesignatorFormatString")?.Trim() is { Length: > 0 } format ? format : DefaultDesignatorFormat;
        RoomNamingStyle = design?.Get("ChannelRoomNamingStyle")?.Trim() is { Length: > 0 } style ? style : "0";
    }

    /// <summary>The project the hierarchy was built for, or <c>null</c> when built from sheets alone.</summary>
    public AltiumProject? Project { get; }

    /// <summary>
    /// The format of a channel component's physical designator: the project's
    /// <c>ChannelDesignatorFormatString</c>, trimmed, or <see cref="DefaultDesignatorFormat"/> when that
    /// is absent or blank. Its keywords are
    /// those Altium documents (see <see cref="FormatDesignator(string, string, string, string?, int?)"/>).
    /// A component the format cannot be filled for (an unknown keyword, or one the component or its
    /// sheet has no value for) gets <see cref="DefaultDesignatorFormat"/> instead, and a diagnostic says so.
    /// </summary>
    public string DesignatorFormat { get; }

    /// <summary>The project's <c>ChannelRoomNamingStyle</c>, trimmed (<c>"0"</c> when absent or blank). Only style 0 is modelled.</summary>
    public string RoomNamingStyle { get; }

    /// <summary>
    /// The schematic documents the hierarchy was built from, keyed by file name
    /// (<see cref="NormalizeFileName"/>, compared case-insensitively). When documents read from
    /// different locations share a file name (see <see cref="DocumentsByKey"/>), this holds the first.
    /// </summary>
    public IReadOnlyDictionary<string, SchDocument> Documents => _documentsByName;

    /// <summary>
    /// The schematic documents the hierarchy was built from, keyed by
    /// <see cref="HierarchySheetInstance.DocumentKey"/> (compared case-insensitively): the location a
    /// loader read each one from (the full path for <see cref="LoadAsync(AltiumProject, ProjectHierarchyOptions?, CancellationToken)"/>),
    /// or the file name for a document known by its file name (<see cref="Build(AltiumProject?, IEnumerable{SchDocument}, ProjectHierarchyOptions?)"/>
    /// and <see cref="LoadAsync(AltiumProject, Func{string, Stream?}, ProjectHierarchyOptions?, CancellationToken)"/>),
    /// in the order they were read.
    /// </summary>
    public IReadOnlyDictionary<string, SchDocument> DocumentsByKey => _documentsByKey;

    /// <summary>The top-level sheet instances (usually one).</summary>
    public IReadOnlyList<HierarchySheetInstance> Roots => _roots;

    /// <summary>Every sheet instance, depth-first, each parent before its children.</summary>
    public IReadOnlyList<HierarchySheetInstance> Sheets => _sheets;

    /// <summary>Every placed component part, in <see cref="Sheets"/> order then document order.</summary>
    public IReadOnlyList<PhysicalComponentPart> Parts => _parts;

    /// <summary>
    /// The physical components, in the order their first part appears. Components that share a
    /// designator (see <see cref="DuplicateDesignators"/>) are each listed, so a BOM counts every one.
    /// </summary>
    public IReadOnlyList<PhysicalComponent> Components => _components;

    /// <summary>
    /// The physical designators that more than one component has, in the order they first appear:
    /// separate components given the same designator, which Altium's compiler reports as an error.
    /// <see cref="FindComponents"/> returns the components; each designator is also reported as an
    /// <see cref="AltiumDiagnosticCode.DuplicateDesignator"/> diagnostic. Unannotated designators
    /// (<c>R?</c>) are not listed.
    /// </summary>
    public IReadOnlyList<string> DuplicateDesignators => _duplicateDesignators;

    /// <summary>Problems found while building: missing sheets, duplicate designators and the like.</summary>
    public IReadOnlyList<AltiumDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Whether a <see cref="ProjectHierarchyOptions"/> limit was reached, so part of the design is
    /// missing from <see cref="Sheets"/>, <see cref="Parts"/> and <see cref="Components"/>. An
    /// error in <see cref="Diagnostics"/> says which limit.
    /// </summary>
    public bool IsTruncated { get; private set; }

    /// <summary>
    /// The schematic documents the design needs but that were not supplied, each once and as written:
    /// project documents (their <see cref="ProjectDocument.DocumentPath"/>) and sheets that sheet
    /// symbols use (the symbol's file name, e.g. an Altium device sheet).
    /// </summary>
    public IReadOnlyList<string> MissingDocuments => _missing;

    /// <summary>
    /// Returns the physical component with the given physical designator (case-insensitive), or
    /// <c>null</c>. For a designator in <see cref="DuplicateDesignators"/> it is the first of them;
    /// <see cref="FindComponents"/> returns them all.
    /// </summary>
    public PhysicalComponent? FindComponent(string designator)
    {
        ArgumentNullException.ThrowIfNull(designator);
        return _byDesignator.TryGetValue(designator.Trim(), out var components) ? components[0] : null;
    }

    /// <summary>
    /// Returns every physical component with the given physical designator (case-insensitive), in
    /// <see cref="Components"/> order: more than one for a designator in <see cref="DuplicateDesignators"/>,
    /// none when no component has it.
    /// </summary>
    public IReadOnlyList<PhysicalComponent> FindComponents(string designator)
    {
        ArgumentNullException.ThrowIfNull(designator);
        return _byDesignator.TryGetValue(designator.Trim(), out var components) ? components : Array.Empty<PhysicalComponent>();
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
    /// for documents opened from a stream. A sheet symbol places the document of its file name, so
    /// only one document of each file name is used.
    /// </summary>
    /// <param name="project">The project, for its structure file and channel settings; may be <c>null</c>.</param>
    /// <param name="sheets">The project's schematic documents, in project order.</param>
    /// <param name="options">Limits on the walk; <see cref="ProjectHierarchyOptions.Default"/> when <c>null</c>.</param>
    public static ProjectHierarchy Build(AltiumProject? project, IEnumerable<SchDocument> sheets, ProjectHierarchyOptions? options = null) =>
        Build(project, sheets, options, CancellationToken.None);

    /// <summary>
    /// Builds the hierarchy from schematic documents that are already open, as
    /// <see cref="Build(AltiumProject?, IEnumerable{SchDocument}, ProjectHierarchyOptions?)"/>, checking
    /// <paramref name="cancellationToken"/> while it works.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static ProjectHierarchy Build(AltiumProject? project, IEnumerable<SchDocument> sheets, ProjectHierarchyOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        var list = sheets.Where(s => s is not null).ToList();
        var diagnostics = new List<AltiumDiagnostic>();
        var missing = new List<string>();
        if (project is not null)
        {
            ReportSameNamedDocuments(project, diagnostics, "only one document of that name is used");
            var supplied = new HashSet<string>(
                list.Select(s => NormalizeFileName(s.FileName) ?? NormalizeFileName(s.FilePath)).OfType<string>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var document in project.SchematicDocuments)
            {
                if (NormalizeFileName(document.DocumentPath) is { } name && !supplied.Contains(name))
                {
                    missing.Add(document.DocumentPath!);
                    diagnostics.Add(NotSupplied(document.DocumentPath!));
                }
            }
        }

        // Each document is known by its file name.
        var finder = new SheetFinder(pathsAreRelative: false);
        var held = new List<HeldDocument>();
        foreach (var sheet in list)
        {
            var name = NormalizeFileName(sheet.FileName) ?? NormalizeFileName(sheet.FilePath);
            if (name is null)
            {
                diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning,
                    "A schematic document without a file name was skipped; set SchDocument.FileName to the name its sheet symbols use.")
                {
                    Code = AltiumDiagnosticCode.DocumentWithoutFileName,
                });
                continue;
            }
            if (finder.NameKeyed.ContainsKey(name))
            {
                diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning,
                    $"More than one schematic document is named '{name}'; only the first is used.")
                {
                    Code = AltiumDiagnosticCode.DuplicateDocumentName,
                    DocumentName = name,
                });
                continue;
            }
            var document = new HeldDocument(sheet, name, name, name, location: null, listed: false);
            held.Add(document);
            finder.NameKeyed[name] = document;
        }
        return Build(project, held, finder, diagnostics, missing, options ?? ProjectHierarchyOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Opens the schematic documents through <paramref name="openDocument"/> (for example from the
    /// entries of an uploaded zip) and builds the hierarchy. The callback is given a document's path
    /// as written: first each schematic <see cref="ProjectDocument.DocumentPath"/> of the project, then
    /// the file name of each sheet a sheet symbol uses that the project does not list (such as an
    /// Altium device sheet, e.g. <c>"DeviceSheets\USB-C.SchDoc"</c>), repeating for the sheets those
    /// use until no new names appear. It returns a stream of the document's bytes, or <c>null</c>
    /// when it is not available; each path of a name is asked for in turn until one gives a
    /// document, and only one document of each file name is read. Names no document was found for
    /// are listed in <see cref="MissingDocuments"/> and reported in <see cref="Diagnostics"/>. Each
    /// returned stream is read and then disposed; it need not be seekable.
    /// </summary>
    /// <exception cref="AltiumCorruptFileException">A supplied document is not a readable SchDoc; <see cref="AltiumFileException.FilePath"/> is its path as given to the callback.</exception>
    /// <exception cref="AltiumUnsupportedFeatureException">A supplied document uses a format or feature the reader does not support; <see cref="AltiumFileException.FilePath"/> is its path as given to the callback.</exception>
    public static ValueTask<ProjectHierarchy> LoadAsync(
        AltiumProject project, Func<string, Stream?> openDocument,
        ProjectHierarchyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(openDocument);

        // Known by file name, so a name's first document stands for every path of that name.
        return LoadCoreAsync(project, pathsAreRelative: false, sameNamedListed: true, (path, _, _) =>
            ValueTask.FromResult(openDocument(path) is { } stream ? FromStream(stream, location: null, path) : null),
            options ?? ProjectHierarchyOptions.Default, cancellationToken);
    }

    // Reads a stream (buffered when it cannot seek) as a SchDoc and disposes it.
    private static DocumentSource FromStream(Stream stream, string? location, string path) => new(location, async ct =>
    {
        await using (stream.ConfigureAwait(false))
        {
            var seekable = stream;
            if (!stream.CanSeek)
            {
                seekable = new MemoryStream();
                await stream.CopyToAsync(seekable, ct).ConfigureAwait(false);
                seekable.Position = 0;
            }
            try
            {
                return new SchDocReader().Read(seekable);
            }
            catch (AltiumFileException ex) when (ex.FilePath is null)
            {
                throw WithPath(ex, path);
            }
        }
    }, stream);

    // The same kind of exception, naming the document it was thrown for.
    private static AltiumFileException WithPath(AltiumFileException ex, string path) => ex switch
    {
        AltiumAsciiFileException => new AltiumAsciiFileException(ex.Message, path, ex),
        AltiumCorruptFileException corrupt => new AltiumCorruptFileException(ex.Message, corrupt.StreamName, path, ex),
        AltiumUnsupportedFeatureException unsupported => new AltiumUnsupportedFeatureException(ex.Message, unsupported.RecordType, path, ex),
        _ => new AltiumFileException(ex.Message, path, ex),
    };

    /// <summary>
    /// Opens the schematic documents from disk and builds the hierarchy: the project's schematic
    /// documents next to the project file (<see cref="AltiumProject.FilePath"/> must be set), then any
    /// sheet a sheet symbol uses that the project does not list, looked for at the symbol's path if it
    /// is absolute, else next to the sheet that uses it, else next to the project. Documents are
    /// known by the file they were read from (<see cref="HierarchySheetInstance.DocumentKey"/>), so
    /// two files of one name in different folders are two sheets; a sheet symbol places the project
    /// document of its file name, unless its path leads to another document the project lists, and
    /// otherwise the file its path leads to. Documents that are not found are listed in
    /// <see cref="MissingDocuments"/> and reported in <see cref="Diagnostics"/>.
    /// </summary>
    /// <exception cref="AltiumCorruptFileException">A document is not a readable SchDoc; <see cref="AltiumFileException.FilePath"/> is the file.</exception>
    /// <exception cref="AltiumUnsupportedFeatureException">A document uses a format or feature the reader does not support; <see cref="AltiumFileException.FilePath"/> is the file.</exception>
    public static ValueTask<ProjectHierarchy> LoadAsync(
        AltiumProject project, ProjectHierarchyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        return LoadCoreAsync(project, pathsAreRelative: true, sameNamedListed: false, (path, usedBy, _) =>
        {
            var file = FindOnDisk(project, path, usedBy);
            return ValueTask.FromResult(file is null ? null : new DocumentSource(file, async ct =>
            {
                try
                {
                    return (SchDocument)await AltiumLibrary.OpenSchDocAsync(file, ct).ConfigureAwait(false);
                }
                catch (AltiumFileException ex) when (ex.FilePath is null)
                {
                    throw WithPath(ex, file);
                }
            }));
        }, options ?? ProjectHierarchyOptions.Default, cancellationToken);
    }

    // Where a document comes from: its location (null to know it by its file name) and how to read
    // it. A source that is not read is disposed.
    private sealed class DocumentSource(string? location, Func<CancellationToken, ValueTask<SchDocument>> read, IDisposable? owned = null)
        : IDisposable
    {
        public string? Location { get; } = location;
        public ValueTask<SchDocument> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
        public void Dispose() => owned?.Dispose();
    }

    // A document the hierarchy holds. Its key is the location it was read from, or its file name when
    // it has none; its path is the path it was first asked for by.
    private sealed class HeldDocument(SchDocument document, string key, string name, string path, string? location, bool listed)
    {
        public SchDocument Document { get; } = document;
        public string Key { get; } = key;
        public string Name { get; } = name;
        public string Path { get; } = path;
        public string? Location { get; } = location;
        public bool Listed { get; } = listed;

        // What the sheets its symbols use are asked for with.
        public string SymbolsUsedBy => Location ?? Path;
    }

    // Which document each sheet symbol places. A symbol places the project's document of its file
    // name (the one its path leads to when the project lists several of that name); else the
    // document its path led to from its sheet; else the document known by that file name.
    private sealed class SheetFinder(bool pathsAreRelative)
    {
        // The project's documents that were read, by file name, in project order.
        public Dictionary<string, List<HeldDocument>> Listed { get; } = new(StringComparer.OrdinalIgnoreCase);

        // The document each path gave, asked for from a sheet (or by the project, with no sheet).
        public Dictionary<(string Path, string? UsedBy), HeldDocument> Found { get; } = new(PathAndLocationComparer.Instance);

        // The documents with no location, known by their file names.
        public Dictionary<string, HeldDocument> NameKeyed { get; } = new(StringComparer.OrdinalIgnoreCase);

        public (string Path, string? UsedBy) Request(string path, HeldDocument? on) =>
            (NormalizePath(path)!, pathsAreRelative ? on?.SymbolsUsedBy : null);

        public HeldDocument? Find(HeldDocument on, SchSheetSymbol symbol, string name)
        {
            var found = Found.GetValueOrDefault(Request(symbol.FileName!, on));
            if (Listed.TryGetValue(name, out var listed))
                return found is not null && listed.Contains(found) ? found : listed[0];
            return found ?? NameKeyed.GetValueOrDefault(name);
        }

        public void AddListed(string name, HeldDocument document)
        {
            if (!Listed.TryGetValue(name, out var list))
                Listed[name] = list = new List<HeldDocument>();
            if (!list.Contains(document))
                list.Add(document);
        }
    }

    // Opens the project's schematic documents, then the sheets their sheet symbols use, until no new
    // path appears. The callback gets a path as written and the location of the sheet that uses it
    // (null for project documents), and returns where the document is (its location, passed on to the
    // sheets it uses; null to know it by its file name), or null when it is unavailable. A document is
    // read once for each location. A symbol whose file name is that of a project document that was
    // read is not looked up, unless the project lists several of that name. A path that gave nothing
    // is not asked for again from the same sheet; when pathsAreRelative is false the callback ignores
    // the location, so it is not asked for again at all. sameNamedListed reports project documents
    // that share a file name up front (for loaders that read one document of each name).
    private static async ValueTask<ProjectHierarchy> LoadCoreAsync(AltiumProject project, bool pathsAreRelative, bool sameNamedListed,
        Func<string, string?, CancellationToken, ValueTask<DocumentSource?>> open,
        ProjectHierarchyOptions options, CancellationToken cancellationToken)
    {
        var diagnostics = new List<AltiumDiagnostic>();
        var missing = new List<string>();
        var held = new List<HeldDocument>();
        var byKey = new Dictionary<string, HeldDocument>(StringComparer.OrdinalIgnoreCase);
        var finder = new SheetFinder(pathsAreRelative);
        var asked = new HashSet<(string Path, string? UsedBy)>(PathAndLocationComparer.Instance);
        if (sameNamedListed)
            ReportSameNamedDocuments(project, diagnostics, "only one is read");
        var queue = new Queue<(string Path, HeldDocument? On, bool Listed)>();
        foreach (var document in project.SchematicDocuments)
        {
            if (!string.IsNullOrWhiteSpace(document.DocumentPath))
                queue.Enqueue((document.DocumentPath, null, true));
        }

        while (queue.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = NormalizeFileName(item.Path);
            if (name is null)
                continue;
            var request = finder.Request(item.Path, item.On);
            if (!item.Listed && finder.Listed.TryGetValue(name, out var listed))
            {
                // The project's document of that name is placed: only where the path leads matters,
                // when the project lists several of that name.
                if (listed.Count > 1 && asked.Add(request)
                    && await open(item.Path, item.On?.SymbolsUsedBy, cancellationToken).ConfigureAwait(false) is { } other)
                {
                    using (other)
                    {
                        if (other.Location is { } at && byKey.TryGetValue(at, out var document) && listed.Contains(document))
                            finder.Found[request] = document;
                    }
                }
                continue;
            }
            if (finder.NameKeyed.ContainsKey(name) || !asked.Add(request))
                continue;

            var source = await open(item.Path, item.On?.SymbolsUsedBy, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                // A sheet only a symbol uses is reported by the walk, which knows which sheet uses it.
                if (item.Listed)
                {
                    missing.Add(item.Path);
                    diagnostics.Add(NotSupplied(item.Path));
                }
                continue;
            }

            var key = source.Location ?? name;
            if (byKey.TryGetValue(key, out var known))
            {
                source.Dispose();
                finder.Found[request] = known;
                if (item.Listed)
                    finder.AddListed(name, known);
                continue;
            }
            SchDocument sheet;
            try
            {
                sheet = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                source.Dispose();
            }
            if (!item.Listed)
                diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Info,
                    $"Sheet '{item.Path}' is used by a sheet symbol but is not a project document; it was loaded too.")
                {
                    Code = AltiumDiagnosticCode.UnlistedSheetLoaded,
                    DocumentName = item.Path,
                });

            sheet.FileName = name;
            var loaded = new HeldDocument(sheet, key, name, item.Path, source.Location, item.Listed);
            held.Add(loaded);
            byKey[key] = loaded;
            finder.Found[request] = loaded;
            if (source.Location is null)
                finder.NameKeyed[name] = loaded;
            if (item.Listed)
                finder.AddListed(name, loaded);
            // In the order the walk visits them, so Documents lists unlisted sheets in walk order too.
            foreach (var symbol in OrderSiblings(sheet.SheetSymbols))
            {
                if (!string.IsNullOrWhiteSpace(symbol.FileName))
                    queue.Enqueue((symbol.FileName, loaded, false));
            }
        }

        ReportSharedFileNames(held, diagnostics);
        return Build(project, held, finder, diagnostics, missing, options, cancellationToken);
    }

    // Project documents in different folders with one file name share a key, and the hierarchy
    // holds one document per key.
    private static void ReportSameNamedDocuments(AltiumProject project, List<AltiumDiagnostic> diagnostics, string outcome)
    {
        var first = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in project.SchematicDocuments.Select(d => d.DocumentPath))
        {
            if (NormalizeFileName(path) is not { } name || first.TryAdd(name, path!))
                continue;
            diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Warning,
                $"Project documents '{first[name]}' and '{path}' have the same file name; {outcome}.")
            {
                Code = AltiumDiagnosticCode.DuplicateDocumentName,
                DocumentName = path,
            });
        }
    }

    // Documents read from different locations that share a file name are each a sheet of their own.
    private static void ReportSharedFileNames(List<HeldDocument> held, List<AltiumDiagnostic> diagnostics)
    {
        var first = new Dictionary<string, HeldDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in held)
        {
            if (first.TryAdd(document.Name, document))
                continue;
            diagnostics.Add(new AltiumDiagnostic(DiagnosticSeverity.Info,
                $"Sheets '{first[document.Name].Path}' and '{document.Path}' have the same file name; each was read as a sheet of its own.")
            {
                Code = AltiumDiagnosticCode.SharedFileName,
                DocumentName = document.Path,
            });
        }
    }

    // Paths compared as Windows opens them (already normalized), locations as written; both ignore case.
    private sealed class PathAndLocationComparer : IEqualityComparer<(string Path, string? UsedBy)>
    {
        public static PathAndLocationComparer Instance { get; } = new();

        public bool Equals((string Path, string? UsedBy) x, (string Path, string? UsedBy) y) =>
            string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.UsedBy, y.UsedBy, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, string? UsedBy) obj) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path),
            obj.UsedBy is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.UsedBy));
    }

    private static AltiumDiagnostic NotSupplied(string path) =>
        new(DiagnosticSeverity.Warning, $"Schematic document '{path}' was not supplied.")
        {
            Code = AltiumDiagnosticCode.DocumentNotSupplied,
            DocumentName = path,
        };

    // The first existing file for a document path: as given when absolute, else next to the sheet
    // that uses it, else in the project folder.
    private static string? FindOnDisk(AltiumProject project, string path, string? usedBy)
    {
        var native = Path.DirectorySeparatorChar == '\\' ? path : path.Replace('\\', '/');
        var candidates = new List<string?>();
        if (Path.IsPathRooted(native))
            candidates.Add(native);
        else
        {
            if (Path.GetDirectoryName(usedBy) is { Length: > 0 } usedByDirectory)
                candidates.Add(Path.Combine(usedByDirectory, native));
            candidates.Add(project.ResolveDocumentPath(native));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static ProjectHierarchy Build(AltiumProject? project, List<HeldDocument> held, SheetFinder finder,
        List<AltiumDiagnostic> diagnostics, List<string> missing, ProjectHierarchyOptions options, CancellationToken cancellationToken)
    {
        var hierarchy = new ProjectHierarchy(project, held, finder, diagnostics, missing, options);
        hierarchy.Compile(cancellationToken);
        return hierarchy;
    }

    private void Compile(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (RoomNamingStyle != "0")
            Report(DiagnosticSeverity.Warning, AltiumDiagnosticCode.UnsupportedRoomNamingStyle,
                $"Channel room naming style {RoomNamingStyle} is not modelled; rooms are named as for style 0 (flat numbered).");
        var unknownTokens = DesignatorTokens(DesignatorFormat).Where(t => !IsKnownToken(t)).ToList();
        _formatHasUnknownTokens = unknownTokens.Count > 0;
        if (_formatHasUnknownTokens)
        {
            var tokens = unknownTokens.Select(t => "$" + t).ToArray();
            Report(DiagnosticSeverity.Warning, AltiumDiagnosticCode.UnsupportedDesignatorToken,
                $"The channel designator format '{DesignatorFormat}' uses {string.Join(", ", tokens)}, " +
                $"which are not designator keywords; channel components use {DefaultDesignatorFormat} instead.", tokens: tokens);
        }

        foreach (var root in DetermineRoots())
        {
            if (_stopped)
                break;
            Walk(root);
        }

        var instantiated = new HashSet<string>(_sheets.Select(s => s.DocumentKey), StringComparer.OrdinalIgnoreCase);
        foreach (var document in _held.Where(d => !instantiated.Contains(d.Key)))
            Report(DiagnosticSeverity.Info, AltiumDiagnosticCode.SheetNotInHierarchy,
                $"Sheet '{Describe(document)}' is not part of the hierarchy and was left out.", document: document.Name);

        AssignRooms();
        CollectParts();
        MergeComponents();
    }

    // A document's file name, or the path it was read for when another document has that file name.
    private string Describe(HeldDocument document) =>
        ReferenceEquals(_documentsByName[document.Name], document.Document) && _held.Count(d => string.Equals(d.Name, document.Name, StringComparison.OrdinalIgnoreCase)) == 1
            ? document.Name
            : document.Path;

    // The document a sheet symbol on a held document places, or null when it names no file or
    // none was supplied for it.
    private HeldDocument? Child(HeldDocument on, SchSheetSymbol symbol) =>
        NormalizeFileName(symbol.FileName) is { } name ? _finder.Find(on, symbol, name) : null;

    private List<HeldDocument> DetermineRoots()
    {
        var referenced = new HashSet<HeldDocument>(ReferenceEqualityComparer.Instance);
        foreach (var document in _held)
        {
            foreach (var symbol in document.Document.SheetSymbols)
            {
                if (Child(document, symbol) is { } child && !ReferenceEquals(child, document))
                    referenced.Add(child);
            }
        }

        // The compiled top-level document, unless the structure file is stale and another sheet now uses it.
        var top = NormalizeFileName(Project?.Structure?.TopLevelDocument);
        var topDocument = top is null ? null
            : _held.FirstOrDefault(d => d.Listed && string.Equals(d.Name, top, StringComparison.OrdinalIgnoreCase))
              ?? _held.FirstOrDefault(d => string.Equals(d.Name, top, StringComparison.OrdinalIgnoreCase));
        if (topDocument is not null && !referenced.Contains(topDocument))
            return new List<HeldDocument> { topDocument };
        if (top is not null)
            Report(DiagnosticSeverity.Info, AltiumDiagnosticCode.StructureFileStale,
                $"The structure file's top-level document '{top}' is {(topDocument is not null ? "used by a sheet symbol" : "not supplied")}; the roots are the sheets no sheet symbol uses.",
                document: top);
        else if (Project is not null)
            Report(DiagnosticSeverity.Info, AltiumDiagnosticCode.StructureFileMissing,
                "The project has no structure file naming its top-level document; the roots are the sheets no sheet symbol uses.");

        var roots = _held.Where(d => !referenced.Contains(d)).ToList();
        if (roots.Count == 0 && _held.Count > 0)
        {
            Report(DiagnosticSeverity.Warning, AltiumDiagnosticCode.NoRootSheet,
                $"Every sheet is used by a sheet symbol (a cycle); the hierarchy starts from '{Describe(_held[0])}'.",
                document: _held[0].Name);
            roots.Add(_held[0]);
        }
        return roots;
    }

    // A sheet instance whose sheet symbols are being expanded. The walk keeps these on an explicit
    // stack rather than recursing, so a crafted chain of thousands of sheets cannot overflow the
    // call stack (which would end the process).
    private sealed class WalkFrame(HierarchySheetInstance instance, HeldDocument document, List<SchSheetSymbol> symbols)
    {
        public HierarchySheetInstance Instance { get; } = instance;
        public HeldDocument Document { get; } = document;
        public List<SchSheetSymbol> Symbols { get; } = symbols;
        public int NextSymbol { get; set; }
        public SchSheetSymbol? Symbol { get; set; }
        public HeldDocument? Child { get; set; }
        public string SymbolUid { get; set; } = string.Empty;
        public RepeatInfo Repeat { get; set; }
        public int NextIndex { get; set; }
    }

    // Depth-first, each instance before its children and siblings in OrderSiblings order.
    private void Walk(HeldDocument rootDocument)
    {
        var root = Enter(rootDocument, parent: null, symbol: null, repeatIndex: null, Array.Empty<string>());
        if (root is null)
            return;
        _roots.Add(root);

        // The documents of the instances from the root to the current one.
        var ancestors = new HashSet<HeldDocument>(ReferenceEqualityComparer.Instance) { rootDocument };
        var stack = new Stack<WalkFrame>();
        stack.Push(new WalkFrame(root, rootDocument, OrderSiblings(root.Document.SheetSymbols).ToList()));
        while (stack.Count > 0 && !_stopped)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var frame = stack.Peek();
            if (frame.Symbol is null && !StartNextSymbol(frame, ancestors))
            {
                stack.Pop();
                if (frame.Instance.Parent is not null)
                    ancestors.Remove(frame.Document);
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
            var childDocument = frame.Child!;
            var child = Enter(childDocument, frame.Instance, frame.Symbol, repeat.IsRepeated ? k : null,
                Append(frame.Instance.SymbolUniqueIds, segment));
            if (child is null)
                break;
            ancestors.Add(childDocument);
            stack.Push(new WalkFrame(child, childDocument, OrderSiblings(child.Document.SheetSymbols).ToList()));
        }
    }

    // Moves the frame to its next sheet symbol that can be followed, reporting those that cannot.
    private bool StartNextSymbol(WalkFrame frame, HashSet<HeldDocument> ancestors)
    {
        var fileName = frame.Instance.FileName;
        while (frame.NextSymbol < frame.Symbols.Count)
        {
            var symbol = frame.Symbols[frame.NextSymbol++];
            var childName = NormalizeFileName(symbol.FileName);
            if (childName is null)
            {
                ReportSymbol(DiagnosticSeverity.Warning, AltiumDiagnosticCode.SheetSymbolWithoutFile,
                    $"Sheet symbol '{SymbolName(symbol)}' on '{fileName}' names no sheet file.", frame.Instance, symbol, fileName);
                continue;
            }
            if (_finder.Find(frame.Document, symbol, childName) is not { } child)
            {
                AddMissing(symbol.FileName!);
                ReportSymbol(DiagnosticSeverity.Warning, AltiumDiagnosticCode.SubSheetNotSupplied,
                    $"Sheet symbol '{SymbolName(symbol)}' on '{fileName}' uses sub-sheet '{symbol.FileName}', which was not supplied.",
                    frame.Instance, symbol, symbol.FileName);
                continue;
            }
            if (ancestors.Contains(child))
            {
                ReportSymbol(DiagnosticSeverity.Warning, AltiumDiagnosticCode.SheetCycle,
                    $"Sheet symbol '{SymbolName(symbol)}' on '{fileName}' uses '{childName}', which is one of its own parents; the cycle was skipped.",
                    frame.Instance, symbol, childName);
                continue;
            }
            if (frame.Instance.Depth >= _options.MaxDepth)
            {
                IsTruncated = true;
                Report(DiagnosticSeverity.Error, AltiumDiagnosticCode.MaxDepthExceeded,
                    $"The hierarchy is deeper than {_options.MaxDepth} levels (ProjectHierarchyOptions.MaxDepth); sheets below that depth were left out.",
                    frame.Instance, symbol, childName);
                continue;
            }

            var repeat = symbol.Repeat;
            if (repeat.IsUnreadable)
                ReportSymbol(DiagnosticSeverity.Warning, AltiumDiagnosticCode.UnreadableRepeat,
                    $"Sheet symbol '{SymbolName(symbol)}' on '{fileName}' has a Repeat(...) that cannot be read " +
                    "(it needs a name and two whole numbers at most 100,000 apart); it is treated as one instance.",
                    frame.Instance, symbol, childName);

            frame.Symbol = symbol;
            frame.Child = child;
            frame.SymbolUid = string.IsNullOrEmpty(symbol.UniqueId) ? (symbol.SheetName ?? childName) : symbol.UniqueId;
            frame.Repeat = repeat;
            frame.NextIndex = repeat.FirstInstance;
            return true;
        }
        return false;
    }

    // Creates a sheet instance, or stops the walk (returning null) once a limit is reached.
    private HierarchySheetInstance? Enter(HeldDocument held, HierarchySheetInstance? parent, SchSheetSymbol? symbol,
        int? repeatIndex, IReadOnlyList<string> symbolUniqueIds)
    {
        if (_sheets.Count >= _options.MaxSheetInstances)
        {
            Stop(AltiumDiagnosticCode.MaxSheetInstancesExceeded,
                $"The hierarchy has more than {_options.MaxSheetInstances} sheet instances (ProjectHierarchyOptions.MaxSheetInstances); " +
                "the walk stopped there.", parent, symbol, held.Name);
            return null;
        }
        var parts = DesignatedComponentCount(held.Document);
        if (_plannedParts + parts > _options.MaxComponentParts)
        {
            Stop(AltiumDiagnosticCode.MaxComponentPartsExceeded,
                $"The design places more than {_options.MaxComponentParts} component parts (ProjectHierarchyOptions.MaxComponentParts); " +
                "the walk stopped there.", parent, symbol, held.Name);
            return null;
        }
        _plannedParts += parts;

        var instance = new HierarchySheetInstance(_sheets.Count, held.Document, held.Name, held.Key, parent, symbol, repeatIndex, symbolUniqueIds);
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

    private void AddMissing(string path)
    {
        if (NormalizeFileName(path) is { } name && _missingNames.Add(name))
            _missing.Add(path);
    }

    private void Stop(AltiumDiagnosticCode code, string message, HierarchySheetInstance? sheet, SchSheetSymbol? symbol, string document)
    {
        _stopped = true;
        IsTruncated = true;
        Report(DiagnosticSeverity.Error, code, message, sheet, symbol, document);
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
            sheet.Ordinal = instancesPerFile[sheet.DocumentKey] = instancesPerFile.GetValueOrDefault(sheet.DocumentKey) + 1;
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
            sheet.IsMultiChannel = instancesPerFile[sheet.DocumentKey] > 1;
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
            var instances = shared.ToArray();
            Report(DiagnosticSeverity.Warning, AltiumDiagnosticCode.SharedRoomName,
                $"Room name '{shared.Key}' is shared by {instances.Length} sheet instances, so their physical designators can collide.",
                instances[0], instances: instances);
        }
    }

    private void CollectParts()
    {
        foreach (var sheet in _sheets)
        {
            _cancellationToken.ThrowIfCancellationRequested();
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
                    ? ChannelDesignator(sheet, logical, room)
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
                Report(DiagnosticSeverity.Info, AltiumDiagnosticCode.ComponentWithoutDesignator,
                    $"{skipped} component(s) on '{sheet.FileName}' have no designator and were left out.", sheet, document: sheet.FileName);
        }
    }

    private void MergeComponents()
    {
        // Each designator's components. A part joins the first of them that lacks its part number and
        // comes from the same library symbol (the parts of one multi-part component, wherever they
        // sit); otherwise it starts another component with that designator.
        var byDesignator = new Dictionary<string, DesignatorGroup>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<PartGroup>();
        var unannotated = 0;
        foreach (var part in _parts)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            // An unannotated designator ("R?") names no particular component: keep each record apart.
            if (part.LogicalDesignator.Contains('?', StringComparison.Ordinal))
            {
                unannotated++;
                ordered.Add(new PartGroup(part));
                continue;
            }
            if (!byDesignator.TryGetValue(part.PhysicalDesignator, out var designatorGroup))
                byDesignator[part.PhysicalDesignator] = designatorGroup = new DesignatorGroup();
            if (designatorGroup.FindOwner(part) is { } owner)
                owner.Add(part);
            else
                ordered.Add(designatorGroup.Start(part));
        }

        foreach (var group in ordered)
        {
            var component = new PhysicalComponent(this, group.Parts.OrderBy(p => p.PartId).ToList());
            _components.Add(component);
            if (component.LogicalDesignator.Contains('?', StringComparison.Ordinal))
                continue;
            if (!_byDesignator.TryGetValue(component.Designator, out var same))
                _byDesignator[component.Designator] = same = new List<PhysicalComponent>();
            same.Add(component);
        }

        foreach (var components in byDesignator.Values.Select(g => g.Components).Where(c => c.Count > 1))
        {
            var designator = components[0].Parts[0].PhysicalDesignator;
            _duplicateDesignators.Add(designator);
            Report(DiagnosticSeverity.Warning, AltiumDiagnosticCode.DuplicateDesignator,
                $"Designator '{designator}' is used by {components.Count} separate components; each is kept as its own component.",
                components[1].Parts[0].Sheet, designator: designator);
        }

        if (unannotated > 0)
            Report(DiagnosticSeverity.Warning, AltiumDiagnosticCode.UnannotatedDesignator,
                $"{unannotated} component(s) are not annotated (their designator contains '?'); each is kept as a separate component.");
    }

    // A channel component's physical designator: DesignatorFormat, or the default format when a token
    // cannot be filled for it. $ChannelIndex is the number in the room name (the Repeat index, or the
    // number given to symbols that share a designator), else the instance's position among its
    // sheet's instances.
    private string ChannelDesignator(HierarchySheetInstance sheet, string logical, string room)
    {
        var channelIndex = sheet.ChannelIndex ?? sheet.Ordinal;
        if (Expand(DesignatorFormat, logical, room, sheet.ChannelName, channelIndex, out var unfilled) is { } designator
            && unfilled is null)
            return designator;
        if (!_formatHasUnknownTokens && _unfilledTokens.Add(unfilled!))
            Report(DiagnosticSeverity.Warning, AltiumDiagnosticCode.UnfilledDesignatorToken,
                $"The channel designator format '{DesignatorFormat}' has ${unfilled}, which '{logical}' in room '{room}' has no value for; " +
                $"such components use {DefaultDesignatorFormat} instead.", sheet, designator: logical, tokens: new[] { "$" + unfilled });
        return FormatDesignator(DefaultDesignatorFormat, logical, room);
    }

    // The parts gathered for one component, with the part numbers it has.
    private sealed class PartGroup(PhysicalComponentPart first)
    {
        public List<PhysicalComponentPart> Parts { get; } = new() { first };
        public HashSet<int> PartIds { get; } = new() { first.PartId };
        public string Library { get; } = first.Component.LibReference ?? string.Empty;

        public void Add(PhysicalComponentPart part)
        {
            Parts.Add(part);
            PartIds.Add(part.PartId);
        }

        // Whether a part can join: its part number is not taken yet, and it comes from the same
        // library symbol (when both records name one).
        public bool CanTake(int partId, string library) =>
            !PartIds.Contains(partId) && (Library.Length == 0 || library.Length == 0
                || string.Equals(Library, library, StringComparison.OrdinalIgnoreCase));
    }

    // The components that share one physical designator, in the order they were started.
    private sealed class DesignatorGroup
    {
        // For each part number and library symbol, the index of the first component that may still
        // take such a part. The components before it have that part number or another symbol, and a
        // component never loses a part, so a search resumes there instead of scanning them all again.
        private Dictionary<int, Dictionary<string, int>>? _nextCandidate;

        public List<PartGroup> Components { get; } = new();

        public PartGroup? FindOwner(PhysicalComponentPart part)
        {
            if (Components.Count == 0)
                return null;
            _nextCandidate ??= new Dictionary<int, Dictionary<string, int>>();
            if (!_nextCandidate.TryGetValue(part.PartId, out var byLibrary))
                _nextCandidate[part.PartId] = byLibrary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var library = part.Component.LibReference ?? string.Empty;
            var i = byLibrary.GetValueOrDefault(library);
            while (i < Components.Count && !Components[i].CanTake(part.PartId, library))
                i++;
            byLibrary[library] = i;
            return i < Components.Count ? Components[i] : null;
        }

        public PartGroup Start(PhysicalComponentPart part)
        {
            var component = new PartGroup(part);
            Components.Add(component);
            return component;
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// Expands a channel designator format without channel details: as
    /// <see cref="FormatDesignator(string, string, string, string?, int?)"/> with no channel prefix or
    /// index, so <c>$ChannelPrefix</c>, <c>$ChannelIndex</c> and <c>$ChannelAlpha</c> are kept as written.
    /// </summary>
    public static string FormatDesignator(string format, string component, string roomName) =>
        FormatDesignator(format, component, roomName, channelPrefix: null, channelIndex: null);

    /// <summary>
    /// Expands a channel designator format with Altium's keywords (case-insensitive):
    /// <c>$Component</c> is <paramref name="component"/> (the logical designator, e.g. <c>U12</c>),
    /// <c>$ComponentPrefix</c> and <c>$ComponentIndex</c> are its letters and its trailing number
    /// (<c>U</c> and <c>12</c>), <c>$RoomName</c> is <paramref name="roomName"/>, <c>$ChannelPrefix</c>
    /// is <paramref name="channelPrefix"/> (the sheet symbol's designator or <c>Repeat</c> name),
    /// <c>$ChannelIndex</c> is <paramref name="channelIndex"/> and <c>$ChannelAlpha</c> is that index as
    /// letters (1 = A, 26 = Z, 27 = AA). Other text, unknown tokens and tokens with no value (such as
    /// <c>$ComponentIndex</c> for <c>J_PWR</c>, or <c>$ChannelAlpha</c> for index 0) are kept as written.
    /// </summary>
    public static string FormatDesignator(string format, string component, string roomName, string? channelPrefix, int? channelIndex)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(roomName);
        return Expand(format, component, roomName, channelPrefix, channelIndex, out _);
    }

    // Expands the format, keeping a token with no value as written and returning the first such
    // token's name in unfilled.
    private static string Expand(string format, string component, string? roomName, string? channelPrefix,
        int? channelIndex, out string? unfilled)
    {
        unfilled = null;
        var sb = new StringBuilder(format.Length + component.Length + (roomName?.Length ?? 0));
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '$')
            {
                sb.Append(format[i]);
                continue;
            }
            var end = TokenEnd(format, i + 1);
            var token = format.AsSpan(i + 1, end - i - 1);
            if (token.IsEmpty)
                sb.Append('$');
            else if (TokenValue(token, component, roomName, channelPrefix, channelIndex) is { } value)
                sb.Append(value);
            else
            {
                unfilled ??= token.ToString();
                sb.Append(format, i, end - i);
            }
            i = end - 1;
        }
        return sb.ToString();
    }

    private static readonly string[] KnownTokens =
        { "Component", "ComponentPrefix", "ComponentIndex", "RoomName", "ChannelPrefix", "ChannelIndex", "ChannelAlpha" };

    private static string? TokenValue(ReadOnlySpan<char> token, string component, string? roomName, string? channelPrefix, int? channelIndex)
    {
        var digits = component.Length;
        while (digits > 0 && char.IsAsciiDigit(component[digits - 1]))
            digits--;
        if (token.Equals("Component", StringComparison.OrdinalIgnoreCase))
            return component;
        if (token.Equals("ComponentPrefix", StringComparison.OrdinalIgnoreCase))
            return component[..digits];
        if (token.Equals("ComponentIndex", StringComparison.OrdinalIgnoreCase))
            return digits < component.Length ? component[digits..] : null;
        if (token.Equals("RoomName", StringComparison.OrdinalIgnoreCase))
            return roomName;
        if (token.Equals("ChannelPrefix", StringComparison.OrdinalIgnoreCase))
            return channelPrefix;
        if (token.Equals("ChannelIndex", StringComparison.OrdinalIgnoreCase))
            return channelIndex?.ToString(CultureInfo.InvariantCulture);
        if (token.Equals("ChannelAlpha", StringComparison.OrdinalIgnoreCase))
            return channelIndex is { } index and > 0 ? Alpha(index) : null;
        return null;
    }

    // 1 = A ... 26 = Z, 27 = AA, as spreadsheet columns.
    private static string Alpha(int index)
    {
        var letters = new StringBuilder();
        for (var n = index; n > 0; n = (n - 1) / 26)
            letters.Insert(0, (char)('A' + (n - 1) % 26));
        return letters.ToString();
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
        Array.Exists(KnownTokens, t => t.Equals(token, StringComparison.OrdinalIgnoreCase));

    // Reports each message once; a problem met again keeps the location where it was first found.
    private void Report(DiagnosticSeverity severity, AltiumDiagnosticCode code, string message,
        HierarchySheetInstance? sheet = null, SchSheetSymbol? symbol = null, string? document = null, string? designator = null,
        IReadOnlyList<string>? tokens = null, IReadOnlyList<HierarchySheetInstance>? instances = null)
    {
        if (_reported.Add(message))
            Add(severity, code, message, sheet, symbol, document, designator, tokens, instances);
    }

    // Reports a problem with one sheet symbol once, however many instances of its sheet the walk
    // visits, so symbols with the same problem on one sheet are each reported.
    private void ReportSymbol(DiagnosticSeverity severity, AltiumDiagnosticCode code, string message,
        HierarchySheetInstance sheet, SchSheetSymbol symbol, string? document)
    {
        if (_reportedSymbols.Add((code, symbol)))
            Add(severity, code, message, sheet, symbol, document, designator: null, tokens: null, instances: null);
    }

    private void Add(DiagnosticSeverity severity, AltiumDiagnosticCode code, string message,
        HierarchySheetInstance? sheet, SchSheetSymbol? symbol, string? document, string? designator, IReadOnlyList<string>? tokens,
        IReadOnlyList<HierarchySheetInstance>? instances) =>
        _diagnostics.Add(new AltiumDiagnostic(severity, message)
        {
            Code = code,
            SheetInstance = sheet,
            SheetInstances = instances,
            SheetSymbol = symbol,
            DocumentName = document,
            Designator = designator,
            DesignatorTokens = tokens,
        });

    private static string SymbolName(SchSheetSymbol symbol) =>
        (symbol.NameLabel?.Text ?? symbol.SheetName) is { } name && !string.IsNullOrWhiteSpace(name) ? name : "(unnamed)";

    private static IReadOnlyList<string> Append(IReadOnlyList<string> path, string entry)
    {
        var list = new List<string>(path.Count + 1);
        list.AddRange(path);
        list.Add(entry);
        return list;
    }

    /// <summary>
    /// Returns the file name the hierarchy knows a document by: the last segment of the path as
    /// Windows resolves it (see <see cref="NormalizePath"/>), after a drive's <c>:</c> too, without
    /// leading or trailing white space or trailing periods; <c>null</c> when nothing is left. So
    /// <c>"Main.SchDoc\."</c>, <c>"Sub\Main.SchDoc\X\.."</c>, <c>"Main.SchDoc."</c> and
    /// <c>"Main.SchDoc "</c> are all <c>"Main.SchDoc"</c>, as Windows and Altium open them, and
    /// <c>"Sheets\C:Power.SchDoc"</c> is <c>"Power.SchDoc"</c>; <c>"."</c>, <c>"X\.."</c> and
    /// <c>"Sheets\"</c> name no file. Unlike Windows it also drops leading spaces
    /// (<c>"Sheets\ Power.SchDoc"</c> is <c>"Power.SchDoc"</c>). A file name is its own file name, and
    /// file names are compared case-insensitively. They are the keys of <see cref="Documents"/> and the
    /// <see cref="HierarchySheetInstance.FileName"/> of <see cref="Sheets"/>; apply this to a path in
    /// <see cref="MissingDocuments"/> or a sheet symbol's file name to match them.
    /// </summary>
    public static string? NormalizeFileName(string? fileName) =>
        fileName is null ? null : DocumentPaths.FileName(fileName);

    /// <summary>
    /// Returns a document path as Windows (and so Altium) resolves it, the same on every platform,
    /// without making it absolute, as <see cref="Path.GetFullPath(string)"/> does on Windows:
    /// <c>\</c> and <c>/</c> both separate segments and runs of them count as one, <c>.</c> segments are
    /// dropped, <c>..</c> removes the segment before it (a relative path keeps the <c>..</c> it starts
    /// with; a rooted one never climbs above its root), a segment that ends in one period loses it
    /// (<c>"Sheets.\Power.SchDoc"</c> is <c>"Sheets\Power.SchDoc"</c>, while <c>"Sheets..\"</c> is kept),
    /// and the last segment loses its trailing periods and spaces. The result uses <c>\</c>, keeps a
    /// trailing separator and the case as written, and is empty when nothing is left (<c>"."</c>).
    /// Use it to look up a path the way the hierarchy's loaders do.
    /// </summary>
    public static string? NormalizePath(string? path) =>
        path is null ? null : DocumentPaths.Normalize(path);
}

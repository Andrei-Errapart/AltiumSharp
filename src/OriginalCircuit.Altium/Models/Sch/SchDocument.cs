using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Writers;

namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Implementation of a schematic document (.SchDoc file).
/// Contains a flat list of primitives where components own their children
/// via OWNERINDEX relationships.
/// </summary>
public sealed class SchDocument : ISchDocument
{
    /// <summary>
    /// Diagnostics collected during file reading (warnings about skipped records, parse errors, etc.).
    /// </summary>
    public IReadOnlyList<AltiumDiagnostic> Diagnostics { get; internal set; } = Array.Empty<AltiumDiagnostic>();

    private readonly List<SchComponent> _components = new();
    private readonly List<SchWire> _wires = new();
    private readonly List<SchTemplate> _templates = new();
    private readonly List<SchNote> _notes = new();
    private readonly List<SchHyperlink> _hyperlinks = new();
    private readonly List<SchNetLabel> _netLabels = new();
    private readonly List<SchJunction> _junctions = new();
    private readonly List<SchPowerObject> _powerObjects = new();
    private readonly List<SchLabel> _labels = new();
    private readonly List<SchParameter> _parameters = new();
    private readonly List<SchLine> _lines = new();
    private readonly List<SchRectangle> _rectangles = new();
    private readonly List<SchPolygon> _polygons = new();
    private readonly List<SchPolyline> _polylines = new();
    private readonly List<SchArc> _arcs = new();
    private readonly List<SchBezier> _beziers = new();
    private readonly List<SchEllipse> _ellipses = new();
    private readonly List<SchRoundedRectangle> _roundedRectangles = new();
    private readonly List<SchPie> _pies = new();
    private readonly List<SchTextFrame> _textFrames = new();
    private readonly List<SchImage> _images = new();
    private readonly List<SchSymbol> _symbols = new();
    private readonly List<SchEllipticalArc> _ellipticalArcs = new();
    private readonly List<SchNoErc> _noErcs = new();
    private readonly List<SchBusEntry> _busEntries = new();
    private readonly List<SchBus> _buses = new();
    private readonly List<SchPort> _ports = new();
    private readonly List<SchSheetSymbol> _sheetSymbols = new();
    private readonly List<SchSheetEntry> _sheetEntries = new();
    private readonly List<SchBlanket> _blankets = new();
    private readonly List<SchParameterSet> _parameterSets = new();

    /// <summary>
    /// Document header parameters from the FileHeader record (RECORD=31 equivalent).
    /// Contains page size, font definitions, grid settings, and other document metadata.
    /// When null, defaults (HEADER + WEIGHT) are written for new files.
    /// </summary>
    public Dictionary<string, string>? HeaderParameters { get; set; }

    /// <summary>
    /// Sheet settings record (RECORD=31) containing font definitions, grid/border/title-block settings.
    /// Preserved as raw parameters for round-trip fidelity.
    /// </summary>
    public Dictionary<string, string>? SheetSettings { get; set; }

    /// <summary>
    /// Additional OLE storages/streams preserved for round-trip fidelity.
    /// Key format: "StreamName" for root streams, "StorageName/StreamName" for nested streams.
    /// </summary>
    public Dictionary<string, byte[]>? AdditionalStreams { get; set; }

    /// <summary>
    /// Opaque (unmodeled) records preserved for round-trip fidelity.
    /// Each entry is the raw parameter dictionary from an unrecognized record type.
    /// </summary>
    public List<Dictionary<string, string>> OpaqueRecords { get; } = new();

    /// <summary>
    /// The FileHeader document-header block parsed as an ordered key/value list, preserving order
    /// and duplicate keys for a byte-faithful round-trip. The typed model and <see cref="HeaderParameters"/>
    /// remain populated for from-scratch authoring.
    /// </summary>
    internal List<KeyValuePair<string, string>>? HeaderParametersOrdered { get; set; }

    /// <summary>
    /// Every FileHeader record (after the document header) captured as an ordered parameter list in
    /// original record order. When present, the writer re-emits these verbatim so unmodeled parameters
    /// and record ordering round-trip exactly; null (or after binary-pin records) falls back to the
    /// typed-model serialization that supports from-scratch authoring.
    /// </summary>
    internal List<List<KeyValuePair<string, string>>>? RawRecords { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<ISchComponent> Components => _components;

    /// <inheritdoc />
    public IReadOnlyList<ISchWire> Wires => _wires;

    /// <summary>Sheet-template references (record type 39) applied to this document.</summary>
    public IReadOnlyList<SchTemplate> Templates => _templates;

    /// <summary>Design notes (record type 209) on this document.</summary>
    public IReadOnlyList<SchNote> Notes => _notes;

    /// <summary>Hyperlinks (record type 226) on this document.</summary>
    public IReadOnlyList<SchHyperlink> Hyperlinks => _hyperlinks;

    /// <inheritdoc />
    public IReadOnlyList<ISchNetLabel> NetLabels => _netLabels;

    /// <inheritdoc />
    public IReadOnlyList<ISchJunction> Junctions => _junctions;

    /// <inheritdoc />
    public IReadOnlyList<ISchPowerObject> PowerObjects => _powerObjects;

    /// <inheritdoc />
    public IReadOnlyList<ISchLabel> Labels => _labels;

    /// <inheritdoc />
    public IReadOnlyList<ISchParameter> Parameters => _parameters;

    /// <summary>
    /// All lines in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchLine> Lines => _lines;

    /// <summary>
    /// All rectangles in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchRectangle> Rectangles => _rectangles;

    /// <summary>
    /// All polygons in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchPolygon> Polygons => _polygons;

    /// <summary>
    /// All polylines in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchPolyline> Polylines => _polylines;

    /// <summary>
    /// All arcs in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchArc> Arcs => _arcs;

    /// <summary>
    /// All beziers in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchBezier> Beziers => _beziers;

    /// <summary>
    /// All ellipses in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchEllipse> Ellipses => _ellipses;

    /// <summary>
    /// All rounded rectangles in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchRoundedRectangle> RoundedRectangles => _roundedRectangles;

    /// <summary>
    /// All pies in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchPie> Pies => _pies;

    /// <summary>
    /// All text frames in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchTextFrame> TextFrames => _textFrames;

    /// <summary>
    /// All images in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchImage> Images => _images;

    /// <summary>
    /// All symbols in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchSymbol> Symbols => _symbols;

    /// <summary>
    /// All elliptical arcs in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchEllipticalArc> EllipticalArcs => _ellipticalArcs;

    /// <summary>
    /// All No-ERC markers in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchNoErc> NoErcs => _noErcs;

    /// <inheritdoc />
    public IReadOnlyList<ISchNoConnect> NoConnects => _noErcs;

    /// <summary>
    /// All bus entries in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchBusEntry> BusEntries => _busEntries;

    /// <inheritdoc />
    IReadOnlyList<ISchBusEntry> ISchDocument.BusEntries => _busEntries;

    /// <summary>
    /// All buses in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchBus> Buses => _buses;

    /// <inheritdoc />
    IReadOnlyList<ISchBus> ISchDocument.Buses => _buses;

    /// <summary>
    /// All ports in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchPort> Ports => _ports;

    /// <summary>
    /// All sheet symbols in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchSheetSymbol> SheetSymbols => _sheetSymbols;

    /// <summary>
    /// All sheet entries in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchSheetEntry> SheetEntries => _sheetEntries;

    /// <summary>
    /// All blankets in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchBlanket> Blankets => _blankets;

    /// <summary>
    /// All parameter sets (directives) in this document (top-level).
    /// </summary>
    public IReadOnlyList<SchParameterSet> ParameterSets => _parameterSets;

    /// <inheritdoc />
    public CoordRect Bounds
    {
        get
        {
            var bounds = CoordRect.Empty;
            foreach (var comp in _components) bounds = bounds.Union(comp.Bounds);
            foreach (var wire in _wires) bounds = bounds.Union(wire.Bounds);
            foreach (var netLabel in _netLabels) bounds = bounds.Union(netLabel.Bounds);
            foreach (var junction in _junctions) bounds = bounds.Union(junction.Bounds);
            foreach (var power in _powerObjects) bounds = bounds.Union(power.Bounds);
            foreach (var label in _labels) bounds = bounds.Union(label.Bounds);
            foreach (var param in _parameters) bounds = bounds.Union(param.Bounds);
            foreach (var line in _lines) bounds = bounds.Union(line.Bounds);
            foreach (var rect in _rectangles) bounds = bounds.Union(rect.Bounds);
            return bounds;
        }
    }

    /// <summary>
    /// Adds a component to the document.
    /// </summary>
    public void AddComponent(SchComponent component) => _components.Add(component);

    void ISchDocument.AddComponent(ISchComponent component)
    {
        if (component is not SchComponent c) throw new ArgumentException($"Expected {nameof(SchComponent)}", nameof(component));
        _components.Add(c);
    }

    bool ISchDocument.RemoveComponent(ISchComponent component) => component is SchComponent c && _components.Remove(c);

    void ISchDocument.AddWire(ISchWire wire)
    {
        if (wire is not SchWire w) throw new ArgumentException($"Expected {nameof(SchWire)}", nameof(wire));
        _wires.Add(w);
    }

    bool ISchDocument.RemoveWire(ISchWire wire) => wire is SchWire w && _wires.Remove(w);

    void ISchDocument.AddNetLabel(ISchNetLabel netLabel)
    {
        if (netLabel is not SchNetLabel nl) throw new ArgumentException($"Expected {nameof(SchNetLabel)}", nameof(netLabel));
        _netLabels.Add(nl);
    }

    bool ISchDocument.RemoveNetLabel(ISchNetLabel netLabel) => netLabel is SchNetLabel nl && _netLabels.Remove(nl);

    void ISchDocument.AddJunction(ISchJunction junction)
    {
        if (junction is not SchJunction j) throw new ArgumentException($"Expected {nameof(SchJunction)}", nameof(junction));
        _junctions.Add(j);
    }

    bool ISchDocument.RemoveJunction(ISchJunction junction) => junction is SchJunction j && _junctions.Remove(j);

    void ISchDocument.AddPowerObject(ISchPowerObject powerObject)
    {
        if (powerObject is not SchPowerObject po) throw new ArgumentException($"Expected {nameof(SchPowerObject)}", nameof(powerObject));
        _powerObjects.Add(po);
    }

    bool ISchDocument.RemovePowerObject(ISchPowerObject powerObject) => powerObject is SchPowerObject po && _powerObjects.Remove(po);

    void ISchDocument.AddLabel(ISchLabel label)
    {
        if (label is not SchLabel l) throw new ArgumentException($"Expected {nameof(SchLabel)}", nameof(label));
        _labels.Add(l);
    }

    bool ISchDocument.RemoveLabel(ISchLabel label) => label is SchLabel l && _labels.Remove(l);

    void ISchDocument.AddNoConnect(ISchNoConnect noConnect)
    {
        if (noConnect is not SchNoErc ne) throw new ArgumentException($"Expected {nameof(SchNoErc)}", nameof(noConnect));
        _noErcs.Add(ne);
    }

    bool ISchDocument.RemoveNoConnect(ISchNoConnect noConnect) => noConnect is SchNoErc ne && _noErcs.Remove(ne);

    void ISchDocument.AddBus(ISchBus bus)
    {
        if (bus is not SchBus b) throw new ArgumentException($"Expected {nameof(SchBus)}", nameof(bus));
        _buses.Add(b);
    }

    bool ISchDocument.RemoveBus(ISchBus bus) => bus is SchBus b && _buses.Remove(b);

    void ISchDocument.AddBusEntry(ISchBusEntry busEntry)
    {
        if (busEntry is not SchBusEntry be) throw new ArgumentException($"Expected {nameof(SchBusEntry)}", nameof(busEntry));
        _busEntries.Add(be);
    }

    bool ISchDocument.RemoveBusEntry(ISchBusEntry busEntry) => busEntry is SchBusEntry be && _busEntries.Remove(be);

    /// <summary>
    /// Adds a top-level primitive to the document.
    /// </summary>
    public void AddPrimitive(object primitive)
    {
        switch (primitive)
        {
            case SchWire wire: _wires.Add(wire); break;
            case SchNetLabel netLabel: _netLabels.Add(netLabel); break;
            case SchJunction junction: _junctions.Add(junction); break;
            case SchPowerObject power: _powerObjects.Add(power); break;
            case SchLabel label: _labels.Add(label); break;
            case SchParameter param: _parameters.Add(param); break;
            case SchLine line: _lines.Add(line); break;
            case SchRectangle rect: _rectangles.Add(rect); break;
            case SchPolygon polygon: _polygons.Add(polygon); break;
            case SchPolyline polyline: _polylines.Add(polyline); break;
            case SchArc arc: _arcs.Add(arc); break;
            case SchBezier bezier: _beziers.Add(bezier); break;
            case SchEllipse ellipse: _ellipses.Add(ellipse); break;
            case SchRoundedRectangle roundedRect: _roundedRectangles.Add(roundedRect); break;
            case SchPie pie: _pies.Add(pie); break;
            case SchTextFrame textFrame: _textFrames.Add(textFrame); break;
            case SchImage image: _images.Add(image); break;
            case SchSymbol symbol: _symbols.Add(symbol); break;
            case SchEllipticalArc ellipticalArc: _ellipticalArcs.Add(ellipticalArc); break;
            case SchNoErc noErc: _noErcs.Add(noErc); break;
            case SchBusEntry busEntry: _busEntries.Add(busEntry); break;
            case SchBus bus: _buses.Add(bus); break;
            case SchPort port: _ports.Add(port); break;
            case SchSheetSymbol sheetSymbol: _sheetSymbols.Add(sheetSymbol); break;
            case SchSheetEntry sheetEntry: _sheetEntries.Add(sheetEntry); break;
            case SchBlanket blanket: _blankets.Add(blanket); break;
            case SchParameterSet parameterSet: _parameterSets.Add(parameterSet); break;
            case SchComponent comp: _components.Add(comp); break;
            case SchTemplate template: _templates.Add(template); break;
            case SchNote note: _notes.Add(note); break;
            case SchHyperlink hyperlink: _hyperlinks.Add(hyperlink); break;
        }
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(string path, OriginalCircuit.Eda.Models.SaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        await new SchDocWriter().WriteAsync(this, path, overwrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(Stream stream, OriginalCircuit.Eda.Models.SaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        await new SchDocWriter().WriteAsync(this, stream, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

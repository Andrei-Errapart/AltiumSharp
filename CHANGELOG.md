# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0-alpha.5] - Unreleased

### Added

- `ProjectHierarchy.LoadAsync(project, openDocument, openByFileName, options, cancellationToken)`: a stream loader that knows each document by the location its callback gives. The callback is given each path as written and the location of the sheet whose symbol asks for it (`null` for a project document), so it can follow paths relative to that sheet, and returns the document's stream with its location, which becomes the document's key and is passed on to the sheets that document uses. Each location is read once. The optional `openByFileName` is a last resort: once no path is left to try, it is asked for the first file name none of whose paths gave a document, and the paths of the sheet it gives are tried before the next such name is asked for. Each symbol that places such a sheet is reported as `SheetFoundByFileName`.
- `HierarchySheetInstance.DocumentKey` and `ProjectHierarchy.DocumentsByKey`: the key the hierarchy holds a document by, the location a loader read it from (the full path for the disk loader) or its file name for a document known by its file name.
- `ProjectHierarchy.NormalizePath`: a document path as Windows (and Altium) resolves it, on every platform, as `Path.GetFullPath` does on Windows without making it absolute (`.` dropped, `..` removing the segment before it, a segment's single trailing period and the last segment's trailing periods and spaces dropped).
- `ProjectHierarchy.OrderSiblings` is public: the order the hierarchy visits a sheet's symbols, numbers channels in and asks for the sheets they use.
- `ProjectHierarchyOptions.FollowSheetSymbol`: decides per sheet symbol whether the hierarchy follows it. A symbol not followed is not asked for, places nothing when the top sheets are chosen and is skipped with the new `SheetSymbolNotFollowed`, so callers need not change documents around `Build`.
- `ProjectHierarchyOptions.MaxUniqueIdLength`, `MaxDesignatorLength` and `MaxRoomNameLength` (255 each): a sheet symbol with a longer unique id or channel name is not followed, and a component with a longer unique id or designator is left out, with an error (`MaxUniqueIdLengthExceeded`, `MaxDesignatorLengthExceeded`, `MaxRoomNameLengthExceeded`) and `IsTruncated`; a longer channel designator format is replaced by the default with a warning.
- `ProjectHierarchyOptions.ReaderOptions`: the reader options the loaders read sheets with.
- `ProjectNetlist.Hierarchy`: the compiled sheet hierarchy the netlist was solved over (a `NetPin.SheetInstanceId` is a `HierarchySheetInstance.Index`), and `ProjectNetlist.PhysicalDesignatorOf(pin)`: the designator of a pin's component on its sheet instance, a channel designator such as `U1_CH2` in a multi-channel design and the logical designator elsewhere, so the channels' pins can be told apart by the designator the board carries.
- `AltiumReaderOptions`, taken by every binary reader's new constructor and by new `AltiumLibrary.Open*Async(Stream, AltiumReaderOptions, CancellationToken)` overloads: `MaxInflatedPayloadBytes` (512 MiB) and `MaxInflatedTotalBytes` (4 GiB) bound what one embedded image or 3D model, and all of a document's together, inflate to, leaving the rest out with a warning; `ReadEmbeddedPayloads = false` skips them unread.
- `AltiumDiagnostic.DocumentKey`: the same string for every hierarchy diagnostic about one document (its key, or the first spelling of a missing file name; for sheet-symbol diagnostics, the document the symbol places). `AltiumDiagnostic.DocumentPath`: the path as written. `DocumentName` keeps its meaning for each code.
- New diagnostic codes, numbered after the existing ones: `SharedFileName` (documents read from different locations share a file name; each is a sheet of its own), `SheetFoundByFileName`, `SheetSymbolNotFollowed` and the three length limits above.
- `Read(Stream, CancellationToken)` on `SchDocReader`, `SchLibReader`, `PcbDocReader`, `PcbLibReader` and `PrjPcbReader` (also with a structure stream), and `ProjectStructure.Parse(Stream, CancellationToken)`.
- `ParameterScope`: a sheet's and a project's parameters indexed by name once, with `SchComponent.GetResolvedComment(ParameterScope)`, `GetResolvedParameterValue(name, ParameterScope)` and `ResolveParameterText(text, ParameterScope)` overloads, and `ProjectHierarchy.ParameterScopeFor(sheet)`, which keeps one per sheet.

### Changed

- `ProjectHierarchy.NormalizeFileName` resolves the path as Windows does before taking its last segment, so `Main.SchDoc\.`, `Sub\Main.SchDoc\X\..`, `Main.SchDoc.` and `Main.SchDoc ` are all `Main.SchDoc` (they were `.`, `..`, `Main.SchDoc.` and `Main.SchDoc`), and a path that ends at a folder (`X\..`, `Sheets\`) names no file. Leading white space is still dropped.
- `ProjectHierarchy.LoadAsync(project)` (from disk) knows each sheet by the file it was read from, so two files of one name in different folders are two sheets, and a symbol that places another file of an ancestor's name is no cycle. A sheet symbol places the project's document of its file name (when the project lists several of that name, the one its path leads to, else the first), otherwise the file its path leads to. `Documents` stays keyed by file name and holds the first document of each name; `IsMultiChannel` and channel numbering go by document. Project documents of one name in different folders are each read (reported as `SharedFileName`), where `DuplicateDocumentName` said only the first was read. `Build` and the stream loader that takes only a path still know documents by file name.
- `PhysicalComponent.Comment` and `GetParameterValue` resolve against the sheet's and project's parameters as they are when first resolved through the hierarchy.
- `HierarchySheetInstance.UniqueIdPath`, `HierarchicalPath` and `PhysicalComponentPart.UniqueIdPath` are built when first read; the unique-id lookups no longer index every path.

### Fixed

- `ProjectHierarchy.LoadAsync` tries every path of a sheet's name until one gives a document. It marked a name as asked for before the callback found it, so a listed path the folder lacked, or an earlier symbol's path, hid every later path of that name. A path that gave nothing is not asked for again from the same sheet, and the duplicate-name warning now says "only one is read".
- `ProjectDocumentKinds.FromPath` (and so `AltiumProject.SchematicDocuments` and `DocumentsOfKind`) takes the extension of the file name Windows opens, so a listed `Main.SchDoc `, `Main.SchDoc.` or `Main.SchDoc\.` is a schematic, and the same for BomDocs; they were kinded `Other` and never read.
- The disk loader resolves paths itself, the same on every platform: `.`/`..`, trailing periods and spaces, drive paths as rooted, and (off Windows) file names ignoring case. It returns the file's full path, so a sheet found through `Sheets\Mid.SchDoc\.` looks for its own sheets in `Sheets`, not under a folder named after its file.
- A damaged board whose wide-string table names a huge text index no longer allocates a list up to it (an 8.7 KB board took 32 GB): the texts are kept by index.
- A compound-file stream that claims more bytes than the whole file is refused with `AltiumCorruptFileException` before anything is allocated (a 4 KB document claiming 2 GB allocated 2 GB first).
- A single document read can be cancelled: the `AltiumLibrary` stream methods and the hierarchy's stream loaders pass their token to the reader, which checks it between streams and records, and a cancellation is no longer wrapped as a corrupt-file error.
- Embedded images no longer shift onto the wrong `SchImage` when an earlier one is left out (damaged, or now past a limit), and per-pin records inflate to at most 1 MiB.
- `ProjectVariant.Variations` and `ParameterVariations` take the numbered entries that exist, indexing the section once, instead of looking up every number up to a count read from the file (100,000 entries took 39 s; a count of 2³¹ about 40 s).
- Reading a `.PrjPcb` or `.PrjPcbStructure` no longer holds 8 to 16 times the file: it is read into one array, decoded once, and parsed line by line from spans, with repeated keys held once.

## [2.0.0-alpha.4] - Unreleased

### Added

- `AltiumAsciiFileException`, derived from `AltiumCorruptFileException`: every binary reader (and so `ProjectHierarchy.LoadAsync`, with the document's path) throws it for a document Altium saved in its ASCII text format, such as a schematic beginning `|HEADER=Protel for Windows - Schematic Capture Ascii File`, so a caller can tell the user to save it as binary. Such files were reported as a generic `AltiumCorruptFileException`, which still catches them.
- `AltiumDiagnostic.SheetInstances`: every sheet instance a diagnostic concerns when there are several. A `SharedRoomName` diagnostic now lists each instance with that room name; it carried only the first.
- `AltiumDiagnostic.DesignatorTokens`: the channel designator format tokens, as written with their `$`, that an `UnsupportedDesignatorToken` (every unknown one) or `UnfilledDesignatorToken` (the keyword with no value) diagnostic is about.
- `ProjectHierarchy.NormalizeFileName` is public: the key the hierarchy knows a document by (the keys of `Documents` and the `FileName` of each sheet instance), for matching paths in `MissingDocuments` or sheet-symbol file names without a copy of the rule.
- `ProjectHierarchy.Build(project, sheets, options, cancellationToken)`. Building the hierarchy now checks the cancellation token while it walks, collects and merges, also in `LoadAsync` after the documents are read.

### Fixed

- `ProjectHierarchy` reports project documents in different folders that share a file name (`Rev A\Main.SchDoc` and `Rev B\Main.SchDoc`) as `DuplicateDocumentName`, naming both paths. `LoadAsync` reads only the first, and said nothing about the second.
- `ProjectHierarchy` reports each sheet symbol that names no file, places a missing sub-sheet, places one of its own parents or has an unreadable `Repeat(...)` once per symbol. They were de-duplicated by message text, so a second symbol with the same problem on the same sheet went unreported. Those messages now name the symbol (`Sheet symbol 'NOFILE2' on 'Child.SchDoc' names no sheet file.`).
- `ProjectHierarchy` trims the project's `ChannelDesignatorFormatString` and `ChannelRoomNamingStyle`, and takes a blank format as absent (the default). A format of `" $Component_$RoomName "` gave `" R1_CH1 "`, a blank one gave every channel part the designator `"   "`, and a style of `" 0"` was reported as unsupported.
- Merging component records was quadratic in the number of records that share one physical designator (a regression in 2.0.0-alpha.3): a format with no tokens over `Repeat(CH,1,1999)` of a 50-part sheet, 99,950 records all designated `X`, took minutes. Each record now finds its component in constant time on average; that design builds in about 0.1 s.

## [2.0.0-alpha.3] - Unreleased

### Added

- `AltiumDiagnostic.Code` (new `AltiumDiagnosticCode` enum) says what a diagnostic reports, and `DocumentName`, `SheetInstance`, `SheetSymbol` and `Designator` say where, so callers need not match message text. Every `ProjectHierarchy` diagnostic has a code: each `ProjectHierarchyOptions` limit (with the sheet symbol that went past it), a missing project document or sub-sheet (with the symbol that places it), a cycle, an unreadable `Repeat(...)`, duplicate, unannotated and missing designators, shared room names, unsupported channel settings, and a stale or missing structure file. Messages are unchanged; other diagnostics have `AltiumDiagnosticCode.None`.
- Channel designator formats support every keyword Altium documents: `$Component`, `$ComponentPrefix`, `$ComponentIndex`, `$RoomName`, `$ChannelPrefix`, `$ChannelIndex` and `$ChannelAlpha` (1 = A, 26 = Z, 27 = AA), so formats such as `$Component$ChannelAlpha` and `$Component_$ChannelPrefix$ChannelIndex` give each channel its own designator. `$ChannelIndex` is the number in the room name (the `Repeat` index, or the number given to symbols that share a designator), and otherwise the instance's position among its sheet's instances. New overload `ProjectHierarchy.FormatDesignator(format, component, roomName, channelPrefix, channelIndex)`.
- `ProjectHierarchy.DuplicateDesignators` lists the physical designators that separate components share, and `FindComponents(designator)` returns every component with a designator.
- `ProjectHierarchy` reports a project with no structure file (or none naming a top-level document) as an Info diagnostic, `StructureFileMissing`.
- `ProjectStructure.BuildTree(int maxNodes)` and `ProjectSheetNode.IsTruncated`; `BuildTree()` stops at `ProjectStructure.DefaultMaxTreeNodes` (100,000).
- `SchComponent.AddImplementation` is now public, so code can give a component a footprint (`PCBLIB`) or other model link.
- `RepeatInfo.IsUnreadable` tells a sheet symbol whose designator has a `Repeat(...)` that cannot be read (`Repeat(CH,1,200000)`, `Repeat(CH,-1,3)`, `Repeat(,1,3)`, an unclosed one) from a plain designator; both have `IsRepeated == false`. `RepeatInfo.MentionsRepeat` is now public.

### Changed

- `ProjectHierarchy` merges records with one physical designator only when they can be the parts of one component: different part numbers from the same library symbol (`LibReference`, when both have one). Separate components that share a designator, such as an R5 on each of two sheets or a second copy of U1A, are now separate `PhysicalComponent`s, so a BOM taken from `Components` counts them all; they were merged into one with a warning. The `DuplicateDesignator` warning now says they are kept apart, and `FindComponent` returns the first of them.
- A channel designator format token that cannot be filled no longer appears literally in designators (`R1$ChannelAlpha` in every channel, which then merged): a component whose format has an unknown token, or a keyword with no value for it (`$ComponentIndex` for `J_PWR`, `$ChannelAlpha` for channel 0), gets the default `$Component_$RoomName` instead, reported as `UnsupportedDesignatorToken` or `UnfilledDesignatorToken`. The three-argument `FormatDesignator` now also expands `$ComponentPrefix` and `$ComponentIndex`; it still keeps channel keywords and unknown tokens as written.

### Fixed

- `ProjectStructure.BuildTree` no longer recurses once per level, so a deep chain of sheet-symbol records cannot overflow the stack, and it stops after a node limit, because a structure file of a few kilobytes whose sheets each place the next several times describes an exponentially large tree. The nodes whose descendants were cut off have `IsTruncated` set.
- A `.PrjPcbStructure` or `.PrjPcb` without a byte-order mark that is not valid UTF-8 is now read as Windows-1252, the ANSI code page such files can be saved in; it was decoded as UTF-8, so `FileName=Übersicht.SchDoc` became `�bersicht.SchDoc` and the top-level sheet was not found. This applies to `ProjectStructure.Parse(Stream)`, `AltiumLibrary.OpenProjectAsync` from a path or from streams, and `PrjPcbReader`. `PrjPcbWriter` writes such files back as Windows-1252 (UTF-8 if an edit added a character it cannot hold), so they still round-trip byte for byte.
- `ProjectHierarchy.LoadAsync` names the failing document in every `AltiumFileException` it lets through, not only in `AltiumCorruptFileException`: an `AltiumUnsupportedFeatureException` now also carries the path (as given to the callback, or the file on disk) in `FilePath`, keeping its type and `RecordType`. (An ASCII-format SchDoc is reported as `AltiumCorruptFileException`, as before.)
- `ProjectHierarchy.LoadAsync` asks for the sheets that symbols use but the project does not list in sheet-position order, as the walk visits them, rather than in record order, so `Documents` and `Sheets` list them in the same order.
- `ProjectHierarchy` now knows a sheet by one key everywhere: the last segment of its path, trimmed afterwards, with `\`, `/` and a drive's `:` as separators on every platform. A sheet symbol whose file name needed trimming after its folder (`Sheets\ Power.SchDoc`) or held a colon (`Sheets\C:Power.SchDoc`) did not find the sheet it was given, which then became a second root and was also listed in `MissingDocuments`.

## [2.0.0-alpha.2] - Unreleased

### Added

- `ComponentType` enum (Altium's component "Type": Standard, Mechanical, Graphical, Net Tie (In BOM), Net Tie, Standard (No BOM), Jumper) with `IsInBom()`. `SchComponent.ComponentType` and `PcbComponent.ComponentType` give the effective type.
- `SchComponent.Designator` (the `Designator` parameter), `FindParameter`, `GetParameterValue`, `GetResolvedParameterValue`, `GetResolvedComment` and `ResolveParameterText`, plus the `ParameterReference` helper. They resolve Altium's `=Name` parameter references (such as a Comment of `=Value` or `=Part Number`) against the component's parameters, then the sheet's, then the project's.
- `AltiumLibrary.OpenProjectAsync(Stream, Stream? structureStream)` and `PrjPcbReader.Read(Stream, Stream?)` load a project together with its `.PrjPcbStructure` when neither is on disk. `ProjectStructure.Parse(Stream)` reads a structure file's bytes, and `Parse(string)` now ignores a leading byte-order mark.
- `ProjectHierarchy`: a public, in-memory compile of a project's schematic hierarchy. `ProjectHierarchy.LoadAsync(project, openDocument)` opens the sheets through a callback that is given each document's path as written (for example to read an uploaded zip; streams need not be seekable), `LoadAsync(project)` reads them from disk, and `Build(project, sheets)` takes documents already open. Both `LoadAsync` overloads also load sheets that sheet symbols use but the project does not list, such as Altium device sheets, until no new names appear; `MissingDocuments` lists every needed document that was not supplied. It produces one `HierarchySheetInstance` per sheet-symbol use and `Repeat(name, first, last)` channel, with Altium's room names (channel name plus index; shared symbol designators numbered across the whole design in sheet-position order), unique-id paths that equal a PCB component's `SourceUniqueId`, and `HierarchicalPath` equal to its `SourceHierarchicalPath`. Each `PhysicalComponentPart` carries its physical designator (`ChannelDesignatorFormatString`, by default `$Component_$RoomName`, for sheets used more than once), and parts with the same physical designator merge into one `PhysicalComponent` (multi-part components, across sheets). `FindComponentByUniqueIdPath` also matches the PCB's mixed path for a multi-part component split across sheets. Problems (missing sheets, cycles, duplicate or unannotated designators, unsupported format tokens) are reported in `Diagnostics`.
- `ProjectHierarchyOptions` bounds the hierarchy walk for untrusted input: `MaxDepth` (default 64 levels), `MaxSheetInstances` (default 10,000) and `MaxComponentParts` (default 200,000; checked before each sheet instance, so a `Repeat(...)` over a large sheet stops early). `Build` and both `LoadAsync` overloads take it; when a limit is reached the walk reports an error and sets `ProjectHierarchy.IsTruncated`. The walk is iterative, so a deep chain of sheets cannot overflow the stack.
- `ProjectNetlistBuilder.Build(ProjectHierarchy)` builds a netlist from documents in memory.

### Changed

- `ProjectNetlistBuilder` now walks the hierarchy with `ProjectHierarchy` (the internal walker is gone). Sibling sheet symbols are visited in sheet-position order rather than record order, so per-parent channel numbers follow Altium's.
- The `LICENSE` file is now the MIT license, matching the README and the package metadata; it previously held the Apache License 2.0. The copyright line reads 2019-2026 Original Circuit Limited in both the file and the package.

### Fixed

- `SchComponent` now reads `COMPONENTKINDVERSION2` (new `ComponentKindVersion2` property) in SchDocs and SchLibs, and the writer emits it. Newer Altium versions keep the legacy `COMPONENTKIND` at 0 and store the real kind there, so "Standard (No BOM)" parts such as test points and fiducials previously read as Standard.
- `SchComponent.Comment` read from a SchDoc is now the component's `Comment` parameter, as it already was for a SchLib. It was previously filled with the Design Item ID.
- Reading a SchDoc, SchLib, PcbDoc or PcbLib from a writable stream no longer writes to it. OpenMcdf flushed the stream on dispose, which trimmed and rewrote the caller's data, and threw on a fixed-size `MemoryStream` ("Memory stream is not expandable") or on a FAT that ran past the end. Any seekable stream now works.
- `ProjectSheetInstance.SymbolUidPath` for a `Repeat()` channel is now `{index}{symbolUid}`, as Altium writes it, instead of `{symbolUid}~{index}`, so `ProjectNetlist.FindInstanceByUidPath` matches PCB `SourceUniqueId`s on Repeat designs.
- `RepeatInfo.Parse` (and so `SchSheetSymbol.Repeat`) no longer throws: a `Repeat(...)` whose bounds overflow `int` or use non-ASCII digits threw `OverflowException` or `FormatException`. Such a directive, or one with an empty name, signs or separators, is now read as not repeated, and `ProjectHierarchy` reports it as a warning. The pattern also no longer backtracks quadratically on long designators.

## [2.0.0-alpha.1] - Unreleased

### Added

- Complete rewrite of the library with a focus on correctness, performance, and maintainability.
- Fully async read and write API (`ReadAsync` / `WriteAsync`) with `CancellationToken` support on all readers and writers.
- Roslyn source generator (`OriginalCircuit.Altium.Generators`) that generates `FromParameters` / `ToParameters` methods from `[AltiumParameter]` attributes, eliminating reflection-based serialization at runtime.
- Support for reading and writing PCB documents (`.PcbDoc`), including nets, rules, classes, differential pairs, rooms, and advanced storage sections.
- Cross-platform rendering via two new packages:
  - `OriginalCircuit.Altium.Rendering.Raster` — PNG/JPG output using SkiaSharp
  - `OriginalCircuit.Altium.Rendering.Svg` — vector SVG output using .NET XML APIs
- 3D rendering via `OriginalCircuit.Altium.Rendering.Gltf`: exports a `PcbDocument` to a glTF 2.0 model (`.glb`/`.gltf`) — the full physical layer stack (laminate at true thickness, copper layers, solder mask, silkscreen and plated drills) plus the embedded component 3D STEP bodies, placed from their footprint/3D transforms. Every board feature and component is a named, individually toggleable node. Driven by `GltfRenderer` / `GltfRenderSettings`; built on a native earcut triangulator and the `OriginalCircuit.Mech.STEP` / `OriginalCircuit.Mech.GLTF` libraries (vendored as submodules). Adds a `PcbStackup` model surfaced via `PcbDocument.GetStackup()` that exposes per-layer thicknesses and absolute Z positions.
- Shared rendering abstractions in `OriginalCircuit.Altium.Rendering.Core` (`IRenderContext`, `IRenderer`, `CoordTransform`, `LayerColors`, visitor-pattern component renderers).
- Structured diagnostics system: readers collect non-fatal warnings and errors as `AltiumDiagnostic` records (with `DiagnosticSeverity` of Info, Warning, or Error) on the returned model object instead of throwing.
- Exception hierarchy: `AltiumFileException` base, `AltiumCorruptFileException` (includes stream name), `AltiumUnsupportedFeatureException` (includes record type).
- Property coverage test infrastructure and auto-generated `COVERAGE.md` report.
- Fluent builder API for constructing components and primitives programmatically.
- `PcbBinaryConstants` class with named flag constants and a `DecodeFlags` helper.
- `WriterUtilities` shared between `PcbLibWriter` and `SchLibWriter`.
- 2-D barcode rendering: a PCB `Text` whose `BarCodeType` is `PcbBarCodeKind.DataMatrix` or `PcbBarCodeKind.QrCode` is now encoded on the fly and drawn as its module pattern in both the Altium-style and photorealistic renderers, instead of falling back to the source text. Data Matrix uses ISO/IEC 16022 ECC200 (ASCII encodation, Reed-Solomon, Annex F placement); QR Code uses ISO/IEC 18004 (numeric/alphanumeric/byte modes, Reed-Solomon, automatic version selection and penalty-based data masking, error-correction level M to match Altium). On the solder-mask layer the symbol is rendered as mask openings (honoring `BarCodeInverted`, the box size and quiet-zone margins), so an inverted symbol over a copper fill reads as a gold field with green data modules. New public `DataMatrixEncoder` / `QrCodeEncoder` APIs (with `DataMatrixSymbol` / `QrCodeSymbol`) in `OriginalCircuit.Altium.Barcodes`, and a `PcbBarCodeKind` enum (`Code39`, `Code128`, `QrCode`, `DataMatrix`).

### Changed

- New top-level namespace: `OriginalCircuit.Altium` (previously `OriginalCircuit.AltiumSharp`).
- New NuGet package ID: `OriginalCircuit.Altium` (previously `OriginalCircuit.AltiumSharp`).
- Interface-driven API: data models implement `IContainer`, `IComponent`, and related interfaces rather than inheriting from concrete base classes where possible.
- Target framework updated to `net10.0`.
- All four readers and writers are stateless and thread-safe; they can be instantiated once and reused across calls.
- `ParameterCollection` is now a value-type-friendly immutable record supporting efficient serialization round-trips.
- `Coord` conversions use `checked()` arithmetic and throw `OverflowException` on values that exceed the internal fixed-point range.

### Fixed

- Renderers now write to the output stream asynchronously (the image/SVG is encoded in memory, then written with `WriteAsync`), so `RenderAsync` works with streams that disallow synchronous I/O such as an ASP.NET Core response body.
- Raster rendering now produces JPEG as well as PNG: set `RenderOptions.Format` and `Quality`, or render to a `.jpg`/`.jpeg`/`.png` path (the format is inferred from the extension). Previously only PNG was produced despite the documented JPG support.
- `SchLibWriter` now preserves each pin's `OwnerPartId`, so multi-part component symbols round-trip with their pins correctly assigned to their parts (previously every pin was written as part 1).

### Removed

- `System.Drawing.Common` dependency removed from the core library; rendering is now handled exclusively by the optional rendering packages.
- Synchronous-only read and write API removed; all I/O is async.
- Windows-specific rendering code (GDI+) removed; replaced by cross-platform SkiaSharp and SVG backends.

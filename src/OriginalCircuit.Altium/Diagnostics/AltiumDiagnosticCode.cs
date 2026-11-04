namespace OriginalCircuit.Altium.Diagnostics;

/// <summary>
/// What an <see cref="AltiumDiagnostic"/> reports, so callers can act on it without reading its
/// message. Diagnostics that have no code of their own (most file-reader diagnostics) use
/// <see cref="None"/>.
/// </summary>
public enum AltiumDiagnosticCode
{
    /// <summary>No specific code.</summary>
    None = 0,

    // ---- Documents ----

    /// <summary>A project's schematic document was not supplied. <see cref="AltiumDiagnostic.DocumentName"/> is its path as written.</summary>
    DocumentNotSupplied,

    /// <summary>A schematic document without a file name was skipped.</summary>
    DocumentWithoutFileName,

    /// <summary>
    /// More than one schematic document has one file name, and the hierarchy holds one document per
    /// name: two supplied documents (<see cref="AltiumDiagnostic.DocumentName"/> is the name; the first
    /// is used), or two project documents in different folders (<see cref="AltiumDiagnostic.DocumentName"/>
    /// is the second path as written; the message names both).
    /// </summary>
    DuplicateDocumentName,

    /// <summary>A sheet that a sheet symbol uses but the project does not list (such as a device sheet) was loaded too.</summary>
    UnlistedSheetLoaded,

    /// <summary>A supplied sheet is not placed anywhere in the hierarchy and was left out.</summary>
    SheetNotInHierarchy,

    // ---- Structure file ----

    /// <summary>The project has no <c>.PrjPcbStructure</c>; the roots are the sheets no sheet symbol uses.</summary>
    StructureFileMissing,

    /// <summary>
    /// The structure file's top-level document (<see cref="AltiumDiagnostic.DocumentName"/>) was not
    /// supplied or is used by a sheet symbol, so the structure file is stale; the roots are the
    /// sheets no sheet symbol uses.
    /// </summary>
    StructureFileStale,

    // ---- Sheet symbols ----

    /// <summary>
    /// A sheet symbol (<see cref="AltiumDiagnostic.SheetSymbol"/>, on <see cref="AltiumDiagnostic.SheetInstance"/>)
    /// uses a sheet that was not supplied; <see cref="AltiumDiagnostic.DocumentName"/> is its path as written.
    /// </summary>
    SubSheetNotSupplied,

    /// <summary>A sheet symbol names no sheet file.</summary>
    SheetSymbolWithoutFile,

    /// <summary>
    /// A sheet symbol places its own sheet or one of its parents (<see cref="AltiumDiagnostic.DocumentName"/>);
    /// that use was skipped.
    /// </summary>
    SheetCycle,

    /// <summary>Every sheet is used by a sheet symbol, so the hierarchy started from the first document (<see cref="AltiumDiagnostic.DocumentName"/>).</summary>
    NoRootSheet,

    /// <summary>
    /// A sheet symbol's designator has a <c>Repeat(...)</c> that cannot be read (see
    /// <see cref="Models.Sch.RepeatInfo.IsUnreadable"/>); the sheet was placed once.
    /// </summary>
    UnreadableRepeat,

    // ---- Limits (ProjectHierarchyOptions) ----

    /// <summary>
    /// The hierarchy is deeper than <c>ProjectHierarchyOptions.MaxDepth</c>. <see cref="AltiumDiagnostic.SheetSymbol"/>
    /// is the first symbol that would have gone deeper and <see cref="AltiumDiagnostic.SheetInstance"/> the sheet it is on.
    /// </summary>
    MaxDepthExceeded,

    /// <summary>
    /// The walk stopped at <c>ProjectHierarchyOptions.MaxSheetInstances</c>. <see cref="AltiumDiagnostic.SheetSymbol"/>
    /// is the symbol whose sheet would have been one too many.
    /// </summary>
    MaxSheetInstancesExceeded,

    /// <summary>
    /// The walk stopped at <c>ProjectHierarchyOptions.MaxComponentParts</c>. <see cref="AltiumDiagnostic.SheetSymbol"/>
    /// is the symbol whose sheet would have placed too many parts.
    /// </summary>
    MaxComponentPartsExceeded,

    // ---- Rooms and designators ----

    /// <summary>The project's channel room naming style is not modelled; rooms are named as for style 0.</summary>
    UnsupportedRoomNamingStyle,

    /// <summary>
    /// The channel designator format uses a token that is not a designator keyword, so channel
    /// components get the default format instead.
    /// </summary>
    UnsupportedDesignatorToken,

    /// <summary>
    /// The channel designator format has a keyword with no value for a component (such as
    /// <c>$ComponentIndex</c> for <c>J_PWR</c>, or <c>$ChannelAlpha</c> for channel 0), so it gets the default
    /// format instead. <see cref="AltiumDiagnostic.Designator"/> and <see cref="AltiumDiagnostic.SheetInstance"/>
    /// are the first such component and its sheet; it is reported once per keyword.
    /// </summary>
    UnfilledDesignatorToken,

    /// <summary>
    /// Several sheet instances share a room name (the first is <see cref="AltiumDiagnostic.SheetInstance"/>), so
    /// their physical designators can collide.
    /// </summary>
    SharedRoomName,

    /// <summary>
    /// Separate components have the physical designator in <see cref="AltiumDiagnostic.Designator"/>
    /// (see <c>ProjectHierarchy.DuplicateDesignators</c>); each is kept as its own component.
    /// <see cref="AltiumDiagnostic.SheetInstance"/> is the sheet of the second one.
    /// </summary>
    DuplicateDesignator,

    /// <summary>Components are not annotated (their designator contains <c>?</c>).</summary>
    UnannotatedDesignator,

    /// <summary>Components on <see cref="AltiumDiagnostic.SheetInstance"/> have no designator and were left out.</summary>
    ComponentWithoutDesignator,
}

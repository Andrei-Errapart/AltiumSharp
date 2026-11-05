using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Eda.Enums;

namespace OriginalCircuit.Altium.Diagnostics;

/// <summary>
/// A diagnostic message emitted during Altium file reading/writing or while compiling a project's
/// hierarchy. <see cref="Code"/> and the location properties say what it reports and where, so
/// callers need not read <see cref="Message"/>.
/// </summary>
/// <param name="Severity">The severity level of this diagnostic.</param>
/// <param name="Message">Human-readable description of the issue.</param>
/// <param name="StreamName">The OLE stream name where the issue occurred, if applicable.</param>
/// <param name="RecordIndex">The record index where the issue occurred, if applicable.</param>
public sealed record AltiumDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? StreamName = null,
    int? RecordIndex = null)
{
    /// <summary>What the diagnostic reports; <see cref="AltiumDiagnosticCode.None"/> when it has no code of its own.</summary>
    public AltiumDiagnosticCode Code { get; init; }

    /// <summary>
    /// The document the diagnostic is about, when there is one: a path as written (for a document
    /// that was not supplied) or a file name.
    /// </summary>
    public string? DocumentName { get; init; }

    /// <summary>
    /// The document a <see cref="ProjectHierarchy"/> diagnostic is about, the same string for every
    /// diagnostic about one document, so callers can group them without normalizing paths or folding
    /// case: for a document the hierarchy holds, its key (<see cref="HierarchySheetInstance.DocumentKey"/>,
    /// a key of <c>ProjectHierarchy.DocumentsByKey</c>); for one it does not hold, such as a document
    /// that was not supplied, its file name (<c>ProjectHierarchy.NormalizeFileName</c>) as first spelled.
    /// For a sheet-symbol diagnostic it is the document the symbol places (the missing sub-sheet, the
    /// ancestor of a cycle); <c>null</c> when the symbol places none (no file name, not followed) and for
    /// diagnostics about no document. <see cref="DocumentName"/> keeps its meaning for each code.
    /// </summary>
    public string? DocumentKey { get; init; }

    /// <summary>
    /// The path as written in the project or the sheet symbol, when a <see cref="ProjectHierarchy"/>
    /// diagnostic is about one (e.g. <c>"Sub\Gone.SchDoc"</c> or <c>"Main.SchDoc\."</c>); <c>null</c> otherwise.
    /// </summary>
    public string? DocumentPath { get; init; }

    /// <summary>
    /// The <see cref="ProjectHierarchy"/> sheet instance the diagnostic concerns, when there is one:
    /// for a sheet-symbol problem, the instance of the sheet the symbol is on.
    /// </summary>
    public HierarchySheetInstance? SheetInstance { get; init; }

    /// <summary>
    /// Every sheet instance the diagnostic concerns when it concerns several: for
    /// <see cref="AltiumDiagnosticCode.SharedRoomName"/>, each instance with that room name, in
    /// <c>ProjectHierarchy.Sheets</c> order (<see cref="SheetInstance"/> is the first). Otherwise <c>null</c>.
    /// </summary>
    public IReadOnlyList<HierarchySheetInstance>? SheetInstances { get; init; }

    /// <summary>The sheet symbol the diagnostic concerns, when there is one.</summary>
    public SchSheetSymbol? SheetSymbol { get; init; }

    /// <summary>The component designator the diagnostic concerns, when there is one.</summary>
    public string? Designator { get; init; }

    /// <summary>
    /// The channel designator format tokens the diagnostic concerns, as written in the format with
    /// their <c>$</c> (e.g. <c>"$Sheet"</c>): every unknown token for
    /// <see cref="AltiumDiagnosticCode.UnsupportedDesignatorToken"/>, the keyword with no value for
    /// <see cref="AltiumDiagnosticCode.UnfilledDesignatorToken"/>; otherwise <c>null</c>.
    /// </summary>
    public IReadOnlyList<string>? DesignatorTokens { get; init; }
}

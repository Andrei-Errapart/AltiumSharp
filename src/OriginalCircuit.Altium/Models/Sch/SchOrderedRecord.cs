namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// One FileHeader record captured on read, linking the typed model object it produced to its original
/// ordered parameters. The SchDoc writer walks <see cref="SchDocument.ReadOrderedRecords"/> to reproduce
/// the exact on-disk record order; this is the document-level analogue of the SchLib per-component
/// <c>ReadOrderedPrimitives</c> + <c>RawRecordParams</c> pairing, and of each PCB model's
/// <c>RawParametersOrdered</c>. This is the authorable, byte-faithful representation of the document's
/// record stream; the typed model collections remain the convenience/from-scratch authoring surface.
/// </summary>
public sealed class SchOrderedRecord
{
    /// <summary>
    /// The typed model object this record produced — a primitive, a <see cref="SchComponent"/>, or a
    /// <see cref="SchRawRecord"/> holder for records with no first-class model (the RECORD=31 sheet
    /// settings, the implementation/map-definer container markers, and unknown record types).
    /// </summary>
    public required object Model { get; init; }

    /// <summary>The record's parameters in original key order (preserves duplicates and unmodeled keys).</summary>
    public required List<KeyValuePair<string, string>> OrderedParams { get; init; }
}

/// <summary>
/// A holder for a captured SchDoc record that has no first-class typed model (RECORD=31 sheet settings,
/// the implementation/map-definer container markers RECORD=44/46/48, and unrecognized record types).
/// Keeps the record addressable in <see cref="SchDocument.ReadOrderedRecords"/> so the on-disk order and
/// bytes round-trip without a detached parallel record list.
/// </summary>
public sealed class SchRawRecord
{
    public SchRawRecord(Dictionary<string, string> parameters) => Parameters = parameters;

    /// <summary>The parsed parameters of the record.</summary>
    public Dictionary<string, string> Parameters { get; }
}

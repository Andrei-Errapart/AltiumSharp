namespace OriginalCircuit.Altium;

/// <summary>
/// Options for reading Altium documents. A small document can hold compressed payloads (embedded
/// sheet images, embedded 3D models) that inflate to gigabytes, so the readers inflate each payload
/// only up to <see cref="MaxInflatedPayloadBytes"/> and a document's payloads only up to
/// <see cref="MaxInflatedTotalBytes"/> together, leaving the rest out with a warning in the document's
/// diagnostics. Callers that do not need the payloads at all (such as BOM extraction) can skip them
/// with <see cref="ReadEmbeddedPayloads"/>. The defaults are far above any real document.
/// </summary>
public sealed class AltiumReaderOptions
{
    private readonly int _maxInflatedPayloadBytes = 512 * 1024 * 1024;
    private readonly long _maxInflatedTotalBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>The default options.</summary>
    public static AltiumReaderOptions Default { get; } = new();

    /// <summary>
    /// Whether embedded payloads are inflated: <c>SchImage.ImageData</c> of embedded schematic images
    /// and <c>PcbModel.StepData</c> of embedded 3D models. When <c>false</c> image data is left <c>null</c>
    /// and model data empty (models keep their other properties), and their compressed bytes are not
    /// read, so a document read this way should not be written back. Default <c>true</c>.
    /// </summary>
    public bool ReadEmbeddedPayloads { get; init; } = true;

    /// <summary>
    /// The most bytes one embedded payload may inflate to; a payload that would inflate to more is left
    /// out (as with <see cref="ReadEmbeddedPayloads"/> <c>false</c>) with a warning. Default 512 MiB.
    /// </summary>
    public int MaxInflatedPayloadBytes
    {
        get => _maxInflatedPayloadBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxInflatedPayloadBytes = Math.Min(value, Array.MaxLength);
        }
    }

    /// <summary>
    /// The most bytes the embedded payloads of one document may inflate to together; once they reach it,
    /// the rest are left out with a warning. Default 4 GiB.
    /// </summary>
    public long MaxInflatedTotalBytes
    {
        get => _maxInflatedTotalBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxInflatedTotalBytes = value;
        }
    }
}

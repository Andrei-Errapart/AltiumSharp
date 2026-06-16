using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OriginalCircuit.Altium.Models.Pcb;

/// <summary>
/// Represents a 3D model embedded in a PCB library.
/// Each model contains STEP data and metadata linking it to component bodies.
/// </summary>
public sealed class PcbModel
{
    /// <summary>
    /// Unique identifier (GUID) for this model. Referenced by <see cref="PcbComponentBody.ModelId"/>.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Original filename of the STEP model (e.g., "PSEMI QFN-24 4x4.step").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the model data is embedded in the library file.
    /// </summary>
    public bool IsEmbedded { get; set; } = true;

    /// <summary>
    /// Model source type (typically "Undefined" for embedded models).
    /// </summary>
    public string ModelSource { get; set; } = "Undefined";

    /// <summary>
    /// X-axis rotation in degrees.
    /// </summary>
    public double RotationX { get; set; }

    /// <summary>
    /// Y-axis rotation in degrees.
    /// </summary>
    public double RotationY { get; set; }

    /// <summary>
    /// Z-axis rotation in degrees.
    /// </summary>
    public double RotationZ { get; set; }

    /// <summary>
    /// Z-axis offset.
    /// </summary>
    public int Dz { get; set; }

    /// <summary>
    /// Checksum of the embedded STEP payload, as computed by Altium's 3D engine (see
    /// <see cref="ComputeChecksum"/>). Stored as the 32-bit pattern (may read as negative for
    /// high-bit values). Preserved verbatim through round-trips; 0 is tolerated by Altium for new
    /// models. Call <see cref="ComputeChecksum"/> to derive it when authoring a model from scratch.
    /// </summary>
    public int Checksum { get; set; }

    /// <summary>
    /// The STEP model text data (ISO-10303-21 format).
    /// Stored compressed (zlib) in the file; decompressed here for direct access.
    /// </summary>
    public string StepData { get; set; } = string.Empty;

    /// <summary>
    /// Parses a PCB "Models" storage into <see cref="PcbModel"/> instances. Model metadata comes
    /// from the <c>Data</c> stream (a sequence of length-prefixed, pipe-delimited parameter records,
    /// one per model: <c>ID</c>, <c>NAME</c>, <c>EMBED</c>, <c>MODELSOURCE</c>, <c>ROTX/Y/Z</c>,
    /// <c>DZ</c>, <c>CHECKSUM</c>); each model's STEP payload comes from the matching numbered stream
    /// (<c>0</c>, <c>1</c>, ...), zlib-compressed. Shared by the PcbLib reader (<c>Library/Models</c>)
    /// and the PcbDoc model view (root <c>Models</c> storage).
    /// </summary>
    /// <summary>
    /// Computes the Altium 3D-model checksum: a position-weighted byte sum over the uncompressed STEP
    /// payload, mod 2^32. The weight of byte <c>i</c> is <c>1</c> for <c>i==0</c> and <c>i</c> otherwise.
    /// (Reverse-engineered; e.g. the payload <c>[10,20,30,40,50]</c> yields <c>410</c>.) Use this when
    /// authoring a model from scratch; loaded models preserve their stored <see cref="Checksum"/>.
    /// </summary>
    public static uint ComputeChecksum(ReadOnlySpan<byte> stepPayload)
    {
        uint checksum = 0;
        for (var i = 0; i < stepPayload.Length; i++)
            checksum = unchecked(checksum + stepPayload[i] * (i == 0 ? 1u : (uint)i));
        return checksum;
    }

    /// <summary>
    /// Recomputes <see cref="Checksum"/> from the current <see cref="StepData"/> (UTF-8 encoded).
    /// Call after setting <see cref="StepData"/> on a model authored from scratch.
    /// </summary>
    public void RecomputeChecksum()
        => Checksum = unchecked((int)ComputeChecksum(Encoding.UTF8.GetBytes(StepData)));

    /// <param name="dataStreamBytes">Raw bytes of the <c>Data</c> metadata stream, or null/empty if absent.</param>
    /// <param name="getModelStreamBytes">Returns the bytes of numbered payload stream <paramref name="getModelStreamBytes"/>(i), or null to stop.</param>
    internal static List<PcbModel> ParseModels(byte[]? dataStreamBytes, Func<int, byte[]?> getModelStreamBytes)
    {
        var models = new List<PcbModel>();

        // Parse the metadata records (one per model, in order).
        var modelMetadata = new List<Dictionary<string, string>>();
        if (dataStreamBytes is { Length: > 0 })
        {
            using var ms = new MemoryStream(dataStreamBytes);
            using var br = new BinaryReader(ms, Encoding.ASCII);

            while (ms.Position + 4 <= ms.Length)
            {
                var paramLen = br.ReadInt32();
                if (paramLen <= 0 || ms.Position + paramLen > ms.Length)
                    break;

                var paramStr = Encoding.ASCII.GetString(br.ReadBytes(paramLen)).TrimEnd('\0');
                var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in paramStr.Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eqIdx = part.IndexOf('=');
                    if (eqIdx > 0)
                        meta[part[..eqIdx]] = part[(eqIdx + 1)..];
                }
                modelMetadata.Add(meta);
            }
        }

        // Read numbered STEP streams (0, 1, 2, ...) - zlib compressed STEP text.
        for (var i = 0; ; i++)
        {
            var compressedData = getModelStreamBytes(i);
            if (compressedData is null)
                break;

            var model = new PcbModel();

            if (i < modelMetadata.Count)
            {
                var meta = modelMetadata[i];
                if (meta.TryGetValue("ID", out var id)) model.Id = id;
                if (meta.TryGetValue("NAME", out var name)) model.Name = name;
                if (meta.TryGetValue("EMBED", out var embed)) model.IsEmbedded = string.Equals(embed, "TRUE", StringComparison.OrdinalIgnoreCase);
                if (meta.TryGetValue("MODELSOURCE", out var source)) model.ModelSource = source;
                if (meta.TryGetValue("ROTX", out var rotx) && double.TryParse(rotx, CultureInfo.InvariantCulture, out var rx)) model.RotationX = rx;
                if (meta.TryGetValue("ROTY", out var roty) && double.TryParse(roty, CultureInfo.InvariantCulture, out var ry)) model.RotationY = ry;
                if (meta.TryGetValue("ROTZ", out var rotz) && double.TryParse(rotz, CultureInfo.InvariantCulture, out var rz)) model.RotationZ = rz;
                if (meta.TryGetValue("DZ", out var dz) && int.TryParse(dz, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dzVal)) model.Dz = dzVal;
                // Altium serializes the checksum as a SIGNED 32-bit decimal (negative for high-bit
                // values), so parse/write it as int to round-trip the exact on-disk string.
                if (meta.TryGetValue("CHECKSUM", out var cs) && int.TryParse(cs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var csVal)) model.Checksum = csVal;
            }

            if (compressedData.Length > 0)
            {
                using var ms = new MemoryStream(compressedData);
                using var zs = new ZLibStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                zs.CopyTo(outMs);
                model.StepData = Encoding.UTF8.GetString(outMs.ToArray());
            }

            models.Add(model);
        }

        return models;
    }
}

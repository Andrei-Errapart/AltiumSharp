using OpenMcdf;
using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Binary;
using OriginalCircuit.Altium.Serialization.Compound;
using System.Buffers;
using System.IO.Compression;
using System.Text;
using PadShape = OriginalCircuit.Altium.Models.Pcb.PadShape;
using PadHoleType = OriginalCircuit.Altium.Models.Pcb.PadHoleType;

namespace OriginalCircuit.Altium.Serialization.Readers;

/// <summary>
/// Primitive object IDs used in PCB binary format.
/// </summary>
internal enum PcbPrimitiveObjectId : byte
{
    Arc = 1,
    Pad = 2,
    Via = 3,
    Track = 4,
    Text = 5,
    Fill = 6,
    Region = 11,
    ComponentBody = 12
}

/// <summary>
/// Reads PCB footprint library (.PcbLib) files.
/// </summary>
public sealed class PcbLibReader
{
    private readonly Dictionary<string, string> _sectionKeys = new(StringComparer.OrdinalIgnoreCase);
    private List<AltiumDiagnostic> _diagnostics = new();

    /// <summary>
    /// Reads a PcbLib file from the specified path.
    /// </summary>
    /// <param name="path">Path to the .PcbLib file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed PCB footprint library.</returns>
    /// <exception cref="AltiumCorruptFileException">Thrown when the file cannot be parsed.</exception>
    /// <remarks>This method is not thread-safe. Create a new reader instance per thread.</remarks>
    public async ValueTask<PcbLibrary> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var accessor = await CompoundFileAccessor.OpenAsync(path, writable: false, cancellationToken);
            return Read(accessor, cancellationToken);
        }
        catch (Exception ex) when (ex is not AltiumFileException and not OperationCanceledException
            and not OutOfMemoryException and not FileNotFoundException and not UnauthorizedAccessException
            and not DirectoryNotFoundException)
        {
            throw new AltiumCorruptFileException($"Failed to read PcbLib file: {ex.Message}", filePath: path, innerException: ex);
        }
    }

    /// <summary>
    /// Reads a PcbLib file from a stream.
    /// </summary>
    /// <param name="stream">A readable stream containing compound file data. The stream is not closed.</param>
    /// <returns>The parsed PCB footprint library.</returns>
    /// <exception cref="AltiumCorruptFileException">Thrown when the stream cannot be parsed.</exception>
    /// <remarks>This method is not thread-safe. Create a new reader instance per thread.</remarks>
    public PcbLibrary Read(Stream stream)
    {
        try
        {
            using var accessor = CompoundFileAccessor.Open(stream, leaveOpen: true);
            return Read(accessor);
        }
        catch (Exception ex) when (ex is not AltiumFileException and not OutOfMemoryException)
        {
            throw new AltiumCorruptFileException($"Failed to read PcbLib file: {ex.Message}", innerException: ex);
        }
    }

    private PcbLibrary Read(CompoundFileAccessor accessor, CancellationToken cancellationToken = default)
    {
        _diagnostics = new List<AltiumDiagnostic>();
        var library = new PcbLibrary();

        // Read and preserve FileHeader trailing data (after version string)
        ReadFileHeader(accessor, library);

        // Preserve additional root-level streams/storages for round-trip fidelity
        // (FileVersionInfo, etc.)
        PreserveAdditionalRootStreams(accessor, library);

        // Read section keys mapping
        ReadSectionKeys(accessor, library);

        // Read library data (components list and their primitives)
        ReadLibrary(accessor, library, cancellationToken);

        library.Diagnostics = _diagnostics;
        return library;
    }

    private static void ReadFileHeader(CompoundFileAccessor accessor, PcbLibrary library)
    {
        var stream = accessor.TryGetStream("FileHeader");
        if (stream == null)
            return;

        var data = stream.GetData();
        using var ms = new MemoryStream(data);
        using var reader = new BinaryFormatReader(ms, leaveOpen: true);

        // Read the version string: int32 length + pascal short string
        var blockLen = reader.ReadInt32();
        if (blockLen <= 0)
            return;
        var stringLen = reader.ReadByte();
        reader.Skip(stringLen);
        var consumed = 4 + 1 + stringLen;
        // Skip any padding within the block
        if (consumed < 4 + blockLen)
            reader.Skip(4 + blockLen - consumed);

        // After the version string block, there are 3 pascal short strings:
        // 1) Format version double (5.01) + 2 padding bytes
        // 2) Empty string (placeholder)
        // 3) 8-character unique library identifier
        if (reader.HasMore)
        {
            var versionLen = reader.ReadByte();
            if (versionLen > 0)
                reader.Skip(versionLen); // skip version double + padding
        }
        if (reader.HasMore)
        {
            var emptyLen = reader.ReadByte();
            if (emptyLen > 0)
                reader.Skip(emptyLen); // skip empty string
        }
        if (reader.HasMore)
        {
            var idLen = reader.ReadByte();
            if (idLen > 0)
            {
                var idBytes = new byte[idLen];
                reader.ReadExact(idBytes);
                library.UniqueId = AltiumEncoding.Windows1252.GetString(idBytes);
            }
        }
    }

    private static void PreserveAdditionalRootStreams(CompoundFileAccessor accessor, PcbLibrary library)
    {
        library.AdditionalRootStreams = new Dictionary<string, byte[]>();

        // Known additional storages that Altium writes
        var additionalStorages = new[] { "FileVersionInfo" };
        foreach (var storageName in additionalStorages)
        {
            var storage = accessor.TryGetStorage(storageName);
            if (storage != null)
            {
                storage.VisitEntries(entry =>
                {
                    if (entry is OpenMcdf.CFStream stream)
                    {
                        library.AdditionalRootStreams[$"{storageName}/{entry.Name}"] = stream.GetData();
                    }
                }, false);
            }
        }
    }

    private void ReadSectionKeys(CompoundFileAccessor accessor, PcbLibrary library)
    {
        _sectionKeys.Clear();

        var stream = accessor.TryGetStream("SectionKeys");
        if (stream == null)
            return;

        var data = stream.GetData();
        using var ms = new MemoryStream(data);
        using var reader = new BinaryFormatReader(ms, leaveOpen: true);

        var keyCount = reader.ReadInt32();
        for (var i = 0; i < keyCount; i++)
        {
            var libRef = ReadPascalString(reader);
            var sectionKey = reader.ReadStringBlock();
            _sectionKeys[libRef] = sectionKey;
        }

        // Preserve section keys for round-trip fidelity
        library.SectionKeys = new Dictionary<string, string>(_sectionKeys, StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadPascalString(BinaryFormatReader reader)
    {
        var size = reader.ReadInt32();
        if (size <= 0)
            return string.Empty;

        // Read null-terminated string
        var length = reader.ReadByte();
        if (length == 0)
        {
            reader.Skip(size - 1);
            return string.Empty;
        }

        // Use stack for small strings, rent for larger ones
        var encoding = AltiumEncoding.Windows1252;
        int stringLength = length; // byte fits in int, avoid Math.Min ambiguity
        Span<byte> buffer = stackalloc byte[Math.Min(stringLength, 256)];

        if (stringLength <= 256)
        {
            reader.ReadExact(buffer.Slice(0, length));
            var consumed = 1 + length;
            if (consumed < size)
                reader.Skip(size - consumed);
            return encoding.GetString(buffer.Slice(0, length));
        }
        else
        {
            var rentedBuffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                reader.ReadExact(rentedBuffer.AsSpan(0, length));
                var consumed = 1 + length;
                if (consumed < size)
                    reader.Skip(size - consumed);
                return encoding.GetString(rentedBuffer, 0, length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private void ReadLibrary(CompoundFileAccessor accessor, PcbLibrary library, CancellationToken cancellationToken)
    {
        var libraryStorage = accessor.TryGetStorage("Library")
            ?? throw new InvalidDataException("PcbLib file missing 'Library' storage");

        // Read header (record count - not currently used)
        GetChildStream(libraryStorage, "Header");

        // Read library data
        var dataStream = GetChildStream(libraryStorage, "Data")
            ?? throw new InvalidDataException("Library storage missing 'Data' stream");

        var data = dataStream.GetData();
        using var ms = new MemoryStream(data);
        using var reader = new BinaryFormatReader(ms, leaveOpen: true);

        // Read library parameters (header info). The header is an ordered parameter list that
        // contains duplicate keys (repeated RECORD=Board markers), so capture the ordered form
        // for faithful round-trip in addition to the flattened dictionary view.
        var libraryParams = ReadParameterBlock(reader, out var rawLibraryParams);
        library.LibraryParameters = libraryParams;
        library.LibraryParametersOrdered = ParseParametersOrdered(rawLibraryParams);

        // Read footprint count
        var footprintCount = reader.ReadUInt32();

        for (var i = 0; i < footprintCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refName = reader.ReadStringBlock();
            var sectionKey = GetSectionKeyFromRefName(refName);

            var component = ReadFootprint(accessor, sectionKey, cancellationToken);
            if (component != null)
            {
                library.Add(component);
            }
        }

        // Preserve additional library-level streams for round-trip fidelity
        library.AdditionalLibraryStreams = new Dictionary<string, byte[]>();
        var knownLibraryChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Header", "Data", "Models" };
        libraryStorage.VisitEntries(entry =>
        {
            if (knownLibraryChildren.Contains(entry.Name))
                return;
            if (entry is OpenMcdf.CFStream stream)
            {
                library.AdditionalLibraryStreams[entry.Name] = stream.GetData();
            }
            else if (entry is OpenMcdf.CFStorage subStorage)
            {
                // Preserve sub-storage streams (e.g., ComponentParamsTOC/Data)
                subStorage.VisitEntries(subEntry =>
                {
                    if (subEntry is OpenMcdf.CFStream subStream)
                    {
                        library.AdditionalLibraryStreams[$"{entry.Name}/{subEntry.Name}"] = subStream.GetData();
                    }
                }, false);
            }
        }, false);

        // Parse 3D model streams (STEP data + metadata)
        if (libraryStorage.TryGetStorage("Models", out var modelsStorage))
        {
            ReadModels(modelsStorage, library);
        }
    }

    private static void ReadModels(CFStorage modelsStorage, PcbLibrary library)
    {
        // Read Data stream: contains parameter blocks with model metadata
        // Format: int32 length + C-string (null-terminated pipe-delimited params)
        // One entry per model with: EMBED, MODELSOURCE, ID, ROTX, ROTY, ROTZ, DZ, CHECKSUM, NAME
        var modelMetadata = new List<Dictionary<string, string>>();
        if (modelsStorage.TryGetStream("Data", out var dataStream))
        {
            var dataBytes = dataStream.GetData();
            using var ms = new MemoryStream(dataBytes);
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

        // Read numbered STEP streams (0, 1, 2, ...) - zlib compressed STEP text
        for (var i = 0; ; i++)
        {
            if (!modelsStorage.TryGetStream(i.ToString(), out var modelStream))
                break;

            var compressedData = modelStream.GetData();
            var model = new PcbModel();

            // Apply metadata from Data stream if available
            if (i < modelMetadata.Count)
            {
                var meta = modelMetadata[i];
                if (meta.TryGetValue("ID", out var id)) model.Id = id;
                if (meta.TryGetValue("NAME", out var name)) model.Name = name;
                if (meta.TryGetValue("EMBED", out var embed)) model.IsEmbedded = string.Equals(embed, "TRUE", StringComparison.OrdinalIgnoreCase);
                if (meta.TryGetValue("MODELSOURCE", out var source)) model.ModelSource = source;
                if (meta.TryGetValue("ROTX", out var rotx) && double.TryParse(rotx, System.Globalization.CultureInfo.InvariantCulture, out var rx)) model.RotationX = rx;
                if (meta.TryGetValue("ROTY", out var roty) && double.TryParse(roty, System.Globalization.CultureInfo.InvariantCulture, out var ry)) model.RotationY = ry;
                if (meta.TryGetValue("ROTZ", out var rotz) && double.TryParse(rotz, System.Globalization.CultureInfo.InvariantCulture, out var rz)) model.RotationZ = rz;
                if (meta.TryGetValue("DZ", out var dz) && int.TryParse(dz, out var dzVal)) model.Dz = dzVal;
                if (meta.TryGetValue("CHECKSUM", out var cs) && int.TryParse(cs, out var csVal)) model.Checksum = csVal;
            }

            // Decompress STEP data
            if (compressedData.Length > 0)
            {
                using var ms = new MemoryStream(compressedData);
                using var zs = new ZLibStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                zs.CopyTo(outMs);
                model.StepData = Encoding.UTF8.GetString(outMs.ToArray());
            }

            library.Models.Add(model);
        }
    }

    private string GetSectionKeyFromRefName(string refName)
    {
        if (_sectionKeys.TryGetValue(refName, out var sectionKey))
            return sectionKey;

        // Fallback: mangle name to fit compound storage limitations
        var maxLength = Math.Min(refName.Length, 31);
        return refName.Substring(0, maxLength).Replace('/', '_');
    }

    private PcbComponent? ReadFootprint(CompoundFileAccessor accessor, string sectionKey, CancellationToken cancellationToken = default)
    {
        var storage = accessor.TryGetStorage(sectionKey);
        if (storage == null)
            return null;

        var component = new PcbComponent();

        // Read header (record count - not currently used beyond validation)
        GetChildStream(storage, "Header");

        // Read parameters
        var paramStream = GetChildStream(storage, "Parameters");
        if (paramStream != null)
        {
            var paramData = paramStream.GetData();
            using var paramMs = new MemoryStream(paramData);
            using var paramReader = new BinaryFormatReader(paramMs, leaveOpen: true);

            var parameters = ReadParameterBlock(paramReader);
            ApplyComponentParameters(component, parameters);
        }

        // Read wide strings (Unicode text for Text primitives)
        var wideStrings = ReadWideStrings(storage);

        // Preserve additional component-level streams (PrimitiveGuids, UniqueIdPrimitiveInformation, etc.)
        component.AdditionalStreams = new Dictionary<string, byte[]>();
        var knownChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Header", "Parameters", "WideStrings", "Data" };
        storage.VisitEntries(entry =>
        {
            if (knownChildren.Contains(entry.Name))
                return;
            if (entry is OpenMcdf.CFStream stream)
            {
                component.AdditionalStreams[entry.Name] = stream.GetData();
            }
            else if (entry is OpenMcdf.CFStorage subStorage)
            {
                subStorage.VisitEntries(subEntry =>
                {
                    if (subEntry is OpenMcdf.CFStream subStream)
                    {
                        component.AdditionalStreams[$"{entry.Name}/{subEntry.Name}"] = subStream.GetData();
                    }
                }, false);
            }
        }, false);

        // Read primitive data
        var dataStream = GetChildStream(storage, "Data");
        if (dataStream != null)
        {
            var data = dataStream.GetData();
            using var ms = new MemoryStream(data);
            using var reader = new BinaryFormatReader(ms, leaveOpen: true);

            // First comes the pattern name
            var pattern = reader.ReadStringBlock();
            if (string.IsNullOrEmpty(component.Name))
                component.Name = pattern;

            // Then the primitives
            while (reader.HasMore)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var objectId = (PcbPrimitiveObjectId)reader.ReadByte();

                switch (objectId)
                {
                    case PcbPrimitiveObjectId.Arc:
                        var arc = ReadArc(reader);
                        if (arc != null)
                            component.AddArc(arc);
                        break;

                    case PcbPrimitiveObjectId.Pad:
                        var pad = ReadPad(reader);
                        if (pad != null)
                            component.AddPad(pad);
                        break;

                    case PcbPrimitiveObjectId.Via:
                        var via = ReadVia(reader);
                        if (via != null)
                            component.AddVia(via);
                        break;

                    case PcbPrimitiveObjectId.Track:
                        var track = ReadTrack(reader);
                        if (track != null)
                            component.AddTrack(track);
                        break;

                    case PcbPrimitiveObjectId.Text:
                        var text = ReadText(reader, wideStrings);
                        if (text != null)
                            component.AddText(text);
                        break;

                    case PcbPrimitiveObjectId.Fill:
                        var fill = ReadFill(reader);
                        if (fill != null)
                            component.AddFill(fill);
                        break;

                    case PcbPrimitiveObjectId.Region:
                        var region = ReadRegion(reader);
                        if (region != null)
                            component.AddRegion(region);
                        break;

                    case PcbPrimitiveObjectId.ComponentBody:
                        var body = ReadComponentBody(reader);
                        if (body != null)
                            component.AddComponentBody(body);
                        break;

                    default:
                        // Unknown primitive - try to skip
                        reader.SkipBlock();
                        break;
                }
            }
        }

        return component;
    }

    internal static CFStream? GetChildStream(CFStorage storage, string name)
    {
        return storage.TryGetStream(name, out var stream) ? stream : null;
    }

    internal static Dictionary<string, string> ReadParameterBlock(BinaryFormatReader reader)
    {
        return ReadParameterBlock(reader, out _);
    }

    internal static Dictionary<string, string> ReadParameterBlock(BinaryFormatReader reader, out string rawString)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
        {
            rawString = string.Empty;
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // PCB parameter blocks are C-strings (null-terminated, no length prefix).
        // Read the entire block as raw bytes and decode as a string.
        byte[] buffer;
        if (sanitizedSize <= 512)
        {
            Span<byte> stackBuffer = stackalloc byte[sanitizedSize];
            reader.ReadExact(stackBuffer);
            buffer = stackBuffer.ToArray();
        }
        else
        {
            buffer = new byte[sanitizedSize];
            reader.ReadExact(buffer);
        }

        // Find the null terminator (if present) and decode the string
        var nullIndex = Array.IndexOf(buffer, (byte)0);
        var length = nullIndex >= 0 ? nullIndex : sanitizedSize;
        var paramString = AltiumEncoding.Windows1252.GetString(buffer, 0, length);

        rawString = paramString;
        return ParseParameters(paramString);
    }

    internal static Dictionary<string, string> ParseParameters(string paramString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(paramString))
            return result;

        // Parameters are in format: |KEY1=VALUE1|KEY2=VALUE2|...
        var span = paramString.AsSpan();

        var start = 0;
        while (start < span.Length)
        {
            // Skip leading pipe
            if (span[start] == '|')
                start++;

            if (start >= span.Length)
                break;

            // Find the equals sign
            var equalsIndex = span.Slice(start).IndexOf('=');
            if (equalsIndex < 0)
                break;

            var key = span.Slice(start, equalsIndex).ToString();
            start += equalsIndex + 1;

            // Find the next pipe or end
            var pipeIndex = span.Slice(start).IndexOf('|');
            string value;
            if (pipeIndex < 0)
            {
                value = span.Slice(start).ToString();
                start = span.Length;
            }
            else
            {
                value = span.Slice(start, pipeIndex).ToString();
                start += pipeIndex;
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Parses a pipe-delimited parameter string into an ordered list, preserving key order
    /// and duplicate keys (unlike <see cref="ParseParameters"/> which flattens into a map).
    /// </summary>
    internal static List<KeyValuePair<string, string>> ParseParametersOrdered(string paramString)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(paramString))
            return result;

        var span = paramString.AsSpan();
        var start = 0;
        while (start < span.Length)
        {
            if (span[start] == '|')
                start++;
            if (start >= span.Length)
                break;

            var equalsIndex = span.Slice(start).IndexOf('=');
            if (equalsIndex < 0)
                break;

            var key = span.Slice(start, equalsIndex).ToString();
            start += equalsIndex + 1;

            var pipeIndex = span.Slice(start).IndexOf('|');
            string value;
            if (pipeIndex < 0)
            {
                value = span.Slice(start).ToString();
                start = span.Length;
            }
            else
            {
                value = span.Slice(start, pipeIndex).ToString();
                start += pipeIndex;
            }

            result.Add(new KeyValuePair<string, string>(key, value));
        }

        return result;
    }

    private static void ApplyComponentParameters(PcbComponent component, Dictionary<string, string> parameters)
    {
        if (parameters.TryGetValue("PATTERN", out var pattern))
            component.Name = pattern;

        if (parameters.TryGetValue("DESCRIPTION", out var description))
            component.Description = description;

        if (parameters.TryGetValue("HEIGHT", out var heightStr) && TryParseCoord(heightStr, out var height))
            component.Height = height;

        if (parameters.TryGetValue("ITEMGUID", out var itemGuid))
            component.ItemGUID = itemGuid;

        if (parameters.TryGetValue("REVISIONGUID", out var revisionGuid))
            component.ItemRevisionGUID = revisionGuid;

        // Preserve any additional parameters not modeled as typed properties
        component.AdditionalParameters = ExtractAdditionalParameters(parameters,
            ["PATTERN", "DESCRIPTION", "HEIGHT", "ITEMGUID", "REVISIONGUID"]);
    }

    private static Dictionary<string, string>? ExtractAdditionalParameters(
        Dictionary<string, string> parameters, IEnumerable<string> knownKeys)
    {
        var known = new HashSet<string>(knownKeys, StringComparer.OrdinalIgnoreCase);
        var additional = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in parameters)
        {
            if (!known.Contains(kvp.Key))
                additional[kvp.Key] = kvp.Value;
        }
        return additional.Count > 0 ? additional : null;
    }

    private static bool TryParseCoord(string value, out Coord result)
    {
        result = default;

        // Altium stores coords as strings like "10mil" or raw integers
        if (string.IsNullOrEmpty(value))
            return false;

        // Try parse as integer (internal units)
        if (int.TryParse(value, out var intValue))
        {
            result = Coord.FromRaw(intValue);
            return true;
        }

        // Try parse with unit suffix
        var span = value.AsSpan();
        if (span.EndsWith("mil", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(span.Slice(0, span.Length - 3), out var mils))
            {
                result = Coord.FromMils(mils);
                return true;
            }
        }
        else if (span.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(span.Slice(0, span.Length - 2), out var mm))
            {
                result = Coord.FromMm(mm);
                return true;
            }
        }

        return false;
    }

    internal static List<string> ReadWideStrings(CFStorage storage)
    {
        var result = new List<string>();

        if (!storage.TryGetStream("WideStrings", out var stream))
            return result;

        var data = stream.GetData();
        using var ms = new MemoryStream(data);
        using var reader = new BinaryFormatReader(ms, leaveOpen: true);

        var parameters = ReadParameterBlock(reader);

        for (var i = 0; i < parameters.Count; i++)
        {
            var key = $"ENCODEDTEXT{i}";
            if (!parameters.TryGetValue(key, out var encodedText))
                break;

            // Encoded text is comma-separated UTF-16 code points
            var text = DecodeWideString(encodedText);
            result.Add(text);
        }

        return result;
    }

    internal static string DecodeWideString(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return string.Empty;

        var parts = encoded.Split(',');
        var chars = new char[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out var codePoint))
            {
                chars[i] = (char)codePoint;
            }
        }

        return new string(chars);
    }

    internal static void ReadCommonPrimitiveData(BinaryFormatReader reader, out byte layer, out ushort flags)
    {
        ReadCommonPrimitiveData(reader, out layer, out flags, out _);
    }

    internal static void ReadCommonPrimitiveData(BinaryFormatReader reader, out byte layer, out ushort flags, out int componentIndex)
    {
        layer = reader.ReadByte();
        flags = reader.ReadUInt16();

        // 10 bytes: uint16 netIndex, uint16 reserved, uint16 componentIndex, uint32 reserved
        reader.Skip(4); // net index + reserved
        componentIndex = reader.ReadUInt16(); // component index (0xFFFF = free primitive)
        if (componentIndex == 0xFFFF)
            componentIndex = -1;
        reader.Skip(4); // remaining reserved
    }

    internal static CoordPoint ReadCoordPoint(BinaryFormatReader reader)
    {
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        return new CoordPoint(Coord.FromRaw(x), Coord.FromRaw(y));
    }

    internal static PcbArc? ReadArc(BinaryFormatReader reader)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        var sr = reader.ReadBytes(sanitizedSize);
        bool Has(int off, int width) => off >= 0 && off + width <= sr.Length;
        byte B(int off) => Has(off, 1) ? sr[off] : (byte)0;
        int I32(int off) => Has(off, 4) ? BitConverter.ToInt32(sr, off) : 0;
        double Dbl(int off) => Has(off, 8) ? BitConverter.ToDouble(sr, off) : 0.0;

        var layer = B(0);
        var flags = (ushort)(B(1) | (B(2) << 8));

        var arc = PcbArc.Create()
            .At(Coord.FromRaw(I32(13)), Coord.FromRaw(I32(17)))
            .Radius(Coord.FromRaw(I32(21)))
            .Angles(Dbl(25), Dbl(33))
            .Width(Coord.FromRaw(I32(41)))
            .Layer(layer)
            .Build();

        arc.SolderMaskExpansion = Coord.FromRaw(I32(47)); // 47-50
        arc.KeepoutRestrictions = B(56);                  // 56

        // Decode flags
        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        arc.IsLocked = isLocked;
        arc.IsTentingTop = isTentingTop;
        arc.IsTentingBottom = isTentingBottom;
        arc.IsKeepout = isKeepout;

        return arc;
    }

    internal static PcbPad? ReadPad(BinaryFormatReader reader)
    {
        // Pad has a complex multi-block structure
        var designator = reader.ReadStringBlock();

        // Skip reserved blocks (always single zero byte) and net string (always "|&|0")
        reader.SkipBlock();
        reader.ReadStringBlock(); // Usually "|&|0" - discard
        reader.SkipBlock();

        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        var startPos = reader.Position;
        ReadCommonPrimitiveData(reader, out var layer, out var flags, out var componentIndex);

        // Read main block fields
        var location = ReadCoordPoint(reader);
        var sizeTop = ReadCoordPoint(reader);
        var sizeMiddle = ReadCoordPoint(reader);
        var sizeBottom = ReadCoordPoint(reader);
        var holeSize = Coord.FromRaw(reader.ReadInt32());
        var shapeTop = reader.ReadByte();
        var shapeMiddle = reader.ReadByte();
        var shapeBottom = reader.ReadByte();
        var rotation = reader.ReadDouble();
        var isPlated = reader.ReadByte() != 0;

        // Read extended main block fields (power plane, masks, mask modes, jumper, tolerances)
        ReadPadExtendedFields(reader, startPos, sanitizedSize,
            out var stackMode, out var powerPlaneConnectStyle,
            out var reliefAirGap, out var reliefConductorWidth, out var reliefEntries,
            out var powerPlaneClearance, out var powerPlaneReliefExpansion,
            out var pasteMaskExpansion, out var solderMaskExpansion,
            out var pasteMaskMode, out var solderMaskMode,
            out var jumperId, out var holePositiveTolerance, out var holeNegativeTolerance);

        // Read size/shape block (596 bytes for extended pad data)
        ReadPadSizeShapeBlock(reader, stackMode, ref shapeTop, ref shapeMiddle, ref shapeBottom,
            out var layerXSizes, out var layerYSizes, out var internalLayerShapes,
            out var holeShapeByte, out var holeSlotLength, out var holeRotation,
            out var offsetX, out var offsetY, out var hasRoundedRectByte,
            out var perLayerShapes, out var perLayerCornerRadii, out var hasSizeShapeBlock,
            out var fullStackEntries);

        // Build the pad model
        var pad = PcbPad.Create()
            .At(location.X, location.Y)
            .Size(sizeTop.X, sizeTop.Y)
            .Shape((PadShape)shapeTop)
            .HoleSize(holeSize)
            .Plated(isPlated)
            .Rotation(rotation)
            .WithDesignator(designator)
            .Layer(layer)
            .Build();

        pad.ComponentIndex = componentIndex;
        pad.Sr5Length = sanitizedSize;
        pad.SizeMiddle = sizeMiddle;
        pad.SizeBottom = sizeBottom;
        pad.ShapeMiddle = (PadShape)shapeMiddle;
        pad.ShapeBottom = (PadShape)shapeBottom;
        pad.PasteMaskExpansion = Coord.FromRaw(pasteMaskExpansion);
        pad.SolderMaskExpansion = Coord.FromRaw(solderMaskExpansion);
        pad.PasteMaskExpansionMode = pasteMaskMode;
        pad.SolderMaskExpansionMode = solderMaskMode;
        pad.HolePositiveTolerance = Coord.FromRaw(holePositiveTolerance);
        pad.HoleNegativeTolerance = Coord.FromRaw(holeNegativeTolerance);
        pad.Mode = stackMode;
        pad.JumperID = jumperId;

        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        pad.IsLocked = isLocked;
        pad.IsTentingTop = isTentingTop;
        pad.IsTentingBottom = isTentingBottom;
        pad.IsKeepout = isKeepout;

        pad.PowerPlaneConnectStyle = powerPlaneConnectStyle;
        pad.ReliefAirGap = Coord.FromRaw(reliefAirGap);
        pad.ReliefConductorWidth = Coord.FromRaw(reliefConductorWidth);
        pad.ReliefEntries = reliefEntries;
        pad.PowerPlaneClearance = Coord.FromRaw(powerPlaneClearance);
        pad.PowerPlaneReliefExpansion = Coord.FromRaw(powerPlaneReliefExpansion);

        pad.LayerXSizes = layerXSizes;
        pad.LayerYSizes = layerYSizes;
        pad.InternalLayerShapes = internalLayerShapes;
        pad.HoleType = (PadHoleType)holeShapeByte;
        pad.HoleSlotLength = holeSlotLength;
        pad.HoleRotation = holeRotation;
        pad.OffsetXFromHoleCenter = offsetX;
        pad.OffsetYFromHoleCenter = offsetY;
        pad.HasRoundedRectByte = hasRoundedRectByte;
        pad.PerLayerShapes = perLayerShapes;
        pad.PerLayerCornerRadii = perLayerCornerRadii;
        pad.HasSizeShapeBlock = hasSizeShapeBlock;
        pad.FullStackEntries.AddRange(fullStackEntries);
        return pad;
    }

    // Pad SubRecord-5 extended tail (offsets relative to the start of the SubRecord).
    // Layout verified against the Altium binary format: the thermal-relief / mask /
    // tolerance fields live at fixed offsets, interleaved with reserved and pad-cache bytes.
    private const int PadExtendedStart = 61;             // first byte after the basic geometry
    private const int PadHolePositiveToleranceOffset = 162;
    private const int PadHoleNegativeToleranceOffset = 166;

    private static void ReadPadExtendedFields(BinaryFormatReader reader, long startPos, long blockSize,
        out int stackMode, out byte powerPlaneConnectStyle,
        out int reliefAirGap, out int reliefConductorWidth, out short reliefEntries,
        out int powerPlaneClearance, out int powerPlaneReliefExpansion,
        out int pasteMaskExpansion, out int solderMaskExpansion,
        out byte pasteMaskMode, out byte solderMaskMode,
        out short jumperId,
        out int holePositiveTolerance, out int holeNegativeTolerance)
    {
        stackMode = 0;
        powerPlaneConnectStyle = 0;
        reliefAirGap = 0; reliefConductorWidth = 0; reliefEntries = 4;
        powerPlaneClearance = 0; powerPlaneReliefExpansion = 0;
        pasteMaskExpansion = 0; solderMaskExpansion = 0;
        pasteMaskMode = 1; solderMaskMode = 1; // 1 = From rule
        jumperId = 0;
        holePositiveTolerance = int.MaxValue; // 0x7FFFFFFF = unset
        holeNegativeTolerance = int.MaxValue;

        // We are positioned at offset 61 (start of the extended tail). Read the rest of the
        // SubRecord into a buffer and index it by absolute offset; this also consumes the
        // remainder so the SubRecord-6 read that follows stays aligned.
        var consumed = (int)(reader.Position - startPos);
        var tailLength = (int)(blockSize - consumed);
        if (tailLength <= 0)
            return;
        var tail = reader.ReadBytes(tailLength);

        bool Has(int offset, int width) => offset >= PadExtendedStart
            && offset - PadExtendedStart + width <= tail.Length;
        byte B(int offset, byte fallback) => Has(offset, 1) ? tail[offset - PadExtendedStart] : fallback;
        short I16(int offset, short fallback) => Has(offset, 2)
            ? (short)(tail[offset - PadExtendedStart] | (tail[offset - PadExtendedStart + 1] << 8)) : fallback;
        int I32(int offset, int fallback) => Has(offset, 4)
            ? BitConverter.ToInt32(tail, offset - PadExtendedStart) : fallback;

        stackMode = B(62, 0);                              // 62: pad stack mode
        // 63-66: reserved
        powerPlaneConnectStyle = B(67, 0);                 // 67: plane connection style
        reliefConductorWidth = I32(68, 0);                 // 68-71
        reliefEntries = I16(72, 4);                        // 72-73
        reliefAirGap = I32(74, 0);                         // 74-77
        powerPlaneReliefExpansion = I32(78, 0);            // 78-81
        powerPlaneClearance = I32(82, 0);                  // 82-85
        pasteMaskExpansion = I32(86, 0);                   // 86-89 (manual paste expansion)
        solderMaskExpansion = I32(90, 0);                  // 90-93 (manual solder expansion)
        // 94-100: pad-cache validity bytes (regenerated from template on write)
        pasteMaskMode = B(101, 1);                         // 101: 0=None,1=Rule,2=Manual
        solderMaskMode = B(102, 1);                        // 102
        // 103-105: cache validity + gap; 106-109: user union; 110-111: jumper; 114-117: save id
        jumperId = I16(110, 0);                            // 110-111
        holePositiveTolerance = I32(PadHolePositiveToleranceOffset, int.MaxValue); // 162-165
        holeNegativeTolerance = I32(PadHoleNegativeToleranceOffset, int.MaxValue); // 166-169
    }

    private static void ReadPadSizeShapeBlock(BinaryFormatReader reader, int stackMode,
        ref byte shapeTop, ref byte shapeMiddle, ref byte shapeBottom,
        out int[] layerXSizes, out int[] layerYSizes, out byte[] internalLayerShapes,
        out byte holeShapeByte, out int holeSlotLength, out double holeRotation,
        out int[] offsetX, out int[] offsetY, out byte hasRoundedRectByte,
        out byte[] perLayerShapes, out byte[] perLayerCornerRadii, out bool hasSizeShapeBlock,
        out List<PadFullStackEntry> fullStackEntries)
    {
        fullStackEntries = new List<PadFullStackEntry>();
        var sizeShapeBlockSize = reader.ReadInt32();
        var sanitizedSize = sizeShapeBlockSize & 0x00FFFFFF;
        hasSizeShapeBlock = sanitizedSize > 0;

        layerXSizes = new int[29];
        layerYSizes = new int[29];
        internalLayerShapes = new byte[29];
        holeShapeByte = 0;
        holeSlotLength = 0;
        holeRotation = 0;
        offsetX = new int[32];
        offsetY = new int[32];
        hasRoundedRectByte = 0;
        perLayerShapes = new byte[32];
        perLayerCornerRadii = new byte[32];

        if (sanitizedSize >= 596)
        {
            var ssStartPos = reader.Position;

            for (var i = 0; i < 29; i++) layerXSizes[i] = reader.ReadInt32();
            for (var i = 0; i < 29; i++) layerYSizes[i] = reader.ReadInt32();
            for (var i = 0; i < 29; i++) internalLayerShapes[i] = reader.ReadByte();
            reader.Skip(1); // reserved byte
            holeShapeByte = reader.ReadByte();
            holeSlotLength = reader.ReadInt32();
            holeRotation = reader.ReadDouble();
            for (var i = 0; i < 32; i++) offsetX[i] = reader.ReadInt32();
            for (var i = 0; i < 32; i++) offsetY[i] = reader.ReadInt32();
            hasRoundedRectByte = reader.ReadByte();
            for (var i = 0; i < 32; i++) perLayerShapes[i] = reader.ReadByte();
            for (var i = 0; i < 32; i++) perLayerCornerRadii[i] = reader.ReadByte();

            var ssRemaining = sanitizedSize - (reader.Position - ssStartPos);
            // Full-stack tail: [32 reserved][u32 count][u32 stride][count x stride-byte entries].
            if (ssRemaining >= 40)
            {
                reader.Skip(32); // reserved (zeros)
                var count = reader.ReadInt32();
                var stride = reader.ReadInt32();
                for (var i = 0; i < count; i++)
                {
                    if (stride < 15 || reader.Position - ssStartPos + stride > sanitizedSize)
                        break;
                    fullStackEntries.Add(new PadFullStackEntry
                    {
                        LayerCode = reader.ReadByte(),
                        Flag1 = reader.ReadByte(),
                        Flag2 = reader.ReadByte(),
                        Flag3 = reader.ReadByte(),
                        Flag4 = reader.ReadByte(),
                        SizeX = reader.ReadInt32(),
                        SizeY = reader.ReadInt32(),
                        CornerPercent = reader.ReadByte(),
                        Trailing = reader.ReadByte(),
                    });
                    if (stride > 15)
                        reader.Skip(stride - 15);
                }
                var ssRemaining2 = sanitizedSize - (reader.Position - ssStartPos);
                if (ssRemaining2 > 0)
                    reader.Skip((int)ssRemaining2);
            }
            else if (ssRemaining > 0)
            {
                reader.Skip((int)ssRemaining);
            }
        }
        else if (sanitizedSize > 0)
        {
            reader.Skip(sanitizedSize);
        }

        // Apply per-layer shape overrides when hasRoundedRect is set
        if (hasRoundedRectByte != 0)
        {
            shapeTop = perLayerShapes[0];
            shapeMiddle = perLayerShapes[1];
            shapeBottom = stackMode == 0 ? shapeTop : perLayerShapes[31];
        }
    }

    internal static PcbVia? ReadVia(BinaryFormatReader reader)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        // Read the entire SubRecord 1 into a buffer and parse by absolute offset.
        var sr1 = reader.ReadBytes(sanitizedSize);

        bool Has(int off, int width) => off >= 0 && off + width <= sr1.Length;
        byte B(int off) => Has(off, 1) ? sr1[off] : (byte)0;
        int I32(int off, int dflt = 0) => Has(off, 4) ? BitConverter.ToInt32(sr1, off) : dflt;

        var layer = B(0);
        var flags = (ushort)(B(1) | (B(2) << 8));

        var via = PcbVia.Create()
            .At(Coord.FromRaw(I32(13)), Coord.FromRaw(I32(17)))
            .Diameter(Coord.FromRaw(I32(21)))
            .HoleSize(Coord.FromRaw(I32(25)))
            .Layers(B(29), B(30))
            .Build();

        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        via.IsLocked = isLocked;
        via.Layer = layer;
        via.IsKeepout = isKeepout;
        via.IsTentingTop = isTentingTop;
        via.IsTentingBottom = isTentingBottom;

        via.PowerPlaneConnectStyle = B(31);              // 31
        via.ThermalReliefAirGap = Coord.FromRaw(I32(32)); // 32-35
        via.ThermalReliefConductors = B(36);             // 36
        via.ThermalReliefConductorsWidth = Coord.FromRaw(I32(38)); // 38-41
        via.PowerPlaneReliefExpansion = Coord.FromRaw(I32(42));    // 42-45
        via.PowerPlaneClearance = Coord.FromRaw(I32(46));          // 46-49
        via.PasteMaskExpansion = Coord.FromRaw(I32(50));           // 50-53
        via.SolderMaskExpansion = Coord.FromRaw(I32(54));          // 54-57 (front)
        var solderMaskMode = B(66);                                // 66
        via.SolderMaskExpansionMode = solderMaskMode;
        via.SolderMaskExpansionManual = solderMaskMode == 2;
        via.Mode = B(74);                                          // 74
        for (var i = 0; i < 32; i++)
            via.Diameters[i] = Coord.FromRaw(I32(75 + i * 4));     // 75-202
        via.SolderMaskExpansionFromHoleEdge = B(258) != 0;        // 258
        via.HolePositiveTolerance = Coord.FromRaw(I32(291, int.MaxValue)); // 291-294
        via.HoleNegativeTolerance = Coord.FromRaw(I32(295, int.MaxValue)); // 295-298
        via.DrillLayerPairType = B(312);                          // 312

        return via;
    }

    internal static PcbTrack? ReadTrack(BinaryFormatReader reader)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        var sr = reader.ReadBytes(sanitizedSize);
        bool Has(int off, int width) => off >= 0 && off + width <= sr.Length;
        byte B(int off) => Has(off, 1) ? sr[off] : (byte)0;
        int I32(int off) => Has(off, 4) ? BitConverter.ToInt32(sr, off) : 0;

        var layer = B(0);
        var flags = (ushort)(B(1) | (B(2) << 8));

        var track = PcbTrack.Create()
            .From(Coord.FromRaw(I32(13)), Coord.FromRaw(I32(17)))
            .To(Coord.FromRaw(I32(21)), Coord.FromRaw(I32(25)))
            .Width(Coord.FromRaw(I32(29)))
            .Layer(layer)
            .Build();

        track.SolderMaskExpansion = Coord.FromRaw(I32(35)); // 35-38
        track.KeepoutRestrictions = B(45);                  // 45

        // Decode flags
        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        track.IsLocked = isLocked;
        track.IsTentingTop = isTentingTop;
        track.IsTentingBottom = isTentingBottom;
        track.IsKeepout = isKeepout;

        return track;
    }

    internal static PcbText? ReadText(BinaryFormatReader reader, List<string> wideStrings)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        // Read the entire SubRecord 1 into a buffer and parse by absolute offset
        // (the Altium text layout is fixed: geometry, font, text-box, barcode block, tail).
        var sr1 = reader.ReadBytes(sanitizedSize);

        bool Has(int off, int width) => off >= 0 && off + width <= sr1.Length;
        byte B(int off) => Has(off, 1) ? sr1[off] : (byte)0;
        short I16(int off) => Has(off, 2) ? (short)(sr1[off] | (sr1[off + 1] << 8)) : (short)0;
        int I32(int off) => Has(off, 4) ? BitConverter.ToInt32(sr1, off) : 0;
        double Dbl(int off) => Has(off, 8) ? BitConverter.ToDouble(sr1, off) : 0.0;
        string Utf16(int off, int byteLen)
        {
            if (!Has(off, byteLen)) return string.Empty;
            var s = System.Text.Encoding.Unicode.GetString(sr1, off, byteLen);
            var nul = s.IndexOf('\0');
            return nul >= 0 ? s.Substring(0, nul) : s;
        }

        var layer = B(0);
        var flags = (ushort)(B(1) | (B(2) << 8));
        var x = I32(13);
        var y = I32(17);
        var height = I32(21);
        var strokeFont = I16(25);
        var rotation = Dbl(27);
        var mirrored = B(35) != 0;
        var strokeWidth = I32(36);
        var isComment = B(40) != 0;
        var isDesignator = B(41) != 0;
        var charSet = B(42);
        var fontTypeAt43 = B(43);
        var fontBold = B(44) != 0;
        var fontItalic = B(45) != 0;
        var fontName = Utf16(46, 64);
        var isInverted = B(110) != 0;
        var marginBorderWidth = I32(111);
        var wideStringIndex = I32(115);
        var textUnionIndex = I32(119);
        var useInvertedRect = B(123) != 0;
        var textboxWidth = I32(124);
        var textboxHeight = I32(128);
        var textboxJustification = B(132);
        var textOffsetWidth = I32(133);
        // Barcode block (offsets 137-229)
        var bcFullWidth = I32(137);
        var bcFullHeight = I32(141);
        var bcXMargin = I32(145);
        var bcYMargin = I32(149);
        var bcMinWidth = I32(153);
        var bcKind = B(157);
        var bcRenderMode = B(158);
        var bcInverted = B(159) != 0;
        var bcFontType = B(160); // bc[23]: authoritative text kind when barcode block present
        var bcFontName = Utf16(161, 64);
        var bcShowText = B(225) != 0;
        // Frame tail (offsets 230-251)
        var isFrame = B(230) != 0;
        var isOffsetBorder = B(231) != 0;
        var isJustificationValid = B(240) != 0;
        var advanceSnapping = B(241) != 0;
        var snapPointX = I32(244);
        var snapPointY = I32(248);

        // Read ASCII text (SubRecord 2)
        var asciiText = reader.ReadStringBlock();
        var text = asciiText;
        if (wideStringIndex >= 0 && wideStringIndex < wideStrings.Count)
            text = wideStrings[wideStringIndex];
        if (string.IsNullOrEmpty(text))
            return null;

        // The barcode block's font-type byte (bc[23]) overrides the offset-43 value when present.
        var textKind = (PcbTextKind)(Has(160, 1) ? bcFontType : fontTypeAt43);

        var result = PcbText.Create(text)
            .At(Coord.FromRaw(x), Coord.FromRaw(y))
            .Height(Coord.FromRaw(height))
            .StrokeWidth(Coord.FromRaw(strokeWidth))
            .Rotation(rotation)
            .Mirrored(mirrored)
            .Layer(layer)
            .Build();

        result.StrokeFont = (PcbStrokeFont)strokeFont;
        result.TextKind = textKind;
        result.IsTrueType = textKind == PcbTextKind.TrueType;
        result.IsComment = isComment;
        result.IsDesignator = isDesignator;
        result.CharSet = charSet;
        result.FontBold = fontBold;
        result.FontItalic = fontItalic;
        result.FontName = fontName;
        result.IsInverted = isInverted;
        result.InvertedBorder = Coord.FromRaw(marginBorderWidth);
        result.WideStringIndex = wideStringIndex;
        result.UnionIndex = textUnionIndex;
        result.UseInvertedRectangle = useInvertedRect;
        result.InvertedRectWidth = Coord.FromRaw(textboxWidth);
        result.InvertedRectHeight = Coord.FromRaw(textboxHeight);
        result.InvertedRectJustification = (TextJustification)textboxJustification;
        result.InvertedRectTextOffset = Coord.FromRaw(textOffsetWidth);
        result.BarCodeFullWidth = Coord.FromRaw(bcFullWidth);
        result.BarCodeFullHeight = Coord.FromRaw(bcFullHeight);
        result.BarCodeXMargin = Coord.FromRaw(bcXMargin);
        result.BarCodeYMargin = Coord.FromRaw(bcYMargin);
        result.BarCodeMinWidth = Coord.FromRaw(bcMinWidth);
        result.BarCodeKind = bcKind;
        result.BarCodeRenderMode = bcRenderMode;
        result.BarCodeInverted = bcInverted;
        result.BarCodeFontName = bcFontName;
        result.BarCodeShowText = bcShowText;
        result.IsFrame = isFrame;
        result.IsOffsetBorder = isOffsetBorder;
        result.IsJustificationValid = isJustificationValid;
        result.AdvanceSnapping = advanceSnapping;
        result.SnapPointX = Coord.FromRaw(snapPointX);
        result.SnapPointY = Coord.FromRaw(snapPointY);

        // Decode flags
        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        result.IsLocked = isLocked;
        result.IsTentingTop = isTentingTop;
        result.IsTentingBottom = isTentingBottom;
        result.IsKeepout = isKeepout;

        return result;
    }

    internal static PcbFill? ReadFill(BinaryFormatReader reader)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        var sr = reader.ReadBytes(sanitizedSize);
        bool Has(int off, int width) => off >= 0 && off + width <= sr.Length;
        byte B(int off) => Has(off, 1) ? sr[off] : (byte)0;
        int I32(int off) => Has(off, 4) ? BitConverter.ToInt32(sr, off) : 0;
        double Dbl(int off) => Has(off, 8) ? BitConverter.ToDouble(sr, off) : 0.0;

        var layer = B(0);
        var flags = (ushort)(B(1) | (B(2) << 8));

        var fill = PcbFill.Create()
            .From(Coord.FromRaw(I32(13)), Coord.FromRaw(I32(17)))
            .To(Coord.FromRaw(I32(21)), Coord.FromRaw(I32(25)))
            .Rotation(Dbl(29))
            .OnLayer(layer)
            .Build();

        fill.SolderMaskExpansion = Coord.FromRaw(I32(37)); // 37-40
        fill.KeepoutRestrictions = B(46);                  // 46

        // Decode flags
        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        fill.IsLocked = isLocked;
        fill.IsTentingTop = isTentingTop;
        fill.IsTentingBottom = isTentingBottom;
        fill.IsKeepout = isKeepout;

        return fill;
    }

    internal static PcbRegion? ReadRegion(BinaryFormatReader reader)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        var startPos = reader.Position;

        ReadCommonPrimitiveData(reader, out var layer, out var flags);

        // Header: reserved byte @13 + hole_count uint16 @14-15 + 2 reserved bytes @16-17,
        // then the nested parameter block and the geometry.
        reader.Skip(1);
        var holeCount = reader.ReadUInt16();
        reader.Skip(2);

        // Read nested C-string parameter block (capture ordered form for faithful round-trip)
        var parameters = ReadParameterBlock(reader, out var rawRegionParams);
        var orderedRegionParams = ParseParametersOrdered(rawRegionParams);

        // Read outline vertices (stored as 16-byte x,y doubles in Altium format)
        var vertexCount = reader.ReadUInt32();
        var kind = 0;
        if (parameters.TryGetValue("KIND", out var kindStr))
            int.TryParse(kindStr, out kind);

        var region = PcbRegion.Create()
            .OnLayer(layer)
            .Kind(kind);

        for (var i = 0; i < vertexCount; i++)
        {
            var x = Coord.FromRaw((int)reader.ReadDouble());
            var y = Coord.FromRaw((int)reader.ReadDouble());
            region.AddPoint(x, y);
        }

        // Read hole / cutout contours: [uint32 count][count x,y doubles] per hole.
        var holes = new List<List<CoordPoint>>(holeCount);
        for (var h = 0; h < holeCount; h++)
        {
            if (reader.Position - startPos + 4 > sanitizedSize)
                break;
            var holeVertexCount = reader.ReadUInt32();
            if (reader.Position - startPos + (long)holeVertexCount * 16 > sanitizedSize)
                break;
            var hole = new List<CoordPoint>((int)holeVertexCount);
            for (var i = 0; i < holeVertexCount; i++)
            {
                var hx = Coord.FromRaw((int)reader.ReadDouble());
                var hy = Coord.FromRaw((int)reader.ReadDouble());
                hole.Add(new CoordPoint(hx, hy));
            }
            holes.Add(hole);
        }

        // Skip trailing data
        var consumed = reader.Position - startPos;
        var remaining = sanitizedSize - consumed;
        if (remaining > 0)
            reader.Skip((int)remaining);

        var result = region.Build();
        result.RawParametersOrdered = orderedRegionParams;
        result.Holes = holes;

        // Decode flags
        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        result.IsLocked = isLocked;
        result.IsTentingTop = isTentingTop;
        result.IsTentingBottom = isTentingBottom;
        result.IsKeepout = isKeepout;

        // Extract typed properties from parameter block
        if (parameters.TryGetValue("NET", out var net))
            result.Net = net;
        if (parameters.TryGetValue("UNIQUEID", out var uid))
            result.UniqueId = uid;
        if (parameters.TryGetValue("NAME", out var name))
            result.Name = name;

        // Preserve any additional parameters not modeled as typed properties
        result.AdditionalParameters = ExtractAdditionalParameters(parameters,
            ["KIND", "NET", "UNIQUEID", "NAME"]);

        return result;
    }

    internal static PcbComponentBody? ReadComponentBody(BinaryFormatReader reader)
    {
        var size = reader.ReadInt32();
        var sanitizedSize = size & 0x00FFFFFF;

        if (sanitizedSize <= 0)
            return null;

        var startPos = reader.Position;

        ReadCommonPrimitiveData(reader, out var layer, out var flags);

        // Structure: uint32 prefix + byte prefix + nested parameter block + outline vertices
        reader.Skip(4); // reserved uint32 prefix
        reader.Skip(1); // reserved byte prefix

        // Read nested C-string parameter block (contains 3D model references etc.). Capture the
        // ordered form for faithful round-trip (the block has duplicate keys and mil-formatted values).
        var parameters = ReadParameterBlock(reader, out var rawBodyParams);
        var orderedBodyParams = ParseParametersOrdered(rawBodyParams);

        // Read outline vertices (stored as doubles in Altium format)
        var vertexCount = reader.ReadUInt32();
        var body = PcbComponentBody.Create();

        for (var i = 0; i < vertexCount; i++)
        {
            var x = Coord.FromRaw((int)reader.ReadDouble());
            var y = Coord.FromRaw((int)reader.ReadDouble());
            body.AddPoint(x, y);
        }

        // Skip trailing data
        var consumed = reader.Position - startPos;
        var remaining = sanitizedSize - consumed;
        if (remaining > 0)
            reader.Skip((int)remaining);

        var result = body.Build();
        result.RawParametersOrdered = orderedBodyParams;

        // Decode flags
        PcbBinaryConstants.DecodeFlags(flags, out var isLocked, out var isTentingTop, out var isTentingBottom, out var isKeepout);
        result.IsLocked = isLocked;
        result.IsTentingTop = isTentingTop;
        result.IsTentingBottom = isTentingBottom;
        result.IsKeepout = isKeepout;

        // Extract typed properties from parameter block
        if (parameters.TryGetValue("V7_LAYER", out var v7Layer))
            result.LayerName = v7Layer;
        if (parameters.TryGetValue("NAME", out var name))
            result.Name = name;
        if (parameters.TryGetValue("KIND", out var kindStr) && int.TryParse(kindStr, out var kind))
            result.Kind = kind;
        if (parameters.TryGetValue("SUBPOLYINDEX", out var subPoly) && int.TryParse(subPoly, out var subPolyVal))
            result.SubPolyIndex = subPolyVal;
        if (parameters.TryGetValue("UNIONINDEX", out var unionIdx) && int.TryParse(unionIdx, out var unionIdxVal))
            result.UnionIndex = unionIdxVal;
        if (parameters.TryGetValue("ARCRESOLUTION", out var arcRes) && double.TryParse(arcRes, System.Globalization.CultureInfo.InvariantCulture, out var arcResVal))
            result.ArcResolution = arcResVal;
        if (parameters.TryGetValue("ISSHAPEBASED", out var isShapeBased))
            result.IsShapeBased = string.Equals(isShapeBased, "TRUE", StringComparison.OrdinalIgnoreCase);
        if (parameters.TryGetValue("CAVITYHEIGHT", out var cavHeight) && int.TryParse(cavHeight, out var cavHeightVal))
            result.CavityHeight = Coord.FromRaw(cavHeightVal);
        if (parameters.TryGetValue("STANDOFFHEIGHT", out var standoff) && int.TryParse(standoff, out var standoffVal))
            result.StandoffHeight = Coord.FromRaw(standoffVal);
        if (parameters.TryGetValue("OVERALLHEIGHT", out var overall) && int.TryParse(overall, out var overallVal))
            result.OverallHeight = Coord.FromRaw(overallVal);
        if (parameters.TryGetValue("BODYCOLOR3D", out var bodyColor) && int.TryParse(bodyColor, out var bodyColorVal))
            result.BodyColor3D = bodyColorVal;
        if (parameters.TryGetValue("BODYOPACITY3D", out var opacity) && double.TryParse(opacity, System.Globalization.CultureInfo.InvariantCulture, out var opacityVal))
            result.BodyOpacity3D = opacityVal;
        if (parameters.TryGetValue("MODELID", out var modelId))
            result.ModelId = modelId;
        if (parameters.TryGetValue("MODEL.EMBED", out var modelEmbed))
            result.ModelEmbed = string.Equals(modelEmbed, "TRUE", StringComparison.OrdinalIgnoreCase);
        if (parameters.TryGetValue("MODEL.2D.X", out var m2dx) && int.TryParse(m2dx, out var m2dxVal))
            result.Model2DLocation = new CoordPoint(Coord.FromRaw(m2dxVal),
                parameters.TryGetValue("MODEL.2D.Y", out var m2dy) && int.TryParse(m2dy, out var m2dyVal)
                    ? Coord.FromRaw(m2dyVal) : Coord.FromRaw(0));
        if (parameters.TryGetValue("MODEL.2D.ROTATION", out var m2dRot) && double.TryParse(m2dRot, System.Globalization.CultureInfo.InvariantCulture, out var m2dRotVal))
            result.Model2DRotation = m2dRotVal;
        if (parameters.TryGetValue("MODEL.3D.ROTX", out var m3dRotX) && double.TryParse(m3dRotX, System.Globalization.CultureInfo.InvariantCulture, out var m3dRotXVal))
            result.Model3DRotX = m3dRotXVal;
        if (parameters.TryGetValue("MODEL.3D.ROTY", out var m3dRotY) && double.TryParse(m3dRotY, System.Globalization.CultureInfo.InvariantCulture, out var m3dRotYVal))
            result.Model3DRotY = m3dRotYVal;
        if (parameters.TryGetValue("MODEL.3D.ROTZ", out var m3dRotZ) && double.TryParse(m3dRotZ, System.Globalization.CultureInfo.InvariantCulture, out var m3dRotZVal))
            result.Model3DRotZ = m3dRotZVal;
        if (parameters.TryGetValue("MODEL.3D.DZ", out var m3dDz) && int.TryParse(m3dDz, out var m3dDzVal))
            result.Model3DDz = Coord.FromRaw(m3dDzVal);
        if (parameters.TryGetValue("MODEL.CHECKSUM", out var modelCs) && int.TryParse(modelCs, out var modelCsVal))
            result.ModelChecksum = modelCsVal;
        if (parameters.TryGetValue("MODEL.NAME", out var modelName))
            result.ModelName = modelName;
        if (parameters.TryGetValue("MODEL.MODELTYPE", out var modelType) && int.TryParse(modelType, out var modelTypeVal))
            result.ModelType = modelTypeVal;
        if (parameters.TryGetValue("MODEL.MODELSOURCE", out var modelSource))
            result.ModelSource = modelSource;
        if (parameters.TryGetValue("BODYPROJECTION", out var bodyProj) && int.TryParse(bodyProj, out var bodyProjVal))
            result.BodyProjection = bodyProjVal;
        if (parameters.TryGetValue("IDENTIFIER", out var identifier))
            result.Identifier = identifier;
        if (parameters.TryGetValue("TEXTURE", out var texture))
            result.Texture = texture;

        // Preserve any additional parameters not modeled as typed properties
        result.AdditionalParameters = ExtractAdditionalParameters(parameters,
        [
            "V7_LAYER", "NAME", "KIND", "SUBPOLYINDEX", "UNIONINDEX", "ARCRESOLUTION",
            "ISSHAPEBASED", "CAVITYHEIGHT", "STANDOFFHEIGHT", "OVERALLHEIGHT",
            "BODYCOLOR3D", "BODYOPACITY3D", "BODYPROJECTION",
            "MODELID", "MODEL.EMBED", "MODEL.2D.X", "MODEL.2D.Y", "MODEL.2D.ROTATION",
            "MODEL.3D.ROTX", "MODEL.3D.ROTY", "MODEL.3D.ROTZ", "MODEL.3D.DZ",
            "MODEL.CHECKSUM", "MODEL.NAME", "MODEL.MODELTYPE", "MODEL.MODELSOURCE",
            "IDENTIFIER", "TEXTURE"
        ]);

        return result;
    }
}

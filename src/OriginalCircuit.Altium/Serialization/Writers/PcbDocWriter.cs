using System.Globalization;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Binary;
using OriginalCircuit.Altium.Serialization.Compound;

namespace OriginalCircuit.Altium.Serialization.Writers;

/// <summary>
/// Writes PCB document (.PcbDoc) files.
/// PcbDoc files store primitives in separate storages per type
/// (e.g., [Arcs6], [Pads6], [Tracks6]).
/// </summary>
public sealed class PcbDocWriter
{
    /// <summary>
    /// The fixed 24-byte legacy <c>FileHeader</c> stamp every PcbDoc carries: <c>uint32</c> char-count
    /// 19 followed by the truncated UTF-16LE text <c>"PCB 5.0 Bi"</c> (docs/decompile/fileheaders.md §3).
    /// Entirely constant — no per-document data.
    /// </summary>
    private static readonly byte[] LegacyFileHeaderStamp = BuildLegacyFileHeaderStamp();

    private static byte[] BuildLegacyFileHeaderStamp()
    {
        var payload = System.Text.Encoding.Unicode.GetBytes("PCB 5.0 Bi"); // 20 bytes UTF-16LE
        var stamp = new byte[4 + payload.Length];
        BitConverter.TryWriteBytes(stamp, 19);                              // char-count = 19 (constant)
        payload.CopyTo(stamp, 4);
        return stamp;
    }

    /// <summary>
    /// Writes a PcbDoc file to the specified path.
    /// </summary>
    /// <param name="document">The PCB document to write.</param>
    /// <param name="path">Destination file path.</param>
    /// <param name="overwrite">If true, overwrites an existing file; otherwise throws if the file exists.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>This instance is stateless and thread-safe.</remarks>
    public async ValueTask WriteAsync(PcbDocument document, string path, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await WriteAsync(document, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a PcbDoc file to a stream.
    /// </summary>
    /// <param name="document">The PCB document to write.</param>
    /// <param name="stream">Destination stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>This instance is stateless and thread-safe.</remarks>
    public async ValueTask WriteAsync(PcbDocument document, Stream stream, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        Write(document, ms, cancellationToken);
        ms.Position = 0;
        await ms.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a PcbDoc file to a stream synchronously.
    /// </summary>
    /// <param name="document">The PCB document to write.</param>
    /// <param name="stream">Destination stream.</param>
    /// <remarks>This instance is stateless and thread-safe.</remarks>
    public void Write(PcbDocument document, Stream stream, CancellationToken cancellationToken = default)
    {
        using var cf = CompoundFileAccessor.Create();

        WriteFileHeader(cf, document);
        WriteFileHeaderSix(cf, document);
        WriteBoard(cf, document);
        WriteNets(cf, document);
        cancellationToken.ThrowIfCancellationRequested();
        WriteArcs(cf, document);
        WritePads(cf, document);
        WriteVias(cf, document);
        WriteTracks(cf, document);
        cancellationToken.ThrowIfCancellationRequested();
        WriteTexts(cf, document);
        WriteFills(cf, document);
        WriteRegions(cf, document);
        WriteBoardRegions(cf, document);
        WriteShapeBased(cf, document, "ShapeBasedRegions6", document.ShapeBasedRegions);
        WriteShapeBased(cf, document, "ShapeBasedComponentBodies6", document.ShapeBasedComponentBodies);
        WriteComponentBodies(cf, document);
        cancellationToken.ThrowIfCancellationRequested();
        WritePolygons(cf, document);
        WriteComponents(cf, document);
        WriteEmbeddedBoards(cf, document);
        WriteRules(cf, document);
        WriteClasses(cf, document);
        WriteSignalClasses(cf, document);
        WriteSmartUnions(cf, document);
        WriteUnionNames(cf, document);
        WriteDifferentialPairs(cf, document);
        WriteRooms(cf, document);
        WriteWideStrings(cf, document);
        // Empty optional feature storages (no instances in the corpus) reproduced exactly: a
        // [u32 count=0] Header + empty Data. Removing them from the AdditionalStreams catch-all.
        WriteEmptyStorageIfPresent(cf, document, "Dimensions6");
        WriteEmptyStorageIfPresent(cf, document, "Coordinates6");
        WriteEmptyStorageIfPresent(cf, document, "FromTos6");
        WriteEmptyStorageIfPresent(cf, document, "Embeddeds6");
        WriteEmptyStorageIfPresent(cf, document, "Textures");
        WriteEmptyStorageIfPresent(cf, document, "ModelsNoEmbed");
        WriteDocumentPrimitiveGuids(cf, document);
        WriteDocumentPrimitiveUniqueIds(cf, document);
        PcbLibWriter.WriteFileVersionInfo(cf.RootStorage, document.FileVersionInfo);
        if (document.LayerKindMapping is { } lkm) PcbLibWriter.WriteLayerKindMapping(cf.RootStorage, lkm);
        if (document.PadViaLibrary is { } pvl) PcbLibWriter.WritePadViaLibrary(cf.RootStorage, pvl, "PadViaLibrary");
        WriteEmptyStorageIfPresent(cf, document, "PadViaLibraryLinks");
        WriteAdditionalStreams(cf, document);

        cf.Save(stream);
    }

    private static void WriteFileHeader(CompoundFileAccessor cf, PcbDocument document)
    {
        var headerStream = cf.RootStorage.AddStream("FileHeader");

        // Fully modeled — no replay. The legacy PcbDoc FileHeader is a fixed 24-byte stamp
        // (docs/decompile/fileheaders.md §3): a uint32 char-count of 19 followed by the truncated
        // UTF-16LE text "PCB 5.0 Bi" (20 bytes). It is entirely constant — no per-document data — so
        // we emit the exact bytes. (The real 6.0 version stamp + per-document GUID live in
        // FileHeaderSix; see WriteFileHeaderSix.)
        headerStream.SetData(LegacyFileHeaderStamp);
    }

    private static void WriteFileHeaderSix(CompoundFileAccessor cf, PcbDocument document)
    {
        // The modern 6.0 version stamp + per-document GUID (docs/decompile/fileheaders.md §4). Fully
        // modeled — no replay. 75-byte two-block layout: version text + 5.01 double (both Reserved
        // constants) and the document GUID (Identity) as a brace-wrapped uppercase token. A loaded
        // document without a FileHeaderSix stream leaves FileGuid null and emits nothing.
        if (document.FileGuid is not Guid guid)
            return;

        var headerStream = cf.RootStorage.AddStream("FileHeaderSix");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        const string versionText = "PCB 6.0 Binary File";   // Reserved constant
        writer.Write(versionText.Length);
        writer.WritePascalShortString(versionText);

        writer.Write(5.01d);                                 // Reserved constant (no length prefix)

        var guidText = "{" + guid.ToString("D").ToUpperInvariant() + "}";   // Identity (38 chars)
        writer.Write(guidText.Length);
        writer.WritePascalShortString(guidText);

        writer.Flush();
        headerStream.SetData(ms.ToArray());
    }

    private static void WriteBoard(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.BoardParameters == null || document.BoardParameters.Count == 0)
            return;

        var storage = cf.RootStorage.AddStorage("Board6");
        PcbLibWriter.WriteStorageHeader(storage, 1); // Board6 always holds exactly one board record

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        if (document.BoardParametersOrdered is { Count: > 0 } ordered)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in ordered)
                sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
            writer.WriteCStringParameterBlockRaw(sb.ToString());
        }
        else
        {
            writer.WriteCStringParameterBlock(document.BoardParameters);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    /// <summary>
    /// Writes an empty Header(count=0) + empty Data storage when the named storage existed in the
    /// source file but its collection is now empty, so a present-but-empty storage round-trips.
    /// </summary>
    private static void WriteEmptyStorageIfPresent(CompoundFileAccessor cf, PcbDocument document, string name)
    {
        if (!document.PresentStorages.Contains(name))
            return;
        var storage = cf.RootStorage.AddStorage(name);
        PcbLibWriter.WriteStorageHeader(storage, 0);
        // Emit an explicit empty Data stream (the lazy stream handle only materializes on SetData).
        storage.AddStream("Data").SetData([]);
    }

    private static void WriteNets(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.Nets.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "Nets6");
            return;
        }

        var storage = cf.RootStorage.AddStorage("Nets6");
        PcbLibWriter.WriteStorageHeader(storage, document.Nets.Count);

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        foreach (var net in document.Nets)
        {
            if (net.RawParametersOrdered is { Count: > 0 } ordered)
            {
                // Re-emit the full net parameter block verbatim (color, layer, visibility, etc.).
                var sb = new System.Text.StringBuilder();
                foreach (var kvp in ordered)
                    sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
                writer.WriteCStringParameterBlockRaw(sb.ToString());
            }
            else
            {
                writer.WriteCStringParameterBlock(new Dictionary<string, string> { ["NAME"] = net.Name });
            }
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    private static void WriteParameterBlockStorage(CompoundFileAccessor cf, string storageName, IReadOnlyList<Dictionary<string, string>> parameterSets)
    {
        if (parameterSets.Count == 0)
            return;

        var storage = cf.RootStorage.AddStorage(storageName);
        PcbLibWriter.WriteStorageHeader(storage, parameterSets.Count);

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        foreach (var parameters in parameterSets)
        {
            writer.WriteCStringParameterBlock(parameters);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    private static void WriteRules(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.Rules.Count == 0)
            return;

        var storage = cf.RootStorage.AddStorage("Rules6");
        PcbLibWriter.WriteStorageHeader(storage, document.Rules.Count);
        var dataStream = storage.AddStream("Data");

        using var ms = new MemoryStream();
        foreach (var rule in document.Rules)
        {
            // Serialize the rule's parameter list (ordered + verbatim for round-trip; typed
            // fallback for new rules), then frame it as [2-byte leader][4-byte length][text][null].
            var sb = new System.Text.StringBuilder();
            if (rule.RawParametersOrdered is { Count: > 0 } ordered)
            {
                foreach (var kvp in ordered)
                    sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
            }
            else
            {
                foreach (var kvp in rule.ToParameters())
                    sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
            }

            var textBytes = AltiumEncoding.Windows1252.GetBytes(sb.ToString());
            var length = textBytes.Length + 1; // include the trailing null
            ms.WriteByte((byte)(rule.RawLeader & 0xFF));
            ms.WriteByte((byte)((rule.RawLeader >> 8) & 0xFF));
            ms.WriteByte((byte)(length & 0xFF));
            ms.WriteByte((byte)((length >> 8) & 0xFF));
            ms.WriteByte((byte)((length >> 16) & 0xFF));
            ms.WriteByte((byte)((length >> 24) & 0xFF));
            ms.Write(textBytes, 0, textBytes.Length);
            ms.WriteByte(0);
        }

        dataStream.SetData(ms.ToArray());
    }

    private static void WriteClasses(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.Classes.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "Classes6");
            return;
        }

        var texts = new List<string>();
        foreach (var objectClass in document.Classes)
            texts.Add(BuildParamText(objectClass.RawParametersOrdered, objectClass.ToParameters()));
        WriteParameterStringStorage(cf, "Classes6", texts);
    }

    private static void WriteSignalClasses(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.SignalClasses.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "SignalClasses");
            return;
        }

        var texts = new List<string>();
        foreach (var sc in document.SignalClasses)
            texts.Add(BuildParamText(sc.RawParametersOrdered, sc.ToParameters()));
        WriteParameterStringStorage(cf, "SignalClasses", texts);
    }

    private static void WriteSmartUnions(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.SmartUnions.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "SmartUnions");
            return;
        }
        var texts = new List<string>();
        foreach (var u in document.SmartUnions)
            texts.Add(BuildParamText(u.RawParametersOrdered, u.ToParameters()));
        WriteParameterStringStorage(cf, "SmartUnions", texts);
    }

    private static void WriteUnionNames(CompoundFileAccessor cf, PcbDocument document)
    {
        // UnionNames/Header is the constant 1; Data is [u32 count][per record...] and is always present
        // (a [u32 0] for an empty list) whenever the storage existed.
        if (document.UnionNames.Count == 0 && !document.PresentStorages.Contains("UnionNames"))
            return;
        var storage = cf.RootStorage.AddStorage("UnionNames");
        PcbLibWriter.WriteStorageHeader(storage, 1);
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((uint)document.UnionNames.Count));
        foreach (var n in document.UnionNames)
        {
            var nameBytes = System.Text.Encoding.Unicode.GetBytes(n.Name + '\0'); // UTF-16LE + 00 00
            ms.Write(BitConverter.GetBytes(n.UnionIndex));
            ms.Write(BitConverter.GetBytes((uint)nameBytes.Length));
            ms.Write(nameBytes);
        }
        storage.AddStream("Data").SetData(ms.ToArray());
    }

    private static void WriteDifferentialPairs(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.DifferentialPairs.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "DifferentialPairs6");
            return;
        }

        var texts = new List<string>();
        foreach (var pair in document.DifferentialPairs)
            texts.Add(BuildParamText(pair.RawParametersOrdered, pair.ToParameters()));
        WriteParameterStringStorage(cf, "DifferentialPairs6", texts);
    }

    private static void WriteRooms(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.Rooms.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "Rooms6");
            return;
        }

        var texts = new List<string>();
        foreach (var room in document.Rooms)
            texts.Add(BuildParamText(room.RawParametersOrdered, room.ToParameters()));
        WriteParameterStringStorage(cf, "Rooms6", texts);
    }

    /// <summary>
    /// Builds a pipe-delimited parameter string, preferring the ordered list (verbatim round-trip)
    /// and falling back to the typed dictionary for items created from scratch.
    /// </summary>
    private static string BuildParamText(List<KeyValuePair<string, string>>? ordered, Dictionary<string, string> fallback)
    {
        var sb = new System.Text.StringBuilder();
        if (ordered is { Count: > 0 })
        {
            foreach (var kvp in ordered)
                sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
        }
        else
        {
            foreach (var kvp in fallback)
                sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Writes a list of pre-formatted parameter strings as a Header + Data storage, each framed
    /// as a length-prefixed C-string block.
    /// </summary>
    private static void WriteParameterStringStorage(CompoundFileAccessor cf, string storageName, List<string> texts)
    {
        if (texts.Count == 0)
            return;

        var storage = cf.RootStorage.AddStorage(storageName);
        PcbLibWriter.WriteStorageHeader(storage, texts.Count);
        var dataStream = storage.AddStream("Data");

        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);
        foreach (var text in texts)
            writer.WriteCStringParameterBlockRaw(text);
        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    private static void WriteArcs(CompoundFileAccessor cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Arcs6", document.Arcs, (writer, arc) =>
        {
            writer.Write((byte)1);
            PcbLibWriter.WriteArc(writer, (PcbArc)arc);
        });
    }

    private static void WritePads(CompoundFileAccessor cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Pads6", document.Pads, (writer, pad) =>
        {
            writer.Write((byte)2);
            PcbLibWriter.WritePad(writer, (PcbPad)pad);
        });
    }

    private static void WriteVias(CompoundFileAccessor cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Vias6", document.Vias, (writer, via) =>
        {
            writer.Write((byte)3);
            PcbLibWriter.WriteVia(writer, (PcbVia)via);
        });
    }

    private static void WriteTracks(CompoundFileAccessor cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Tracks6", document.Tracks, (writer, track) =>
        {
            writer.Write((byte)4);
            PcbLibWriter.WriteTrack(writer, (PcbTrack)track);
        });
    }

    private static void WriteTexts(CompoundFileAccessor cf, PcbDocument document)
    {
        var textIndex = 0;
        WritePrimitiveStorage(cf, "Texts6", document.Texts, (writer, text) =>
        {
            writer.Write((byte)5);
            PcbLibWriter.WriteText(writer, (PcbText)text, textIndex++);
        });
    }

    private static void WriteFills(CompoundFileAccessor cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Fills6", document.Fills, (writer, fill) =>
        {
            writer.Write((byte)6);
            PcbLibWriter.WriteFill(writer, (PcbFill)fill);
        });
    }

    private static void WriteRegions(CompoundFileAccessor cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Regions6", document.Regions, (writer, region) =>
        {
            writer.Write((byte)11);
            PcbLibWriter.WriteRegion(writer, (PcbRegion)region);
        });
    }

    private static void WriteDocumentPrimitiveGuids(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.PrimitiveGuids.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "PrimitiveGuids");
            return;
        }
        var storage = cf.RootStorage.AddStorage("PrimitiveGuids");
        PcbLibWriter.WriteStorageHeader(storage, document.PrimitiveGuids.Count);
        using var ms = new MemoryStream();
        foreach (var g in document.PrimitiveGuids)
        {
            ms.Write(BitConverter.GetBytes(g.TypeId));
            ms.Write(BitConverter.GetBytes(g.Index));
            ms.Write(g.Guid.ToByteArray());
        }
        storage.AddStream("Data").SetData(ms.ToArray());
    }

    private static void WriteDocumentPrimitiveUniqueIds(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.PrimitiveUniqueIds.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "UniqueIDPrimitiveInformation");
            return;
        }
        var storage = cf.RootStorage.AddStorage("UniqueIDPrimitiveInformation");
        PcbLibWriter.WriteStorageHeader(storage, document.PrimitiveUniqueIds.Count);
        storage.AddStream("Data").SetData(PcbLibWriter.BuildPrimitiveUniqueIdData(document.PrimitiveUniqueIds));
    }

    private static void WriteShapeBased(CompoundFileAccessor cf, PcbDocument document, string storageName, List<PcbShapeBasedRegion> items)
    {
        if (items.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, storageName);
            return;
        }
        var storage = cf.RootStorage.AddStorage(storageName);
        PcbLibWriter.WriteStorageHeader(storage, items.Count);
        using var ms = new MemoryStream();
        foreach (var r in items)
        {
            ms.WriteByte(r.TypeByte);
            ms.Write(r.Sr1LengthBytes.Length == 4 ? r.Sr1LengthBytes : new byte[4]);
            ms.WriteByte(r.Layer);
            ms.WriteByte(r.Flags1);
            ms.WriteByte(r.Flags2);
            ms.Write(BitConverter.GetBytes(r.NetIndex));
            ms.Write(BitConverter.GetBytes(r.PolygonIndex));
            ms.Write(BitConverter.GetBytes(r.ComponentIndex));
            ms.Write(r.HeaderSkip5.Length == 5 ? r.HeaderSkip5 : new byte[5]);
            ms.Write(BitConverter.GetBytes((ushort)r.Holes.Count));
            ms.Write(r.HeaderSkip2.Length == 2 ? r.HeaderSkip2 : new byte[2]);
            ms.Write(BitConverter.GetBytes(r.RawPropertyBytes.Length));
            ms.Write(r.RawPropertyBytes);
            if (r.PropsHasTrailingNull) ms.WriteByte(0);
            ms.Write(BitConverter.GetBytes((uint)Math.Max(0, r.Outline.Count - 1)));   // disk count = N-1
            foreach (var v in r.Outline)
            {
                ms.WriteByte(v.IsRoundRaw);
                ms.Write(BitConverter.GetBytes(v.X)); ms.Write(BitConverter.GetBytes(v.Y));
                ms.Write(BitConverter.GetBytes(v.CenterX)); ms.Write(BitConverter.GetBytes(v.CenterY));
                ms.Write(BitConverter.GetBytes(v.Radius));
                ms.Write(BitConverter.GetBytes(v.StartAngle)); ms.Write(BitConverter.GetBytes(v.EndAngle));
            }
            foreach (var hole in r.Holes)
            {
                ms.Write(BitConverter.GetBytes((uint)hole.Count));
                foreach (var (x, y) in hole) { ms.Write(BitConverter.GetBytes(x)); ms.Write(BitConverter.GetBytes(y)); }
            }
        }
        storage.AddStream("Data").SetData(ms.ToArray());
    }

    private static void WriteBoardRegions(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.BoardRegions.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "BoardRegions");
            return;
        }
        WritePrimitiveStorage(cf, "BoardRegions", document.BoardRegions, (writer, region) =>
        {
            writer.Write((byte)11);
            PcbLibWriter.WriteRegion(writer, region);
        });
    }

    private static void WriteComponentBodies(CompoundFileAccessor cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "ComponentBodies6", document.ComponentBodies, (writer, body) =>
        {
            writer.Write((byte)12);
            PcbLibWriter.WriteComponentBody(writer, (PcbComponentBody)body);
        });
    }

    private static void WritePolygons(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.Polygons.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "Polygons6");
            return;
        }

        var storage = cf.RootStorage.AddStorage("Polygons6");
        PcbLibWriter.WriteStorageHeader(storage, document.Polygons.Count);

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        foreach (var polygon in document.Polygons)
        {
            WritePolygonParameters(writer, polygon);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    private static void WritePolygonParameters(BinaryFormatWriter writer, PcbPolygon polygon)
    {
        // Round-trip: re-emit the original polygon parameter block verbatim (preserves the
        // outline/arc vertex keys, key order and formatting). New polygons use typed fields.
        if (polygon.RawParametersOrdered is { Count: > 0 } ordered)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in ordered)
                sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
            writer.WriteCStringParameterBlockRaw(sb.ToString());
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Merge AdditionalParameters first (typed properties override)
        if (polygon.AdditionalParameters != null)
        {
            foreach (var kvp in polygon.AdditionalParameters)
                parameters[kvp.Key] = kvp.Value;
        }

        // Basic identity
        parameters["LAYER"] = polygon.Layer.ToString(CultureInfo.InvariantCulture);
        parameters["NET"] = polygon.Net ?? string.Empty;
        parameters["POLYGONTYPE"] = polygon.PolygonType.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrEmpty(polygon.Name))
            parameters["NAME"] = polygon.Name;
        if (!string.IsNullOrEmpty(polygon.UniqueId))
            parameters["UNIQUEID"] = polygon.UniqueId;

        // Hatch/pour settings - use DTO keys
        parameters["HATCHSTYLE"] = polygon.PolyHatchStyle.ToString(CultureInfo.InvariantCulture);
        parameters["POURMODE"] = polygon.PourOver.ToString(CultureInfo.InvariantCulture);

        // Boolean flags - use DTO keys
        parameters["REMOVEISLANDSBYAREA"] = polygon.RemoveIslandsByArea ? "TRUE" : "FALSE";
        parameters["ISLANDAREATHRESHOLD"] = polygon.IslandAreaThreshold.ToString(CultureInfo.InvariantCulture);
        parameters["REMOVEDEAD"] = polygon.RemoveDead ? "TRUE" : "FALSE";
        parameters["REMOVENECKS"] = polygon.RemoveNarrowNecks ? "TRUE" : "FALSE";
        parameters["USEOCTAGONS"] = polygon.UseOctagons ? "TRUE" : "FALSE";
        parameters["AVOIDOBST"] = polygon.AvoidObstacles ? "TRUE" : "FALSE";

        // Coord properties
        if (polygon.Grid.ToRaw() != 0)
            parameters["GRIDSIZE"] = polygon.Grid.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.TrackSize.ToRaw() != 0)
            parameters["TRACKWIDTH"] = polygon.TrackSize.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.MinTrack.ToRaw() != 0)
            parameters["MINPRIMLENGTH"] = polygon.MinTrack.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.NeckWidthThreshold.ToRaw() != 0)
            parameters["NECKWIDTH"] = polygon.NeckWidthThreshold.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.ArcApproximation.ToRaw() != 0)
            parameters["ARCAPPROXIMATION"] = polygon.ArcApproximation.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.BorderWidth.ToRaw() != 0)
            parameters["BORDERWIDTH"] = polygon.BorderWidth.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.SolderMaskExpansion.ToRaw() != 0)
            parameters["SOLDERMASKEXPANSION"] = polygon.SolderMaskExpansion.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.PasteMaskExpansion.ToRaw() != 0)
            parameters["PASTEMASKEXPANSION"] = polygon.PasteMaskExpansion.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.ReliefAirGap.ToRaw() != 0)
            parameters["RELIEFAIRGAP"] = polygon.ReliefAirGap.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.ReliefConductorWidth.ToRaw() != 0)
            parameters["RELIEFCONDUCTORWIDTH"] = polygon.ReliefConductorWidth.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.PowerPlaneClearance.ToRaw() != 0)
            parameters["POWERPLANECLEARANCE"] = polygon.PowerPlaneClearance.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (polygon.PowerPlaneReliefExpansion.ToRaw() != 0)
            parameters["POWERPLANERELIEFEXPANSION"] = polygon.PowerPlaneReliefExpansion.ToRaw().ToString(CultureInfo.InvariantCulture);

        // Integer properties
        if (polygon.PourIndex != 0)
            parameters["POURORDER"] = polygon.PourIndex.ToString(CultureInfo.InvariantCulture);
        if (polygon.ReliefEntries != 0)
            parameters["RELIEFENTRIES"] = polygon.ReliefEntries.ToString(CultureInfo.InvariantCulture);
        if (polygon.PowerPlaneConnectStyle != 0)
            parameters["POWERPLANECONNECTSTYLE"] = polygon.PowerPlaneConnectStyle.ToString(CultureInfo.InvariantCulture);

        // Long properties
        if (polygon.AreaSize != 0)
            parameters["REPOURAREA"] = polygon.AreaSize.ToString(CultureInfo.InvariantCulture);

        // More boolean flags
        if (polygon.PrimitiveLock)
            parameters["PRIMITIVELOCK"] = "TRUE";
        if (polygon.IsHidden)
            parameters["SHELVED"] = "TRUE";
        if (polygon.PourOverSameNetPolygons)
            parameters["POUROVERSAMENETPOLYGONS"] = "TRUE";
        if (!polygon.Enabled)
            parameters["ENABLED"] = "FALSE";
        if (polygon.IsKeepout)
            parameters["KEEPOUT"] = "TRUE";
        if (polygon.PolygonOutline)
            parameters["POLYGONOUTLINE"] = "TRUE";
        if (polygon.Poured)
            parameters["POURED"] = "TRUE";
        if (polygon.AutoGenerateName)
            parameters["AUTOGENERATENAME"] = "TRUE";
        if (polygon.ClipAcuteCorners)
            parameters["CLIPACUTECORNERS"] = "TRUE";
        if (polygon.DrawDeadCopper)
            parameters["DRAWDEADCOPPER"] = "TRUE";
        if (polygon.DrawRemovedIslands)
            parameters["DRAWREMOVEDISLANDS"] = "TRUE";
        if (polygon.DrawRemovedNecks)
            parameters["DRAWREMOVEDNECKS"] = "TRUE";
        if (polygon.ExpandOutline)
            parameters["EXPANDOUTLINE"] = "TRUE";
        if (polygon.IgnoreViolations)
            parameters["IGNOREVIOLATIONS"] = "TRUE";
        if (polygon.MitreCorners)
            parameters["MITRECORNERS"] = "TRUE";
        if (polygon.ObeyPolygonCutout)
            parameters["OBEYPOLYGONCUTOUT"] = "TRUE";
        if (polygon.OptimalVoidRotation)
            parameters["OPTIMALVOIDROTATION"] = "TRUE";
        if (polygon.AllowGlobalEdit)
            parameters["ALLOWGLOBALEDIT"] = "TRUE";
        if (polygon.Moveable)
            parameters["MOVEABLE"] = "TRUE";
        if (polygon.ArcPourMode)
            parameters["ARCPOURMODE"] = "TRUE";

        // Vertices
        parameters["POINTCOUNT"] = polygon.Vertices.Count.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < polygon.Vertices.Count; i++)
        {
            var prefix = $"SA{i}";
            parameters[$"{prefix}.X"] = polygon.Vertices[i].X.ToRaw().ToString(CultureInfo.InvariantCulture);
            parameters[$"{prefix}.Y"] = polygon.Vertices[i].Y.ToRaw().ToString(CultureInfo.InvariantCulture);
        }

        writer.WriteCStringParameterBlock(parameters);
    }

    private static void WriteComponents(CompoundFileAccessor cf, PcbDocument document)
    {
        var storage = cf.RootStorage.AddStorage("Components6");
        PcbLibWriter.WriteStorageHeader(storage, document.Components.Count);

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        foreach (var icomp in document.Components)
        {
            var comp = (PcbComponent)icomp;
            WriteComponentParameters(writer, comp);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    private static void WriteComponentParameters(BinaryFormatWriter writer, PcbComponent comp)
    {
        // Round-trip: re-emit the original component parameter block verbatim (preserves key
        // order, mil formatting and all attributes). New components fall back to typed fields.
        if (comp.RawParametersOrdered is { Count: > 0 } ordered)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in ordered)
                sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
            writer.WriteCStringParameterBlockRaw(sb.ToString());
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Merge AdditionalParameters first (typed properties override)
        if (comp.AdditionalParameters != null)
        {
            foreach (var kvp in comp.AdditionalParameters)
                parameters[kvp.Key] = kvp.Value;
        }

        // Basic identity
        parameters["PATTERN"] = comp.Name;
        if (!string.IsNullOrEmpty(comp.Description))
            parameters["DESCRIPTION"] = comp.Description;
        if (comp.Height.ToRaw() != 0)
            parameters["HEIGHT"] = comp.Height.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(comp.Comment))
            parameters["COMMENT"] = comp.Comment;
        if (comp.X.ToRaw() != 0)
            parameters["X"] = comp.X.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (comp.Y.ToRaw() != 0)
            parameters["Y"] = comp.Y.ToRaw().ToString(CultureInfo.InvariantCulture);
        if (comp.Rotation != 0)
            parameters["ROTATION"] = comp.Rotation.ToString(CultureInfo.InvariantCulture);
        if (comp.Layer != 0)
            parameters["LAYER"] = comp.Layer.ToString(CultureInfo.InvariantCulture);

        // Display
        if (comp.CommentOn)
            parameters["COMMENTON"] = "TRUE";
        if (comp.CommentAutoPosition != 0)
            parameters["COMMENTAUTOPOSITION"] = comp.CommentAutoPosition.ToString(CultureInfo.InvariantCulture);
        if (comp.NameOn)
            parameters["NAMEON"] = "TRUE";
        if (comp.NameAutoPosition != 0)
            parameters["NAMEAUTOPOSITION"] = comp.NameAutoPosition.ToString(CultureInfo.InvariantCulture);
        if (comp.LockStrings)
            parameters["LOCKSTRINGS"] = "TRUE";

        // Component state
        if (comp.ComponentKind != 0)
            parameters["COMPONENTKIND"] = comp.ComponentKind.ToString(CultureInfo.InvariantCulture);
        if (!comp.Enabled)
            parameters["ENABLED"] = "FALSE";
        if (comp.FlippedOnLayer)
            parameters["FLIPPEDONLAYER"] = "TRUE";
        if (comp.GroupNum != 0)
            parameters["GROUPNUM"] = comp.GroupNum.ToString(CultureInfo.InvariantCulture);
        if (comp.IsBGA)
            parameters["ISBGA"] = "TRUE";

        // Source info
        if (!string.IsNullOrEmpty(comp.SourceDesignator))
            parameters["SOURCEDESIGNATOR"] = comp.SourceDesignator;
        if (!string.IsNullOrEmpty(comp.SourceLibReference))
            parameters["SOURCELIBREFRENCE"] = comp.SourceLibReference;
        if (!string.IsNullOrEmpty(comp.SourceComponentLibrary))
            parameters["SOURCECOMPONENTLIBRARY"] = comp.SourceComponentLibrary;
        if (!string.IsNullOrEmpty(comp.SourceDescription))
            parameters["SOURCEDESCRIPTION"] = comp.SourceDescription;
        if (!string.IsNullOrEmpty(comp.SourceFootprintLibrary))
            parameters["SOURCEFOOTPRINTLIBRARY"] = comp.SourceFootprintLibrary;
        if (!string.IsNullOrEmpty(comp.SourceUniqueId))
            parameters["SOURCEUNIQUEID"] = comp.SourceUniqueId;
        if (!string.IsNullOrEmpty(comp.SourceHierarchicalPath))
            parameters["SOURCEHIERARCHICALPATH"] = comp.SourceHierarchicalPath;
        if (!string.IsNullOrEmpty(comp.SourceCompDesignItemID))
            parameters["SOURCECOMPDESIGNITEMID"] = comp.SourceCompDesignItemID;

        // Vault/GUID
        if (!string.IsNullOrEmpty(comp.ItemGUID))
            parameters["ITEMGUID"] = comp.ItemGUID;
        if (!string.IsNullOrEmpty(comp.ItemRevisionGUID))
            parameters["REVISIONGUID"] = comp.ItemRevisionGUID;
        if (!string.IsNullOrEmpty(comp.VaultGUID))
            parameters["VAULTGUID"] = comp.VaultGUID;
        if (!string.IsNullOrEmpty(comp.UniqueId))
            parameters["UNIQUEID"] = comp.UniqueId;

        // Hash/model
        if (!string.IsNullOrEmpty(comp.ModelHash))
            parameters["MODELHASH"] = comp.ModelHash;
        if (!string.IsNullOrEmpty(comp.PackageSpecificHash))
            parameters["PACKAGESPECIFICHASH"] = comp.PackageSpecificHash;
        if (!string.IsNullOrEmpty(comp.DefaultPCB3DModel))
            parameters["DEFAULTPCB3DMODEL"] = comp.DefaultPCB3DModel;

        writer.WriteCStringParameterBlock(parameters);
    }

    private static void WriteEmbeddedBoards(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.EmbeddedBoards.Count == 0)
        {
            WriteEmptyStorageIfPresent(cf, document, "EmbeddedBoards6");
            return;
        }

        var storage = cf.RootStorage.AddStorage("EmbeddedBoards6");
        PcbLibWriter.WriteStorageHeader(storage, document.EmbeddedBoards.Count);

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        foreach (var board in document.EmbeddedBoards)
        {
            WriteEmbeddedBoardParameters(writer, board);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    private static void WriteEmbeddedBoardParameters(BinaryFormatWriter writer, PcbEmbeddedBoard board)
    {
        // Round-trip: re-emit the original parameter block verbatim. New boards use typed fields.
        if (board.RawParametersOrdered is { Count: > 0 } ordered)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in ordered)
                sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
            writer.WriteCStringParameterBlockRaw(sb.ToString());
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Boolean properties (written as present=TRUE/FALSE, matching real Altium format)
        parameters["SELECTION"] = "FALSE";
        parameters["LOCKED"] = "FALSE";
        parameters["POLYGONOUTLINE"] = board.PolygonOutline ? "TRUE" : "FALSE";
        parameters["USERROUTED"] = board.UserRouted ? "TRUE" : "FALSE";
        parameters["KEEPOUT"] = board.IsKeepout ? "TRUE" : "FALSE";
        parameters["MIRROR"] = board.MirrorFlag ? "TRUE" : "FALSE";

        // Layer (as name, matching real format)
        parameters["LAYER"] = LayerByteToName(board.Layer);

        // Integer properties
        parameters["UNIONINDEX"] = board.UnionIndex.ToString(CultureInfo.InvariantCulture);
        if (board.OriginMode != 0)
            parameters["ORIGINMODE"] = board.OriginMode.ToString(CultureInfo.InvariantCulture);
        parameters["COLCOUNT"] = board.ColCount.ToString(CultureInfo.InvariantCulture);
        parameters["ROWCOUNT"] = board.RowCount.ToString(CultureInfo.InvariantCulture);

        // Coord properties (stored as "NNNNmil" format)
        parameters["X1"] = FormatMilCoord(board.X1Location);
        parameters["Y1"] = FormatMilCoord(board.Y1Location);
        parameters["X2"] = FormatMilCoord(board.X2Location);
        parameters["Y2"] = FormatMilCoord(board.Y2Location);
        parameters["X"] = FormatMilCoord(board.X1Location);
        parameters["Y"] = FormatMilCoord(board.Y1Location);
        parameters["COLSPACING"] = FormatMilCoord(board.ColSpacing);
        parameters["ROWSPACING"] = FormatMilCoord(board.RowSpacing);

        // Rotation (scientific notation)
        parameters["ROTATION"] = " " + board.Rotation.ToString("E14", CultureInfo.InvariantCulture);

        // Viewport properties
        parameters["ISVIEWPORT"] = board.IsViewport ? "TRUE" : "FALSE";
        parameters["VIEWPORTVISIBLE"] = board.ViewportVisible ? "TRUE" : "FALSE";
        if (!string.IsNullOrEmpty(board.ViewportTitle))
            parameters["VIEWPORTTITLE"] = board.ViewportTitle;
        if (board.Scale != 0)
            parameters["VIEWPORTSCALE"] = board.Scale.ToString("F3", CultureInfo.InvariantCulture);

        // Font properties
        if (!string.IsNullOrEmpty(board.TitleFontName))
            parameters["FONTNAME"] = board.TitleFontName;
        if (board.TitleFontSize != 0)
            parameters["FONTSIZE"] = board.TitleFontSize.ToString(CultureInfo.InvariantCulture);
        if (board.TitleFontColor != 0)
            parameters["FONTCOLOR"] = board.TitleFontColor.ToString(CultureInfo.InvariantCulture);

        // Document path
        if (!string.IsNullOrEmpty(board.DocumentPath))
            parameters["DOCUMENTPATH"] = board.DocumentPath;

        writer.WriteCStringParameterBlock(parameters);
    }

    private static string FormatMilCoord(Coord coord)
    {
        return coord.ToMils().ToString("F4", CultureInfo.InvariantCulture) + "mil";
    }

    private static string LayerByteToName(int layer)
    {
        return layer switch
        {
            1 => "TOP",
            32 => "BOTTOM",
            33 => "TOPOVERLAY",
            34 => "BOTTOMOVERLAY",
            35 => "TOPPASTE",
            36 => "BOTTOMPASTE",
            37 => "TOPSOLDER",
            38 => "BOTTOMSOLDER",
            55 => "DRILLGUIDE",
            56 => "KEEPOUT",
            73 => "DRILLDRAWING",
            74 => "MULTILAYER",
            _ when layer >= 2 && layer <= 31 => $"MIDLAYER{layer - 1}",
            _ when layer >= 39 && layer <= 54 => $"INTERNALPLANE{layer - 38}",
            _ when layer >= 57 && layer <= 72 => $"MECHANICAL{layer - 56}",
            _ => layer.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static void WriteWideStrings(CompoundFileAccessor cf, PcbDocument document)
    {
        // Collect all text strings that need wide encoding
        var hasWideStrings = false;
        foreach (var text in document.Texts)
        {
            if (text.Text.Any(c => c > 127))
            {
                hasWideStrings = true;
                break;
            }
        }

        _ = hasWideStrings;
        // Altium always writes WideStrings6 when the document has text.
        if (document.Texts.Count == 0 && !document.PresentStorages.Contains("WideStrings6"))
            return;

        var storage = cf.RootStorage.AddStorage("WideStrings6");
        PcbLibWriter.WriteStorageHeader(storage, document.Texts.Count);

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();

        // Each text is stored as [u32 text_index][u32 byte_len][UTF-16LE string incl. null].
        var textIndex = 0;
        foreach (var text in document.Texts)
        {
            var utf16 = System.Text.Encoding.Unicode.GetBytes((text.Text ?? string.Empty) + "\0");
            ms.Write(BitConverter.GetBytes(textIndex), 0, 4);
            ms.Write(BitConverter.GetBytes(utf16.Length), 0, 4);
            ms.Write(utf16, 0, utf16.Length);
            textIndex++;
        }

        dataStream.SetData(ms.ToArray());
    }

    private static void WriteAdditionalStreams(CompoundFileAccessor cf, PcbDocument document)
    {
        if (document.AdditionalStreams == null || document.AdditionalStreams.Count == 0)
            return;

        // Group entries by storage name
        var storageGroups = new Dictionary<string, List<(string StreamName, byte[] Data)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in document.AdditionalStreams)
        {
            var slashIndex = kvp.Key.IndexOf('/');
            if (slashIndex > 0)
            {
                var storageName = kvp.Key[..slashIndex];
                var streamName = kvp.Key[(slashIndex + 1)..];
                if (!storageGroups.TryGetValue(storageName, out var list))
                {
                    list = new List<(string, byte[])>();
                    storageGroups[storageName] = list;
                }
                list.Add((streamName, kvp.Value));
            }
            else
            {
                // Root-level stream
                var stream = cf.RootStorage.AddStream(kvp.Key);
                stream.SetData(kvp.Value);
            }
        }

        // Create each storage and its streams
        foreach (var group in storageGroups)
        {
            var storage = cf.RootStorage.AddStorage(group.Key);
            foreach (var (streamName, data) in group.Value)
            {
                var stream = storage.AddStream(streamName);
                stream.SetData(data);
            }
        }
    }

    private static void WritePrimitiveStorage<T>(
        CompoundFileAccessor cf,
        string storageName,
        IReadOnlyList<T> primitives,
        Action<BinaryFormatWriter, T> writePrimitive)
    {
        var storage = cf.RootStorage.AddStorage(storageName);
        PcbLibWriter.WriteStorageHeader(storage, primitives.Count);

        var dataStream = storage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        foreach (var primitive in primitives)
        {
            writePrimitive(writer, primitive);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }
}

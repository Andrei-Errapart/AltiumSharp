using OpenMcdf;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Binary;

namespace OriginalCircuit.Altium.Serialization.Writers;

/// <summary>
/// Writes PCB document (.PcbDoc) files.
/// PcbDoc files store primitives in separate storages per type
/// (e.g., [Arcs6], [Pads6], [Tracks6]).
/// </summary>
public sealed class PcbDocWriter
{
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
        await WriteAsync(document, stream, cancellationToken);
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
        await ms.CopyToAsync(stream, cancellationToken);
    }

    /// <summary>
    /// Writes a PcbDoc file to a stream synchronously.
    /// </summary>
    /// <param name="document">The PCB document to write.</param>
    /// <param name="stream">Destination stream.</param>
    /// <remarks>This instance is stateless and thread-safe.</remarks>
    public void Write(PcbDocument document, Stream stream, CancellationToken cancellationToken = default)
    {
        using var cf = new CompoundFile();

        WriteFileHeader(cf);
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
        WriteComponentBodies(cf, document);
        cancellationToken.ThrowIfCancellationRequested();
        WritePolygons(cf, document);
        WriteComponents(cf, document);
        WriteEmbeddedBoards(cf, document);
        WriteRules(cf, document);
        WriteClasses(cf, document);
        WriteDifferentialPairs(cf, document);
        WriteRooms(cf, document);
        WriteWideStrings(cf, document);
        WriteAdditionalStreams(cf, document);

        cf.Save(stream);
    }

    private static void WriteFileHeader(CompoundFile cf)
    {
        var headerStream = cf.RootStorage.AddStream("FileHeader");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        var versionText = "PCB 6.0 Binary Document File";
        writer.Write(versionText.Length);
        writer.WritePascalShortString(versionText);

        writer.Flush();
        headerStream.SetData(ms.ToArray());
    }

    private static void WriteBoard(CompoundFile cf, PcbDocument document)
    {
        if (document.BoardParameters == null || document.BoardParameters.Count == 0)
            return;

        var storage = cf.RootStorage.AddStorage("Board6");
        PcbLibWriter.WriteStorageHeader(storage, 0);

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
    private static void WriteEmptyStorageIfPresent(CompoundFile cf, PcbDocument document, string name)
    {
        if (!document.PresentStorages.Contains(name))
            return;
        var storage = cf.RootStorage.AddStorage(name);
        PcbLibWriter.WriteStorageHeader(storage, 0);
        storage.AddStream("Data");
    }

    private static void WriteNets(CompoundFile cf, PcbDocument document)
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

    private static void WriteParameterBlockStorage(CompoundFile cf, string storageName, IReadOnlyList<Dictionary<string, string>> parameterSets)
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

    private static void WriteRules(CompoundFile cf, PcbDocument document)
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

    private static void WriteClasses(CompoundFile cf, PcbDocument document)
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

    private static void WriteDifferentialPairs(CompoundFile cf, PcbDocument document)
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

    private static void WriteRooms(CompoundFile cf, PcbDocument document)
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
    private static void WriteParameterStringStorage(CompoundFile cf, string storageName, List<string> texts)
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

    private static void WriteArcs(CompoundFile cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Arcs6", document.Arcs, (writer, arc) =>
        {
            writer.Write((byte)1);
            PcbLibWriter.WriteArc(writer, (PcbArc)arc);
        });
    }

    private static void WritePads(CompoundFile cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Pads6", document.Pads, (writer, pad) =>
        {
            writer.Write((byte)2);
            PcbLibWriter.WritePad(writer, (PcbPad)pad);
        });
    }

    private static void WriteVias(CompoundFile cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Vias6", document.Vias, (writer, via) =>
        {
            writer.Write((byte)3);
            PcbLibWriter.WriteVia(writer, (PcbVia)via);
        });
    }

    private static void WriteTracks(CompoundFile cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Tracks6", document.Tracks, (writer, track) =>
        {
            writer.Write((byte)4);
            PcbLibWriter.WriteTrack(writer, (PcbTrack)track);
        });
    }

    private static void WriteTexts(CompoundFile cf, PcbDocument document)
    {
        var textIndex = 0;
        WritePrimitiveStorage(cf, "Texts6", document.Texts, (writer, text) =>
        {
            writer.Write((byte)5);
            PcbLibWriter.WriteText(writer, (PcbText)text, textIndex++);
        });
    }

    private static void WriteFills(CompoundFile cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Fills6", document.Fills, (writer, fill) =>
        {
            writer.Write((byte)6);
            PcbLibWriter.WriteFill(writer, (PcbFill)fill);
        });
    }

    private static void WriteRegions(CompoundFile cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "Regions6", document.Regions, (writer, region) =>
        {
            writer.Write((byte)11);
            PcbLibWriter.WriteRegion(writer, (PcbRegion)region);
        });
    }

    private static void WriteComponentBodies(CompoundFile cf, PcbDocument document)
    {
        WritePrimitiveStorage(cf, "ComponentBodies6", document.ComponentBodies, (writer, body) =>
        {
            writer.Write((byte)12);
            PcbLibWriter.WriteComponentBody(writer, (PcbComponentBody)body);
        });
    }

    private static void WritePolygons(CompoundFile cf, PcbDocument document)
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
        parameters["LAYER"] = polygon.Layer.ToString();
        parameters["NET"] = polygon.Net ?? string.Empty;
        parameters["POLYGONTYPE"] = polygon.PolygonType.ToString();

        if (!string.IsNullOrEmpty(polygon.Name))
            parameters["NAME"] = polygon.Name;
        if (!string.IsNullOrEmpty(polygon.UniqueId))
            parameters["UNIQUEID"] = polygon.UniqueId;

        // Hatch/pour settings - use DTO keys
        parameters["HATCHSTYLE"] = polygon.PolyHatchStyle.ToString();
        parameters["POURMODE"] = polygon.PourOver.ToString();

        // Boolean flags - use DTO keys
        parameters["REMOVEISLANDSBYAREA"] = polygon.RemoveIslandsByArea ? "TRUE" : "FALSE";
        parameters["ISLANDAREATHRESHOLD"] = polygon.IslandAreaThreshold.ToString();
        parameters["REMOVEDEAD"] = polygon.RemoveDead ? "TRUE" : "FALSE";
        parameters["REMOVENECKS"] = polygon.RemoveNarrowNecks ? "TRUE" : "FALSE";
        parameters["USEOCTAGONS"] = polygon.UseOctagons ? "TRUE" : "FALSE";
        parameters["AVOIDOBST"] = polygon.AvoidObstacles ? "TRUE" : "FALSE";

        // Coord properties
        if (polygon.Grid.ToRaw() != 0)
            parameters["GRIDSIZE"] = polygon.Grid.ToRaw().ToString();
        if (polygon.TrackSize.ToRaw() != 0)
            parameters["TRACKWIDTH"] = polygon.TrackSize.ToRaw().ToString();
        if (polygon.MinTrack.ToRaw() != 0)
            parameters["MINPRIMLENGTH"] = polygon.MinTrack.ToRaw().ToString();
        if (polygon.NeckWidthThreshold.ToRaw() != 0)
            parameters["NECKWIDTH"] = polygon.NeckWidthThreshold.ToRaw().ToString();
        if (polygon.ArcApproximation.ToRaw() != 0)
            parameters["ARCAPPROXIMATION"] = polygon.ArcApproximation.ToRaw().ToString();
        if (polygon.BorderWidth.ToRaw() != 0)
            parameters["BORDERWIDTH"] = polygon.BorderWidth.ToRaw().ToString();
        if (polygon.SolderMaskExpansion.ToRaw() != 0)
            parameters["SOLDERMASKEXPANSION"] = polygon.SolderMaskExpansion.ToRaw().ToString();
        if (polygon.PasteMaskExpansion.ToRaw() != 0)
            parameters["PASTEMASKEXPANSION"] = polygon.PasteMaskExpansion.ToRaw().ToString();
        if (polygon.ReliefAirGap.ToRaw() != 0)
            parameters["RELIEFAIRGAP"] = polygon.ReliefAirGap.ToRaw().ToString();
        if (polygon.ReliefConductorWidth.ToRaw() != 0)
            parameters["RELIEFCONDUCTORWIDTH"] = polygon.ReliefConductorWidth.ToRaw().ToString();
        if (polygon.PowerPlaneClearance.ToRaw() != 0)
            parameters["POWERPLANECLEARANCE"] = polygon.PowerPlaneClearance.ToRaw().ToString();
        if (polygon.PowerPlaneReliefExpansion.ToRaw() != 0)
            parameters["POWERPLANERELIEFEXPANSION"] = polygon.PowerPlaneReliefExpansion.ToRaw().ToString();

        // Integer properties
        if (polygon.PourIndex != 0)
            parameters["POURORDER"] = polygon.PourIndex.ToString();
        if (polygon.ReliefEntries != 0)
            parameters["RELIEFENTRIES"] = polygon.ReliefEntries.ToString();
        if (polygon.PowerPlaneConnectStyle != 0)
            parameters["POWERPLANECONNECTSTYLE"] = polygon.PowerPlaneConnectStyle.ToString();

        // Long properties
        if (polygon.AreaSize != 0)
            parameters["REPOURAREA"] = polygon.AreaSize.ToString();

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
        parameters["POINTCOUNT"] = polygon.Vertices.Count.ToString();
        for (var i = 0; i < polygon.Vertices.Count; i++)
        {
            var prefix = $"SA{i}";
            parameters[$"{prefix}.X"] = polygon.Vertices[i].X.ToRaw().ToString();
            parameters[$"{prefix}.Y"] = polygon.Vertices[i].Y.ToRaw().ToString();
        }

        writer.WriteCStringParameterBlock(parameters);
    }

    private static void WriteComponents(CompoundFile cf, PcbDocument document)
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
            parameters["HEIGHT"] = comp.Height.ToRaw().ToString();
        if (!string.IsNullOrEmpty(comp.Comment))
            parameters["COMMENT"] = comp.Comment;
        if (comp.X.ToRaw() != 0)
            parameters["X"] = comp.X.ToRaw().ToString();
        if (comp.Y.ToRaw() != 0)
            parameters["Y"] = comp.Y.ToRaw().ToString();
        if (comp.Rotation != 0)
            parameters["ROTATION"] = comp.Rotation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (comp.Layer != 0)
            parameters["LAYER"] = comp.Layer.ToString();

        // Display
        if (comp.CommentOn)
            parameters["COMMENTON"] = "TRUE";
        if (comp.CommentAutoPosition != 0)
            parameters["COMMENTAUTOPOSITION"] = comp.CommentAutoPosition.ToString();
        if (comp.NameOn)
            parameters["NAMEON"] = "TRUE";
        if (comp.NameAutoPosition != 0)
            parameters["NAMEAUTOPOSITION"] = comp.NameAutoPosition.ToString();
        if (comp.LockStrings)
            parameters["LOCKSTRINGS"] = "TRUE";

        // Component state
        if (comp.ComponentKind != 0)
            parameters["COMPONENTKIND"] = comp.ComponentKind.ToString();
        if (!comp.Enabled)
            parameters["ENABLED"] = "FALSE";
        if (comp.FlippedOnLayer)
            parameters["FLIPPEDONLAYER"] = "TRUE";
        if (comp.GroupNum != 0)
            parameters["GROUPNUM"] = comp.GroupNum.ToString();
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

    private static void WriteEmbeddedBoards(CompoundFile cf, PcbDocument document)
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
        parameters["UNIONINDEX"] = board.UnionIndex.ToString();
        if (board.OriginMode != 0)
            parameters["ORIGINMODE"] = board.OriginMode.ToString();
        parameters["COLCOUNT"] = board.ColCount.ToString();
        parameters["ROWCOUNT"] = board.RowCount.ToString();

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
        parameters["ROTATION"] = $" {board.Rotation:E14}";

        // Viewport properties
        parameters["ISVIEWPORT"] = board.IsViewport ? "TRUE" : "FALSE";
        parameters["VIEWPORTVISIBLE"] = board.ViewportVisible ? "TRUE" : "FALSE";
        if (!string.IsNullOrEmpty(board.ViewportTitle))
            parameters["VIEWPORTTITLE"] = board.ViewportTitle;
        if (board.Scale != 0)
            parameters["VIEWPORTSCALE"] = board.Scale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        // Font properties
        if (!string.IsNullOrEmpty(board.TitleFontName))
            parameters["FONTNAME"] = board.TitleFontName;
        if (board.TitleFontSize != 0)
            parameters["FONTSIZE"] = board.TitleFontSize.ToString();
        if (board.TitleFontColor != 0)
            parameters["FONTCOLOR"] = board.TitleFontColor.ToString();

        // Document path
        if (!string.IsNullOrEmpty(board.DocumentPath))
            parameters["DOCUMENTPATH"] = board.DocumentPath;

        writer.WriteCStringParameterBlock(parameters);
    }

    private static string FormatMilCoord(Coord coord)
    {
        return $"{coord.ToMils():F4}mil";
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
            _ => layer.ToString()
        };
    }

    private static void WriteWideStrings(CompoundFile cf, PcbDocument document)
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

    private static void WriteAdditionalStreams(CompoundFile cf, PcbDocument document)
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
        CompoundFile cf,
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

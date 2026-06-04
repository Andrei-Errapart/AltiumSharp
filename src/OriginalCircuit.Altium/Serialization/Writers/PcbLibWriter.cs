using OpenMcdf;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Altium.Serialization.Binary;

namespace OriginalCircuit.Altium.Serialization.Writers;

/// <summary>
/// Writes PCB footprint library (.PcbLib) files.
/// </summary>
public sealed class PcbLibWriter
{
    /// <summary>
    /// Writes a PcbLib file to the specified path.
    /// </summary>
    /// <param name="library">The PCB footprint library to write.</param>
    /// <param name="path">Destination file path.</param>
    /// <param name="overwrite">If true, overwrites an existing file; otherwise throws if the file exists.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>This instance is stateless and thread-safe.</remarks>
    public async ValueTask WriteAsync(PcbLibrary library, string path, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await WriteAsync(library, stream, cancellationToken);
    }

    /// <summary>
    /// Writes a PcbLib file to a stream.
    /// </summary>
    /// <param name="library">The PCB footprint library to write.</param>
    /// <param name="stream">Destination stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>This instance is stateless and thread-safe.</remarks>
    public async ValueTask WriteAsync(PcbLibrary library, Stream stream, CancellationToken cancellationToken = default)
    {
        // Write synchronously to memory, then copy to output stream
        using var ms = new MemoryStream();
        Write(library, ms, cancellationToken);
        ms.Position = 0;
        await ms.CopyToAsync(stream, cancellationToken);
    }

    /// <summary>
    /// Writes a PcbLib file to a stream synchronously.
    /// </summary>
    /// <param name="library">The PCB footprint library to write.</param>
    /// <param name="stream">Destination stream.</param>
    /// <remarks>This instance is stateless and thread-safe.</remarks>
    public void Write(PcbLibrary library, Stream stream, CancellationToken cancellationToken = default)
    {
        using var cf = new CompoundFile();
        var sectionKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        WriteFileHeader(cf, library);
        WriteSectionKeys(cf, library, sectionKeys);
        WriteLibrary(cf, library, sectionKeys, cancellationToken);
        WriteAdditionalRootStreams(cf, library);

        cf.Save(stream);
    }

    private static void WriteFileHeader(CompoundFile cf, PcbLibrary library)
    {
        var headerStream = cf.RootStorage.AddStream("FileHeader");

        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        var versionText = "PCB 6.0 Binary Library File";
        writer.Write(versionText.Length);
        writer.WritePascalShortString(versionText);

        // String 1: Format version double (5.01) + 2 padding bytes
        writer.Write((byte)10); // length prefix
        writer.Write(5.01d);    // version double (8 bytes)
        writer.Write((short)0); // 2 padding bytes

        // String 2: Empty placeholder
        writer.Write((byte)0);

        // String 3: 8-character unique library identifier
        var uniqueId = library.UniqueId ?? "AAAAAAAA";
        var idBytes = System.Text.Encoding.ASCII.GetBytes(uniqueId);
        writer.Write((byte)idBytes.Length);
        writer.Write(idBytes);

        writer.Flush();
        headerStream.SetData(ms.ToArray());
    }

    private static void WriteSectionKeys(CompoundFile cf, PcbLibrary library, Dictionary<string, string> sectionKeys)
    {
        // Use preserved section keys if available, otherwise generate new ones
        if (library.SectionKeys != null && library.SectionKeys.Count > 0)
        {
            foreach (var kvp in library.SectionKeys)
                sectionKeys[kvp.Key] = kvp.Value;
        }

        // Build section keys for components that need them
        var componentsNeedingKeys = new List<IPcbComponent>();
        foreach (var component in library.Components)
        {
            if (sectionKeys.ContainsKey(component.Name))
            {
                componentsNeedingKeys.Add(component);
            }
            else
            {
                var sectionKey = GetSectionKeyFromName(component.Name);
                if (sectionKey != component.Name)
                {
                    sectionKeys[component.Name] = sectionKey;
                    componentsNeedingKeys.Add(component);
                }
            }
        }

        if (componentsNeedingKeys.Count == 0)
            return;

        var sectionKeysStream = cf.RootStorage.AddStream("SectionKeys");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        writer.Write(componentsNeedingKeys.Count);
        foreach (var component in componentsNeedingKeys)
        {
            writer.WritePascalString(component.Name);
            writer.WriteStringBlock(sectionKeys[component.Name]);
        }

        writer.Flush();
        sectionKeysStream.SetData(ms.ToArray());
    }

    private static void WriteLibrary(CompoundFile cf, PcbLibrary library, Dictionary<string, string> sectionKeys, CancellationToken cancellationToken = default)
    {
        var libraryStorage = cf.RootStorage.AddStorage("Library");

        // Write header (record count)
        WriteStorageHeader(libraryStorage, 1);

        // Write library data
        WriteLibraryData(cf, libraryStorage, library, sectionKeys, cancellationToken);

        // Write models
        WriteLibraryModels(libraryStorage, library);

        // Write additional library-level streams (ComponentParamsTOC, EmbeddedFonts, etc.)
        WriteAdditionalLibraryStreams(libraryStorage, library);
    }

    internal static void WriteStorageHeader(CFStorage storage, int recordCount)
    {
        var headerStream = storage.AddStream("Header");
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(recordCount);
        writer.Flush();
        headerStream.SetData(ms.ToArray());
    }

    private static void WriteLibraryData(CompoundFile cf, CFStorage libraryStorage, PcbLibrary library, Dictionary<string, string> sectionKeys, CancellationToken cancellationToken = default)
    {
        var dataStream = libraryStorage.AddStream("Data");
        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        // Generate library parameters from dictionary or defaults
        if (library.LibraryParametersOrdered is { Count: > 0 } ordered)
        {
            // The header is an ordered parameter list with duplicate RECORD=Board markers;
            // serialize it verbatim from the ordered model (no WEIGHT key is injected — the
            // PcbLib library header does not carry one; the footprint count follows separately).
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in ordered)
                sb.Append('|').Append(kvp.Key).Append('=').Append(kvp.Value);
            writer.WriteCStringParameterBlockRaw(sb.ToString());
        }
        else if (library.LibraryParameters != null && library.LibraryParameters.Count > 0)
        {
            var headerParams = new Dictionary<string, string>(library.LibraryParameters, StringComparer.OrdinalIgnoreCase);
            headerParams["WEIGHT"] = library.Components.Count.ToString();
            writer.WriteCStringParameterBlock(headerParams);
        }
        else
        {
            var headerParams = new Dictionary<string, string>
            {
                ["HEADER"] = "PCB 6.0 Binary Library File",
                ["WEIGHT"] = library.Components.Count.ToString()
            };
            writer.WriteCStringParameterBlock(headerParams);
        }

        // Write component count and names
        writer.Write((uint)library.Components.Count);
        foreach (var component in library.Components.Cast<PcbComponent>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteStringBlock(component.Name);
            WriteFootprint(cf, component, sectionKeys);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    private static void WriteFootprint(CompoundFile cf, PcbComponent component, Dictionary<string, string> sectionKeys)
    {
        var sectionKey = sectionKeys.TryGetValue(component.Name, out var key)
            ? key
            : GetSectionKeyFromName(component.Name);

        var footprintStorage = cf.RootStorage.AddStorage(sectionKey);

        // Write header (primitive count)
        var primitiveCount = component.Pads.Count + component.Tracks.Count +
                            component.Vias.Count + component.Arcs.Count +
                            component.Texts.Count + component.Fills.Count +
                            component.Regions.Count + component.ComponentBodies.Count;
        WriteStorageHeader(footprintStorage, primitiveCount);

        WriteFootprintParameters(footprintStorage, component);
        WriteWideStrings(footprintStorage, component);
        WriteFootprintData(footprintStorage, component);
        WriteUniqueIdPrimitiveInformation(footprintStorage, component);
        WriteAdditionalComponentStreams(footprintStorage, component);
    }

    private static void WriteFootprintParameters(CFStorage storage, PcbComponent component)
    {
        var paramsStream = storage.AddStream("Parameters");

        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        // Generate parameters from typed properties
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Merge any additional parameters first (typed properties override)
        if (component is PcbComponent { AdditionalParameters: not null } pcbComp)
        {
            foreach (var kvp in pcbComp.AdditionalParameters)
                parameters[kvp.Key] = kvp.Value;
        }
        parameters["PATTERN"] = component.Name;
        // Altium serializes HEIGHT as a mil-suffixed coordinate string (e.g. "0mil"),
        // not a raw integer, with trailing zeros trimmed.
        parameters["HEIGHT"] = FormatMilCoord(component.Height);
        if (!string.IsNullOrEmpty(component.Description))
            parameters["DESCRIPTION"] = component.Description;
        // Altium always emits ITEMGUID and REVISIONGUID, even when empty.
        parameters["ITEMGUID"] = component.ItemGUID ?? "";
        parameters["REVISIONGUID"] = component.ItemRevisionGUID ?? "";
        writer.WriteCStringParameterBlock(parameters);

        writer.Flush();
        paramsStream.SetData(ms.ToArray());
    }

    /// <summary>
    /// Formats a coordinate as an Altium mil-suffixed string with trailing zeros trimmed
    /// (e.g. "0mil", "19.685mil"), matching how Altium serializes PcbLib component HEIGHT.
    /// </summary>
    private static string FormatMilCoord(Coord coord)
        => coord.ToMils().ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + "mil";

    private static void WriteWideStrings(CFStorage storage, PcbComponent component)
    {
        var wideStringsStream = storage.AddStream("WideStrings");

        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        var parameters = new Dictionary<string, string>();
        var textIndex = 0;

        foreach (var text in component.Texts)
        {
            var encoded = string.Join(",", text.Text.Select(c => ((int)c).ToString()));
            parameters[$"ENCODEDTEXT{textIndex}"] = encoded;
            textIndex++;
        }

        writer.WriteCStringParameterBlock(parameters);

        writer.Flush();
        wideStringsStream.SetData(ms.ToArray());
    }

    private static void WriteFootprintData(CFStorage storage, PcbComponent component)
    {
        var dataStream = storage.AddStream("Data");

        using var ms = new MemoryStream();
        using var writer = new BinaryFormatWriter(ms, leaveOpen: true);

        // Write pattern name
        writer.WriteStringBlock(component.Name);

        // Write primitives
        foreach (var arc in component.Arcs)
        {
            writer.Write((byte)1); // Arc object ID
            WriteArc(writer, (PcbArc)arc);
        }

        foreach (var pad in component.Pads)
        {
            writer.Write((byte)2); // Pad object ID
            WritePad(writer, (PcbPad)pad);
        }

        foreach (var via in component.Vias)
        {
            writer.Write((byte)3); // Via object ID
            WriteVia(writer, (PcbVia)via);
        }

        foreach (var track in component.Tracks)
        {
            writer.Write((byte)4); // Track object ID
            WriteTrack(writer, (PcbTrack)track);
        }

        var textIndex = 0;
        foreach (var text in component.Texts)
        {
            writer.Write((byte)5); // Text object ID
            WriteText(writer, (PcbText)text, textIndex++);
        }

        foreach (var fill in component.Fills)
        {
            writer.Write((byte)6); // Fill object ID
            WriteFill(writer, (PcbFill)fill);
        }

        foreach (var region in component.Regions)
        {
            writer.Write((byte)11); // Region object ID
            WriteRegion(writer, (PcbRegion)region);
        }

        foreach (var body in component.ComponentBodies)
        {
            writer.Write((byte)12); // ComponentBody object ID
            WriteComponentBody(writer, (PcbComponentBody)body);
        }

        writer.Flush();
        dataStream.SetData(ms.ToArray());
    }

    internal static void WriteCommonPrimitiveData(BinaryFormatWriter writer, int layer, ushort flags = 0)
    {
        writer.Write((byte)layer);
        writer.Write(flags);
        writer.WriteFill(0xFF, 10); // 10 bytes of 0xFF
    }

    /// <summary>
    /// Encodes boolean properties into the flags word. All bits are computed from properties.
    /// </summary>
    internal static ushort EncodeFlags(bool isLocked, bool isTentingTop,
        bool isTentingBottom, bool isKeepout)
    {
        ushort flags = 0;
        if (!isLocked)
            flags |= PcbBinaryConstants.FlagUnlocked;
        if (isTentingTop)
            flags |= PcbBinaryConstants.FlagTentingTop;
        if (isTentingBottom)
            flags |= PcbBinaryConstants.FlagTentingBottom;
        if (isKeepout)
            flags |= PcbBinaryConstants.FlagKeepout;
        return flags;
    }

    internal static void WriteArc(BinaryFormatWriter writer, PcbArc arc)
    {
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(arc.IsLocked, arc.IsTentingTop,
                arc.IsTentingBottom, arc.IsKeepout);
            WriteCommonPrimitiveData(w, arc.Layer, flags); // 0-12
            w.WriteCoordPoint(arc.Center);                 // 13-20
            w.WriteCoord(arc.Radius);                      // 21-24
            w.Write(arc.StartAngle);                       // 25-32
            w.Write(arc.EndAngle);                         // 33-40
            w.WriteCoord(arc.Width);                       // 41-44
            // Extended tail (offsets 45-59)
            w.Write((short)0);                              // 45-46 subpoly index
            w.WriteCoord(arc.SolderMaskExpansion);          // 47-50 solder mask expansion
            w.Write((byte)0);                               // 51 paste mask expansion
            w.Write(new byte[] { 0x01, 0x00, 0x00, 0x01 }); // 52-55 v7 layer id
            w.Write((byte)arc.KeepoutRestrictions);         // 56 keepout restrictions
            w.Write(new byte[3]);                           // 57-59 reserved
        });
    }

    internal static void WritePad(BinaryFormatWriter writer, PcbPad pad)
    {
        // Pad has a complex multi-block structure
        writer.WriteStringBlock(pad.Designator ?? string.Empty);
        writer.WriteBlock(new byte[] { 0 }); // reserved block 1
        writer.WriteStringBlock("|&|0"); // net string (always this in footprint libraries)
        writer.WriteBlock(new byte[] { 0 }); // reserved block 2

        // Main pad data block (114 bytes standard)
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(pad.IsLocked, pad.IsTentingTop,
                pad.IsTentingBottom, pad.IsKeepout);
            WriteCommonPrimitiveData(w, pad.Layer, flags);
            w.WriteCoordPoint(pad.Location);
            w.WriteCoordPoint(pad.SizeTop);
            w.WriteCoordPoint(pad.SizeMiddle);
            w.WriteCoordPoint(pad.SizeBottom);
            w.WriteCoord(pad.HoleSize);
            w.Write((byte)pad.ShapeTop);
            w.Write((byte)pad.ShapeMiddle);
            w.Write((byte)pad.ShapeBottom);
            w.Write(pad.Rotation);
            w.Write(pad.IsPlated);
            // Offsets 61-201: extended tail (thermal relief, mask expansion + modes,
            // jumper, hole tolerances) built from a fixed template with the typed/semantic
            // fields overlaid at their exact offsets.
            w.Write(BuildPadExtendedTail(pad));
        });

        // Size/shape block (596 bytes standard, or empty if not present in original)
        if (!pad.HasSizeShapeBlock)
        {
            writer.WriteBlock(Array.Empty<byte>());
            return;
        }

        writer.WriteBlock(w =>
        {
            // 29 X sizes for internal copper layers (offset 0-115)
            for (var i = 0; i < 29; i++) w.Write(pad.LayerXSizes[i]);
            // 29 Y sizes for internal copper layers (offset 116-231)
            for (var i = 0; i < 29; i++) w.Write(pad.LayerYSizes[i]);
            // 29 shapes for internal copper layers (offset 232-260)
            for (var i = 0; i < 29; i++) w.Write(pad.InternalLayerShapes[i]);
            // Reserved byte (offset 261)
            w.Write((byte)0);
            // Hole shape (offset 262)
            w.Write((byte)pad.HoleType);
            // Hole slot length (offset 263-266)
            w.Write(pad.HoleSlotLength);
            // Hole rotation (offset 267-274)
            w.Write(pad.HoleRotation);
            // 32 X offsets from hole center (offset 275-402)
            for (var i = 0; i < 32; i++) w.Write(pad.OffsetXFromHoleCenter[i]);
            // 32 Y offsets from hole center (offset 403-530)
            for (var i = 0; i < 32; i++) w.Write(pad.OffsetYFromHoleCenter[i]);
            // HasRoundedRect flag (offset 531)
            w.Write(pad.HasRoundedRectByte);
            // 32 per-layer shapes (offset 532-563)
            for (var i = 0; i < 32; i++) w.Write(pad.PerLayerShapes[i]);
            // 32 corner radius percentages (offset 564-595)
            for (var i = 0; i < 32; i++) w.Write(pad.PerLayerCornerRadii[i]);
        });
    }

    // First offset of the pad SubRecord-5 extended tail (after the basic geometry).
    private const int PadExtendedStart = 61;

    // Canonical 141-byte pad SubRecord-5 extended tail (offsets 61-201), captured from a
    // standard Altium pad. The typed/semantic fields are overlaid at their exact offsets in
    // BuildPadExtendedTail; the remaining bytes are reserved / pad-cache / footprint-identity
    // values reproduced verbatim so the record matches Altium's 202-byte layout.
    private static readonly byte[] PadExtendedTailTemplate =
    {
        // 61-76
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xA0, 0x86, 0x01, 0x00, 0x04, 0x00, 0xA0, 0x86, 0x01,
        // 77-92
        0x00, 0x40, 0x0D, 0x03, 0x00, 0x40, 0x0D, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x9C, 0x00,
        // 93-108
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // 109-124
        0x00, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x03, 0x01, 0x00, 0x00, 0x00, 0x40, 0x9C, 0x00, 0x00,
        // 125-140
        0x00, 0x64, 0x9A, 0x92, 0x26, 0x10, 0xC7, 0xE4, 0x41, 0xA3, 0x2B, 0x29, 0x17, 0xA5, 0x35, 0x2E,
        // 141-156
        0x67, 0x7F, 0xAB, 0x21, 0x20, 0xC3, 0x0B, 0x32, 0x47, 0xAD, 0xCE, 0x6C, 0xB7, 0xB8, 0xC9, 0x7E,
        // 157-172
        0x68, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF, 0x7F, 0x00, 0x01, 0x1A,
        // 173-188
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00,
        // 189-201
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    /// <summary>
    /// Builds the pad SubRecord-5 extended tail (offsets 61-201) by overlaying the typed
    /// semantic fields onto the canonical template.
    /// </summary>
    private static byte[] BuildPadExtendedTail(PcbPad pad)
    {
        var ext = (byte[])PadExtendedTailTemplate.Clone();
        void PutI32(int offset, int value) => BitConverter.GetBytes(value).CopyTo(ext, offset - PadExtendedStart);
        void PutI16(int offset, short value) => BitConverter.GetBytes(value).CopyTo(ext, offset - PadExtendedStart);

        ext[62 - PadExtendedStart] = (byte)pad.Mode;                      // 62: pad stack mode
        ext[67 - PadExtendedStart] = (byte)pad.PowerPlaneConnectStyle;    // 67: plane connection style
        PutI32(68, pad.ReliefConductorWidth.ToRaw());                    // 68-71
        PutI16(72, (short)pad.ReliefEntries);                            // 72-73
        PutI32(74, pad.ReliefAirGap.ToRaw());                            // 74-77
        PutI32(78, pad.PowerPlaneReliefExpansion.ToRaw());               // 78-81
        PutI32(82, pad.PowerPlaneClearance.ToRaw());                     // 82-85
        PutI32(86, pad.PasteMaskExpansion.ToRaw());                      // 86-89 (manual paste)
        PutI32(90, pad.SolderMaskExpansion.ToRaw());                     // 90-93 (manual solder)
        ext[101 - PadExtendedStart] = (byte)pad.PasteMaskExpansionMode;  // 101
        ext[102 - PadExtendedStart] = (byte)pad.SolderMaskExpansionMode; // 102
        PutI16(110, (short)pad.JumperID);                                // 110-111
        PutI32(162, pad.HolePositiveTolerance.ToRaw());                  // 162-165
        PutI32(166, pad.HoleNegativeTolerance.ToRaw());                  // 166-169
        return ext;
    }

    internal static void WriteVia(BinaryFormatWriter writer, PcbVia via)
    {
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(via.IsLocked, via.IsTentingTop,
                via.IsTentingBottom, via.IsKeepout);
            WriteCommonPrimitiveData(w, via.Layer, flags); // offsets 0-12
            // Offsets 13-320: full 321-byte via record built from a template with the typed
            // fields overlaid (geometry, layers, thermal relief, mask expansions, per-layer
            // diameters, drill tolerances). Reserved/cache/identity regions come from template.
            w.Write(BuildViaExtended(via));
        });
    }

    // Canonical 321-byte via SubRecord-1 (offsets 0-320), captured from a standard Altium via.
    private static readonly byte[] ViaSr1Template =
    {
        0x4A, 0x0C, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, // 0
        0x00, 0x00, 0x00, 0x00, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xF0, 0x49, 0x02, 0x00, 0x02, 0x20, 0x00, // 16
        0xA0, 0x86, 0x01, 0x00, 0x04, 0x00, 0xA0, 0x86, 0x01, 0x00, 0x40, 0x0D, 0x03, 0x00, 0x40, 0x0D, // 32
        0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x9C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 48
        0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 64
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 80
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 96
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 112
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 128
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 144
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 160
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, // 176
        0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0xE0, 0x93, 0x04, 0x00, 0x0F, 0x00, 0x03, 0x01, 0x00, // 192
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 208
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 224
        0x00, 0x00, 0x40, 0x9C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A, 0x00, // 240
        0x00, 0x00, 0x00, 0x80, 0x63, 0xD4, 0xE4, 0x65, 0xC4, 0xF4, 0x4E, 0x8B, 0xAD, 0xA7, 0xCE, 0x97, // 256
        0xDC, 0x40, 0xDA, 0xA5, 0xB1, 0xE3, 0xB2, 0x84, 0x25, 0x11, 0x43, 0x83, 0xDB, 0x2B, 0x6A, 0x87, // 272
        0x7C, 0xB1, 0x74, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF, 0x7F, 0x00, 0x00, 0x00, 0x00, 0x00, // 288
        0x1E, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 304
        0x01,                                                                                           // 320
    };

    /// <summary>
    /// Builds the via SubRecord-1 body (offsets 13-320) by overlaying the typed fields onto
    /// the canonical template.
    /// </summary>
    private static byte[] BuildViaExtended(PcbVia via)
    {
        var b = (byte[])ViaSr1Template.Clone();
        void PutI32(int off, int v) => BitConverter.GetBytes(v).CopyTo(b, off);

        PutI32(13, via.Location.X.ToRaw());
        PutI32(17, via.Location.Y.ToRaw());
        PutI32(21, via.Diameter.ToRaw());
        PutI32(25, via.HoleSize.ToRaw());
        b[29] = (byte)via.StartLayer;
        b[30] = (byte)via.EndLayer;
        b[31] = (byte)via.PowerPlaneConnectStyle;
        PutI32(32, via.ThermalReliefAirGap.ToRaw());
        b[36] = (byte)via.ThermalReliefConductors;
        PutI32(38, via.ThermalReliefConductorsWidth.ToRaw());
        PutI32(42, via.PowerPlaneReliefExpansion.ToRaw());
        PutI32(46, via.PowerPlaneClearance.ToRaw());
        PutI32(50, via.PasteMaskExpansion.ToRaw());
        PutI32(54, via.SolderMaskExpansion.ToRaw());
        b[66] = (byte)via.SolderMaskExpansionMode;
        b[74] = (byte)via.Mode;
        var defaultDiameter = via.Diameter.ToRaw();
        for (var i = 0; i < 32; i++)
        {
            var d = via.Diameters[i].ToRaw();
            PutI32(75 + i * 4, d != 0 ? d : defaultDiameter); // simple vias: all layers = via diameter
        }
        PutI32(242, via.SolderMaskExpansion.ToRaw()); // back-side mask (symmetric)
        b[258] = (byte)(via.SolderMaskExpansionFromHoleEdge ? 1 : 0);
        PutI32(291, via.HolePositiveTolerance.ToRaw());
        PutI32(295, via.HoleNegativeTolerance.ToRaw());
        b[312] = (byte)via.DrillLayerPairType;
        return b[13..];
    }

    internal static void WriteTrack(BinaryFormatWriter writer, PcbTrack track)
    {
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(track.IsLocked, track.IsTentingTop,
                track.IsTentingBottom, track.IsKeepout);
            WriteCommonPrimitiveData(w, track.Layer, flags); // 0-12
            w.WriteCoordPoint(track.Start);                  // 13-20
            w.WriteCoordPoint(track.End);                    // 21-28
            w.WriteCoord(track.Width);                       // 29-32
            // Extended tail (offsets 33-48)
            w.Write((short)0);                              // 33-34 subpoly index
            w.WriteCoord(track.SolderMaskExpansion);        // 35-38 solder mask expansion
            w.Write((short)0);                              // 39-40 paste mask expansion
            w.Write(new byte[] { 0x0D, 0x00, 0x03, 0x01 }); // 41-44 v7 layer id
            w.Write((byte)track.KeepoutRestrictions);       // 45 keepout restrictions
            w.Write(new byte[3]);                           // 46-48 reserved
        });
    }

    internal static void WriteText(BinaryFormatWriter writer, PcbText text, int wideStringIndex)
    {
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(text.IsLocked, text.IsTentingTop,
                text.IsTentingBottom, text.IsKeepout);
            WriteCommonPrimitiveData(w, text.Layer, flags); // offsets 0-12
            // Offsets 13-251: geometry, font, text-box, barcode block and frame tail built
            // from a fixed template with the typed/semantic fields overlaid at their offsets.
            w.Write(BuildTextExtended(text, wideStringIndex));
        });

        writer.WriteStringBlock(text.Text);
    }

    // Canonical 252-byte text SubRecord-1 (offsets 0-251), captured from a standard Altium
    // text record. BuildTextExtended overlays the typed fields and returns offsets 13-251
    // (the common header 0-12 is written separately by WriteCommonPrimitiveData).
    private static readonly byte[] TextSr1Template =
    {
        0x21, 0x0C, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, // 0
        0x00, 0x50, 0x8E, 0xF4, 0xFF, 0x80, 0x1A, 0x06, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 16
        0x80, 0x46, 0x40, 0x00, 0x40, 0x9C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x41, 0x00, // 32
        0x72, 0x00, 0x69, 0x00, 0x61, 0x00, 0x6C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 48
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 64
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 80
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 96
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xCE, 0xE5, 0x29, 0x00, // 112
        0x7F, 0x52, 0x07, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0xA0, 0x37, 0xA0, 0x00, 0x20, 0x0B, 0x20, // 128
        0x00, 0x40, 0x0D, 0x03, 0x00, 0x40, 0x0D, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, // 144
        0x00, 0x41, 0x00, 0x72, 0x00, 0x69, 0x00, 0x61, 0x00, 0x6C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 160
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 176
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 192
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 208
        0x00, 0x01, 0x06, 0x00, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80, // 224
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x50, 0x8E, 0xF4, 0xFF,                         // 240
    };

    /// <summary>
    /// Builds the text SubRecord-1 extended body (offsets 13-251) by overlaying the typed
    /// fields onto the canonical template.
    /// </summary>
    private static byte[] BuildTextExtended(PcbText text, int wideStringIndex)
    {
        var b = (byte[])TextSr1Template.Clone();
        void PutI32(int off, int v) => BitConverter.GetBytes(v).CopyTo(b, off);
        void PutI16(int off, short v) => BitConverter.GetBytes(v).CopyTo(b, off);
        void PutDbl(int off, double v) => BitConverter.GetBytes(v).CopyTo(b, off);
        void PutFont(int off, string? s)
        {
            Array.Clear(b, off, 64);
            if (string.IsNullOrEmpty(s)) return;
            var bytes = System.Text.Encoding.Unicode.GetBytes(s);
            Array.Copy(bytes, 0, b, off, Math.Min(bytes.Length, 62)); // leave a UTF-16 null terminator
        }

        PutI32(13, text.Location.X.ToRaw());
        PutI32(17, text.Location.Y.ToRaw());
        PutI32(21, text.Height.ToRaw());
        PutI16(25, (short)text.StrokeFont);
        PutDbl(27, text.Rotation);
        b[35] = (byte)(text.IsMirrored ? 1 : 0);
        PutI32(36, text.StrokeWidth.ToRaw());
        b[40] = (byte)(text.IsComment ? 1 : 0);
        b[41] = (byte)(text.IsDesignator ? 1 : 0);
        b[42] = (byte)text.CharSet;
        b[43] = (byte)text.TextKind;
        b[44] = (byte)(text.FontBold ? 1 : 0);
        b[45] = (byte)(text.FontItalic ? 1 : 0);
        PutFont(46, text.FontName);
        b[110] = (byte)(text.IsInverted ? 1 : 0);
        PutI32(111, text.InvertedBorder.ToRaw());
        PutI32(115, wideStringIndex);
        PutI32(119, text.UnionIndex);
        b[123] = (byte)(text.UseInvertedRectangle ? 1 : 0);
        PutI32(124, text.InvertedRectWidth.ToRaw());
        PutI32(128, text.InvertedRectHeight.ToRaw());
        b[132] = (byte)text.InvertedRectJustification;
        PutI32(133, text.InvertedRectTextOffset.ToRaw());
        PutI32(137, text.BarCodeFullWidth.ToRaw());
        PutI32(141, text.BarCodeFullHeight.ToRaw());
        PutI32(145, text.BarCodeXMargin.ToRaw());
        PutI32(149, text.BarCodeYMargin.ToRaw());
        PutI32(153, text.BarCodeMinWidth.ToRaw());
        b[157] = (byte)text.BarCodeKind;
        b[158] = (byte)text.BarCodeRenderMode;
        b[159] = (byte)(text.BarCodeInverted ? 1 : 0);
        b[160] = (byte)text.TextKind; // bc[23]: authoritative text kind
        PutFont(161, text.BarCodeFontName);
        b[225] = (byte)(text.BarCodeShowText ? 1 : 0);
        b[230] = (byte)(text.IsFrame ? 1 : 0);
        b[231] = (byte)(text.IsOffsetBorder ? 1 : 0);
        b[240] = (byte)(text.IsJustificationValid ? 1 : 0);
        b[241] = (byte)(text.AdvanceSnapping ? 1 : 0);
        PutI32(244, text.SnapPointX.ToRaw());
        PutI32(248, text.SnapPointY.ToRaw());

        return b[13..];
    }

    internal static void WriteFill(BinaryFormatWriter writer, PcbFill fill)
    {
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(fill.IsLocked, fill.IsTentingTop,
                fill.IsTentingBottom, fill.IsKeepout);
            WriteCommonPrimitiveData(w, fill.Layer, flags); // 0-12
            w.WriteCoordPoint(fill.Corner1);                // 13-20
            w.WriteCoordPoint(fill.Corner2);                // 21-28
            w.Write(fill.Rotation);                         // 29-36
            // Extended tail (offsets 37-49)
            w.WriteCoord(fill.SolderMaskExpansion);         // 37-40 solder mask expansion
            w.Write((byte)0);                               // 41 paste mask expansion
            w.Write(new byte[] { 0x01, 0x00, 0x00, 0x01 }); // 42-45 v7 layer id
            w.Write((byte)fill.KeepoutRestrictions);        // 46 keepout restrictions
            w.Write(new byte[3]);                           // 47-49 reserved
        });
    }

    internal static void WriteRegion(BinaryFormatWriter writer, PcbRegion region)
    {
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(region.IsLocked, region.IsTentingTop,
                region.IsTentingBottom, region.IsKeepout);
            WriteCommonPrimitiveData(w, region.Layer, flags);

            // Structure: uint32 prefix + byte prefix + nested parameter block + outline vertices (doubles)
            w.Write((uint)0); // reserved prefix 1
            w.Write((byte)0); // reserved prefix 2

            // Round-trip: serialize the captured ordered parameter list verbatim (preserves key
            // order, duplicates and Altium's mil formatting). New regions fall back to typed fields.
            if (region.RawParametersOrdered is { Count: > 0 } orderedRegionParams)
            {
                var psb = new System.Text.StringBuilder();
                for (var i = 0; i < orderedRegionParams.Count; i++)
                {
                    if (i > 0) psb.Append('|');
                    psb.Append(orderedRegionParams[i].Key).Append('=').Append(orderedRegionParams[i].Value);
                }
                w.WriteCStringParameterBlockRaw(psb.ToString());
                w.Write((uint)region.Outline.Count);
                foreach (var point in region.Outline)
                {
                    w.Write((double)point.X.ToRaw());
                    w.Write((double)point.Y.ToRaw());
                }
                return;
            }

            // Generate parameters from typed properties
            var regionParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Merge any additional parameters first (typed properties override)
            if (region.AdditionalParameters != null)
            {
                foreach (var kvp in region.AdditionalParameters)
                    regionParams[kvp.Key] = kvp.Value;
            }
            if (region.Kind != 0)
                regionParams["KIND"] = region.Kind.ToString();
            if (!string.IsNullOrEmpty(region.Net))
                regionParams["NET"] = region.Net;
            if (!string.IsNullOrEmpty(region.UniqueId))
                regionParams["UNIQUEID"] = region.UniqueId;
            if (!string.IsNullOrEmpty(region.Name))
                regionParams["NAME"] = region.Name;
            w.WriteCStringParameterBlock(regionParams);

            // Write outline vertices as doubles (Altium PCB format)
            w.Write((uint)region.Outline.Count);
            foreach (var point in region.Outline)
            {
                w.Write((double)point.X.ToRaw());
                w.Write((double)point.Y.ToRaw());
            }
        });
    }

    internal static void WriteComponentBody(BinaryFormatWriter writer, PcbComponentBody body)
    {
        writer.WriteBlock(w =>
        {
            var flags = EncodeFlags(body.IsLocked, body.IsTentingTop,
                body.IsTentingBottom, body.IsKeepout);
            var binaryLayer = LayerNameToByte(body.LayerName);
            WriteCommonPrimitiveData(w, binaryLayer, flags);

            // Structure: uint32 prefix + byte prefix + nested parameter block + outline vertices (doubles)
            w.Write((uint)0); // reserved prefix 1
            w.Write((byte)0); // reserved prefix 2

            // Round-trip: serialize the captured ordered parameter list verbatim. It preserves
            // key order, duplicate keys (e.g. ARCRESOLUTION) and Altium's mil formatting that a
            // flattened typed view cannot reproduce. New bodies fall back to the typed fields below.
            if (body.RawParametersOrdered is { Count: > 0 } orderedBodyParams)
            {
                var psb = new System.Text.StringBuilder();
                for (var i = 0; i < orderedBodyParams.Count; i++)
                {
                    if (i > 0) psb.Append('|');
                    psb.Append(orderedBodyParams[i].Key).Append('=').Append(orderedBodyParams[i].Value);
                }
                w.WriteCStringParameterBlockRaw(psb.ToString());
                w.Write((uint)body.Outline.Count);
                foreach (var point in body.Outline)
                {
                    w.Write((double)point.X.ToRaw());
                    w.Write((double)point.Y.ToRaw());
                }
                return;
            }

            // Generate ALL parameters from typed properties
            var bodyParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Merge any additional parameters first (typed properties override)
            if (body.AdditionalParameters != null)
            {
                foreach (var kvp in body.AdditionalParameters)
                    bodyParams[kvp.Key] = kvp.Value;
            }
            bodyParams["V7_LAYER"] = body.LayerName ?? "MECHANICAL1";
            bodyParams["NAME"] = body.Name ?? string.Empty;
            bodyParams["KIND"] = body.Kind.ToString();
            bodyParams["SUBPOLYINDEX"] = body.SubPolyIndex.ToString();
            bodyParams["UNIONINDEX"] = body.UnionIndex.ToString();
            bodyParams["ARCRESOLUTION"] = body.ArcResolution.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bodyParams["ISSHAPEBASED"] = body.IsShapeBased ? "TRUE" : "FALSE";
            bodyParams["CAVITYHEIGHT"] = body.CavityHeight.ToRaw().ToString();
            bodyParams["STANDOFFHEIGHT"] = body.StandoffHeight.ToRaw().ToString();
            bodyParams["OVERALLHEIGHT"] = body.OverallHeight.ToRaw().ToString();
            bodyParams["BODYCOLOR3D"] = body.BodyColor3D.ToString();
            bodyParams["BODYOPACITY3D"] = body.BodyOpacity3D.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bodyParams["BODYPROJECTION"] = body.BodyProjection.ToString();
            bodyParams["MODELID"] = body.ModelId ?? string.Empty;
            bodyParams["MODEL.EMBED"] = body.ModelEmbed ? "TRUE" : "FALSE";
            bodyParams["MODEL.2D.X"] = body.Model2DLocation.X.ToRaw().ToString();
            bodyParams["MODEL.2D.Y"] = body.Model2DLocation.Y.ToRaw().ToString();
            bodyParams["MODEL.2D.ROTATION"] = body.Model2DRotation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bodyParams["MODEL.3D.ROTX"] = body.Model3DRotX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bodyParams["MODEL.3D.ROTY"] = body.Model3DRotY.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bodyParams["MODEL.3D.ROTZ"] = body.Model3DRotZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bodyParams["MODEL.3D.DZ"] = body.Model3DDz.ToRaw().ToString();
            bodyParams["MODEL.CHECKSUM"] = body.ModelChecksum.ToString();
            bodyParams["MODEL.NAME"] = body.ModelName ?? string.Empty;
            bodyParams["MODEL.MODELTYPE"] = body.ModelType.ToString();
            bodyParams["MODEL.MODELSOURCE"] = body.ModelSource ?? string.Empty;
            if (!string.IsNullOrEmpty(body.Identifier))
                bodyParams["IDENTIFIER"] = body.Identifier;
            if (!string.IsNullOrEmpty(body.Texture))
                bodyParams["TEXTURE"] = body.Texture;
            w.WriteCStringParameterBlock(bodyParams);

            // Write outline vertices as doubles (Altium PCB format)
            w.Write((uint)body.Outline.Count);
            foreach (var point in body.Outline)
            {
                w.Write((double)point.X.ToRaw());
                w.Write((double)point.Y.ToRaw());
            }
        });
    }

    private static void WriteUniqueIdPrimitiveInformation(CFStorage storage, PcbComponent component)
    {
        // UniqueIdPrimitiveInformation is optional - skip for new files
    }

    private static void WriteLibraryModels(CFStorage libraryStorage, PcbLibrary library)
    {
        var modelsStorage = libraryStorage.AddStorage("Models");
        var modelCount = library.Models.Count;

        WriteStorageHeader(modelsStorage, modelCount);

        // Write Data stream: model metadata as length-prefixed C-string parameter blocks
        using var dataMs = new MemoryStream();
        foreach (var model in library.Models)
        {
            var paramStr = string.Join("|",
                $"EMBED={( model.IsEmbedded ? "TRUE" : "FALSE" )}",
                $"MODELSOURCE={model.ModelSource}",
                $"ID={model.Id}",
                $"ROTX={model.RotationX.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}",
                $"ROTY={model.RotationY.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}",
                $"ROTZ={model.RotationZ.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}",
                $"DZ={model.Dz}",
                $"CHECKSUM={model.Checksum}",
                $"NAME={model.Name}");
            var paramBytes = System.Text.Encoding.ASCII.GetBytes(paramStr + '\0');
            var lenBytes = BitConverter.GetBytes(paramBytes.Length);
            dataMs.Write(lenBytes);
            dataMs.Write(paramBytes);
        }
        var dataStream = modelsStorage.AddStream("Data");
        dataStream.SetData(dataMs.ToArray());

        // Write numbered model streams: zlib-compressed STEP text
        for (var i = 0; i < library.Models.Count; i++)
        {
            var model = library.Models[i];
            byte[] compressedData;
            if (!string.IsNullOrEmpty(model.StepData))
            {
                var stepBytes = System.Text.Encoding.UTF8.GetBytes(model.StepData);
                using var outMs = new MemoryStream();
                using (var zs = new System.IO.Compression.ZLibStream(outMs, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                {
                    zs.Write(stepBytes);
                }
                compressedData = outMs.ToArray();
            }
            else
            {
                compressedData = Array.Empty<byte>();
            }

            var modelStream = modelsStorage.AddStream(i.ToString());
            modelStream.SetData(compressedData);
        }
    }

    private static void WriteAdditionalComponentStreams(CFStorage storage, PcbComponent component)
    {
        if (component is PcbComponent { AdditionalStreams: not null } pcbComp)
            WriteAdditionalStreams(storage, pcbComp.AdditionalStreams);
    }

    private static void WriteAdditionalLibraryStreams(CFStorage libraryStorage, PcbLibrary library)
    {
        if (library.AdditionalLibraryStreams != null)
            WriteAdditionalStreams(libraryStorage, library.AdditionalLibraryStreams);
    }

    private static void WriteAdditionalRootStreams(CompoundFile cf, PcbLibrary library)
    {
        if (library.AdditionalRootStreams != null)
            WriteAdditionalStreams(cf.RootStorage, library.AdditionalRootStreams);
    }

    internal static void WriteAdditionalStreams(CFStorage storage, Dictionary<string, byte[]> streams)
    {
        foreach (var kvp in streams)
        {
            if (kvp.Key.Contains('/'))
            {
                var parts = kvp.Key.Split('/', 2);
                if (!storage.TryGetStorage(parts[0], out var subStorage))
                    subStorage = storage.AddStorage(parts[0]);
                var stream = subStorage.AddStream(parts[1]);
                stream.SetData(kvp.Value);
            }
            else
            {
                var stream = storage.AddStream(kvp.Key);
                stream.SetData(kvp.Value);
            }
        }
    }

    /// <summary>
    /// Maps a V7_LAYER string (e.g., "MECHANICAL1", "TOP", "MULTILAYER") to the binary layer byte.
    /// </summary>
    internal static byte LayerNameToByte(string? layerName)
    {
        if (string.IsNullOrEmpty(layerName))
            return 0;

        // Normalize to uppercase for case-insensitive matching
        var name = layerName.ToUpperInvariant().Replace(" ", "").Replace("-", "");

        // Check common mechanical layers first (most common for ComponentBody)
        if (name.StartsWith("MECHANICAL") && int.TryParse(name.AsSpan("MECHANICAL".Length), out var mechNum) && mechNum >= 1 && mechNum <= 16)
            return (byte)(56 + mechNum); // Mechanical1=57, Mechanical16=72

        return name switch
        {
            "TOPLAYER" or "TOP" => 1,
            "BOTTOMLAYER" or "BOTTOM" => 32,
            "TOPOVERLAY" => 33,
            "BOTTOMOVERLAY" => 34,
            "TOPPASTE" => 35,
            "BOTTOMPASTE" => 36,
            "TOPSOLDER" => 37,
            "BOTTOMSOLDER" => 38,
            "DRILLGUIDE" => 55,
            "KEEPOUTLAYER" or "KEEPOUT" => 56,
            "DRILLDRAWING" => 73,
            "MULTILAYER" => 74,
            _ when name.StartsWith("MIDLAYER") && int.TryParse(name.AsSpan("MIDLAYER".Length), out var midNum) && midNum >= 1 && midNum <= 30
                => (byte)(1 + midNum), // MidLayer1=2, MidLayer30=31
            _ when name.StartsWith("MID") && int.TryParse(name.AsSpan("MID".Length), out var mid2Num) && mid2Num >= 1 && mid2Num <= 30
                => (byte)(1 + mid2Num),
            _ when name.StartsWith("INTERNALPLANE") && int.TryParse(name.AsSpan("INTERNALPLANE".Length), out var planeNum) && planeNum >= 1 && planeNum <= 16
                => (byte)(38 + planeNum), // InternalPlane1=39, InternalPlane16=54
            _ => 0 // NoLayer
        };
    }

    private static string GetSectionKeyFromName(string name) =>
        WriterUtilities.GetSectionKeyFromName(name);
}

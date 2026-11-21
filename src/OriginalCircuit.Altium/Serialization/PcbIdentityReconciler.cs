using System.Globalization;
using OriginalCircuit.Altium.Models.Pcb;

namespace OriginalCircuit.Altium.Serialization;

/// <summary>
/// Keeps a PcbDoc's ordinal-keyed object-identity streams (<c>PrimitiveGuids</c>,
/// <c>UniqueIDPrimitiveInformation</c>, <c>ExtendedPrimitiveInformation</c>) consistent with the
/// primitive collections across edits.
/// <para>
/// Those streams bind each record to a primitive by its per-type ordinal (nth Track, nth Pad, …), so
/// removing, adding or reordering a primitive shifts every later primitive one slot and the records then
/// point at the wrong primitives. <see cref="Attach"/> runs once after a document is read — while the
/// ordinals are still valid — and pins each record to the primitive <em>instance</em> it describes. The
/// instance carries its identity through any collection change, so on save the <c>Build…</c> methods
/// re-emit the records <em>in their original stream order</em>, each re-ordinaled to its primitive's
/// current position; records whose primitive was removed drop out, and newly added primitives of an
/// already-tracked type get a fresh GUID (there is no meaningful short-id / mask-override to mint).
/// </para>
/// <para>
/// Order is preserved verbatim because Altium does not group these entries by type — a real board
/// interleaves them in creation order with scattered ordinals — so an unedited document rebuilds to the
/// exact source bytes: every record keeps its place and its unchanged ordinal, and unmanaged entries
/// (nets, components, classes, document singletons, …) pass through untouched.
/// </para>
/// </summary>
internal static class PcbIdentityReconciler
{
    // PrimitiveGuids type tag -> the drawable collection whose nth element the ordinal addresses.
    // Low byte is the primitive object-id for Arc..Fill; Region/body and the shape-based variants use
    // Altium's own class ids. Values verified against the corpus (counts match the collections exactly).
    private const uint TypeArc = 0x0D01, TypePad = 0x0E02, TypeVia = 0x0F03, TypeTrack = 0x1004,
        TypeText = 0x1105, TypeFill = 0x1206, TypeRegion = 0x1359, TypeShapeRegion = 0x1459,
        TypeComponentBody = 0x155A, TypeShapeComponentBody = 0x165A;

    private static readonly uint[] ManagedGuidTypes =
    {
        TypeArc, TypePad, TypeVia, TypeTrack, TypeText, TypeFill,
        TypeRegion, TypeShapeRegion, TypeComponentBody, TypeShapeComponentBody,
    };

    private static IReadOnlyList<object>? GuidCollection(PcbDocument d, uint typeId) => typeId switch
    {
        TypeArc => d.Arcs,
        TypePad => d.Pads,
        TypeVia => d.Vias,
        TypeTrack => d.Tracks,
        TypeText => d.Texts,
        TypeFill => d.Fills,
        TypeRegion => d.Regions,
        TypeShapeRegion => d.ShapeBasedRegions,
        TypeComponentBody => d.ComponentBodies,
        TypeShapeComponentBody => d.ShapeBasedComponentBodies,
        _ => null,
    };

    // UniqueID / Extended PRIMITIVEOBJECTID token -> the collection whose nth element the ordinal addresses.
    private static IReadOnlyList<object>? ObjectIdCollection(PcbDocument d, string objectId) => objectId switch
    {
        "Arc" => d.Arcs,
        "Pad" => d.Pads,
        "Via" => d.Vias,
        "Track" => d.Tracks,
        "Text" => d.Texts,
        "Fill" => d.Fills,
        "Region" => d.Regions,
        "ComponentBody" => d.ComponentBodies,
        _ => null,
    };

    /// <summary>
    /// Pins each identity record to its primitive instance (by the ordinal that is still valid right after
    /// reading). Records that cannot be bound — unmanaged types, out-of-range ordinals — keep a null
    /// <c>BoundPrimitive</c> and pass through verbatim on write. Safe to call once per read.
    /// </summary>
    public static void Attach(PcbDocument document)
    {
        foreach (var g in document.PrimitiveGuids)
        {
            var collection = GuidCollection(document, g.TypeId);
            if (collection is not null && g.Index < (uint)collection.Count)
                g.BoundPrimitive = collection[(int)g.Index];
        }
        foreach (var u in document.PrimitiveUniqueIds)
            u.BoundPrimitive = Bind(document, u.ObjectId, u.PrimitiveIndex);
        foreach (var e in document.ExtendedPrimitiveInfo)
            e.BoundPrimitive = Bind(document, e.PrimitiveObjectId, e.PrimitiveIndex);
    }

    private static object? Bind(PcbDocument document, string objectId, int index)
    {
        var collection = ObjectIdCollection(document, objectId);
        return collection is not null && index >= 0 && index < collection.Count ? collection[index] : null;
    }

    /// <summary>
    /// The <c>PrimitiveGuids</c> entries to write: source entries in their original order, each
    /// re-ordinaled to its primitive's current position (removed primitives dropped), followed by a fresh
    /// GUID for every newly added primitive of an already-tracked type. Identical to the source for an
    /// unedited document.
    /// </summary>
    public static List<PcbPrimitiveGuid> BuildPrimitiveGuids(PcbDocument document)
    {
        if (document.PrimitiveGuids.Count == 0)
            return document.PrimitiveGuids;

        var indexInCollection = BuildIndexMap(document);
        var bound = new HashSet<object>(ReferenceEqualityComparer.Instance);

        var result = new List<PcbPrimitiveGuid>(document.PrimitiveGuids.Count);
        foreach (var g in document.PrimitiveGuids)
        {
            if (g.BoundPrimitive is null)
            {
                result.Add(g); // unmanaged / unbound — verbatim, keeping its position in the stream
                continue;
            }
            if (!indexInCollection.TryGetValue(g.BoundPrimitive, out var idx))
                continue; // the primitive was removed — drop its stale GUID entry
            bound.Add(g.BoundPrimitive);
            g.Index = (uint)idx;
            result.Add(g);
        }

        // New primitives (added after load) of a type the source already tracked get a fresh object GUID,
        // appended after the carried entries. Altium re-sorts/regenerates these on open.
        var tracked = document.PrimitiveGuids.Select(g => g.TypeId).Where(t => GuidCollection(document, t) is not null).ToHashSet();
        foreach (var typeId in ManagedGuidTypes)
        {
            if (!tracked.Contains(typeId)) continue;
            var collection = GuidCollection(document, typeId)!;
            for (var j = 0; j < collection.Count; j++)
                if (!bound.Contains(collection[j]))
                    result.Add(new PcbPrimitiveGuid { TypeId = typeId, Index = (uint)j, Guid = Guid.NewGuid(), BoundPrimitive = collection[j] });
        }
        return result;
    }

    /// <summary>
    /// The <c>UniqueIDPrimitiveInformation</c> records to write: source records in their original order,
    /// each re-ordinaled to its primitive's current position; removed primitives drop out. A new primitive
    /// has no short-id to mint, so none is added. Identical to the source for an unedited document.
    /// </summary>
    public static List<PcbPrimitiveUniqueId> BuildPrimitiveUniqueIds(PcbDocument document)
    {
        if (document.PrimitiveUniqueIds.Count == 0)
            return document.PrimitiveUniqueIds;

        var indexInCollection = BuildIndexMap(document);
        var result = new List<PcbPrimitiveUniqueId>(document.PrimitiveUniqueIds.Count);
        foreach (var u in document.PrimitiveUniqueIds)
        {
            if (u.BoundPrimitive is null) { result.Add(u); continue; } // unbound — verbatim
            if (!indexInCollection.TryGetValue(u.BoundPrimitive, out var idx)) continue; // primitive removed
            SetOrdinal(u.OrderedParameters, "PRIMITIVEINDEX", idx);
            u.PrimitiveIndex = idx;
            result.Add(u);
        }
        return result;
    }

    /// <summary>
    /// The <c>ExtendedPrimitiveInformation</c> records to write: source records in their original order,
    /// each re-ordinaled to its primitive's current position; records whose primitive was removed drop
    /// out. Identical to the source for an unedited document.
    /// </summary>
    public static List<PcbExtendedPrimitiveInfo> BuildExtendedPrimitiveInfo(PcbDocument document)
    {
        if (document.ExtendedPrimitiveInfo.Count == 0)
            return document.ExtendedPrimitiveInfo;

        var indexInCollection = BuildIndexMap(document);
        var result = new List<PcbExtendedPrimitiveInfo>(document.ExtendedPrimitiveInfo.Count);
        foreach (var e in document.ExtendedPrimitiveInfo)
        {
            if (e.BoundPrimitive is null) { result.Add(e); continue; } // unbound — verbatim
            if (!indexInCollection.TryGetValue(e.BoundPrimitive, out var idx)) continue; // primitive removed
            SetOrdinal(e.OrderedParameters, "PRIMITIVEINDEX", idx);
            e.PrimitiveIndex = idx;
            result.Add(e);
        }
        return result;
    }

    // Maps every managed primitive instance to its current index within its own collection, so a bound
    // record can be re-ordinaled in O(1). A primitive absent from the map was removed from the document.
    private static Dictionary<object, int> BuildIndexMap(PcbDocument document)
    {
        var map = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (var typeId in ManagedGuidTypes)
        {
            var collection = GuidCollection(document, typeId)!;
            for (var j = 0; j < collection.Count; j++)
                map[collection[j]] = j; // a primitive belongs to exactly one managed collection
        }
        return map;
    }

    // Replaces the value of the first key matching <paramref name="key"/> in the captured ordered list,
    // preserving its original casing/position so an unchanged ordinal re-emits byte-identical text.
    private static void SetOrdinal(List<KeyValuePair<string, string>>? ordered, string key, int value)
    {
        if (ordered is null) return;
        var text = value.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!ordered[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            ordered[i] = new KeyValuePair<string, string>(ordered[i].Key, text);
            return;
        }
    }
}

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Altium.Serialization.Writers;
using Xunit;
using Xunit.Abstractions;

namespace OriginalCircuit.Altium.Tests.RoundTrip;

/// <summary>
/// Semantic (model-level) round-trip fidelity. For every test-data file: load it, serialize the
/// loaded model to a canonical JSON tree, write it back to its original format, reload, serialize
/// again, and structurally diff the two trees. Asserts that no modeled document data changes across
/// a load → save → load cycle.
///
/// This complements the byte-fidelity tests (which compare on-disk stream bytes): it verifies the
/// data survives a round-trip independently of container/byte layout, by reflecting over the runtime
/// types of the whole object graph (not just interface-declared members).
/// </summary>
public sealed class JsonModelRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public JsonModelRoundTripTests(ITestOutputHelper output) => _output = output;

    // Properties excluded from the modeled-data view: back-references (cycles), read-time metadata
    // and derived/computed values. Everything else (including raw byte-preservation fields) is compared.
    private static readonly HashSet<string> SkipProps = new(StringComparer.Ordinal)
    {
        "Owner", "Parent", "Diagnostics", "Bounds",
    };

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropCache = new();

    [SkippableFact]
    public void AllFiles_ModelRoundTrip_PreservesAllData()
    {
        var files = GetAllFiles();
        Skip.If(files.Count == 0, "Test data not available");

        var failures = new List<string>();
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var rel = Path.GetFileName(file);
            try
            {
                var bytes = File.ReadAllBytes(file);
                var model1 = Read(ext, bytes);
                var json1 = Normalize(model1, new List<object>());
                var saved = Write(ext, model1);
                var model2 = Read(ext, saved);
                var json2 = Normalize(model2, new List<object>());

                var diffs = new List<string>();
                Diff(json1, json2, "$", diffs, cap: 50);
                if (diffs.Count > 0)
                {
                    failures.Add($"{rel}: {diffs.Count} model difference(s)");
                    foreach (var d in diffs.Take(10))
                        failures.Add($"    {d}");
                    if (diffs.Count > 10)
                        failures.Add($"    ... (+{diffs.Count - 10} more)");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{rel}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
        }

        _output.WriteLine($"Model round-trip over {files.Count} files: {(failures.Count == 0 ? "all identical" : failures.Count + " issue line(s)")}");
        Assert.True(failures.Count == 0,
            $"load → save → load changed modeled data:\n{string.Join("\n", failures)}");
    }

    // ── round-trip dispatch ────────────────────────────────────────────────────

    private static object Read(string ext, byte[] bytes) => ext switch
    {
        ".pcblib" => new PcbLibReader().Read(new MemoryStream(bytes)),
        ".schlib" => new SchLibReader().Read(new MemoryStream(bytes)),
        ".pcbdoc" => new PcbDocReader().Read(new MemoryStream(bytes)),
        ".schdoc" => new SchDocReader().Read(new MemoryStream(bytes)),
        _ => throw new NotSupportedException(ext),
    };

    private static byte[] Write(string ext, object model)
    {
        using var ms = new MemoryStream();
        switch (ext)
        {
            case ".pcblib": new PcbLibWriter().Write((PcbLibrary)model, ms); break;
            case ".schlib": new SchLibWriter().Write((SchLibrary)model, ms); break;
            case ".pcbdoc": new PcbDocWriter().Write((PcbDocument)model, ms); break;
            case ".schdoc": new SchDocWriter().Write((SchDocument)model, ms); break;
            default: throw new NotSupportedException(ext);
        }
        return ms.ToArray();
    }

    // ── canonical model normalization ──────────────────────────────────────────

    private static PropertyInfo[] Props(Type t) => PropCache.GetOrAdd(t, static type =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray());

    /// <summary>
    /// Reduces an object graph to a canonical tree of SortedDictionary&lt;string,object&gt; (objects),
    /// List&lt;object&gt; (collections) and strings (scalars), using runtime types and breaking cycles.
    /// </summary>
    private static object Normalize(object o, List<object> stack)
    {
        if (o is null) return null;
        var t = o.GetType();

        if (o is string s) return s;
        if (o is bool || t.IsPrimitive) return Convert.ToString(o, CultureInfo.InvariantCulture);
        if (t.IsEnum) return o.ToString();
        if (o is decimal || o is DateTime || o is DateTimeOffset || o is Guid || o is TimeSpan)
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        if (o is byte[] bytes)
            return $"bytes[{bytes.Length}]:{(bytes.Length == 0 ? "-" : Convert.ToHexString(SHA256.HashData(bytes), 0, 6))}";

        if (t.Name == "Coord")
        {
            var raw = t.GetMethod("ToRaw")?.Invoke(o, null);
            return raw is null ? o.ToString() : Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        if (o is IDictionary dict)
        {
            var dm = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (DictionaryEntry e in dict)
                dm[Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? "null"] = Normalize(e.Value, stack);
            return dm;
        }

        if (o is IEnumerable en)
        {
            var list = new List<object>();
            foreach (var item in en) list.Add(Normalize(item, stack));
            return list;
        }

        // Complex object: reflect public instance properties of the runtime type.
        if (stack.Any(x => ReferenceEquals(x, o))) return "<cycle>";
        stack.Add(o);
        var map = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var p in Props(t))
        {
            if (SkipProps.Contains(p.Name)) continue;
            object val;
            try { val = p.GetValue(o); }
            catch (Exception ex) { val = $"<err:{ex.GetType().Name}>"; }
            map[p.Name] = Normalize(val, stack);
        }
        stack.RemoveAt(stack.Count - 1);
        return map;
    }

    // ── structural diff ────────────────────────────────────────────────────────

    private static void Diff(object a, object b, string path, List<string> diffs, int cap)
    {
        if (diffs.Count >= cap) return;
        if (a is SortedDictionary<string, object> da && b is SortedDictionary<string, object> db)
        {
            foreach (var k in da.Keys.Union(db.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            {
                var ina = da.TryGetValue(k, out var va);
                var inb = db.TryGetValue(k, out var vb);
                if (ina && !inb) diffs.Add($"{path}.{k}: present in A, MISSING in B");
                else if (!ina && inb) diffs.Add($"{path}.{k}: MISSING in A, present in B");
                else Diff(va, vb, $"{path}.{k}", diffs, cap);
                if (diffs.Count >= cap) return;
            }
        }
        else if (a is List<object> la && b is List<object> lb)
        {
            if (la.Count != lb.Count) diffs.Add($"{path}: list length {la.Count} -> {lb.Count}");
            for (int i = 0; i < Math.Min(la.Count, lb.Count); i++)
            {
                Diff(la[i], lb[i], $"{path}[{i}]", diffs, cap);
                if (diffs.Count >= cap) return;
            }
        }
        else if ((a is SortedDictionary<string, object>) != (b is SortedDictionary<string, object>)
              || (a is List<object>) != (b is List<object>))
        {
            diffs.Add($"{path}: type changed {Kind(a)} -> {Kind(b)}");
        }
        else
        {
            var sa = a as string ?? (a is null ? "null" : a.ToString());
            var sb = b as string ?? (b is null ? "null" : b.ToString());
            if (!string.Equals(sa, sb, StringComparison.Ordinal))
                diffs.Add($"{path}: {sa} -> {sb}");
        }
    }

    private static string Kind(object o) => o switch
    {
        null => "null",
        SortedDictionary<string, object> => "object",
        List<object> => "array",
        _ => "scalar",
    };

    // ── file discovery ─────────────────────────────────────────────────────────

    private static List<string> GetAllFiles()
    {
        var root = GetDataPath("TestData");
        if (!Directory.Exists(root)) return new List<string>();
        var exts = new[] { ".pcblib", ".schlib", ".pcbdoc", ".schdoc" };
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetDataPath(params string[] parts)
    {
        var current = Directory.GetCurrentDirectory();
        var root = Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }
}

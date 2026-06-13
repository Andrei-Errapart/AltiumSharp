using System.Text.Json;
using OriginalCircuit.Altium.Serialization.Readers;

namespace OriginalCircuit.Altium.Tests.PropertyCoverage;

/// <summary>
/// Tests that verify the v2 PCB reader captures all design-time properties
/// from test data. Each test reads all matching JSON+binary file pairs and
/// reports which properties are modeled vs missing.
/// </summary>
public class PcbPropertyCoverageTests : CoverageTestBase
{
    private static IEnumerable<object[]> GetPcbTestFiles(string prefix)
    {
        var dir = GetPcbTestDataPath();
        if (!Directory.Exists(dir)) yield break;

        foreach (var jsonFile in Directory.GetFiles(dir, $"{prefix}*.json").Order())
        {
            var binaryFile = Path.ChangeExtension(jsonFile, ".PcbLib");
            if (File.Exists(binaryFile))
                yield return [Path.GetFileNameWithoutExtension(jsonFile)];
        }
    }

    private static CoverageResult CheckPcbTypeCoverage(
        string objectType,
        Dictionary<string, string> mapping,
        string filePrefix)
    {
        var dir = GetPcbTestDataPath();
        var (allKeys, fileCount, primitiveCount) = CollectPropertyKeys(dir, $"{filePrefix}*.json", objectType, isPcb: true);

        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in allKeys)
        {
            if (mapping.ContainsKey(key))
                mapped.Add(key);
            else
                unmapped.Add(key);
        }

        return new CoverageResult
        {
            TypeName = objectType,
            TotalJsonProperties = allKeys.Count,
            ModeledProperties = mapped.Count,
            AllJsonKeys = allKeys,
            MappedKeys = mapped,
            UnmappedKeys = unmapped,
            FileCount = fileCount,
            PrimitiveCount = primitiveCount
        };
    }

    [Fact]
    public void PcbPad_CoverageReport()
    {
        var result = CheckPcbTypeCoverage("Pad", PcbPropertyMappings.Pad, "PAD_");
        ReportAndAssert(result);
    }

    [Fact]
    public void PcbVia_CoverageReport()
    {
        var result = CheckPcbTypeCoverage("Via", PcbPropertyMappings.Via, "VIA_");
        ReportAndAssert(result);
    }

    [Fact]
    public void PcbTrack_CoverageReport()
    {
        var result = CheckPcbTypeCoverage("Track", PcbPropertyMappings.Track, "TRACK_");
        ReportAndAssert(result);
    }

    [Fact]
    public void PcbArc_CoverageReport()
    {
        var result = CheckPcbTypeCoverage("Arc", PcbPropertyMappings.Arc, "ARC_");
        ReportAndAssert(result);
    }

    [Fact]
    public void PcbText_CoverageReport()
    {
        var result = CheckPcbTypeCoverage("Text", PcbPropertyMappings.Text, "TEXT_");
        ReportAndAssert(result);
    }

    [Fact]
    public void PcbFill_CoverageReport()
    {
        var result = CheckPcbTypeCoverage("Fill", PcbPropertyMappings.Fill, "FILL");
        ReportAndAssert(result);
    }

    [Fact]
    public void PcbRegion_CoverageReport()
    {
        // Region primitives appear in REGION_* and KEEPOUT_REGION* and REGIONS_* files
        var result = CheckPcbTypeCoverage("Region", PcbPropertyMappings.Region, "*REGION*");
        ReportAndAssert(result);
    }

    [Fact]
    public void PcbComponentBody_CoverageReport()
    {
        var result = CheckPcbTypeCoverage("ComponentBody", PcbPropertyMappings.ComponentBody, "*BODY*3D*");
        ReportAndAssert(result);
    }

    /// <summary>
    /// Reads ALL PCB test files and verifies no exceptions are thrown.
    /// Also verifies primitive counts match between JSON and binary.
    /// </summary>
    [SkippableFact]
    public void AllPcbTestFiles_ReadWithoutExceptions()
    {
        var dir = GetPcbTestDataPath();
        if (!Directory.Exists(dir)) { Skip.If(true, "Test data not available"); return; }

        var failures = new List<string>();

        foreach (var jsonFile in Directory.GetFiles(dir, "*.json").Order())
        {
            var binaryFile = Path.ChangeExtension(jsonFile, ".PcbLib");
            if (!File.Exists(binaryFile)) continue;

            var fileName = Path.GetFileNameWithoutExtension(jsonFile);

            try
            {
                // Read binary with v2 reader
                using var stream = File.OpenRead(binaryFile);
                var library = new PcbLibReader().Read(stream);

                // Read JSON for comparison
                using var doc = LoadJson(jsonFile);
                if (doc == null) continue;

                var jsonFootprints = doc.RootElement.GetProperty("footprints");

                // Compare component counts
                var jsonCount = jsonFootprints.GetArrayLength();
                var v2Count = library.Components.Count();
                if (jsonCount != v2Count)
                    failures.Add($"{fileName}: component count mismatch (JSON={jsonCount}, v2={v2Count})");
            }
            catch (Exception ex)
            {
                failures.Add($"{fileName}: EXCEPTION - {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Every committed PcbLib test file must read without exceptions and with a matching
        // component count. If a new file genuinely uses an unsupported feature, add it to an
        // explicit skip list with a comment rather than weakening this assertion.
        Assert.True(failures.Count == 0,
            $"{failures.Count} PcbLib file(s) failed to read or had mismatched component counts:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// Properties that are known to be unmapped and are explicitly allowed.
    /// If a new unmapped property appears, the test will fail — add it here
    /// (with a comment explaining why) or add a mapping.
    /// </summary>
    private static readonly HashSet<string> AllowedUnmappedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // Currently empty — all properties are mapped at 100% coverage.
        // Add entries here if a property is intentionally unmapped, e.g.:
        // "someProperty", // Reason: not relevant for library context
    };

    private static void ReportAndAssert(CoverageResult result)
    {
        var message = $"\n=== {result.TypeName} Coverage ===\n" +
            $"Files: {result.FileCount}, Primitives: {result.PrimitiveCount}\n" +
            $"JSON properties: {result.TotalJsonProperties}\n" +
            $"Mapped: {result.ModeledProperties} ({result.CoveragePercent}%)\n" +
            $"Missing ({result.UnmappedKeys.Count}): {string.Join(", ", result.UnmappedKeys.Order())}\n";

        // Skip types that don't appear in test data
        if (result.FileCount == 0) return;

        // Fail if there are unmapped properties not in the explicit allowlist.
        var unexpected = result.UnmappedKeys
            .Where(k => !AllowedUnmappedProperties.Contains(k))
            .OrderBy(k => k)
            .ToList();

        Assert.True(unexpected.Count == 0,
            $"{result.TypeName} has {unexpected.Count} unexpected unmapped properties: " +
            string.Join(", ", unexpected) +
            "\nEither add a mapping or add to AllowedUnmappedProperties with a reason.");
    }
}

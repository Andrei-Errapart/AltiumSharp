// ============================================================================
// Example: Extracting embedded binary assets (3D models and images)
// ============================================================================
//
// Altium files embed binary payloads alongside the design data:
//
//   * PcbLib files embed 3D STEP models (ISO-10303-21 text) for footprints.
//   * Schematic sheets and symbols can embed bitmap images (e.g. a title-block
//     logo or a connector drawing).
//
// This example pulls both out to disk so you can reuse them in a viewer,
// datasheet, or model library.
//
// WHERE THE DATA LIVES
// ────────────────────
//   3D models - PcbLibrary.Models is a list of PcbModel. Each has Name (the
//               original .step filename), Id (the GUID component bodies reference),
//               and StepData (the decompressed STEP text). Cast the IPcbLibrary
//               returned by the reader to the concrete PcbLibrary to reach Models.
//   Images    - A schematic image is an ISchImage exposing ImageData (raw bytes).
//               Document-level images live on SchDocument.Images (cast the
//               ISchDocument); symbol-level images live on each component's Images.
//               Cast to the concrete SchImage for the optional source Filename.
//
// Image bytes keep their original encoding (PNG/JPEG/BMP/...), so we sniff the
// magic bytes to choose a file extension.
//
// RUNNING
// ───────
//   dotnet run --project examples/ExtractEmbeddedAssets            (bundled samples)
//   dotnet run --project examples/ExtractEmbeddedAssets -- file.PcbLib   (your file;
//   dotnet run --project examples/ExtractEmbeddedAssets -- file.SchDoc    .SchLib /
//                                                                          .SchDoc too)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;

var outDir = Path.Combine(Path.GetTempPath(), "AltiumAssetsExample");
Directory.CreateDirectory(outDir);

// ── Explicit file: dispatch by type ─────────────────────────────────────────
if (args.Length > 0)
{
    if (!File.Exists(args[0]))
    {
        Console.Error.WriteLine($"File not found: {args[0]}");
        return;
    }
    await ProcessByExtension(args[0], outDir);
    Console.WriteLine($"\nExtracted assets are under: {outDir}");
    return;
}

// ── No argument: extract from bundled TestData ──────────────────────────────
var testData = FindRepoTestDataDir();
if (testData is null)
{
    Console.WriteLine("No file supplied and no bundled TestData folder was found.");
    Console.WriteLine("Usage: dotnet run --project examples/ExtractEmbeddedAssets -- <file.PcbLib|.SchLib|.SchDoc>");
    return;
}

var stepLib = LocateStepLib(testData);
if (stepLib is not null)
    await ExtractModels(stepLib, outDir);

await ScanForImages(testData, outDir);

Console.WriteLine($"Extracted assets are under: {outDir}");

// ── Processing ──────────────────────────────────────────────────────────────

static async Task ProcessByExtension(string path, string outDir)
{
    switch (Path.GetExtension(path).ToLowerInvariant())
    {
        case ".pcblib":
            await ExtractModels(path, outDir);
            break;
        case ".schlib":
        case ".schdoc":
            if (await ExtractImages(path, outDir) == 0)
                Console.WriteLine($"No embedded images found in {Path.GetFileName(path)}.");
            break;
        default:
            Console.WriteLine("Unsupported file type. Provide a .PcbLib, .SchLib or .SchDoc.");
            break;
    }
}

static async Task ExtractModels(string path, string outDir)
{
    Console.WriteLine($"=== 3D models in {Path.GetFileName(path)} ===");

    await using var lib = (PcbLibrary)await AltiumLibrary.OpenPcbLibAsync(path);
    var models = lib.Models.Where(m => !string.IsNullOrEmpty(m.StepData)).ToList();
    if (models.Count == 0)
    {
        Console.WriteLine("  (no embedded STEP models)\n");
        return;
    }

    var dir = Path.Combine(outDir, "models");
    Directory.CreateDirectory(dir);
    foreach (var m in models)
    {
        var name = SanitizeFileName(string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name);
        if (!name.EndsWith(".step", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".stp", StringComparison.OrdinalIgnoreCase))
            name += ".step";
        await File.WriteAllTextAsync(Path.Combine(dir, name), m.StepData);
        Console.WriteLine($"  {name}  ({m.StepData.Length:N0} chars)");
    }
    Console.WriteLine($"  -> {models.Count} model(s) written to {dir}\n");
}

// Returns the number of images extracted from a single schematic file.
static async Task<int> ExtractImages(string path, string outDir)
{
    var images = new List<SchImage>();
    if (Path.GetExtension(path).Equals(".SchDoc", StringComparison.OrdinalIgnoreCase))
    {
        await using var doc = await AltiumLibrary.OpenSchDocAsync(path);
        images.AddRange(((SchDocument)doc).Images);                 // placed on the sheet
        foreach (var component in doc.Components)                   // placed inside a symbol
            foreach (var img in component.Images)
                images.Add((SchImage)img);
    }
    else
    {
        await using var lib = await AltiumLibrary.OpenSchLibAsync(path);
        foreach (var component in lib.Components)
            foreach (var img in component.Images)
                images.Add((SchImage)img);
    }

    var withData = images.Where(i => i.ImageData is { Length: > 0 }).ToList();
    if (withData.Count == 0)
        return 0;

    Console.WriteLine($"=== images in {Path.GetFileName(path)} ===");
    var dir = Path.Combine(outDir, "images");
    Directory.CreateDirectory(dir);
    var prefix = SanitizeFileName(Path.GetFileNameWithoutExtension(path));

    var index = 0;
    foreach (var img in withData)
    {
        var data = img.ImageData!;
        var source = string.IsNullOrWhiteSpace(img.Filename) ? null : Path.GetFileName(img.Filename!);
        var name = source is not null
            ? $"{prefix}-{SanitizeFileName(source)}"
            : $"{prefix}-image{++index}{SniffExtension(data)}";
        await File.WriteAllBytesAsync(Path.Combine(dir, name), data);
        Console.WriteLine($"  {name}  ({data.Length:N0} bytes)");
    }
    Console.WriteLine($"  -> {withData.Count} image(s) written to {dir}\n");
    return withData.Count;
}

// Scans bundled schematics until one yields images, so the example shows real output.
static async Task ScanForImages(string testData, string outDir)
{
    var schFiles = TopLevel(testData, ".SchDoc").Concat(TopLevel(testData, ".SchLib"));
    var total = 0;
    foreach (var file in schFiles)
    {
        total += await ExtractImages(file, outDir);
        if (total > 0) break;   // demonstrated; stop scanning the rest
    }
    if (total == 0)
        Console.WriteLine("=== images ===\n  No embedded images found in the bundled schematics.\n" +
                          "  (Images appear in sheets/symbols that embed a bitmap, e.g. a title-block\n" +
                          "   logo. Pass your own .SchDoc or .SchLib to extract from it.)\n");
}

// ── Helpers ─────────────────────────────────────────────────────────────────

static string SniffExtension(byte[] d)
{
    bool Match(params byte[] sig) => d.Length >= sig.Length && d.Take(sig.Length).SequenceEqual(sig);
    if (Match(0x89, 0x50, 0x4E, 0x47)) return ".png";
    if (Match(0xFF, 0xD8, 0xFF)) return ".jpg";
    if (Match(0x47, 0x49, 0x46, 0x38)) return ".gif";
    if (Match(0x42, 0x4D)) return ".bmp";
    if (Match(0x49, 0x49, 0x2A, 0x00) || Match(0x4D, 0x4D, 0x00, 0x2A)) return ".tif";
    return ".bin";
}

static string SanitizeFileName(string name)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        name = name.Replace(c, '_');
    return name;
}

static string? LocateStepLib(string testData)
{
    // A small generated library that is guaranteed to embed a STEP model.
    var known = Path.Combine(testData, "Generated", "Individual", "PCB", "BODY_3D_STEP.PcbLib");
    if (File.Exists(known)) return known;
    // Otherwise the first real footprint library (leadless parts usually embed a body).
    return TopLevel(testData, ".PcbLib").FirstOrDefault();
}

static IEnumerable<string> TopLevel(string dir, string extension) =>
    Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
        .Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase));

static string? FindRepoTestDataDir()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TestData");
            if (Directory.Exists(candidate)) return candidate;
        }
    return null;
}

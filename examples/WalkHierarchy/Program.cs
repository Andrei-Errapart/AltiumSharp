// ============================================================================
// Example: Walking a hierarchical schematic
// ============================================================================
//
// A multi-sheet design has a top sheet whose sheet symbols point at child .SchDoc
// files, which may themselves contain sheet symbols. This example walks that tree
// and prints it, listing each sheet's components and the ports (sheet entries) that
// connect it to its parent.
//
// WHERE THE DATA LIVES
// ────────────────────
// Sheet symbols are on SchDocument.SheetSymbols (cast the ISchDocument the reader
// returns). Each SchSheetSymbol has FileName (the child .SchDoc it references),
// SheetName (its label) and Entries (the SchSheetEntry ports, each with a Name).
//
// SCOPE NOTE: there is no project-file (.PrjScr) reader, so the child file name is
// resolved relative to the parent sheet's folder. A real project may keep sheets in
// other directories; adapt the resolution if yours does.
//
// RUNNING
// ───────
//   dotnet run --project examples/WalkHierarchy                 (best bundled sheet)
//   dotnet run --project examples/WalkHierarchy -- Top.SchDoc    (your own top sheet)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Sch;

string? start;
if (args.Length > 0)
{
    if (!File.Exists(args[0])) { Console.Error.WriteLine($"File not found: {args[0]}"); return; }
    start = args[0];
}
else
{
    var testData = FindRepoTestDataDir();
    start = testData is null ? null : await FindBestTopSheet(testData);
}

if (start is null)
{
    Console.WriteLine("No .SchDoc supplied and no bundled TestData sheet was found.");
    Console.WriteLine("Usage: dotnet run --project examples/WalkHierarchy -- <top-sheet.SchDoc>");
    return;
}

Console.WriteLine($"Walking hierarchy from: {Path.GetFileName(start)}\n");
var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
await Walk(start, 0);

// Recursively prints a sheet and the child sheets its symbols reference.
async Task Walk(string path, int depth)
{
    var indent = new string(' ', depth * 2);
    var label = Path.GetFileName(path);

    if (!File.Exists(path)) { Console.WriteLine($"{indent}• {label}  (referenced file not found)"); return; }
    if (!visited.Add(Path.GetFullPath(path))) { Console.WriteLine($"{indent}• {label}  (already visited)"); return; }

    OriginalCircuit.Eda.Models.Sch.ISchDocument idoc;
    try { idoc = await AltiumLibrary.OpenSchDocAsync(path); }
    catch (Exception ex) { Console.WriteLine($"{indent}• {label}  (read error: {ex.Message})"); return; }

    await using (idoc)
    {
        var doc = (SchDocument)idoc;
        Console.WriteLine($"{indent}• {label}  —  {doc.Components.Count} components, {doc.SheetSymbols.Count} sub-sheet(s)");

        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (var sheet in doc.SheetSymbols)
        {
            var ports = sheet.Entries.Count == 0
                ? ""
                : $"  ports: {string.Join(", ", sheet.Entries.Select(e => e.Name))}";
            var title = string.IsNullOrWhiteSpace(sheet.SheetName) ? (sheet.FileName ?? "(unnamed)") : sheet.SheetName!;
            Console.WriteLine($"{indent}  ↳ {title} -> {sheet.FileName ?? "(no file)"}{ports}");
            if (!string.IsNullOrWhiteSpace(sheet.FileName))
                await Walk(Path.Combine(folder, sheet.FileName!), depth + 1);
        }
    }
}

// Opens each bundled sheet and starts from whichever has the most sheet symbols.
async Task<string?> FindBestTopSheet(string testData)
{
    string? best = null;
    var bestCount = -1;
    foreach (var file in TopLevel(testData, ".SchDoc"))
    {
        try
        {
            await using var idoc = await AltiumLibrary.OpenSchDocAsync(file);
            var count = ((SchDocument)idoc).SheetSymbols.Count;
            if (count > bestCount) { bestCount = count; best = file; }
        }
        catch { /* skip unreadable sheets */ }
    }
    return best;
}

static IEnumerable<string> TopLevel(string dir, string ext) =>
    Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
        .Where(f => Path.GetExtension(f).Equals(ext, StringComparison.OrdinalIgnoreCase));

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

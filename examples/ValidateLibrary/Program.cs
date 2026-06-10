// ============================================================================
// Example: Validating / linting a library
// ============================================================================
//
// Surfaces two kinds of problem in a .PcbLib or .SchLib:
//
//   1. Read diagnostics  - non-fatal issues the reader recorded (skipped records,
//                          recovered parse errors). Every model exposes a
//                          Diagnostics list; cast to the concrete library type to
//                          reach it.
//   2. Lint checks       - library-hygiene rules this example applies itself:
//                          unnamed components, footprints with no pads / symbols
//                          with no pins, blank or duplicate pad/pin designators.
//
// This is the shape of a pre-commit check you might run over a parts library.
//
// RUNNING
// ───────
//   dotnet run --project examples/ValidateLibrary                    (bundled PcbLib)
//   dotnet run --project examples/ValidateLibrary -- "C:\Parts.SchLib" (your own;
//                                                                        .PcbLib too)
//
// ============================================================================

using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Diagnostics;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Models.Sch;

var input = ResolveInput(args);
if (input is null)
{
    Console.WriteLine("No library supplied and no bundled TestData library was found.");
    Console.WriteLine("Usage: dotnet run --project examples/ValidateLibrary -- <file.PcbLib|.SchLib>");
    return;
}

var issues = new List<Issue>();
IReadOnlyList<AltiumDiagnostic> diagnostics;
int componentCount;

if (Path.GetExtension(input).Equals(".SchLib", StringComparison.OrdinalIgnoreCase))
{
    await using var lib = (SchLibrary)await AltiumLibrary.OpenSchLibAsync(input);
    diagnostics = lib.Diagnostics;
    componentCount = lib.Components.Count;
    foreach (var component in lib.Components)
    {
        var c = (SchComponent)component;
        var who = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name;
        if (string.IsNullOrWhiteSpace(c.Name))
            issues.Add(new("Error", who, "symbol has no name"));
        if (c.Pins.Count == 0)
            issues.Add(new("Warning", who, "symbol has no pins"));
        foreach (var dup in c.Pins.Select(p => p.Designator)
                     .Where(d => !string.IsNullOrWhiteSpace(d))
                     .GroupBy(d => d!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(new("Warning", who, $"duplicate pin designator '{dup.Key}' ({dup.Count()}x)"));
        var unnamedPins = c.Pins.Count(p => string.IsNullOrWhiteSpace(p.Name));
        if (unnamedPins > 0)
            issues.Add(new("Info", who, $"{unnamedPins} pin(s) have no name"));
    }
}
else
{
    await using var lib = (PcbLibrary)await AltiumLibrary.OpenPcbLibAsync(input);
    diagnostics = lib.Diagnostics;
    componentCount = lib.Components.Count;
    foreach (var component in lib.Components)
    {
        var c = (PcbComponent)component;
        var who = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name;
        if (string.IsNullOrWhiteSpace(c.Name))
            issues.Add(new("Error", who, "footprint has no name"));
        if (c.Pads.Count == 0)
            issues.Add(new("Warning", who, "footprint has no pads"));
        var blank = c.Pads.Count(p => string.IsNullOrWhiteSpace(((PcbPad)p).Designator));
        if (blank > 0)
            issues.Add(new("Warning", who, $"{blank} pad(s) have no designator"));
        foreach (var dup in c.Pads.Select(p => ((PcbPad)p).Designator)
                     .Where(d => !string.IsNullOrWhiteSpace(d))
                     .GroupBy(d => d!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(new("Warning", who, $"duplicate pad designator '{dup.Key}' ({dup.Count()}x)"));
    }
}

// ── Report ──────────────────────────────────────────────────────────────────
Console.WriteLine($"Validated {Path.GetFileName(input)} — {componentCount} component(s)\n");

Console.WriteLine($"Reader diagnostics: {diagnostics.Count}");
foreach (var d in diagnostics.Take(20))
    Console.WriteLine($"  [{d.Severity}] {d.Message}");

Console.WriteLine($"\nLint issues: {issues.Count}");
foreach (var severity in new[] { "Error", "Warning", "Info" })
{
    foreach (var issue in issues.Where(i => i.Severity == severity))
        Console.WriteLine($"  [{issue.Severity}] {issue.Component}: {issue.Message}");
}

if (issues.Count == 0 && diagnostics.Count == 0)
    Console.WriteLine("\nClean — no diagnostics or lint issues.");

// ── Helpers ─────────────────────────────────────────────────────────────────

static string? ResolveInput(string[] args)
{
    if (args.Length > 0)
    {
        if (File.Exists(args[0])) return args[0];
        Console.Error.WriteLine($"File not found: {args[0]}");
        return null;
    }
    var testData = FindRepoTestDataDir();
    return testData is null
        ? null
        : LocateSample(testData, ".PcbLib", "QFN", "LFCSP", "BGA");
}

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

static string? LocateSample(string dir, string extension, params string[] preferred)
{
    var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
        .Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase))
        .ToList();
    foreach (var hint in preferred)
    {
        var hit = files.FirstOrDefault(f =>
            Path.GetFileName(f).Contains(hint, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
    }
    return files.FirstOrDefault();
}

// ── Types ───────────────────────────────────────────────────────────────────

record Issue(string Severity, string Component, string Message);

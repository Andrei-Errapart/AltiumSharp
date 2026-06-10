# Extracting embedded assets (3D models and images)

Altium files carry binary payloads alongside the design data. This example pulls two of
them out to disk:

- **3D STEP models** embedded in a `.PcbLib` (ISO-10303-21 text), and
- **bitmap images** embedded in a `.SchDoc`/`.SchLib` (e.g. a title-block logo).

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
# Extracts a STEP model and an image from bundled TestData
dotnet run --project examples/ExtractEmbeddedAssets

# Point it at one of your own files (.PcbLib, .SchLib or .SchDoc)
dotnet run --project examples/ExtractEmbeddedAssets -- "C:\path\to\Footprints.PcbLib"
```

## Where the data lives

**3D models** are on `PcbLibrary.Models` — a list of `PcbModel`, each with `Name` (the
original `.step` filename), `Id` (the GUID component bodies reference), and `StepData`
(the decompressed STEP text, ready to write straight to a file). `Models` is on the
concrete `PcbLibrary`, so cast the `IPcbLibrary` the reader returns.

```csharp
await using var lib = (PcbLibrary)await AltiumLibrary.OpenPcbLibAsync(path);

foreach (var model in lib.Models.Where(m => !string.IsNullOrEmpty(m.StepData)))
    await File.WriteAllTextAsync(Path.Combine(dir, model.Name), model.StepData);
```

**Images** are `ISchImage` objects exposing `ImageData` (raw bytes). Document-level
images (placed on the sheet, like a logo) live on `SchDocument.Images`; symbol-level
images live on each component's `Images`. The bytes keep their original encoding, so the
example sniffs the magic bytes to choose a `.png`/`.jpg`/`.bmp`/… extension.

```csharp
await using var doc = await AltiumLibrary.OpenSchDocAsync(path);

foreach (var img in ((SchDocument)doc).Images)          // images placed on the sheet
    if (img.ImageData is { Length: > 0 } data)
        await File.WriteAllBytesAsync(dest, data);

foreach (var component in doc.Components)               // images inside a symbol
    foreach (var img in component.Images)
        ...
```

## Sample output

```
=== 3D models in BODY_3D_STEP.PcbLib ===
  PSEMI QFN-24 4x4.step  (58,391 chars)

=== images in DAC.SchDoc ===
  DAC-newAltmLogo.bmp  (149,670 bytes)
```

Assets are written under a temp folder (`models/` and `images/`); the program prints the
path. The extracted `.step` opens in any MCAD viewer; the `.bmp` is the schematic's
title-block logo.

## Notes

- Not every library embeds models and not every sheet embeds images. When run with no
  argument the example scans the bundled schematics and reports honestly if it finds
  none — it never fails on "nothing to extract".
- STEP `StepData` is plain text; image `ImageData` is raw bytes — write them with
  `WriteAllTextAsync` and `WriteAllBytesAsync` respectively.

See the [guides index](../../guides/README.md) for the full set of examples.

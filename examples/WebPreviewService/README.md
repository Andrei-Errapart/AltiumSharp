# Serving component previews from a web app

Renders Altium components straight to an HTTP response — the pattern for live previews in
a web UI. Both renderers accept any `Stream`, so the response body is the render target.

The complete, compiling source for this guide is [Program.cs](Program.cs).

## Run

```bash
# Default: render one component to a stream and exit (no server) — CI-safe
dotnet run --project examples/WebPreviewService

# Start the HTTP server
dotnet run --project examples/WebPreviewService -- serve
```

While serving, request a preview (URL-encode paths with spaces):

```
GET /preview?lib=<path-to-.SchLib-or-.PcbLib>&component=<name>&format=png|svg
```

## How it works

`AltiumLibrary.OpenAsync` returns an `ILibrary`; pick the component by name from
`AllComponents` and render it straight to the response body. The renderers encode in
memory and write to the stream asynchronously, so they work directly with a Kestrel
response — set the content type before the first write.

```csharp
app.MapGet("/preview", async (string lib, string component, string? format, HttpContext ctx) =>
{
    await using var library = await AltiumLibrary.OpenAsync(lib);
    var comp = library.AllComponents.FirstOrDefault(c =>
        string.Equals(c.Name, component, StringComparison.OrdinalIgnoreCase));
    if (comp is null) { ctx.Response.StatusCode = 404; return; }

    var options = new RenderOptions { Width = 600, Height = 450 };
    if (format == "svg") { ctx.Response.ContentType = "image/svg+xml"; await new SvgRenderer().RenderAsync(comp, ctx.Response.Body, options); }
    else                 { ctx.Response.ContentType = "image/png";     await new RasterRenderer().RenderAsync(comp, ctx.Response.Body, options); }
});
```

The project uses the `Microsoft.NET.Sdk.Web` SDK; ASP.NET comes from the shared framework
(no extra NuGet package).

## Verified behaviour

```
GET /preview?...&format=png  ->  HTTP 200  image/png       (valid PNG)
GET /preview?...&format=svg  ->  HTTP 200  image/svg+xml   (valid SVG)
GET /preview?...&component=NOPE -> HTTP 404
```

## Notes

- The default (no-argument) mode renders to an in-memory stream and exits, so the example
  is runnable offline and the render-to-stream code is exercised without a server. It is
  build-verified in CI but not smoke-run there (it needs SkiaSharp at runtime).
- The renderers are asynchronous at the stream level, so rendering straight into a Kestrel
  response body works — no manual buffering required.

See the [guides index](../../guides/README.md) for the full set of examples.

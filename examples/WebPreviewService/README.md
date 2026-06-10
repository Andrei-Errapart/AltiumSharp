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
`AllComponents` and render it. **Gotcha:** the renderers write *synchronously*, and
Kestrel disallows synchronous I/O on the response body — so render into a `MemoryStream`
first, then stream that out asynchronously.

```csharp
app.MapGet("/preview", async (string lib, string component, string? format, HttpContext ctx) =>
{
    await using var library = await AltiumLibrary.OpenAsync(lib);
    var comp = library.AllComponents.FirstOrDefault(c =>
        string.Equals(c.Name, component, StringComparison.OrdinalIgnoreCase));
    if (comp is null) { ctx.Response.StatusCode = 404; return; }

    var options = new RenderOptions { Width = 600, Height = 450 };
    using var buffer = new MemoryStream();                 // buffer: renderers write sync
    if (format == "svg") { await new SvgRenderer().RenderAsync(comp, buffer, options);  ctx.Response.ContentType = "image/svg+xml"; }
    else                 { await new RasterRenderer().RenderAsync(comp, buffer, options); ctx.Response.ContentType = "image/png"; }
    buffer.Position = 0;
    await buffer.CopyToAsync(ctx.Response.Body);           // async copy to the response
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
- Buffer-then-copy is the key pattern: don't hand a Kestrel response body straight to a
  renderer that writes synchronously.

See the [guides index](../../guides/README.md) for the full set of examples.

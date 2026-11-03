using System.Reflection;

namespace DotnetCockpit;

internal sealed class WebAssets
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public WebAssets()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("web/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            _files.Add(name[4..].Replace('\\', '/'), buffer.ToArray());
        }
    }

    public IResult Get(HttpContext context, string? path)
    {
        path ??= "";
        if (!_files.TryGetValue(path, out var bytes)) return Results.NotFound();
        context.Response.Headers.CacheControl = "no-cache";
        var contentType = Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
        return Results.File(bytes, contentType);
    }
}

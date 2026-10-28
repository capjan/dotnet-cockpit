using System.ComponentModel;
using System.Diagnostics;
using DotnetCockpit;
using DotnetCockpit.Installation;
using DotnetCockpit.Tools;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(".NET Cockpit — your local .NET environment in the browser.");
    Console.WriteLine("Usage: dotnet-cockpit [--port <0-65535>] [--no-browser]");
    Console.WriteLine("Port 0 (default) selects an available loopback port.");
    return 0;
}

CommandLineOptions options;
try { options = CommandLineOptions.Parse(args); }
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddSingleton<DotnetInstallationService>();
builder.Services.AddSingleton<DotnetToolsService>();
builder.Services.AddSingleton<WebAssets>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host != "127.0.0.1")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";
    await next(context);
});

app.MapGet("/api/health", () => new HealthStatus("ok"));
app.MapGet("/", async (DotnetInstallationService service, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = await ReadInstallation(service, context, logger);
    return Results.Content(InstallationPage.Render(snapshot, InstallationViewOptions.From(context.Request.Query)),
        "text/html; charset=utf-8", statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapGet("/api/installation", async (DotnetInstallationService service, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = await ReadInstallation(service, context, logger);
    return Results.Json(snapshot, AppJsonContext.Default.InstallationSnapshot, statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapGet("/tools", async (DotnetToolsService service, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = await ReadTools(service, context, logger);
    return Results.Content(ToolsPage.Render(snapshot), "text/html; charset=utf-8", statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapGet("/api/tools", async (DotnetToolsService service, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = await ReadTools(service, context, logger);
    return Results.Json(snapshot, AppJsonContext.Default.ToolsSnapshot, statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapGet("/{**path}", (HttpContext context, WebAssets assets, string? path) => assets.Get(context, path));

static async Task<InstallationSnapshot> ReadInstallation(DotnetInstallationService service, HttpContext context, ILogger<Program> logger)
{
    try { return await service.ReadAsync(context.RequestAborted); }
    catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
    {
        if (context.RequestAborted.IsCancellationRequested) throw;
        logger.LogWarning(exception, "Could not query the .NET installation.");
        return InstallationSnapshot.Create("error", null,
            error: "The .NET installation could not be read. Check your PATH and try again.");
    }
}

static async Task<ToolsSnapshot> ReadTools(DotnetToolsService service, HttpContext context, ILogger<Program> logger)
{
    try { return await service.ReadAsync(context.RequestAborted); }
    catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
    {
        if (context.RequestAborted.IsCancellationRequested) throw;
        logger.LogWarning(exception, "Could not query .NET tools.");
        return ToolsSnapshot.Empty("error", Environment.CurrentDirectory,
            "The tools could not be read. Check the .NET installation and try again.");
    }
}

await app.StartAsync();
var url = app.Urls.Single().TrimEnd('/') + "/";
Console.WriteLine($".NET Cockpit: {url}");
if (options.OpenBrowser)
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
    {
        Console.Error.WriteLine($"Open {url} in your browser.");
    }
}
await app.WaitForShutdownAsync();
return 0;

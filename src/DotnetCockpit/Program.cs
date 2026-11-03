using System.ComponentModel;
using System.Diagnostics;
using DotnetCockpit;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Setup;
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

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });
builder.WebHost.UseKestrel();
builder.Services.AddRouting();
builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddSingleton<DotnetInstallationService>();
builder.Services.AddSingleton<DotnetToolsService>();
builder.Services.AddSingleton<SdkDownloadCatalog>();
builder.Services.AddSingleton<HttpClient>(_ =>
{
    var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-cockpit");
    return client;
});
builder.Services.AddSingleton<DotnetUninstallToolService>();
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
app.MapGet("/", async (DotnetInstallationService service, SdkDownloadCatalog sdkCatalog, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var installationTask = ReadInstallation(service, context, logger);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    await Task.WhenAll(installationTask, sdkTask);
    var snapshot = installationTask.Result;
    var sdkSnapshot = sdkTask.Result;
    return Results.Content(InstallationPage.Render(snapshot, sdkSnapshot, InstallationViewOptions.From(context.Request.Query)),
        "text/html; charset=utf-8", statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapGet("/api/installation", async (DotnetInstallationService service, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = await ReadInstallation(service, context, logger);
    return Results.Json(snapshot, AppJsonContext.Default.InstallationSnapshot, statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapPost("/installation/remove", async (HttpContext context, DotnetUninstallToolService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var result = await service.RemoveInstallationAsync(form["version"].ToString(), form["kind"].ToString(), context.RequestAborted);
    return Results.Redirect(InstallationRedirect(form, result.Success ? "removed" : "remove-failed"));
});
app.MapGet("/preflight", async (DotnetInstallationService installationService, DotnetToolsService toolsService, DotnetUninstallToolService setupService, SdkDownloadCatalog sdkCatalog, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var installationTask = ReadInstallation(installationService, context, logger);
    var toolsTask = ReadTools(toolsService, context, logger);
    var setupTask = setupService.ReadAsync(context.RequestAborted);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    await Task.WhenAll(installationTask, toolsTask, setupTask, sdkTask);
    return Results.Content(PreflightPage.Render(installationTask.Result, toolsTask.Result, setupTask.Result, sdkTask.Result), "text/html; charset=utf-8");
});
app.MapGet("/setup", async (DotnetUninstallToolService service, SdkDownloadCatalog sdkCatalog, DotnetInstallationService installationService, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var setupTask = service.ReadAsync(context.RequestAborted);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    var installationTask = ReadInstallation(installationService, context, logger);
    await Task.WhenAll(setupTask, sdkTask, installationTask);
    return Results.Content(SetupPage.Render(setupTask.Result, sdkTask.Result, installationTask.Result, SetupViewOptions.From(context.Request.Query)), "text/html; charset=utf-8");
});
app.MapPost("/setup/install", async (HttpContext context, DotnetUninstallToolService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var result = await service.InstallAsync(form["version"].ToString(), context.RequestAborted);
    return Results.Redirect($"/setup?notice={(result.Success ? "installed" : "failed")}");
});
app.MapPost("/setup/update", async (HttpContext context, DotnetUninstallToolService service) =>
{
    var result = await service.InstallAsync("latest", context.RequestAborted);
    return Results.Redirect($"/setup?notice={(result.Success ? "updated" : "update-failed")}");
});
app.MapPost("/setup/uninstall", async (HttpContext context, DotnetUninstallToolService service) =>
{
    var result = await service.UninstallAsync(context.RequestAborted);
    return Results.Redirect($"/setup?notice={(result.Success ? "uninstalled" : "uninstall-failed")}");
});
app.MapGet("/tools", async (DotnetToolsService service, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = await ReadTools(service, context, logger);
    var options = ToolsViewOptions.From(context.Request.Query);
    var search = snapshot.Status == "ready" && options.SearchQuery.Length > 0
        ? await service.SearchAsync(options.SearchQuery, context.RequestAborted)
        : new ToolSearch(options.SearchQuery, []);
    return Results.Content(ToolsPage.Render(snapshot, options, search), "text/html; charset=utf-8", statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapPost("/tools/install", async (HttpContext context, DotnetToolsService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var scope = form["scope"].ToString();
    var result = await service.InstallAsync(form["packageId"].ToString(), form["version"].ToString(), scope, context.RequestAborted);
    return Results.Redirect(ToolRedirect(scope, form["q"].ToString(), result.Success ? "installed" : "failed"));
});
app.MapPost("/tools/uninstall", async (HttpContext context, DotnetToolsService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var scope = form["scope"].ToString();
    var result = await service.UninstallAsync(form["packageId"].ToString(), scope, context.RequestAborted);
    return Results.Redirect(ToolRedirect(scope, "", result.Success ? "uninstalled" : "failed"));
});
app.MapPost("/tools/init", async (HttpContext context, DotnetToolsService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var result = await service.InitializeLocalAsync(context.RequestAborted);
    return Results.Redirect(ToolRedirect(form["scope"].ToString(), form["q"].ToString(), result.Success ? "initialized" : "failed"));
});
app.MapPost("/tools/remove-manifest", async (HttpContext context, DotnetToolsService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var result = await service.RemoveLocalManifestAsync(context.RequestAborted);
    return Results.Redirect(ToolRedirect(form["scope"].ToString(), form["q"].ToString(), result.Success ? "removed" : "failed"));
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

static string ToolRedirect(string scope, string query, string notice)
{
    var normalizedScope = scope == "local" ? "local" : "global";
    var normalizedQuery = query.Trim();
    var result = $"/tools?scope={normalizedScope}";
    if (normalizedQuery.Length is > 0 and <= 100) result += $"&q={Uri.EscapeDataString(normalizedQuery)}";
    return $"{result}&notice={notice}";
}

static string InstallationRedirect(IFormCollection form, string notice)
{
    var kind = form["viewKind"].ToString() is "sdks" or "runtimes" ? form["viewKind"].ToString() : "all";
    var sort = form["sort"].ToString() is "name" or "path" ? form["sort"].ToString() : "version";
    var direction = form["direction"].ToString() == "asc" ? "asc" : "desc";
    var query = form["q"].ToString().Trim();
    var result = $"/?q={Uri.EscapeDataString(query)}&kind={kind}&sort={sort}&direction={direction}&notice={notice}";
    return $"{result}#inventory";
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

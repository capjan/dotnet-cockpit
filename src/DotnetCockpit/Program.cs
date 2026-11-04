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
builder.Services.AddSingleton<SettingsStore>();

var app = builder.Build();
var settingsStore = app.Services.GetRequiredService<SettingsStore>();
PageLayout.CurrentSettings = () => settingsStore.Current;
PageLayout.CurrentAccent = () => SystemAccent.Current;
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
app.MapGet("/api/accent", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(new SystemAccentStatus(SystemAccent.Current), AppJsonContext.Default.SystemAccentStatus);
});
app.MapGet("/overview", async (DotnetInstallationService installationService, DotnetToolsService toolsService, DotnetUninstallToolService setupService, SdkDownloadCatalog sdkCatalog, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var installationTask = ReadInstallation(installationService, context, logger);
    var toolsTask = ReadTools(toolsService, context, logger);
    var setupTask = setupService.ReadAsync(context.RequestAborted);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    var activeSdkTask = installationService.ReadActiveSdkAsync(context.RequestAborted);
    await Task.WhenAll(installationTask, toolsTask, setupTask, sdkTask, activeSdkTask);
    return Results.Content(OverviewPage.Render(installationTask.Result, toolsTask.Result, setupTask.Result, sdkTask.Result, activeSdkTask.Result), "text/html; charset=utf-8");
});
app.MapGet("/", async (DotnetInstallationService service, DotnetToolsService toolsService, SdkDownloadCatalog sdkCatalog, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var installationTask = ReadInstallation(service, context, logger);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    var toolsTask = ReadTools(toolsService, context, logger);
    await Task.WhenAll(installationTask, sdkTask, toolsTask);
    var snapshot = installationTask.Result;
    var sdkSnapshot = sdkTask.Result;
    int? toolsCount = toolsTask.Result.Status == "ready" ? toolsTask.Result.Global.Tools.Length + toolsTask.Result.Local.Tools.Length : null;
    return Results.Content(InstallationPage.Render(snapshot, sdkSnapshot, InstallationViewOptions.From(context.Request.Query), navTools: toolsCount),
        "text/html; charset=utf-8", statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapGet("/versions", (HttpContext context) => Results.Redirect("/"));
app.MapGet("/api/installation", async (DotnetInstallationService service, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = await ReadInstallation(service, context, logger);
    return Results.Json(snapshot, AppJsonContext.Default.InstallationSnapshot, statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapPost("/installation/remove", async (HttpContext context, DotnetUninstallToolService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var items = form["items"];
    if (items.Count > 0)
    {
        var targets = items.Select(item => (item ?? "").Split('|', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .Select(parts => ((string?)parts[0], (string?)parts[1]))
            .ToArray();
        var batch = await service.RemoveInstallationsAsync(targets, context.RequestAborted);
        return Results.Redirect(InstallationRedirect(form, batch.Success ? "removed" : "remove-failed"));
    }
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
    return Results.Content(PreflightPage.Render(installationTask.Result, toolsTask.Result, setupTask.Result, sdkTask.Result, settingsStore.Current.PreflightOnLaunch), "text/html; charset=utf-8");
});
app.MapGet("/setup", async (DotnetUninstallToolService service, SdkDownloadCatalog sdkCatalog, DotnetInstallationService installationService, DotnetToolsService toolsService, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var setupTask = service.ReadAsync(context.RequestAborted);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    var installationTask = ReadInstallation(installationService, context, logger);
    var toolsTask = ReadTools(toolsService, context, logger);
    await Task.WhenAll(setupTask, sdkTask, installationTask, toolsTask);
    int? toolsCount = toolsTask.Result.Status == "ready" ? toolsTask.Result.Global.Tools.Length + toolsTask.Result.Local.Tools.Length : null;
    return Results.Content(SetupPage.Render(setupTask.Result, sdkTask.Result, installationTask.Result, SetupViewOptions.From(context.Request.Query), toolsCount), "text/html; charset=utf-8");
});
app.MapGet("/settings", () => Results.Redirect("/setup"));
app.MapPost("/settings/theme", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var theme = SettingsStore.NormalizeTheme(form["theme"].ToString());
    settingsStore.Update(settings => settings with { Theme = theme });
    return Results.Redirect(LocalReferer(context));
});
app.MapPost("/settings/palette", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var palette = SettingsStore.NormalizePalette(form["palette"].ToString());
    settingsStore.Update(settings => settings with { Palette = palette });
    return Results.Redirect(LocalReferer(context));
});
app.MapPost("/settings/preflight", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var fromSettings = form["return"].ToString() == "settings";
    var show = fromSettings ? form["show"].ToString() == "true" : form["skip"].ToString() != "true";
    settingsStore.Update(settings => settings with { PreflightOnLaunch = show });
    return Results.Redirect(fromSettings ? "/setup" : "/");
});
app.MapGet("/confirm", async (DotnetInstallationService installationService, DotnetToolsService toolsService, DotnetUninstallToolService setupService, SdkDownloadCatalog sdkCatalog, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var query = context.Request.Query;
    var installationTask = ReadInstallation(installationService, context, logger);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    var toolsTask = ReadTools(toolsService, context, logger);
    var setupTask = setupService.ReadAsync(context.RequestAborted);
    await Task.WhenAll(installationTask, sdkTask, toolsTask, setupTask);
    var rows = InstallationPage.BuildRows(installationTask.Result, sdkTask.Result, SupportLifecycle.Today);
    var scope = query["scope"].ToString();
    var packageId = query["packageId"].ToString();
    var isListedTool = toolsTask.Result.Global.Tools.Concat(toolsTask.Result.Local.Tools).Any(tool => string.Equals(tool.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
    var viewOptions = InstallationViewOptions.From(query) with { Kind = query["viewKind"].ToString() is "sdks" or "runtimes" or "previews" ? query["viewKind"].ToString() : "all" };
    ConfirmSpec? spec = query["action"].ToString() switch
    {
        "version-remove" when rows.FirstOrDefault(row => row.CanRemove && row.Version == query["version"].ToString() && row.Kind == query["kind"].ToString()) is { } row => ConfirmActions.VersionRemove(row, viewOptions),
        "channel-remove" when rows.Where(row => row.CanRemove && row.Channel == query["channel"].ToString()).ToArray() is { Length: > 0 } channelRows => ConfirmActions.ChannelRemove(query["channel"].ToString(), channelRows),
        "tool-install" when packageId.Length > 0 => ConfirmActions.ToolInstall(packageId, query["version"].ToString(), scope),
        "tool-uninstall" when isListedTool => ConfirmActions.ToolUninstall(packageId, scope),
        "tool-update" when isListedTool => ConfirmActions.ToolUpdate(packageId, scope, null),
        "tools-update-all" when query["items"].Count > 0 => ConfirmActions.ToolsUpdateAll(scope is "local" or "all" ? scope : "global", query["q"].ToString(), query["items"].Select(item => item ?? "").ToArray()),
        "manifest-init" => ConfirmActions.ManifestInit(query["q"].ToString()),
        "manifest-remove" => ConfirmActions.ManifestRemove(query["q"].ToString()),
        "setup-install" => ConfirmActions.SetupInstall(query["version"].ToString(), setupTask.Result.GlobalInstallPath),
        "setup-update" => ConfirmActions.SetupUpdate(setupTask.Result.LatestVersion),
        "setup-uninstall" => ConfirmActions.SetupUninstall(),
        _ => null
    };
    if (spec is null) return Results.Redirect("/");
    var health = EnvironmentHealth.Evaluate(installationTask.Result, sdkTask.Result);
    int? toolsCount = toolsTask.Result.Status == "ready" ? toolsTask.Result.Global.Tools.Length + toolsTask.Result.Local.Tools.Length : null;
    return Results.Content(ConfirmActions.RenderPage(spec, health.Count, installationTask.Result.InstallationCount, toolsCount), "text/html; charset=utf-8");
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
app.MapGet("/tools", async (DotnetToolsService service, DotnetInstallationService installationService, SdkDownloadCatalog sdkCatalog, HttpClient httpClient, HttpContext context, ILogger<Program> logger) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var toolsTask = ReadTools(service, context, logger);
    var installationTask = ReadInstallation(installationService, context, logger);
    var sdkTask = sdkCatalog.ReadAsync(context.RequestAborted);
    await Task.WhenAll(toolsTask, installationTask, sdkTask);
    var snapshot = toolsTask.Result;
    var health = EnvironmentHealth.Evaluate(installationTask.Result, sdkTask.Result);
    if (snapshot.Status == "ready")
    {
        await ToolMetadataCatalog.WarmCacheAsync(snapshot, httpClient, context.RequestAborted);
    }
    var options = ToolsViewOptions.From(context.Request.Query);
    var search = snapshot.Status == "ready" && options.SearchQuery.Length > 0
        ? await service.SearchAsync(options.SearchQuery, context.RequestAborted)
        : new ToolSearch(options.SearchQuery, []);
    return Results.Content(ToolsPage.Render(snapshot, options, search, health.Count, installationTask.Result.InstallationCount, installationTask.Result.Signature), "text/html; charset=utf-8", statusCode: snapshot.Status == "error" ? 503 : 200);
});
app.MapPost("/tools/install", async (HttpContext context, DotnetToolsService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var scope = form["scope"].ToString();
    var result = await service.InstallAsync(form["packageId"].ToString(), form["version"].ToString(), scope, context.RequestAborted);
    return Results.Redirect(ToolRedirect(scope, form["q"].ToString(), result.Success ? "installed" : "failed", form["packageId"].ToString()));
});
app.MapPost("/tools/update", async (HttpContext context, DotnetToolsService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var scope = form["scope"].ToString();
    var result = await service.UpdateAsync(form["packageId"].ToString(), scope, context.RequestAborted);
    return Results.Redirect(ToolRedirect(scope, "", result.Success ? "updated" : "failed", form["packageId"].ToString()));
});
app.MapPost("/tools/update-all", async (HttpContext context, DotnetToolsService service) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var scope = form["scope"].ToString();
    var items = form["items"];
    var anySucceeded = false;
    foreach (var item in items)
    {
        if (string.IsNullOrWhiteSpace(item)) continue;
        var parts = item.Split('|', 2);
        var pkgId = parts[0];
        var itemScope = parts.Length > 1 ? parts[1] : (scope == "local" ? "local" : "global");
        var res = await service.UpdateAsync(pkgId, itemScope, context.RequestAborted);
        if (res.Success) anySucceeded = true;
    }
    return Results.Redirect(ToolRedirect(scope, form["q"].ToString(), anySucceeded ? "updated" : "failed"));
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
app.MapGet("/api/tools/search", async (DotnetToolsService service, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var query = context.Request.Query["q"].ToString().Trim();
    if (query.Length is 0 or > 100)
        return Results.Json(new ToolSearch("", []), AppJsonContext.Default.ToolSearch);
    var search = await service.SearchAsync(query, context.RequestAborted);
    return Results.Json(search, AppJsonContext.Default.ToolSearch);
});
app.MapGet("/api/tools/icon", async (HttpContext context, HttpClient httpClient) =>
{
    var packageId = context.Request.Query["packageId"].ToString().Trim();
    var version = context.Request.Query["version"].ToString().Trim();
    var (bytes, contentType) = await ToolMetadataCatalog.GetOrFetchIconAsync(packageId, version, httpClient, context.RequestAborted);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Bytes(bytes ?? [], contentType);
});
app.MapGet("/{**path}", (HttpContext context, WebAssets assets, string? path) => assets.Get(context, path));

static string LocalReferer(HttpContext context) =>
    Uri.TryCreate(context.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer) && referer.Host == context.Request.Host.Host
        ? referer.PathAndQuery
        : "/";

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

static string ToolRedirect(string scope, string query, string notice, string changed = "")
{
    var normalizedScope = scope == "local" ? "local" : scope == "all" ? "all" : "global";
    var normalizedQuery = query.Trim();
    var result = $"/tools?scope={normalizedScope}";
    if (normalizedQuery.Length is > 0 and <= 100) result += $"&q={Uri.EscapeDataString(normalizedQuery)}";
    if (changed.Length is > 0 and <= 100) result += $"&changed={Uri.EscapeDataString(changed)}";
    return $"{result}&notice={notice}";
}

static string InstallationRedirect(IFormCollection form, string notice)
{
    var kind = form["viewKind"].ToString() is "sdks" or "runtimes" ? form["viewKind"].ToString() : "all";
    var sort = form["sort"].ToString() is "name" or "path" ? form["sort"].ToString() : "version";
    var direction = form["direction"].ToString() == "asc" ? "asc" : "desc";
    var query = form["q"].ToString().Trim();
    return $"/?q={Uri.EscapeDataString(query)}&kind={kind}&sort={sort}&direction={direction}&notice={notice}";
}

await app.StartAsync();
var url = app.Urls.Single().TrimEnd('/') + "/";
Console.WriteLine($".NET Cockpit: {url}");
if (options.OpenBrowser)
{
    var launchSettings = settingsStore.Current;
    var startUrl = launchSettings.PreflightOnLaunch || !launchSettings.FirstLaunchCompleted ? url + "preflight" : url;
    settingsStore.Update(settings => settings with { FirstLaunchCompleted = true });
    try { Process.Start(new ProcessStartInfo(startUrl) { UseShellExecute = true }); }
    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
    {
        Console.Error.WriteLine($"Open {url} in your browser.");
    }
}
await app.WaitForShutdownAsync();
return 0;

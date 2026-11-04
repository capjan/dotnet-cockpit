using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Setup;
using DotnetCockpit.Tools;

namespace DotnetCockpit;

/// <summary>
/// A value that answers from memory at once and checks again in the background. Reading the machine takes
/// external processes, so waiting for them on every page would make each click feel slow.
/// </summary>
internal sealed class StaleWhileRevalidate<T>(Func<Task<T>> load, TimeSpan maxAge, Func<T, bool>? keep = null) where T : class
{
    private readonly Lock gate = new();
    private T? current;
    private DateTimeOffset loadedAt = DateTimeOffset.MinValue;
    private Task<T>? inFlight;

    public T? Current { get { lock (gate) return current; } }

    /// <summary>Returns the last value without waiting and refreshes it in the background once it has aged.</summary>
    public Task<T> GetAsync()
    {
        lock (gate)
        {
            if (current is null) return StartLoad();
            if (DateTimeOffset.UtcNow - loadedAt > maxAge) _ = StartLoad();
            return Task.FromResult(current);
        }
    }

    /// <summary>Waits for a value no older than the given age, joining a read that is already running.</summary>
    public Task<T> GetFreshAsync(TimeSpan age)
    {
        lock (gate)
        {
            return current is not null && DateTimeOffset.UtcNow - loadedAt <= age ? Task.FromResult(current) : StartLoad();
        }
    }

    /// <summary>Forces a read that starts after this call, for use right after a change.</summary>
    public async Task<T> RefreshAsync()
    {
        Task<T>? running;
        lock (gate) running = inFlight;
        if (running is not null)
        {
            try { await running; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { /* The new read reports its own failure. */ }
        }
        Task<T> fresh;
        lock (gate) fresh = StartLoad();
        return await fresh;
    }

    private Task<T> StartLoad()
    {
        return inFlight ??= Run();

        async Task<T> Run()
        {
            await Task.Yield();
            try
            {
                var value = await load();
                lock (gate)
                {
                    if (keep?.Invoke(value) != false)
                    {
                        current = value;
                        loadedAt = DateTimeOffset.UtcNow;
                    }
                    else
                    {
                        current = null;
                    }
                }
                return value;
            }
            finally
            {
                lock (gate) inFlight = null;
            }
        }
    }
}

internal sealed record PageData(InstallationSnapshot Installation, ToolsSnapshot Tools, SdkDownloadSnapshot Catalog)
{
    public int? ToolsCount => Tools.Status == "ready" ? Tools.Global.Tools.Length + Tools.Local.Tools.Length : null;
}

/// <summary>Everything the pages read about the machine, kept current without making them wait.</summary>
internal sealed class LiveData
{
    private static readonly TimeSpan Revalidate = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReleaseListAge = TimeSpan.FromMinutes(10);

    private readonly SdkDownloadCatalog catalog;
    private readonly HttpClient httpClient;
    private readonly ILogger<LiveData> logger;
    private int metadataRefreshRunning;

    public StaleWhileRevalidate<InstallationSnapshot> Installation { get; }
    public StaleWhileRevalidate<ToolsSnapshot> Tools { get; }
    public StaleWhileRevalidate<ActiveSdk> ActiveSdk { get; }
    public StaleWhileRevalidate<SetupSnapshot> Setup { get; }

    public LiveData(DotnetInstallationService installation, DotnetToolsService tools, DotnetUninstallToolService setup, SdkDownloadCatalog catalog, HttpClient httpClient, ILogger<LiveData> logger)
    {
        this.catalog = catalog;
        this.httpClient = httpClient;
        this.logger = logger;
        Installation = new(() => ReadInstallationAsync(installation), Revalidate, snapshot => snapshot.Status != "error");
        Tools = new(() => ReadToolsAsync(tools), Revalidate, snapshot => snapshot.Status != "error");
        ActiveSdk = new(() => installation.ReadActiveSdkAsync(CancellationToken.None), Revalidate);
        Setup = new(() => setup.ReadAsync(CancellationToken.None), ReleaseListAge);
    }

    /// <summary>Starts every read in the background, so the first page finds them finished or nearly so.</summary>
    public void Warm()
    {
        _ = Installation.GetAsync();
        _ = Tools.GetAsync();
        _ = ActiveSdk.GetAsync();
        _ = Setup.GetAsync();
        _ = catalog.ReadAsync(CancellationToken.None);
    }

    public async Task<PageData> ReadAsync(HttpContext context)
    {
        if (context.Request.Query.ContainsKey("refresh")) await RefreshAsync(includeReleases: context.Request.Path == "/setup");
        var installationTask = Installation.GetAsync();
        var toolsTask = Tools.GetAsync();
        var catalogTask = catalog.ReadAsync(context.RequestAborted);
        await Task.WhenAll(installationTask, toolsTask, catalogTask);
        return new(installationTask.Result, toolsTask.Result, catalogTask.Result);
    }

    /// <summary>Reads the machine again and waits for it, so the page after a change shows the result.</summary>
    public async Task RefreshAsync(bool includeReleases = false)
    {
        // The release list comes from the network and only the Settings page shows it, so nothing else waits for it.
        var reads = new List<Task> { Installation.RefreshAsync(), Tools.RefreshAsync(), ActiveSdk.RefreshAsync() };
        if (includeReleases) reads.Add(Setup.RefreshAsync());
        await Task.WhenAll(reads);
    }

    /// <summary>Changes when what a page shows would change. Pages compare it to notice outside changes.</summary>
    public string Revision(bool includeToolMetadata)
    {
        var installation = Installation.Current?.Signature ?? "";
        var tools = Tools.Current is { } snapshot
            ? string.Join('|', snapshot.Global.Tools.Concat(snapshot.Local.Tools).Select(tool => $"{tool.PackageId}:{tool.Version}").Order(StringComparer.Ordinal))
            : "";
        var state = includeToolMetadata ? $"{installation}#{tools}#{ToolMetadataCatalog.Generation}" : $"{installation}#{tools}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)))[..16];
    }

    public async Task<string> ReadFreshRevisionAsync(bool includeToolMetadata)
    {
        var age = TimeSpan.FromSeconds(1.5);
        await Task.WhenAll(Installation.GetFreshAsync(age), Tools.GetFreshAsync(age));
        return Revision(includeToolMetadata);
    }

    private async Task<InstallationSnapshot> ReadInstallationAsync(DotnetInstallationService service)
    {
        try { return await service.ReadAsync(CancellationToken.None); }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not query the .NET installation.");
            return InstallationSnapshot.Create("error", null, error: "The .NET installation could not be read. Check your PATH and try again.");
        }
    }

    private async Task<ToolsSnapshot> ReadToolsAsync(DotnetToolsService service)
    {
        ToolsSnapshot snapshot;
        try { snapshot = await service.ReadAsync(CancellationToken.None); }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not query .NET tools.");
            return ToolsSnapshot.Empty("error", Environment.CurrentDirectory, "The tools could not be read. Check the .NET installation and try again.");
        }

        if (snapshot.Status == "ready" && Interlocked.CompareExchange(ref metadataRefreshRunning, 1, 0) == 0)
            _ = RefreshToolMetadataAsync(snapshot);
        return snapshot;
    }

    private async Task RefreshToolMetadataAsync(ToolsSnapshot snapshot)
    {
        try { await ToolMetadataCatalog.WarmCacheAsync(snapshot, httpClient, CancellationToken.None); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not refresh the tool metadata.");
        }
        finally { Interlocked.Exchange(ref metadataRefreshRunning, 0); }
    }
}

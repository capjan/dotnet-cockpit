using System.Text.Json;
using System.Runtime.InteropServices;

namespace DotnetCockpit.SdkDownloads;

internal sealed record SdkCve(string Id, string? Url);

internal sealed record SdkDownload(string Product, string Version, string InstallationVersion, string? IncludedRuntimeVersion, string Channel, string ReleaseType, string SupportPhase, string? EndOfSupportDate, string ReleaseDate, SdkCve[] Cves, string Url, string Sha512);

internal sealed record SdkDownloadSnapshot(string Status, SdkDownload[] Downloads, string? Error = null);

internal sealed class SdkDownloadCatalog(ILogger<SdkDownloadCatalog> logger, HttpClient httpClient)
{
    private const string IndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";
    private static readonly Uri OfficialHost = new("https://builds.dotnet.microsoft.com/");
    private SdkDownload[]? cachedDownloads;
    private DateTimeOffset cacheExpiresAt;
    private readonly SemaphoreSlim cacheLock = new(1, 1);

    public async Task<SdkDownloadSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var downloads = await ReadCatalogAsync(cancellationToken);
            return new("ready", downloads);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out while reading .NET SDK release metadata.");
            return new("error", [], "The available SDK downloads could not be loaded right now.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException or UriFormatException)
        {
            logger.LogWarning(exception, "Could not read .NET SDK release metadata.");
            return new("error", [], "The available SDK downloads could not be loaded right now.");
        }
    }

    private async Task<SdkDownload[]> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        if (cachedDownloads is not null && cacheExpiresAt > DateTimeOffset.UtcNow) return cachedDownloads;

        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedDownloads is not null && cacheExpiresAt > DateTimeOffset.UtcNow) return cachedDownloads;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            cachedDownloads = await FetchCatalogAsync(timeout.Token);
            cacheExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
            return cachedDownloads;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<SdkDownload[]> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        using var indexDocument = await GetJsonAsync(IndexUrl, cancellationToken);
        if (!indexDocument.RootElement.TryGetProperty("releases-index", out var channels) || channels.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The .NET releases index did not contain a channel list.");

        var channelTasks = channels.EnumerateArray().Select(channel =>
        {
            var channelName = GetString(channel, "channel-version");
            var releaseType = GetString(channel, "release-type")?.ToUpperInvariant() ?? "";
            var supportPhase = GetString(channel, "support-phase")?.ToUpperInvariant() ?? "";
            var endOfSupportDate = GetString(channel, "eol-date");
            var releaseUrl = GetString(channel, "releases.json");
            if (string.IsNullOrWhiteSpace(channelName) || !IsOfficialUrl(releaseUrl))
                throw new InvalidDataException("The .NET releases index contained an invalid channel URL.");
            return ReadChannelAsync(channelName, releaseType, supportPhase, endOfSupportDate, releaseUrl!, cancellationToken);
        }).ToArray();

        var results = await Task.WhenAll(channelTasks);
        return results.SelectMany(downloads => downloads)
            .DistinctBy(download => (download.Product, download.Version), ProductTupleComparer.Instance)
            .OrderByDescending(download => download.ReleaseDate, StringComparer.Ordinal)
            .ThenByDescending(download => download.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<SdkDownload[]> ReadChannelAsync(string channel, string releaseType, string supportPhase, string? endOfSupportDate, string releaseUrl, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(releaseUrl, cancellationToken);
        if (!document.RootElement.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"The .NET {channel} release metadata did not contain releases.");

        var downloads = new List<SdkDownload>();
        foreach (var release in releases.EnumerateArray())
        {
            var releaseDate = GetString(release, "release-date");
            if (string.IsNullOrWhiteSpace(releaseDate)) continue;
            var cves = ReadCves(release);
            AddPackages(release, "sdk", "SDK", "dotnet-sdk", channel, releaseType, supportPhase, endOfSupportDate, releaseDate, cves, downloads);
            AddPackages(release, "runtime", ".NET Runtime", "dotnet-runtime", channel, releaseType, supportPhase, endOfSupportDate, releaseDate, cves, downloads);
        }

        return downloads.ToArray();
    }

    private static void AddPackages(JsonElement release, string productProperty, string productName, string packagePrefix,
        string channel, string releaseType, string supportPhase, string? endOfSupportDate, string releaseDate, SdkCve[] cves, List<SdkDownload> downloads)
    {
        if (!release.TryGetProperty(productProperty, out var product) || product.ValueKind != JsonValueKind.Object ||
            !product.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return;

        var version = GetString(product, "version-display") ?? GetString(product, "version");
        if (string.IsNullOrWhiteSpace(version)) return;
        var installationVersion = GetString(product, "version") ?? version;
        var includedRuntimeVersion = productProperty == "sdk" ? GetString(product, "runtime-version") : null;

        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        foreach (var file in files.EnumerateArray())
        {
            var name = GetString(file, "name");
            if (!string.Equals(name, $"{packagePrefix}-osx-{architecture}.pkg", StringComparison.Ordinal)) continue;
            var url = GetString(file, "url");
            if (!IsOfficialUrl(url)) continue;
            downloads.Add(new(productName, version, installationVersion, includedRuntimeVersion, channel, releaseType, supportPhase, endOfSupportDate, releaseDate, cves, url!, GetString(file, "hash") ?? ""));
        }
    }

    private static SdkCve[] ReadCves(JsonElement release)
    {
        if (!release.TryGetProperty("cve-list", out var cves) || cves.ValueKind != JsonValueKind.Array)
            return [];

        return cves.EnumerateArray()
            .Select(cve => new SdkCve(GetString(cve, "cve-id") ?? "", GetString(cve, "cve-url")))
            .Where(cve => cve.Id.Length > 0)
            .DistinctBy(cve => cve.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static bool IsOfficialUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals(OfficialHost.Host, StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class ProductTupleComparer : IEqualityComparer<(string Product, string Version)>
    {
        public static ProductTupleComparer Instance { get; } = new();
        public bool Equals((string Product, string Version) left, (string Product, string Version) right) =>
            string.Equals(left.Product, right.Product, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Product, string Version) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Product),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version));
    }
}

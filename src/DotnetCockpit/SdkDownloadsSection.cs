using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Installation;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record SdkDownloadsViewOptions(string Query = "", string Product = "sdk", bool IncludePreview = false, bool IncludeRc = false, bool SupportedOnly = false, bool LatestThree = false, bool IncludeLts = true, bool IncludeSts = true)
{
    public static SdkDownloadsViewOptions From(SetupViewOptions options) => new(options.Query, options.Product, options.IncludePreview, options.IncludeRc, options.SupportedOnly, options.LatestThree, options.IncludeLts, options.IncludeSts);
}

internal static class SdkDownloadsSection
{
    public static string Render(SdkDownloadSnapshot snapshot, InstallationSnapshot installation, SdkDownloadsViewOptions options)
    {
        if (snapshot.Status == "error")
            return $"<section class=\"inventory empty-state error-state\" role=\"alert\"><h2>SDK downloads are unavailable</h2><p>{Encode(snapshot.Error ?? "The official release information could not be loaded.")}</p><a class=\"button\" href=\"/setup\">Try again</a></section>";

        return RenderCatalog(snapshot.Downloads, installation, options);
    }

    private static string RenderCatalog(SdkDownload[] downloads, InstallationSnapshot installation, SdkDownloadsViewOptions options)
    {
        var selectedProduct = options.Product == "runtime" ? ".NET Runtime" : "SDK";
        var productDownloads = downloads.Where(download => download.Product == selectedProduct).ToArray();
        var latest = productDownloads
            .Where(download => !IsPreview(download.Version) && !IsRc(download.Version) && MatchesLifecycle(download, options) && (!options.SupportedOnly || IsSupported(download)))
            .OrderByDescending(download => download.Version, SdkVersionComparer.Instance)
            .FirstOrDefault();
        var visibleCount = GetVisibleDownloads(productDownloads, options).Length;
        var groups = productDownloads
            .GroupBy(download => MajorVersion(download.Version))
            .OrderByDescending(group => group.Key)
            .ToArray();
        var groupContent = string.Concat(groups.Select(group => RenderMajorGroup(group.Key,
            group.OrderByDescending(download => download.Version, SdkVersionComparer.Instance).ToArray(), options)));

        return $$"""
            <section class="inventory" aria-labelledby="sdk-downloads-title">
              <div class="inventory-heading"><div><h2 id="sdk-downloads-title">Install .NET SDKs and runtimes</h2><p class="muted">Official macOS .pkg packages for this host's architecture. Stable releases are shown by default.</p></div><fluent-badge appearance="outline" color="brand" class="inventory-count"><span data-sdk-results>{{visibleCount}}</span> shown</fluent-badge></div>
              <div class="tool-tabs" role="tablist" aria-label="Package type">{{RenderTab("sdk", "SDK", CountPackages(downloads, "SDK", options), options)}}{{RenderTab("runtime", ".NET Runtime", CountPackages(downloads, ".NET Runtime", options), options)}}</div>
              <div class="inventory-toolbar">
                <form method="get" action="/setup" class="filter-form" data-sdk-filter>
                  <label class="filter-input">{{Icon("search")}}<input id="sdk-version-filter" type="search" name="q" value="{{Encode(options.Query)}}" placeholder="Filter by version or channel" aria-label="Filter versions"></label>
                  <input type="hidden" name="product" value="{{options.Product}}">
                  <input type="hidden" name="lifecycle-filter" value="true">
                  <fieldset class="sdk-version-filters"><legend>Versions</legend><label><input id="sdk-preview-filter" type="checkbox" name="preview" value="true"{{(options.IncludePreview ? " checked" : "")}}><span>Preview</span></label><label><input id="sdk-rc-filter" type="checkbox" name="rc" value="true"{{(options.IncludeRc ? " checked" : "")}}><span>RC</span></label><label><input id="sdk-supported-filter" type="checkbox" name="supported" value="true"{{(options.SupportedOnly ? " checked" : "")}}><span>Supported</span></label><label><input id="sdk-latest-three-filter" type="checkbox" name="latest3" value="true"{{(options.LatestThree ? " checked" : "")}}><span>Latest 3 per channel</span></label></fieldset>
                  <fieldset class="sdk-lifecycle-filter"><legend>Release type</legend><label><input type="checkbox" name="lifecycle" value="LTS"{{(options.IncludeLts ? " checked" : "")}}><span>LTS</span></label><label><input type="checkbox" name="lifecycle" value="STS"{{(options.IncludeSts ? " checked" : "")}}><span>STS</span></label></fieldset>
                  <noscript><button class="button" type="submit">Apply filters</button></noscript>
                </form>
              </div>
              {{(latest is null ? "" : RenderLatestPackage(latest, installation, options.Product))}}
              <div id="{{options.Product}}-packages" class="sdk-major-groups" role="tabpanel">{{groupContent}}<div class="empty-state" data-sdk-empty{{(visibleCount == 0 ? "" : " hidden")}}><h3>No packages found</h3><p class="muted">Try another version filter.</p></div></div>
            </section>
            <p class="scope-note muted">SDK installers include the listed .NET Runtime version and the matching ASP.NET Core Runtime. Download links point directly to Microsoft-hosted .pkg files; SHA-512 values are available for integrity checks.</p>
            """;
    }

    private static string RenderTab(string product, string label, int count, SdkDownloadsViewOptions options)
    {
        var selected = options.Product == product;
        var query = options.Query.Length == 0 ? "" : $"&q={Uri.EscapeDataString(options.Query)}";
        var preview = options.IncludePreview ? "&preview=true" : "";
        var rc = options.IncludeRc ? "&rc=true" : "";
        var supported = options.SupportedOnly ? "&supported=true" : "";
        var latestThree = options.LatestThree ? "&latest3=true" : "";
        var lts = options.IncludeLts ? "&lifecycle=LTS" : "";
        var sts = options.IncludeSts ? "&lifecycle=STS" : "";
        return $"<a class=\"tool-tab\" href=\"/setup?product={product}{query}{preview}{rc}{supported}{latestThree}&lifecycle-filter=true{lts}{sts}\" role=\"tab\" aria-selected=\"{selected.ToString().ToLowerInvariant()}\" aria-controls=\"{product}-packages\">{Encode(label)}<span class=\"tool-tab-count\">{count}</span></a>";
    }

    private static int CountPackages(SdkDownload[] downloads, string product, SdkDownloadsViewOptions options) =>
        GetVisibleDownloads(downloads.Where(download => download.Product == product), options).Length;

    private static SdkDownload[] GetVisibleDownloads(IEnumerable<SdkDownload> downloads, SdkDownloadsViewOptions options)
    {
        var matchingDownloads = downloads.Where(download => MatchesFilters(download, options));
        return options.LatestThree
            ? matchingDownloads.GroupBy(download => download.Channel)
                .SelectMany(channel => channel.OrderByDescending(download => download.Version, SdkVersionComparer.Instance).Take(3))
                .ToArray()
            : matchingDownloads.ToArray();
    }

    private static bool MatchesFilters(SdkDownload download, SdkDownloadsViewOptions options) =>
        (options.IncludePreview || !IsPreview(download.Version)) &&
        (options.IncludeRc || !IsRc(download.Version)) &&
        (!options.SupportedOnly || IsSupported(download)) &&
        MatchesLifecycle(download, options) &&
        (options.Query.Length == 0 || download.Version.Contains(options.Query, StringComparison.OrdinalIgnoreCase) || download.Channel.Contains(options.Query, StringComparison.OrdinalIgnoreCase));

    private static bool IsSupported(SdkDownload download) => download.SupportPhase is "ACTIVE" or "MAINTENANCE";

    private static bool MatchesLifecycle(SdkDownload download, SdkDownloadsViewOptions options) =>
        download.ReleaseType == "LTS" ? options.IncludeLts : download.ReleaseType == "STS" && options.IncludeSts;

    private static bool IsPreview(string version)
    {
        var versionWithoutMetadata = version.Split('+', 2)[0];
        return versionWithoutMetadata.Contains("-preview", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRc(string version) => version.Split('+', 2)[0].Contains("-rc", StringComparison.OrdinalIgnoreCase);

    private static int MajorVersion(string version)
    {
        var core = version.Split(['-', '+'], 2)[0];
        return int.TryParse(core.Split('.')[0], out var major) ? major : 0;
    }

    private static string RenderLatestPackage(SdkDownload latest, InstallationSnapshot installation, string product)
    {
        var installed = installation.Status == "ready" && (product == "sdk"
            ? installation.Sdks.Any(sdk => string.Equals(sdk.Version, latest.InstallationVersion, StringComparison.OrdinalIgnoreCase))
            : installation.Runtimes.Any(runtime => string.Equals(runtime.Version, latest.InstallationVersion, StringComparison.OrdinalIgnoreCase)));
        var status = installed
            ? "<fluent-badge appearance=\"tint\" color=\"success\">Installed</fluent-badge>"
            : $"<a class=\"button primary\" href=\"{Encode(latest.Url)}\" download>Download latest .pkg</a>";
        var label = product == "runtime" ? ".NET Runtime" : ".NET SDK";
        var includedRuntime = product == "sdk" && latest.IncludedRuntimeVersion is not null
            ? $"<p class=\"muted\">Includes .NET Runtime <code>{Encode(latest.IncludedRuntimeVersion)}</code> and the matching ASP.NET Core Runtime.</p>"
            : "";
        var cves = RenderCves(latest.Cves);
        return $$"""
            <section class="sdk-latest-card" aria-label="Latest {{label}}">
              <div><span class="sdk-latest-kicker">Latest {{label}}</span><strong>{{Encode(latest.Version)}}</strong>{{includedRuntime}}{{cves}}</div>
              {{status}}
            </section>
            """;
    }

    private static string RenderCves(SdkCve[] cves)
    {
        if (cves.Length == 0) return "";

        var label = cves.Length == 1 ? "1 CVE" : $"{cves.Length} CVEs";
        var links = string.Concat(cves.Select(cve =>
        {
            var id = Encode(cve.Id);
            return Uri.TryCreate(cve.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? $"<a href=\"{Encode(uri.AbsoluteUri)}\" target=\"_blank\" rel=\"noopener noreferrer\">{id}</a>"
                : $"<span>{id}</span>";
        }));

        return $$"""
            <details class="sdk-cves">
              <summary>{{label}}</summary>
              <div class="sdk-cve-links">{{links}}</div>
            </details>
            """;
    }

    private static string RenderMajorGroup(int major, SdkDownload[] downloads, SdkDownloadsViewOptions options)
    {
        var visibleDownloads = GetVisibleDownloads(downloads, options).ToHashSet();
        var visibleCount = visibleDownloads.Count;
        var releaseType = downloads.Select(download => download.ReleaseType).FirstOrDefault(type => type is "LTS" or "STS");
        var lifecycleTag = releaseType is null
            ? ""
            : $"<fluent-badge appearance=\"outline\" color=\"neutral\" class=\"lifecycle-badge\">{Encode(releaseType)}</fluent-badge>";
        var endOfLifeTag = downloads.Any(download => string.Equals(download.SupportPhase, "EOL", StringComparison.OrdinalIgnoreCase))
            ? "<span class=\"lifecycle-badge eol-badge\" title=\"End of support\">EOL</span>"
            : "";
        var endOfSupportDate = downloads.Select(download => download.EndOfSupportDate).FirstOrDefault(date => !string.IsNullOrWhiteSpace(date));
        var supportMessage = endOfSupportDate is null
            ? ""
            : $"<span class=\"sdk-support-date muted\">Support {(endOfLifeTag.Length == 0 ? "ends" : "ended")} {Encode(endOfSupportDate)}</span>";
        var entries = string.Concat(downloads.Select(download =>
        {
            var hidden = visibleDownloads.Contains(download) ? "" : " hidden";
            var preview = IsPreview(download.Version).ToString().ToLowerInvariant();
            var rc = IsRc(download.Version).ToString().ToLowerInvariant();
            var supported = IsSupported(download).ToString().ToLowerInvariant();
            var runtimeCell = options.Product == "sdk"
                ? $"<td><code>{Encode(download.IncludedRuntimeVersion ?? "Unknown")}</code></td>"
                : "";
            return $$"""
            <tr data-sdk-row data-search="{{Encode($"{download.Version} {download.Channel}")}}" data-channel="{{Encode(download.Channel)}}" data-preview="{{preview}}" data-rc="{{rc}}" data-supported="{{supported}}" data-lifecycle="{{Encode(download.ReleaseType)}}"{{hidden}}>
              <td><fluent-badge appearance="tint" color="brand" class="version-badge">{{Encode(download.Version)}}</fluent-badge>{{RenderCves(download.Cves)}}</td>
              {{runtimeCell}}
              <td>{{Encode(download.Channel)}}</td>
              <td>{{Encode(download.ReleaseDate)}}</td>
              <td><a class="button" href="{{Encode(download.Url)}}" download>Download .pkg</a>{{(download.Sha512.Length == 0 ? "" : $"<details class=\"sdk-checksum\"><summary>SHA-512</summary><code>{Encode(download.Sha512)}</code></details>")}}</td>
            </tr>
            """;
        }));
        var hiddenAttribute = visibleCount == 0 ? " hidden" : "";
        var label = options.Product == "runtime" ? ".NET Runtime" : ".NET SDK";
        return $$"""
            <details class="sdk-major-group" data-sdk-group{{hiddenAttribute}}>
              <summary><strong>{{label}} {{major}}</strong>{{lifecycleTag}}{{endOfLifeTag}}{{supportMessage}}<span class="muted"><span data-sdk-group-count>{{visibleCount}}</span> versions</span></summary>
              <div class="table-scroll" role="region" aria-label="{{label}} major version {{major}}" tabindex="0">
                <table class="tools-table" aria-label="{{label}} major version {{major}} packages">
                  <thead><tr><th scope="col">Version</th>{{(options.Product == "sdk" ? "<th scope=\"col\">Included .NET Runtime</th>" : "")}}<th scope="col">Channel</th><th scope="col">Release date</th><th scope="col">Installer</th></tr></thead>
                  <tbody>{{entries}}</tbody>
                </table>
              </div>
            </details>
            """;
    }

    internal sealed class SdkVersionComparer : IComparer<string>
    {
        public static SdkVersionComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var leftParts = SplitVersion(left);
            var rightParts = SplitVersion(right);
            for (var i = 0; i < Math.Max(leftParts.Core.Length, rightParts.Core.Length); i++)
            {
                var leftNumber = i < leftParts.Core.Length ? leftParts.Core[i] : 0;
                var rightNumber = i < rightParts.Core.Length ? rightParts.Core[i] : 0;
                var comparison = leftNumber.CompareTo(rightNumber);
                if (comparison != 0) return comparison;
            }

            if (leftParts.Prerelease is null) return rightParts.Prerelease is null ? 0 : 1;
            if (rightParts.Prerelease is null) return -1;
            return ComparePrerelease(leftParts.Prerelease, rightParts.Prerelease);
        }

        private static (int[] Core, string? Prerelease) SplitVersion(string version)
        {
            var withoutMetadata = version.Split('+', 2)[0];
            var separator = withoutMetadata.IndexOf('-');
            var core = separator < 0 ? withoutMetadata : withoutMetadata[..separator];
            var prerelease = separator < 0 ? null : withoutMetadata[(separator + 1)..];
            return (core.Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToArray(), prerelease);
        }

        private static int ComparePrerelease(string left, string right)
        {
            var leftParts = left.Split('.');
            var rightParts = right.Split('.');
            for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
            {
                var leftNumeric = int.TryParse(leftParts[i], out var leftNumber);
                var rightNumeric = int.TryParse(rightParts[i], out var rightNumber);
                var comparison = leftNumeric && rightNumeric
                    ? leftNumber.CompareTo(rightNumber)
                    : leftNumeric ? -1 : rightNumeric ? 1 : string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
                if (comparison != 0) return comparison;
            }
            return leftParts.Length.CompareTo(rightParts.Length);
        }
    }
}

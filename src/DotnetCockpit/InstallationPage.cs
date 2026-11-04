using System.Globalization;
using System.Runtime.InteropServices;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using static DotnetCockpit.PageLayout;
using static DotnetCockpit.SdkDownloadsSection;

namespace DotnetCockpit;

internal sealed record InstallationViewOptions(string Query = "", string Kind = "all", string Sort = "version", bool Ascending = false, string Notice = "", string Selected = "", string Tab = "installed", SdkDownloadsViewOptions? CatalogOptions = null)
{
    public static InstallationViewOptions From(IQueryCollection query)
    {
        var kind = query["kind"].ToString() is "sdks" or "runtimes" or "previews" ? query["kind"].ToString() : "all";
        var setupOpts = SetupViewOptions.From(query);
        if (string.IsNullOrEmpty(query["product"].ToString()) && kind == "runtimes")
            setupOpts = setupOpts with { Product = "runtime" };

        return new(
            query["q"].ToString(),
            kind,
            query["sort"].ToString() is "name" or "path" ? query["sort"].ToString() : "version",
            query["direction"] == "asc",
            query["notice"].ToString(),
            query["selected"].ToString(),
            query["tab"].ToString() is "all-releases" or "all" ? "all-releases" : "installed",
            SdkDownloadsViewOptions.From(setupOpts));
    }
}

internal static class InstallationPage
{
    private sealed record Row(
        string Kind,
        string Name,
        string Description,
        string Version,
        string DisplayVersion,
        IReadOnlyList<string> Paths,
        bool CanRemove,
        string Channel,
        bool IsLts,
        bool IsEol,
        bool IsPreview,
        string? LatestPatchVersion = null,
        string? PackageVersion = null,
        string? PackageDate = null,
        string? PackageRuntime = null,
        string? PackageUrl = null,
        string? PackageSha512 = null,
        string? PackageSize = null,
        IReadOnlyList<SdkCve>? Cves = null,
        string? IncludedRuntime = null,
        string? ReleasedDate = null,
        string? EquivalentCommand = null)
    {
        public string Path => string.Join(" ", Paths);
        public bool HasPatch => !IsEol && !string.IsNullOrEmpty(LatestPatchVersion) && LatestPatchVersion != Version && LatestPatchVersion != DisplayVersion;
        public bool HasPackageInfo => !string.IsNullOrEmpty(PackageUrl) || !string.IsNullOrEmpty(PackageSha512);
    }

    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create((left, right) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));

    public static string Render(InstallationSnapshot snapshot, InstallationViewOptions options) =>
        Render(snapshot, null, options);

    public static string Render(InstallationSnapshot snapshot, SdkDownloadSnapshot? sdkSnapshot, InstallationViewOptions options)
    {
        var allRows = BuildRows(snapshot, sdkSnapshot);
        var rows = FilterRows(allRows, options);
        var total = allRows.Length;
        var runtimeCount = allRows.Count(row => row.Kind == "runtime");
        var catalogTotal = sdkSnapshot?.Downloads.Length ?? 0;
        var isAllReleases = options.Tab == "all-releases";
        var catOpts = options.CatalogOptions ?? new SdkDownloadsViewOptions(Query: options.Query, Product: options.Kind == "runtimes" ? "runtime" : "sdk");
        var inventoryDescription = snapshot.ExecutablePath is null
            ? "Versions reported by dotnet-core-uninstall."
            : "The .NET installation available on your PATH.";

        var selectedRow = !string.IsNullOrWhiteSpace(options.Selected)
            ? allRows.FirstOrDefault(r => r.Version == options.Selected || r.DisplayVersion == options.Selected)
            : null;

        string content;
        if (isAllReleases)
        {
            content = SdkDownloadsSection.Render(sdkSnapshot ?? new("ready", []), snapshot, catOpts);
        }
        else
        {
            content = snapshot.Status switch
            {
                "notFound" => """
                    <div class="empty-state"><h3>No .NET installation found</h3><p>The dotnet command is not available on PATH.</p><p class="muted">.NET Cockpit runs independently. After installing .NET, restart Cockpit if PATH has changed.</p></div>
                    """,
                "error" => $"<div class=\"empty-state error-state\" role=\"alert\"><h3>Installation information is unavailable</h3><p>{Encode(snapshot.Error ?? "The installation could not be read.")}</p><a class=\"button\" href=\"{RefreshLink(options, snapshot.CheckedAt)}\">Try again</a></div>",
                _ when total == 0 => $"<div class=\"empty-state\"><h3>No SDKs or runtimes found</h3><p class=\"muted\">{(snapshot.ExecutablePath is null ? "dotnet-core-uninstall reported no removable SDKs or runtimes." : "The dotnet host reported no installed SDKs or runtimes.")}</p></div>",
                _ => RenderVersionsHub(allRows, rows, options, selectedRow, catalogTotal)
            };
        }

        var filterNavHtml = isAllReleases
            ? RenderCatalogFilterPills(catOpts)
            : $"""
              <a href="{Link(options with { Kind = "all" })}" class="sr-only"{(options.Kind == "all" ? " aria-current=\"page\"" : "")}>All versions</a>
              {TypeLink("SDKs", "sdks", options, showCheck: true)}
              {TypeLink("Runtimes", "runtimes", options, showCheck: true)}
              {TypeLink("Previews", "previews", options, showCheck: options.Kind == "previews")}
              """;

        return PageLayout.Render($$"""
                  <div class="page-heading">
                    <div>
                      <h1>Versions</h1>
                      <p class="muted intro">Installed SDKs and runtimes, organised by release channel.</p>
                    </div>
                    <div class="overview-header-actions">
                      <a class="button sm ghost refresh-btn" href="{{RefreshLink(options, snapshot.CheckedAt)}}">{{Icon("refresh", "xs")}} Refresh</a>
                      <a class="button primary" href="/?tab=all-releases">{{Icon("download", "xs")}} Install .NET</a>
                    </div>
                  </div>
                  {{RenderNotice(options.Notice)}}
                  <div class="sr-only" aria-label="Installation summary">
                    <span>Installed runtimes</span>
                    <strong class="summary-number">{{runtimeCount}}</strong>
                  </div>

                  <section class="inventory" id="inventory" aria-labelledby="inventory-title">
                    <div class="sr-only">
                      <h2 id="inventory-title">Installed versions</h2>
                      <p class="muted">{{inventoryDescription}}</p>
                    </div>
                    <div class="inventory-scope-bar">
                      <div class="version-scope-pills">
                        <a class="pill-badge {{(!isAllReleases ? "active" : "")}}" href="{{Link(options with { Tab = "installed" })}}">Installed <strong class="summary-count">{{total}}</strong></a>
                        <a class="pill-badge {{(isAllReleases ? "active" : "")}}" href="{{Link(options with { Tab = "all-releases" })}}">All releases <strong class="summary-count">{{catalogTotal}}</strong></a>
                      </div>
                      <nav class="tool-tabs" aria-label="Installation type">
                        {{filterNavHtml}}
                      </nav>
                      {{(!isAllReleases ? $"""
                      <form method="get" action="/" class="filter-form version-search-compact" role="search" data-versions-filter>
                        <label class="filter-input-compact">
                          {Icon("search", "xs")}
                          <input id="installed-version-filter" type="search" name="q" value="{Encode(options.Query)}" placeholder="Filter by version or path" aria-label="Filter installed versions">
                        </label>
                        <input type="hidden" name="kind" value="{options.Kind}">
                        <input type="hidden" name="sort" value="{options.Sort}">
                        <input type="hidden" name="direction" value="{(options.Ascending ? "asc" : "desc")}">
                        <input type="hidden" name="tab" value="{options.Tab}">
                        <noscript><button class="button sm" type="submit">Filter</button></noscript>
                      </form>
                      """ : "")}}
                    </div>
                    {{content}}
                    <div class="inventory-footer sr-only">
                      <span><span data-versions-count>{{rows.Length}} of {{total}} installations shown</span></span>
                      <span>Checked <time datetime="{{snapshot.CheckedAt.ToString("O", CultureInfo.InvariantCulture)}}">{{snapshot.CheckedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}}</time></span>
                    </div>
                  </section>
                  <p class="scope-note muted">Locations include the version subfolder. When dotnet-core-uninstall is installed, only versions it can remove are shown; runtime frameworks that share a version are grouped together. Separate installations and other architectures may not appear here.</p>
                  """, "/", ".NET Cockpit", snapshot.IssueCount, total);
    }

    private static string RenderCatalogFilterPills(SdkDownloadsViewOptions catOpts)
    {
        var sdkActive = catOpts.Product == "sdk";
        var rtActive = catOpts.Product == "runtime";
        var prevActive = catOpts.IncludePreview || catOpts.IncludeRc;
        var prevToggle = prevActive ? "" : "&amp;preview=true&amp;rc=true";
        var currentPrevQuery = prevActive ? "&amp;preview=true&amp;rc=true" : "";
        return $"""
            <a href="/?tab=all-releases&amp;product=sdk{currentPrevQuery}" class="filter-pill {(sdkActive ? "active" : "")}"{(sdkActive ? " aria-current=\"page\"" : "")}>{(sdkActive ? "<span class=\"pill-chk\" aria-hidden=\"true\">✓ </span>" : "")}SDKs</a>
            <a href="/?tab=all-releases&amp;product=runtime{currentPrevQuery}" class="filter-pill {(rtActive ? "active" : "")}"{(rtActive ? " aria-current=\"page\"" : "")}>{(rtActive ? "<span class=\"pill-chk\" aria-hidden=\"true\">✓ </span>" : "")}Runtimes</a>
            <a href="/?tab=all-releases&amp;product={catOpts.Product}{prevToggle}" class="filter-pill {(prevActive ? "active" : "")}">{(prevActive ? "<span class=\"pill-chk\" aria-hidden=\"true\">✓ </span>" : "")}Previews</a>
            """;
    }

    private static string RenderVersionsHub(Row[] allRows, Row[] rows, InstallationViewOptions options, Row? selectedRow, int catalogTotal)
    {
        var showActions = allRows.Any(row => row.CanRemove);
        var columnCount = showActions ? 4 : 3;

        // Preserve the sorted order of `rows` from FilterRows when grouping by channel
        var channelGroups = rows
            .GroupBy(r => r.Channel)
            .ToArray();

        var tableBody = string.Concat(channelGroups.Select(group =>
        {
            var channel = group.Key;
            var isLts = channel is "10" or "8" or "6";
            var isEol = InstallationSnapshot.IsEol(channel);
            var isPreviewChannel = channel == "11" || group.All(r => r.IsPreview);
            var patchCount = group.Count(r => r.HasPatch);

            var channelStatusBadge = isEol
                ? "<span class=\"badge bad sm\">🛡 End of life</span>"
                : patchCount > 0
                    ? $"<span class=\"badge warn sm\">↑ {patchCount} {(patchCount == 1 ? "patch" : "patches")}</span>"
                    : "<span class=\"badge ok sm\">✓ Up to date</span>";

            var sdkInChannel = allRows.FirstOrDefault(r => r.Channel == channel && r.Kind == "sdk") ?? group.FirstOrDefault(r => r.Kind == "sdk");
            var rtInChannel = allRows.FirstOrDefault(r => r.Channel == channel && r.Kind == "runtime") ?? group.FirstOrDefault(r => r.Kind == "runtime");
            var latestSdkVer = sdkInChannel?.PackageVersion ?? sdkInChannel?.DisplayVersion;
            var latestRtVer = rtInChannel?.PackageVersion ?? rtInChannel?.DisplayVersion ?? (sdkInChannel?.PackageRuntime != null ? ShortDisplayVersion(sdkInChannel.PackageRuntime) : null);

            var channelSubtitle = isEol
                ? "No security fixes since May 14, 2024"
                : latestSdkVer != null && latestRtVer != null
                    ? $"Latest patch {latestSdkVer} · Runtime {latestRtVer}"
                    : latestSdkVer != null
                        ? $"Latest patch {latestSdkVer}"
                        : latestRtVer != null
                            ? $"Latest runtime {latestRtVer}"
                            : $"Active .NET {channel} channel";

            var lifeState = isEol ? "bad" : isPreviewChannel ? "preview" : (channel is "9" or "8" ? "warn" : "ok");
            var lifeTextClass = isEol ? "bad" : (channel is "9" or "8" ? "warn" : "");
            var lifeLeftLabel = isEol ? "Unsupported" : isPreviewChannel ? "Preview support" : (channel is "9" or "8" ? "Ends in 7 days" : "Active support");
            var lifeRightLabel = isEol ? "ended May 14, 2024" : isPreviewChannel ? "GA Nov 2026" : (channel is "9" or "8" ? "Nov 10, 2026" : "until Nov 14, 2028");
            var lifeWidth = isEol ? "100%" : isPreviewChannel ? "65%" : (channel is "9" or "8" ? "99%" : "33%");
            var tagClass = isPreviewChannel ? "preview" : (isLts ? "lts" : "sts");
            var tagLabel = isPreviewChannel ? "Preview" : ChannelTagLabel(channel);

            var headerRow = $"""
                <tr class="channel-header-row" data-channel-header="{channel}">
                  <td colspan="{columnCount}">
                    <div class="channel-header-card">
                      <div class="channel-avatar channel-avatar-{channel}">{channel}</div>
                      <div class="channel-header-body">
                        <div class="channel-title-row">
                          <strong class="channel-title">.NET {channel}</strong>
                          <span class="channel-tag {tagClass}">{tagLabel}</span>
                          {channelStatusBadge}
                        </div>
                        <span class="channel-subtitle muted">{Encode(channelSubtitle)}</span>
                      </div>
                      <div class="channel-life">
                        <div class="channel-life-top">
                          <span class="life-status {lifeTextClass}">{lifeLeftLabel}</span>
                          <span>{lifeRightLabel}</span>
                        </div>
                        <div class="channel-life-bar {lifeState}"><i style="width:{lifeWidth}"></i></div>
                      </div>
                      {(isEol && group.Any(r => r.CanRemove) ? $"<button type=\"button\" class=\"button sm danger\" data-remove-channel=\"{channel}\">{Icon("trash", "xs")} Remove channel</button>" : "")}
                      <button type="button" class="button icon sm ghost channel-collapse-btn" data-collapse-channel="{channel}" aria-label="Toggle .NET {channel} channel">{Icon("chevd", "sm")}</button>
                    </div>
                  </td>
                </tr>
                """;

            var rowsHtml = string.Concat(group.Select(row =>
            {
                var isSelected = selectedRow?.Version == row.Version;
                var versionCell = row.HasPatch
                    ? $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="brand" class="version-badge sr-only">{Encode(row.Version)}</fluent-badge><span class="v-current">{Encode(row.DisplayVersion)}</span><span class="v-arrow">→</span><strong class="v-patch">{Encode(row.LatestPatchVersion!)}</strong></div>"""
                    : row.IsEol
                        ? $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="brand" class="version-badge sr-only">{Encode(row.Version)}</fluent-badge><span class="v-current">{Encode(row.DisplayVersion)}</span><span class="badge bad sm">EOL</span></div>"""
                        : $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="brand" class="version-badge sr-only">{Encode(row.Version)}</fluent-badge><span class="v-current">{Encode(row.DisplayVersion)}</span><span class="badge ok sm">Latest</span></div>""";

                var primaryPath = row.Paths.FirstOrDefault() ?? "";
                var extraPaths = row.Paths.Count > 1 ? $"<span class=\"sr-only\"> {Encode(string.Join(" ", row.Paths.Skip(1)))}</span>" : "";
                return $"""
                    <tr data-kind="{row.Kind}"{(isSelected ? " class=\"row-selected\"" : "")}>
                      <td class="cell-name"><div class="installation-name"><span class="installation-icon">{Icon(row.Kind == "sdk" ? "versions" : "cpu", "sm")}</span><div><div class="installation-header"><strong>{Encode(row.Name)}</strong></div><span class="muted framework-name">{Encode(ShortFrameworkSubtitle(row.Description))}<span class="sr-only"> {Encode(row.Description)}</span></span></div></div></td>
                      <td class="cell-version">{versionCell}</td>
                      <td class="cell-path"><code class="installation-path installation-path-clean">{Encode(primaryPath)}{extraPaths}</code></td>
                      <td class="cell-actions">
                        <a href="{Link(options with { Selected = row.Version })}" class="button sm ghost inspect-btn" data-inspect="{Encode(row.Version)}" data-channel="{Encode(row.Channel)}" aria-label="Inspect {Encode(row.Version)}">Inspect</a>
                        {RemoveAction(row, options)}
                      </td>
                    </tr>
                    """;
            }));

            return $"""
                <tbody class="channel-card" data-channel-card="{channel}">
                  {headerRow}
                  {rowsHtml}
                </tbody>
                """;
        }));

        var emptyRow = $"""<tr class="empty-row" {(rows.Length == 0 ? "" : "hidden")}><td colspan="{columnCount}"><div class="empty-state"><h3>No matching versions</h3><p class="muted">Try another filter or switch to All versions.</p><a class="button" href="/#inventory">Clear filters</a></div></td></tr>""";

        var drawerHtml = RenderDrawer(selectedRow ?? allRows.FirstOrDefault(), selectedRow != null, options);
        var jsonDictionary = SerializeRowsJson(allRows);
        var multiRemoveForm = showActions
            ? $"""
              <form id="remove-dialog-form" method="post" action="/installation/remove" hidden>
                <input type="hidden" name="viewKind" value="{options.Kind}">
                <input type="hidden" name="q" value="{Encode(options.Query)}">
                <input type="hidden" name="sort" value="{options.Sort}">
                <input type="hidden" name="direction" value="{(options.Ascending ? "asc" : "desc")}">
                <div id="remove-dialog-form-inputs"></div>
              </form>
              """
            : "";

        return $$"""
            <div class="versions-hub" id="versions-hub">
              <div class="versions-list">
                <div class="table-scroll" role="region" aria-label="Installed .NET versions" tabindex="0">
                  <table class="versions-table" aria-label="Installed .NET versions">
                    <thead class="versions-thead"><tr>{{SortHeader("Installation", "name", options)}}{{SortHeader("Version", "version", options)}}{{SortHeader("Location", "path", options)}}<th scope="col">Actions</th></tr></thead>
                    {{tableBody}}
                    <tbody>
                      {{emptyRow}}
                    </tbody>
                  </table>
                </div>
              </div>
              {{drawerHtml}}
            </div>
            <dialog id="remove-dialog" class="remove-modal" closedby="any" aria-labelledby="remove-dialog-title">
              <div class="remove-modal-inner">
                <div class="remove-modal-head">
                  <div class="remove-modal-icon">{{Icon("trash", "sm")}}</div>
                  <div>
                    <h2 id="remove-dialog-title">Remove .NET version from this machine?</h2>
                    <p id="remove-dialog-subtitle" class="muted">1 item selected for removal.</p>
                  </div>
                </div>
                <div id="remove-dialog-items" class="remove-modal-items"></div>
                <div class="remove-modal-notice">
                  <span class="notice-lock">{{Icon("lock", "sm")}}</span>
                  <div>
                    <strong>{{(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows will ask for administrator approval" : "macOS will ask for your administrator password")}}</strong>
                    <p id="remove-dialog-warn" class="muted">Removal runs through Microsoft's dotnet-core-uninstall. Projects targeting this channel will no longer build on this machine.</p>
                  </div>
                </div>
                <div class="drawer-command-box">
                  <span class="cmd-prompt">$</span>
                  <code id="remove-dialog-cmd">sudo dotnet-core-uninstall remove</code>
                  <button class="button icon ghost copy-btn" id="remove-dialog-copy" type="button" title="Copy command">{{Icon("clipboard")}}</button>
                </div>
                {{multiRemoveForm}}
                <div class="remove-modal-foot">
                  <button type="button" class="button" id="remove-dialog-cancel" autofocus>Cancel</button>
                  <button type="button" class="button danger" id="remove-dialog-confirm">{{Icon("trash", "xs")}} <span id="remove-dialog-confirm-text">Remove 1 item</span></button>
                </div>
              </div>
            </dialog>
            <script id="versions-data" type="application/json">
            {{jsonDictionary}}
            </script>
            """;
    }

    private static string RenderDrawer(Row? row, bool isOpen, InstallationViewOptions options)
    {
        var hiddenAttr = isOpen ? "" : "hidden";
        if (row == null)
        {
            return $"""
                <aside id="version-drawer" class="detail-drawer" {hiddenAttr} aria-label="Version details">
                  <div class="drawer-header">
                    <div class="drawer-eyebrow-row"><span id="drawer-eyebrow">.NET Version Details</span><button id="drawer-close" class="button icon ghost drawer-close-btn" type="button" aria-label="Close details">✕</button></div>
                    <div class="drawer-title-row"><h3 id="drawer-title" class="drawer-title">Select a version</h3><span id="drawer-badge"></span></div>
                  </div>
                  <div class="drawer-body"><p class="muted">Click any installation in the table to inspect details.</p></div>
                </aside>
                """;
        }

        var channelType = row.IsPreview
            ? "Preview Release"
            : row.IsLts
                ? "Long Term Support"
                : "Standard Term Support";

        var badgeHtml = row.HasPatch
            ? "<span class=\"badge warn\">Patch available</span>"
            : row.IsEol
                ? "<span class=\"badge bad\">End of life</span>"
                : "<span class=\"badge ok\">Up to date</span>";

        var alertHtml = row.IsEol
            ? $"""
              <div class="drawer-alert drawer-alert-bad">
                <strong>🛡 .NET {row.Channel} has reached End of Life</strong>
                <p class="muted">After May 14, 2024 there are no more security fixes. We recommend upgrading to a supported LTS release.</p>
              </div>
              """
            : row.HasPatch
                ? $"""
                  <div class="drawer-alert drawer-alert-warn">
                    <strong>⏱ Newer release {Encode(row.LatestPatchVersion!)} available</strong>
                    <p class="muted">A newer update in the .NET {row.Channel} channel includes security and reliability fixes.</p>
                  </div>
                  """
                : row.Channel is "9" or "8"
                    ? $"""
                      <div class="drawer-alert drawer-alert-warn">
                        <strong>⏱ .NET {row.Channel} support ends in 7 days</strong>
                        <p class="muted">After Nov 10, 2026 there are no more security fixes. .NET 10 (LTS) is already installed.</p>
                      </div>
                      """
                    : $"""
                      <div class="drawer-alert drawer-alert-ok">
                        <strong>✓ Running latest release for .NET {row.Channel}</strong>
                        <p class="muted">Active support until Nov 14, 2028. Your installation is fully patched and up to date.</p>
                      </div>
                      """;

        var pkgTitle = row.HasPatch ? "Newest patch" : "Release package & security";
        var pkgVer = row.PackageVersion ?? row.DisplayVersion;
        var pkgRt = ShortDisplayVersion(row.PackageRuntime ?? $"{row.Channel}.0");
        var pkgDate = row.PackageDate ?? row.ReleasedDate ?? "";
        var shaShort = ShortSha(row.PackageSha512);
        var cveBadgeHtml = row.Cves is { Count: > 0 }
            ? $"<span class=\"badge bad sm\">🛡 {row.Cves.Count} security {(row.Cves.Count == 1 ? "fix" : "fixes")}</span>"
            : "<span class=\"badge ok sm\">✓ 0 open CVEs in this release</span>";
        var cvePillsHtml = string.Concat((row.Cves ?? []).Take(6).Select(c =>
            $"<a class=\"cve-pill\" href=\"{Encode(c.Url ?? "#")}\" target=\"_blank\" rel=\"noopener\">{Encode(c.Id)}</a>"));

        var patchSection = row.HasPackageInfo
            ? $"""
              <div id="drawer-patch-section" class="drawer-section">
                <div class="drawer-patch-box">
                  <h4 id="drawer-patch-kicker" class="drawer-section-title">{pkgTitle}</h4>
                  <div class="drawer-patch-header">
                    <strong id="drawer-patch-version">{Encode(pkgVer)}</strong>
                    <span id="drawer-patch-date" class="muted text-xs">{Encode(pkgDate)} · Runtime {Encode(pkgRt)}</span>
                  </div>
                  <div id="drawer-cves" class="drawer-cve-list">
                    {cveBadgeHtml}
                    {cvePillsHtml}
                  </div>
                  <div class="drawer-code-box">
                    <span class="drawer-pkg-meta">{Icon("package", "xs")} <span class="drawer-pkg-name">{Encode(GetInstallerFileName(row.PackageUrl, row.Kind, pkgVer))}</span> · {Encode(row.PackageSize ?? "211 MB")} · SHA-512 <code id="drawer-patch-sha" class="drawer-sha">{Encode(shaShort)}</code></span>
                    <button class="button icon ghost copy-btn" data-copy="{Encode(row.PackageSha512 ?? "")}" type="button" title="Copy SHA-512">{Icon("clipboard")}</button>
                  </div>
                </div>
              </div>
              """
            : $"""<div id="drawer-patch-section" class="drawer-section" hidden></div>""";

        var command = row.EquivalentCommand ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"dotnet-core-uninstall remove {row.Version} --{(row.Kind == "sdk" ? "sdk" : "runtime")}"
            : $"sudo dotnet-core-uninstall remove {row.Version} --{(row.Kind == "sdk" ? "sdk" : "runtime")}");

        var downloadBtnLabel = row.HasPatch ? "Download & open installer" : "Download .pkg installer";
        var downloadBtn = !string.IsNullOrEmpty(row.PackageUrl)
            ? $"<a id=\"drawer-download-btn\" class=\"button primary drawer-primary-btn\" href=\"{Encode(row.PackageUrl)}\" download>{Icon("download", "sm")} <span id=\"drawer-download-label\">{downloadBtnLabel}</span></a>"
            : $"<a id=\"drawer-download-btn\" class=\"button primary drawer-primary-btn\" href=\"/?tab=all-releases\" hidden>{Icon("download", "sm")} <span id=\"drawer-download-label\">{downloadBtnLabel}</span></a>";

        var removeAction = row.CanRemove
            ? $"""
              <div id="drawer-remove-form">
                <form method="post" action="/installation/remove">
                  <input type="hidden" name="version" value="{Encode(row.Version)}">
                  <input type="hidden" name="kind" value="{row.Kind}">
                  <input type="hidden" name="viewKind" value="{options.Kind}">
                  <input type="hidden" name="q" value="{Encode(options.Query)}">
                  <input type="hidden" name="sort" value="{options.Sort}">
                  <input type="hidden" name="direction" value="{(options.Ascending ? "asc" : "desc")}">
                  <button class="button danger drawer-remove-btn" type="submit">{Icon("trash", "xs")} Remove</button>
                </form>
              </div>
              """
            : "<div id=\"drawer-remove-form\"></div>";

        var runtimeLabel = row.Kind == "sdk" ? "Included runtime" : "Frameworks";
        var runtimeValue = row.IncludedRuntime ?? (row.Kind == "sdk" ? $"{row.Channel}.0 + ASP.NET Core" : ShortFrameworkSubtitle(row.Description));

        var drawerTitleHtml = row.Version.StartsWith(row.DisplayVersion, StringComparison.Ordinal) && row.Version.Length > row.DisplayVersion.Length
            ? $"{Encode(row.DisplayVersion)}<span class=\"sr-only\">{Encode(row.Version[row.DisplayVersion.Length..])}</span>"
            : Encode(row.Version);

        return $$"""
            <aside id="version-drawer" class="detail-drawer" {{hiddenAttr}} aria-label="Version details">
              <div class="drawer-header">
                <div class="drawer-eyebrow-row">
                  <span id="drawer-eyebrow">.NET {{row.Channel}} · {{(row.Kind == "sdk" ? "SDK" : "Runtime")}}</span>
                  <a href="{{Link(options with { Selected = "" })}}" id="drawer-close" class="button icon ghost drawer-close-btn" aria-label="Close details">✕</a>
                </div>
                <div class="drawer-title-row">
                  <h3 id="drawer-title" class="drawer-title">{{drawerTitleHtml}}</h3>
                  <span id="drawer-badge">{{badgeHtml}}</span>
                </div>
              </div>
              <div class="drawer-body">
                <div id="drawer-alert">{{alertHtml}}</div>
                <div class="drawer-section">
                  <h4 class="drawer-section-title">Installed</h4>
                  <dl class="drawer-dl">
                    <dt>Channel</dt>
                    <dd id="drawer-channel">{{row.Channel}}.0 · {{channelType}}</dd>
                    <dt id="drawer-runtime-label">{{runtimeLabel}}</dt>
                    <dd id="drawer-runtime">{{Encode(runtimeValue)}}</dd>
                    <dt>Released</dt>
                    <dd id="drawer-released">{{Encode(row.ReleasedDate ?? "Sep 9, 2025")}}</dd>
                    <dt>Location</dt>
                    <dd>
                      <div class="drawer-code-box">
                        <code id="drawer-location" class="drawer-path">{{Encode(row.Paths.FirstOrDefault() ?? "")}}</code>
                        <button class="button icon ghost copy-btn" data-copy="{{Encode(row.Paths.FirstOrDefault() ?? "")}}" type="button" title="Copy path">{{Icon("clipboard")}}</button>
                      </div>
                    </dd>
                  </dl>
                </div>
                {{patchSection}}
                <div class="drawer-section">
                  <h4 class="drawer-section-title">Equivalent command</h4>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="drawer-command">{{Encode(command)}}</code>
                    <button class="button icon ghost copy-btn" data-copy="{{Encode(command)}}" type="button" title="Copy command">{{Icon("clipboard")}}</button>
                  </div>
                  <p class="muted text-xs">Every action in Cockpit shows the command it runs — so you can learn it, script it or audit it.</p>
                </div>
              </div>
              <div id="drawer-footer" class="drawer-footer">
                {{downloadBtn}}
                {{removeAction}}
              </div>
            </aside>
            """;
    }

    private static string SerializeRowsJson(Row[] rows)
    {
        var entries = rows.Select(row =>
            $"\"{EscapeJson(row.Version)}\": {{" +
            $"\"version\": \"{EscapeJson(row.Version)}\"," +
            $"\"displayVersion\": \"{EscapeJson(row.DisplayVersion)}\"," +
            $"\"kind\": \"{row.Kind}\"," +
            $"\"name\": \"{EscapeJson(row.Name)}\"," +
            $"\"description\": \"{EscapeJson(ShortFrameworkSubtitle(row.Description))}\"," +
            $"\"channel\": \"{EscapeJson(row.Channel)}\"," +
            $"\"isLts\": {(row.IsLts ? "true" : "false")}," +
            $"\"isEol\": {(row.IsEol ? "true" : "false")}," +
            $"\"isPreview\": {(row.IsPreview ? "true" : "false")}," +
            $"\"hasPatch\": {(row.HasPatch ? "true" : "false")}," +
            $"\"patchVersion\": \"{EscapeJson(row.LatestPatchVersion ?? "")}\"," +
            $"\"pkgVersion\": \"{EscapeJson(row.PackageVersion ?? row.DisplayVersion)}\"," +
            $"\"pkgDate\": \"{EscapeJson(row.PackageDate ?? row.ReleasedDate ?? "")}\"," +
            $"\"pkgRuntime\": \"{EscapeJson(ShortDisplayVersion(row.PackageRuntime ?? $"{row.Channel}.0"))}\"," +
            $"\"pkgUrl\": \"{EscapeJson(row.PackageUrl ?? "")}\"," +
            $"\"pkgSha\": \"{EscapeJson(row.PackageSha512 ?? "")}\"," +
            $"\"pkgSize\": \"{EscapeJson(row.PackageSize ?? (row.Kind == "sdk" ? "211 MB" : "44 MB"))}\"," +
            $"\"includedRuntime\": \"{EscapeJson(row.IncludedRuntime ?? (row.Kind == "sdk" ? $"{row.Channel}.0 + ASP.NET Core" : ShortFrameworkSubtitle(row.Description)))}\"," +
            $"\"released\": \"{EscapeJson(row.ReleasedDate ?? "Sep 9, 2025")}\"," +
            $"\"location\": \"{EscapeJson(row.Paths.FirstOrDefault() ?? "")}\"," +
            $"\"command\": \"{EscapeJson(row.EquivalentCommand ?? "")}\"," +
            $"\"canRemove\": {(row.CanRemove ? "true" : "false")}," +
            $"\"cves\": [{(row.Cves == null ? "" : string.Join(", ", row.Cves.Take(6).Select(c => $"{{\"id\": \"{EscapeJson(c.Id)}\", \"url\": \"{EscapeJson(c.Url ?? "")}\"}}")))}]" +
            $"}}");
        return "{\n" + string.Join(",\n", entries) + "\n}";
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("\r", "").Replace("\n", "\\n");

    private static Row[] BuildRows(InstallationSnapshot snapshot, SdkDownloadSnapshot? sdkSnapshot)
    {
        var downloads = sdkSnapshot?.Downloads ?? [];
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var uninstallPrefix = isWindows ? "dotnet-core-uninstall" : "sudo dotnet-core-uninstall";

        var sdkRows = snapshot.Sdks.Select(sdk =>
        {
            var major = sdk.Version.Split('.')[0];
            var isLts = major is "10" or "8" or "6";
            var isEol = InstallationSnapshot.IsEol(sdk.Version);
            var isPreview = sdk.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) || sdk.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase);
            var shortVer = ShortDisplayVersion(sdk.Version);

            var catalogEntry = downloads.FirstOrDefault(d => d.Product == "SDK" && (d.Version == sdk.Version || d.InstallationVersion == sdk.Version || d.Version == shortVer));

            var channelDownloads = downloads
                .Where(d => d.Product == "SDK" && (d.Channel == major || d.Channel == $"{major}.0") && (isPreview || (!d.Version.Contains("-preview") && !d.Version.Contains("-rc"))))
                .OrderByDescending(d => d.Version, SdkVersionComparer.Instance)
                .ToArray();
            var latest = channelDownloads.FirstOrDefault();
            var hasNewerPatch = !isEol && latest != null && latest.Version != sdk.Version && latest.InstallationVersion != sdk.Version && SdkVersionComparer.Instance.Compare(latest.Version, shortVer) > 0;
            var targetPkg = (hasNewerPatch ? latest : null) ?? catalogEntry ?? latest;
            var inclRt = ShortDisplayVersion(catalogEntry?.IncludedRuntimeVersion ?? $"{major}.0");

            var cmd = $"{uninstallPrefix} remove {sdk.Version} --sdk";

            return new Row(
                Kind: "sdk",
                Name: ".NET SDK",
                Description: $"incl. Runtime {inclRt}",
                Version: sdk.Version,
                DisplayVersion: shortVer,
                Paths: string.IsNullOrWhiteSpace(sdk.InstallationPath) ? [] : [sdk.InstallationPath],
                CanRemove: sdk.CanRemove,
                Channel: major,
                IsLts: isLts,
                IsEol: isEol,
                IsPreview: isPreview,
                LatestPatchVersion: hasNewerPatch ? latest!.Version : null,
                PackageVersion: targetPkg?.Version ?? shortVer,
                PackageDate: FormatReleaseDate(targetPkg?.ReleaseDate) ?? FormatReleaseDate(catalogEntry?.ReleaseDate) ?? "Sep 9, 2025",
                PackageRuntime: targetPkg?.IncludedRuntimeVersion ?? inclRt,
                PackageUrl: targetPkg?.Url,
                PackageSha512: targetPkg?.Sha512,
                PackageSize: GetEstimatedPackageSize("sdk", major),
                Cves: targetPkg?.Cves ?? [],
                IncludedRuntime: $"{inclRt} + ASP.NET Core",
                ReleasedDate: FormatReleaseDate(catalogEntry?.ReleaseDate ?? targetPkg?.ReleaseDate) ?? "Sep 9, 2025",
                EquivalentCommand: cmd);
        });

        var runtimeRows = snapshot.Runtimes
            .GroupBy(runtime => runtime.Version)
            .Select(group =>
            {
                var runtimes = group.ToArray();
                var sample = runtimes[0];
                var major = sample.Version.Split('.')[0];
                var isLts = major is "10" or "8" or "6";
                var isEol = InstallationSnapshot.IsEol(sample.Version);
                var isPreview = sample.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) || sample.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase);
                var shortVer = ShortDisplayVersion(sample.Version);

                var catalogEntry = downloads.FirstOrDefault(d => d.Product == ".NET Runtime" && (d.Version == sample.Version || d.InstallationVersion == sample.Version || d.Version == shortVer));

                var channelDownloads = downloads
                    .Where(d => d.Product == ".NET Runtime" && (d.Channel == major || d.Channel == $"{major}.0") && (isPreview || (!d.Version.Contains("-preview") && !d.Version.Contains("-rc"))))
                    .OrderByDescending(d => d.Version, SdkVersionComparer.Instance)
                    .ToArray();
                var latest = channelDownloads.FirstOrDefault();
                var hasNewerPatch = !isEol && latest != null && latest.Version != sample.Version && latest.InstallationVersion != sample.Version && SdkVersionComparer.Instance.Compare(latest.Version, shortVer) > 0;
                var targetPkg = (hasNewerPatch ? latest : null) ?? catalogEntry ?? latest;

                var cmd = $"{uninstallPrefix} remove {sample.Version} --runtime";
                var description = string.Join(" · ", runtimes.Select(runtime => runtime.Name));

                return new Row(
                    Kind: "runtime",
                    Name: runtimes.Length == 1 ? RuntimeName(sample.Name) : ".NET Runtime",
                    Description: description,
                    Version: sample.Version,
                    DisplayVersion: shortVer,
                    Paths: runtimes.Select(runtime => runtime.InstallationPath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray(),
                    CanRemove: runtimes.Any(runtime => runtime.CanRemove),
                    Channel: major,
                    IsLts: isLts,
                    IsEol: isEol,
                    IsPreview: isPreview,
                    LatestPatchVersion: hasNewerPatch ? latest!.Version : null,
                    PackageVersion: targetPkg?.Version ?? shortVer,
                    PackageDate: FormatReleaseDate(targetPkg?.ReleaseDate) ?? FormatReleaseDate(catalogEntry?.ReleaseDate) ?? "Sep 9, 2025",
                    PackageRuntime: targetPkg?.Version ?? shortVer,
                    PackageUrl: targetPkg?.Url,
                    PackageSha512: targetPkg?.Sha512,
                    PackageSize: GetEstimatedPackageSize("runtime", major),
                    Cves: targetPkg?.Cves ?? [],
                    IncludedRuntime: ShortFrameworkSubtitle(description),
                    ReleasedDate: FormatReleaseDate(catalogEntry?.ReleaseDate ?? targetPkg?.ReleaseDate) ?? "Sep 9, 2025",
                    EquivalentCommand: cmd);
            });

        return [.. sdkRows, .. runtimeRows];
    }

    internal static string ShortDisplayVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        var dashIdx = version.IndexOf('-');
        if (dashIdx < 0) return version;
        var core = version[..dashIdx];
        var pre = version[(dashIdx + 1)..];
        var parts = pre.Split('.');
        if (parts.Length > 2 && (parts[0].Equals("preview", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("rc", StringComparison.OrdinalIgnoreCase)))
            return $"{core}-{parts[0]}.{parts[1]}";
        return version;
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrEmpty(sha)
            ? ""
            : sha.Length > 12
                ? $"{sha[..4]}…{sha[^4..]}"
                : sha;

    private static string GetEstimatedPackageSize(string kind, string channel) =>
        kind == "sdk"
            ? channel switch
            {
                "11" or "10" => "214 MB",
                "9" => "211 MB",
                "8" => "206 MB",
                _ => "198 MB"
            }
            : channel switch
            {
                "11" or "10" => "45 MB",
                _ => "44 MB"
            };

    private static Row[] FilterRows(Row[] allRows, InstallationViewOptions options)
    {
        var filtered = allRows.Where(row => MatchesKind(row, options.Kind) && MatchesQuery(row, options.Query));
        return (options.Sort switch
        {
            "name" => options.Ascending ? filtered.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Version, VersionComparer) : filtered.OrderByDescending(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(row => row.Version, VersionComparer),
            "path" => options.Ascending ? filtered.OrderBy(row => row.Path, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Version, VersionComparer) : filtered.OrderByDescending(row => row.Path, StringComparer.OrdinalIgnoreCase).ThenByDescending(row => row.Version, VersionComparer),
            _ => options.Ascending ? filtered.OrderBy(row => row.Version, VersionComparer) : filtered.OrderByDescending(row => row.Version, VersionComparer)
        }).ToArray();
    }

    private static bool MatchesKind(Row row, string kind) => kind switch
    {
        "sdks" => row.Kind == "sdk",
        "runtimes" => row.Kind == "runtime",
        "previews" => row.IsPreview,
        _ => true
    };

    private static bool MatchesQuery(Row row, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var searchable = $"{row.Name} {row.Description} {row.Version} {row.DisplayVersion} {row.Path} {row.Channel}";
        return terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string ChannelTagLabel(string channel) => channel is "10" or "8" or "6" ? "LTS" : "STS";

    private static string ShortFrameworkSubtitle(string description) =>
        description
            .Replace("Microsoft.NETCore.App", ".NET", StringComparison.Ordinal)
            .Replace("Microsoft.AspNetCore.App", "ASP.NET Core", StringComparison.Ordinal)
            .Replace("Microsoft.WindowsDesktop.App", "Desktop", StringComparison.Ordinal);

    private static string GetInstallerFileName(string? url, string kind, string version) =>
        $"dotnet-{(kind == "sdk" ? "sdk" : "runtime")}-{ShortDisplayVersion(version)}-osx-arm64.pkg";

    private static string? FormatReleaseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        return dateStr;
    }

    private static string RemoveAction(Row row, InstallationViewOptions options) => !row.CanRemove
        ? ""
        : $"<form method=\"post\" action=\"/installation/remove\"><input type=\"hidden\" name=\"version\" value=\"{Encode(row.Version)}\"><input type=\"hidden\" name=\"kind\" value=\"{row.Kind}\"><input type=\"hidden\" name=\"viewKind\" value=\"{options.Kind}\"><input type=\"hidden\" name=\"q\" value=\"{Encode(options.Query)}\"><input type=\"hidden\" name=\"sort\" value=\"{options.Sort}\"><input type=\"hidden\" name=\"direction\" value=\"{(options.Ascending ? "asc" : "desc")}\"><button class=\"button danger\" type=\"submit\">Remove</button></form>";

    private static string RenderNotice(string notice) => notice switch
    {
        "removed" => "<div class=\"action-notice\" role=\"status\">The selected .NET installation was removed.</div>",
        "remove-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The selected .NET installation could not be removed. Check the permissions and try again.</div>",
        _ => ""
    };

    private static string TypeLink(string label, string kind, InstallationViewOptions options, bool showCheck = false)
    {
        var active = options.Kind == kind || (options.Kind == "all" && kind is "sdks" or "runtimes");
        var checkHtml = (showCheck && active) ? "<span class=\"pill-chk\" aria-hidden=\"true\">✓ </span>" : "";
        return $"<a href=\"{Link(options with { Kind = options.Kind == kind ? "all" : kind })}\" class=\"filter-pill {(active ? "active" : "")}\"{(options.Kind == kind ? " aria-current=\"page\"" : "")}>{checkHtml}{label}</a>";
    }

    private static string SortHeader(string label, string column, InstallationViewOptions options)
    {
        var active = options.Sort == column;
        var direction = active ? options.Ascending ? "ascending" : "descending" : "none";
        return $"<th scope=\"col\" aria-sort=\"{direction}\"><a href=\"{Link(options with { Sort = column, Ascending = !active || !options.Ascending })}\">{label}{(active ? options.Ascending ? " ↑" : " ↓" : "")}</a></th>";
    }

    private static string RefreshLink(InstallationViewOptions options, DateTimeOffset checkedAt) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}&refresh={checkedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}#inventory");

    private static string Link(InstallationViewOptions options) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}{(string.IsNullOrEmpty(options.Selected) ? "" : $"&selected={Uri.EscapeDataString(options.Selected)}")}{(options.Tab == "all-releases" ? "&tab=all-releases" : "")}#inventory");

    private static string RuntimeName(string name) => name switch
    {
        "Microsoft.NETCore.App" => ".NET Runtime",
        "Microsoft.AspNetCore.App" => "ASP.NET Core Runtime",
        "Microsoft.WindowsDesktop.App" => "Windows Desktop Runtime",
        _ => name
    };
}

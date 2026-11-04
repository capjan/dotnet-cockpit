using System.Globalization;
using System.Runtime.InteropServices;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using static DotnetCockpit.PageLayout;
using static DotnetCockpit.SdkDownloadsSection;

namespace DotnetCockpit;

internal sealed record InstallationViewOptions(string Query = "", string Kind = "all", string Sort = "version", bool Ascending = false, string Notice = "", string Selected = "", bool OpenInstall = false)
{
    public static InstallationViewOptions From(IQueryCollection query)
    {
        var kind = query["kind"].ToString() is "sdks" or "runtimes" or "previews" ? query["kind"].ToString() : "all";
        var openInstall = query["install"].ToString() is "1" or "true" or "open" || query["tab"].ToString() is "all-releases" or "all";

        return new(
            query["q"].ToString(),
            kind,
            query["sort"].ToString() is "name" or "path" ? query["sort"].ToString() : "version",
            query["direction"] == "asc",
            query["notice"].ToString(),
            query["selected"].ToString(),
            openInstall);
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
        bool LatestPatchInstalled = false,
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
        var inventoryDescription = snapshot.ExecutablePath is null
            ? "Versions reported by dotnet-core-uninstall."
            : "The .NET installation available on your PATH.";

        var selectedRow = !string.IsNullOrWhiteSpace(options.Selected)
            ? allRows.FirstOrDefault(r => r.Version == options.Selected || r.DisplayVersion == options.Selected)
            : null;

        var content = snapshot.Status switch
        {
            "notFound" => """
                <div class="empty-state"><h3>No .NET installation found</h3><p>The dotnet command is not available on PATH.</p><p class="muted">.NET Cockpit runs independently. After installing .NET, restart Cockpit if PATH has changed.</p></div>
                """,
            "error" => $"<div class=\"empty-state error-state\" role=\"alert\"><h3>Installation information is unavailable</h3><p>{Encode(snapshot.Error ?? "The installation could not be read.")}</p><a class=\"button\" href=\"{RefreshLink(options, snapshot.CheckedAt)}\">Try again</a></div>",
            _ when total == 0 => $"<div class=\"empty-state\"><h3>No SDKs or runtimes found</h3><p class=\"muted\">{(snapshot.ExecutablePath is null ? "dotnet-core-uninstall reported no removable SDKs or runtimes." : "The dotnet host reported no installed SDKs or runtimes.")}</p></div>",
            _ => RenderVersionsHub(allRows, rows, options, selectedRow)
        };

        var filterNavHtml = $"""
            <a href="{Link(options with { Kind = "all" })}" class="pill-badge {(options.Kind == "all" ? "active" : "")}" data-version-kind="all"{(options.Kind == "all" ? " aria-current=\"page\"" : "")}>All<span class="sr-only"> versions</span> <strong class="summary-count" aria-hidden="true">{total}</strong></a>
            {TypeLink("SDKs", "sdks", options)}
            {TypeLink("Runtimes", "runtimes", options)}
            {TypeLink("Previews", "previews", options)}
            """;

        var hasSuperseded = allRows
            .Where(r => r.CanRemove)
            .GroupBy(r => (r.Channel, r.Kind))
            .Any(g => g.Count() > 1);
        var hasRemovablePreviews = allRows.Any(r => r.CanRemove && r.IsPreview);
        var hasRemovableEol = allRows.Any(r => r.CanRemove && r.IsEol);
        var canSmartCleanup = hasSuperseded || hasRemovablePreviews || hasRemovableEol;
        var defaultCleanupMode = hasSuperseded
            ? "all-but-latest"
            : hasRemovablePreviews
                ? "all-previews"
                : "eol";

        return PageLayout.Render($$"""
                  <div class="page-heading">
                    <div>
                      <h1>Versions</h1>
                      <p class="muted intro">Installed SDKs and runtimes, organised by release channel.</p>
                    </div>
                    <div class="overview-header-actions">
                      <a class="button sm ghost refresh-btn" href="{{RefreshLink(options, snapshot.CheckedAt)}}">{{Icon("refresh", "xs")}} Refresh</a>
                      {{(canSmartCleanup ? $"<button type=\"button\" class=\"button\" id=\"open-cleanup-btn\" data-cleanup-action=\"{defaultCleanupMode}\">{Icon("sparkle", "xs")} Clean up…</button>" : "")}}
                      <button type="button" class="button primary" id="open-install-dotnet-btn">{{Icon("download", "xs")}} Install .NET</button>
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
                      <nav class="tool-tabs version-scope-pills" aria-label="Installation type">
                        {{filterNavHtml}}
                      </nav>
                      <form method="get" action="/" class="filter-form version-search-compact" role="search" data-versions-filter>
                        <label class="filter-input-compact">
                          {{Icon("search", "xs")}}
                          <input id="installed-version-filter" type="search" name="q" value="{{Encode(options.Query)}}" placeholder="Filter by version or path" aria-label="Filter installed versions">
                        </label>
                        <input type="hidden" name="kind" value="{{options.Kind}}">
                        <input type="hidden" name="sort" value="{{options.Sort}}">
                        <input type="hidden" name="direction" value="{{(options.Ascending ? "asc" : "desc")}}">
                        <noscript><button class="button sm" type="submit">Filter</button></noscript>
                      </form>
                    </div>
                    {{content}}
                    <div class="inventory-footer sr-only">
                      <span><span data-versions-count>{{rows.Length}} of {{total}} installations shown</span></span>
                      <span>Checked <time datetime="{{snapshot.CheckedAt.ToString("O", CultureInfo.InvariantCulture)}}">{{snapshot.CheckedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}}</time></span>
                    </div>
                  </section>
                  {{RenderInstallDotnetSheet(sdkSnapshot ?? new("ready", []), snapshot)}}
                  <p class="scope-note muted">Locations include the version subfolder. When dotnet-core-uninstall is installed, only versions it can remove are shown; runtime frameworks that share a version are grouped together. Separate installations and other architectures may not appear here.</p>
                  """, "/", ".NET Cockpit", snapshot.IssueCount, total);
    }

    private static string RenderVersionsHub(Row[] allRows, Row[] rows, InstallationViewOptions options, Row? selectedRow)
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

            var sdkInChannel = allRows
                .Where(r => r.Channel == channel && r.Kind == "sdk")
                .OrderByDescending(r => r.PackageVersion ?? r.DisplayVersion, SdkVersionComparer.Instance)
                .FirstOrDefault() ?? group.FirstOrDefault(r => r.Kind == "sdk");
            var rtInChannel = allRows
                .Where(r => r.Channel == channel && r.Kind == "runtime")
                .OrderByDescending(r => r.PackageVersion ?? r.DisplayVersion, SdkVersionComparer.Instance)
                .FirstOrDefault() ?? group.FirstOrDefault(r => r.Kind == "runtime");
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
                      {(group.Any(r => r.CanRemove) ? $"<button type=\"button\" class=\"button sm danger\" data-remove-channel=\"{channel}\">{Icon("trash", "xs")} Remove channel</button>" : "")}
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

        var emptyRow = $"""<tr class="empty-row" {(rows.Length == 0 ? "" : "hidden")}><td colspan="{columnCount}"><div class="empty-state"><h3>No matching versions</h3><p class="muted">Try another filter or switch to All versions.</p><a class="button" href="/">Clear filters</a></div></td></tr>""";

        var drawerHtml = RenderDrawer(selectedRow ?? allRows.FirstOrDefault(), selectedRow != null, options, allRows);
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
                  <div class="remove-modal-icon" id="remove-dialog-icon">{{Icon("trash", "sm")}}</div>
                  <div>
                    <h2 id="remove-dialog-title">Remove .NET version from this machine?</h2>
                    <p id="remove-dialog-subtitle" class="muted">1 item selected for removal.</p>
                  </div>
                </div>
                <div id="remove-dialog-modes" class="cleanup-strategy-grid" role="group" aria-label="Cleanup strategy" hidden>
                  <button type="button" class="cleanup-strategy-card active" data-remove-mode="all-but-latest">
                    <div class="cleanup-strategy-top">
                      <strong>Keep only latest</strong>
                      <code>--all-but-latest</code>
                    </div>
                    <span class="muted text-xs">Keeps the newest SDK and runtime in each channel and removes older side-by-side versions.</span>
                  </button>
                  <button type="button" class="cleanup-strategy-card" data-remove-mode="all-previews">
                    <div class="cleanup-strategy-top">
                      <strong>Remove previews</strong>
                      <code>--all-previews</code>
                    </div>
                    <span class="muted text-xs">Removes all preview and RC installations while keeping stable releases untouched.</span>
                  </button>
                  <button type="button" class="cleanup-strategy-card" data-remove-mode="eol">
                    <div class="cleanup-strategy-top">
                      <strong>End-of-life channels</strong>
                      <span class="badge bad sm">EOL</span>
                    </div>
                    <span class="muted text-xs">Removes versions from unsupported channels that no longer receive security fixes.</span>
                  </button>
                  <button type="button" class="cleanup-strategy-card" data-remove-mode="selected" hidden>
                    <div class="cleanup-strategy-top">
                      <strong>All in channel</strong>
                      <span class="badge sm">Channel</span>
                    </div>
                    <span class="muted text-xs">Selects every SDK and runtime installed in this release channel.</span>
                  </button>
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

    private static string RenderDrawer(Row? row, bool isOpen, InstallationViewOptions options, Row[] allRows)
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

        var hasSdkInChannel = allRows.Any(r => r.Channel == row.Channel && r.Kind == "sdk");
        var patchAlertText = row.Kind == "runtime" && hasSdkInChannel
            ? $"A newer update in the .NET {row.Channel} channel includes security and reliability fixes. Tip: Installing the latest .NET {row.Channel} SDK package automatically includes and updates the matching .NET Runtime — you do not need to download both."
            : $"A newer update in the .NET {row.Channel} channel includes security and reliability fixes.";

        var alertHtml = row.IsEol
            ? $"""
              <div class="drawer-alert drawer-alert-bad">
                <strong>🛡 .NET {row.Channel} has reached End of Life</strong>
                <p class="muted">After May 14, 2024 there are no more security fixes. We recommend upgrading to a supported LTS release.</p>
              </div>
              """
            : row.HasPatch && row.LatestPatchInstalled
                ? $"""
                  <div class="drawer-alert drawer-alert-warn">
                    <strong>⏱ Newer release {Encode(row.LatestPatchVersion!)} is already installed</strong>
                    <p class="muted">You already have {Encode(row.LatestPatchVersion!)} installed side-by-side in the .NET {row.Channel} channel. You can remove {Encode(row.DisplayVersion)} if no global.json pins it.</p>
                  </div>
                  """
                : row.HasPatch
                    ? $"""
                      <div class="drawer-alert drawer-alert-warn">
                        <strong>⏱ Newer release {Encode(row.LatestPatchVersion!)} available</strong>
                        <p class="muted">{Encode(patchAlertText)}</p>
                      </div>
                      """
                    : row.Channel is "9" or "8"
                        ? $"""
                          <div class="drawer-alert drawer-alert-warn">
                            <strong>⏱ .NET {row.Channel} support ends Nov 10, 2026</strong>
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
            ? $"dotnet-core-uninstall remove {row.Version} \\\n  --{(row.Kind == "sdk" ? "sdk" : "runtime")} \\\n  --yes"
            : $"sudo dotnet-core-uninstall remove {row.Version} \\\n  --{(row.Kind == "sdk" ? "sdk" : "runtime")} \\\n  --yes");

        var needsPatchDownload = row.HasPatch && !row.LatestPatchInstalled;
        var downloadBtnLabel = needsPatchDownload
            ? "Download & open installer"
            : row.HasPatch && row.LatestPatchInstalled
                ? "Download .pkg installer"
                : "Re-download .pkg installer";
        var downloadBtnClass = needsPatchDownload ? "button primary drawer-primary-btn" : "button drawer-primary-btn";
        var downloadBtn = !string.IsNullOrEmpty(row.PackageUrl)
            ? $"<a id=\"drawer-download-btn\" class=\"{downloadBtnClass}\" href=\"{Encode(row.PackageUrl)}\" download>{Icon("download", "sm")} <span id=\"drawer-download-label\">{downloadBtnLabel}</span></a>"
            : $"<a id=\"drawer-download-btn\" class=\"{downloadBtnClass}\" href=\"/?install=1\" hidden>{Icon("download", "sm")} <span id=\"drawer-download-label\">{downloadBtnLabel}</span></a>";

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

    private static string RenderInstallDotnetSheet(SdkDownloadSnapshot sdkSnapshot, InstallationSnapshot installation)
    {
        var installedSdks = installation.Sdks
            .SelectMany(s => new[] { s.Version, ShortDisplayVersion(s.Version) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedRuntimes = installation.Runtimes
            .SelectMany(r => new[] { r.Version, ShortDisplayVersion(r.Version) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var downloads = sdkSnapshot.Downloads ?? [];
        var channelLeaders = downloads
            .GroupBy(d => (d.Product, d.Channel))
            .Select(g => g.OrderByDescending(d => d.Version, SdkVersionComparer.Instance).First())
            .ToHashSet();
        var latestStableLeaders = downloads
            .Where(d => !d.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) && !d.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => (d.Product, d.Channel))
            .Select(g => g.OrderByDescending(d => d.Version, SdkVersionComparer.Instance).First())
            .ToHashSet();

        var curated = downloads
            .GroupBy(d => (d.Product, d.Channel))
            .SelectMany(g => g
                .OrderByDescending(d => d.Version, SdkVersionComparer.Instance)
                .Take(8))
            .OrderByDescending(d => int.TryParse(d.Channel.Split('.')[0], out var ch) ? ch : 0)
            .ThenByDescending(d => d.Version, SdkVersionComparer.Instance)
            .ToArray();

        var firstDefault = curated.FirstOrDefault(d =>
            d.Product == "SDK" &&
            !string.Equals(d.SupportPhase, "EOL", StringComparison.OrdinalIgnoreCase) &&
            !InstallationSnapshot.IsEol(d.Version) &&
            !d.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) &&
            !d.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase))
            ?? curated.FirstOrDefault();

        var firstFileName = firstDefault != null
            ? GetInstallerFileName(firstDefault.Url, firstDefault.Product == "SDK" ? "sdk" : "runtime", firstDefault.Version)
            : "dotnet-sdk-10.0.403-osx-arm64.pkg";
        var firstSha = firstDefault?.Sha512 ?? "";
        var initialCmd = FormatVerifyInstallCommand(firstSha, firstFileName);

        string resultsHtml;
        if (sdkSnapshot.Status != "ready" || curated.Length == 0)
        {
            resultsHtml = $"""
                <div class="empty-state error-state">
                  <h3>Release catalog unavailable</h3>
                  <p class="muted">{Encode(sdkSnapshot.Error ?? "Could not load official Microsoft release metadata right now.")}</p>
                </div>
                """;
        }
        else
        {
            var channelGroups = curated
                .GroupBy(d => d.Channel.Split('.')[0])
                .OrderByDescending(g => int.TryParse(g.Key, out var ch) ? ch : 0)
                .ToArray();

            var groupsHtml = string.Concat(channelGroups.Select(channelGroup =>
            {
                var major = channelGroup.Key;
                var orderedItems = channelGroup
                    .OrderByDescending(d => d.Version, SdkVersionComparer.Instance)
                    .ToArray();
                var sample = orderedItems[0];
                var chIsPreviewOnly = orderedItems.All(d =>
                    d.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) ||
                    d.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase));
                var chIsEol = orderedItems.All(d =>
                    string.Equals(d.SupportPhase, "EOL", StringComparison.OrdinalIgnoreCase) ||
                    InstallationSnapshot.IsEol(d.Version));
                var chIsLts = orderedItems.Any(d => string.Equals(d.ReleaseType, "LTS", StringComparison.OrdinalIgnoreCase));
                var chTagClass = chIsPreviewOnly ? "preview" : (chIsLts ? "lts" : "sts");
                var chTagLabel = chIsPreviewOnly ? "Preview" : (chIsLts ? "LTS" : "STS");
                var chPhaseBadge = chIsEol
                    ? "<span class=\"badge bad sm\">EOL</span>"
                    : chIsPreviewOnly
                        ? "<span class=\"badge warn sm\">Preview</span>"
                        : "<span class=\"badge ok sm\">Supported</span>";

                var initialVisibleCount = 0;
                var rowsHtml = string.Concat(orderedItems.Select(d =>
                {
                    var isPreview = d.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) || d.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase);
                    var isEol = string.Equals(d.SupportPhase, "EOL", StringComparison.OrdinalIgnoreCase) || InstallationSnapshot.IsEol(d.Version);
                    var isLts = string.Equals(d.ReleaseType, "LTS", StringComparison.OrdinalIgnoreCase);
                    var isInstalled = d.Product == "SDK"
                        ? installedSdks.Contains(d.Version) || installedSdks.Contains(d.InstallationVersion)
                        : installedRuntimes.Contains(d.Version) || installedRuntimes.Contains(d.InstallationVersion);
                    var isActive = ReferenceEquals(d, firstDefault);
                    var isLeader = channelLeaders.Contains(d) || latestStableLeaders.Contains(d);
                    var initiallyHidden = d.Product != "SDK" || isEol || isPreview || !isLeader;
                    if (!initiallyHidden) initialVisibleCount++;

                    var tagClass = isPreview ? "preview" : (isLts ? "lts" : "sts");
                    var tagLabel = isPreview ? "Preview" : (isLts ? "LTS" : "STS");
                    var statusBadge = isInstalled
                        ? "<span class=\"badge ok sm\">✓ Installed</span>"
                        : isEol
                            ? "<span class=\"badge bad sm\">EOL</span>"
                            : isActive
                                ? "<span class=\"badge brand sm\">Recommended</span>"
                                : isLeader
                                    ? "<span class=\"badge ok sm\">Latest</span>"
                                    : "";

                    var relDate = FormatReleaseDate(d.ReleaseDate) ?? d.ReleaseDate;
                    var inclRt = ShortDisplayVersion(d.IncludedRuntimeVersion ?? $"{major}.0");
                    var subtitle = d.Product == "SDK"
                        ? $"Includes .NET Runtime {inclRt} · Released {relDate}"
                        : $"Shared framework · Released {relDate}";

                    var kindKey = d.Product == "SDK" ? "sdk" : "runtime";
                    var fileName = GetInstallerFileName(d.Url, kindKey, d.Version);
                    var size = GetEstimatedPackageSize(kindKey, major);
                    var shortSha = ShortSha(d.Sha512);
                    var verifyCmd = FormatVerifyInstallCommand(d.Sha512, fileName);

                    var cveMeta = d.Cves.Length > 0
                        ? $"<span>🛡 {d.Cves.Length} {(d.Cves.Length == 1 ? "CVE fix" : "CVE fixes")}</span>"
                        : "<span>✓ 0 open CVEs</span>";

                    return $"""
                        <div class="sheet-result-item {(isActive ? "active" : "")}"
                             data-install-row
                             data-product="{Encode(d.Product)}"
                             data-version="{Encode(d.Version)}"
                             data-channel="{Encode(major)}"
                             data-lts="{(isLts ? "true" : "false")}"
                             data-preview="{(isPreview ? "true" : "false")}"
                             data-supported="{(!isEol ? "true" : "false")}"
                             data-latest="{(isLeader ? "true" : "false")}"
                             data-cmd="{Encode(verifyCmd)}"
                             data-search="{Encode($"{d.Product} {d.Version} .NET {major} {d.Channel} {tagLabel} {inclRt} {relDate} {string.Join(' ', d.Cves.Select(c => c.Id))}")}"
                             {(initiallyHidden ? "hidden" : "")}>
                          <span class="channel-avatar channel-avatar-{Encode(major)}">{Encode(major)}</span>
                          <div class="sheet-result-body">
                            <div class="sheet-result-top">
                              <strong>.NET {Encode(d.Product == "SDK" ? "SDK" : "Runtime")} {Encode(d.Version)}</strong>
                              <span class="channel-tag {tagClass}">{tagLabel}</span>
                              {statusBadge}
                            </div>
                            <span class="muted text-xs">{Encode(subtitle)}</span>
                            <div class="sheet-result-meta muted text-xs">
                              <span>📦 {size}</span>
                              {(string.IsNullOrEmpty(shortSha) ? "" : $"<span>SHA-512 <code>{Encode(shortSha)}</code></span>")}
                              {cveMeta}
                            </div>
                          </div>
                          <div class="sheet-result-actions">
                            {(string.IsNullOrEmpty(d.Sha512) ? "" : $"<button type=\"button\" class=\"button icon sm ghost copy-btn\" data-copy=\"{Encode(d.Sha512)}\" title=\"Copy SHA-512 checksum\">{Icon("clipboard", "xs")}</button>")}
                            <a class="button {(isActive && !isInstalled ? "primary" : "")} sm" href="{Encode(d.Url)}" download>{Icon("download", "xs")} {(isInstalled ? "Re-download" : "Download .pkg")}</a>
                          </div>
                        </div>
                        """;
                }));

                var countLabel = initialVisibleCount == 1 ? "1 release" : $"{initialVisibleCount} releases";
                return $"""
                    <section class="sheet-channel-group" data-install-channel-group="{Encode(major)}" {(initialVisibleCount == 0 ? "hidden" : "")}>
                      <div class="sheet-channel-head">
                        <span class="channel-avatar sm channel-avatar-{Encode(major)}">{Encode(major)}</span>
                        <div class="sheet-channel-head-main">
                          <strong>.NET {Encode(major)}</strong>
                          <span class="channel-tag {chTagClass}">{chTagLabel}</span>
                          {chPhaseBadge}
                        </div>
                        <span class="sheet-channel-count muted text-xs" data-install-channel-count>{countLabel}</span>
                      </div>
                      <div class="sheet-channel-items">
                        {rowsHtml}
                      </div>
                    </section>
                    """;
            }));

            resultsHtml = $"""
                {groupsHtml}
                <div id="install-dotnet-empty" class="empty-state" hidden>
                  <h3>No matching releases</h3>
                  <p class="muted">Try another version, channel, or toggle Previews / All releases above.</p>
                </div>
                """;
        }

        return $$"""
            <dialog id="install-dotnet-dialog" class="add-tool-sheet install-dotnet-sheet" closedby="any" aria-labelledby="install-dotnet-sheet-title">
              <div class="sheet-inner">
                <div class="sheet-head">
                  <div class="sheet-title-group">
                    <h2 id="install-dotnet-sheet-title">Install .NET SDK or runtime</h2>
                    <span class="search-source">builds.dotnet.microsoft.com</span>
                  </div>
                  <button type="button" class="button icon ghost" id="close-install-dotnet-btn" aria-label="Close install .NET panel">✕</button>
                </div>

                <div class="sheet-controls">
                  <label class="sheet-search-box" for="install-dotnet-search-input">
                    {{Icon("search", "sm")}}
                    <input id="install-dotnet-search-input" type="search" placeholder="Search by version, channel or CVE (e.g. 10.0, 9.0.320, LTS)..." autocomplete="off">
                    <kbd>↵</kbd>
                  </label>

                  <div class="sheet-scope-bar">
                    <div class="sheet-filter-group">
                      <span class="muted text-xs">Product</span>
                      <div class="version-scope-pills" role="group" aria-label="Product type">
                        <button type="button" class="pill-badge active" data-install-product="SDK">{{Icon("versions", "xs")}} .NET SDK</button>
                        <button type="button" class="pill-badge" data-install-product=".NET Runtime">{{Icon("cpu", "xs")}} .NET Runtime</button>
                      </div>
                    </div>
                    <div class="sheet-filter-group">
                      <span class="muted text-xs">Filter</span>
                      <div class="version-scope-pills" role="group" aria-label="Release filter">
                        <button type="button" class="pill-badge active" data-install-lifecycle="latest" aria-pressed="true">Latest</button>
                        <button type="button" class="pill-badge active" data-install-lifecycle="supported" aria-pressed="true">Supported</button>
                        <button type="button" class="pill-badge" data-install-lifecycle="lts" aria-pressed="false">LTS</button>
                        <button type="button" class="pill-badge" data-install-lifecycle="previews" aria-pressed="false">+ Previews</button>
                        <button type="button" class="pill-badge" data-install-lifecycle="all" aria-pressed="false">All</button>
                      </div>
                    </div>
                  </div>
                </div>

                <div id="install-dotnet-results" class="sheet-results">
                  {{resultsHtml}}
                </div>

                <div class="sheet-foot">
                  <span class="drawer-section-title">VERIFY CHECKSUM &amp; INSTALL (.PKG)</span>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="install-dotnet-cli-preview">{{Encode(initialCmd)}}</code>
                    <button class="button icon ghost copy-btn" id="install-dotnet-cli-copy" data-copy="{{Encode(initialCmd)}}" type="button" title="Copy command">{{Icon("clipboard")}}</button>
                  </div>
                </div>
              </div>
            </dialog>
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
            $"\"patchInstalled\": {(row.LatestPatchInstalled ? "true" : "false")}," +
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
        var installedSdkSet = snapshot.Sdks
            .SelectMany(s => new[] { s.Version, ShortDisplayVersion(s.Version) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedRuntimeSet = snapshot.Runtimes
            .SelectMany(r => new[] { r.Version, ShortDisplayVersion(r.Version) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            var bandPrefix = GetSdkFeatureBandPrefix(shortVer);
            var latestInBand = !string.IsNullOrEmpty(bandPrefix)
                ? channelDownloads.FirstOrDefault(d => GetSdkFeatureBandPrefix(d.Version) == bandPrefix)
                : null;

            // An SDK in an active feature band (e.g. 10.0.114 in 10.0.1xx) is up-to-date if it is the newest in its band
            // and includes the same latest runtime as the channel's newest SDK.
            var isCurrentFeatureBandLatest = latestInBand != null &&
                (latestInBand.Version == sdk.Version || latestInBand.Version == shortVer) &&
                !string.IsNullOrEmpty(catalogEntry?.IncludedRuntimeVersion) &&
                catalogEntry.IncludedRuntimeVersion == latest?.IncludedRuntimeVersion;

            var patchTarget = (latestInBand != null && latestInBand.IncludedRuntimeVersion == latest?.IncludedRuntimeVersion)
                ? latestInBand
                : latest;

            var hasNewerPatch = !isEol && !isCurrentFeatureBandLatest && patchTarget != null &&
                patchTarget.Version != sdk.Version && patchTarget.InstallationVersion != sdk.Version &&
                SdkVersionComparer.Instance.Compare(patchTarget.Version, shortVer) > 0;

            var patchAlreadyInstalled = hasNewerPatch && patchTarget != null &&
                (installedSdkSet.Contains(patchTarget.Version) || installedSdkSet.Contains(patchTarget.InstallationVersion));

            var targetPkg = (hasNewerPatch ? patchTarget : null) ?? catalogEntry ?? latest;
            var inclRt = ShortDisplayVersion(catalogEntry?.IncludedRuntimeVersion ?? InferRuntimeVersion(shortVer, snapshot.Runtimes, major));

            var cmd = $"{uninstallPrefix} remove {sdk.Version} \\\n  --sdk \\\n  --yes";

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
                LatestPatchVersion: hasNewerPatch ? patchTarget!.Version : null,
                LatestPatchInstalled: patchAlreadyInstalled,
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
                var patchAlreadyInstalled = hasNewerPatch && latest != null &&
                    (installedRuntimeSet.Contains(latest.Version) || installedRuntimeSet.Contains(latest.InstallationVersion));
                var targetPkg = (hasNewerPatch ? latest : null) ?? catalogEntry ?? latest;

                var cmd = $"{uninstallPrefix} remove {sample.Version} \\\n  --runtime \\\n  --yes";
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
                    LatestPatchInstalled: patchAlreadyInstalled,
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

    internal static string GetSdkFeatureBandPrefix(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Contains('-')) return "";
        var parts = version.Split('.');
        if (parts.Length < 3 || parts[2].Length < 3) return "";
        return $"{parts[0]}.{parts[1]}.{parts[2][0]}";
    }

    private static string InferRuntimeVersion(string sdkShortVer, RuntimeInstallation[] runtimes, string major)
    {
        if (!sdkShortVer.Contains('-'))
        {
            var parts = sdkShortVer.Split('.');
            if (parts.Length >= 3 && parts[2].Length == 3 && int.TryParse(parts[2][1..], out var patchNum))
            {
                var candidate = $"{parts[0]}.{parts[1]}.{patchNum}";
                if (runtimes.Any(r => r.Version == candidate) || parts[2][0] == '1')
                    return candidate;
            }
        }
        return runtimes
            .Where(r => r.Version.StartsWith($"{major}.", StringComparison.Ordinal))
            .Select(r => ShortDisplayVersion(r.Version))
            .OrderByDescending(v => v, SdkVersionComparer.Instance)
            .FirstOrDefault() ?? $"{major}.0";
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

    private static string FormatVerifyInstallCommand(string? sha512, string fileName) =>
        !string.IsNullOrEmpty(sha512)
            ? $"echo \"{sha512}  {fileName}\" \\\n  | shasum -a 512 -c - \\\n  && sudo installer -pkg {fileName} \\\n    -target /"
            : $"sudo installer -pkg {fileName} \\\n  -target /";

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

    private static string TypeLink(string label, string kind, InstallationViewOptions options)
    {
        var active = options.Kind == kind;
        var checkHtml = active ? "<span class=\"pill-chk\" aria-hidden=\"true\">✓ </span>" : "";
        return $"<a href=\"{Link(options with { Kind = active ? "all" : kind })}\" class=\"filter-pill {(active ? "active" : "")}\" data-version-kind=\"{kind}\" data-version-label=\"{label}\"{(active ? " aria-current=\"page\"" : "")}>{checkHtml}{label}</a>";
    }

    private static string SortHeader(string label, string column, InstallationViewOptions options)
    {
        var active = options.Sort == column;
        var direction = active ? options.Ascending ? "ascending" : "descending" : "none";
        return $"<th scope=\"col\" aria-sort=\"{direction}\"><a href=\"{Link(options with { Sort = column, Ascending = !active || !options.Ascending })}\">{label}{(active ? options.Ascending ? " ↑" : " ↓" : "")}</a></th>";
    }

    private static string RefreshLink(InstallationViewOptions options, DateTimeOffset checkedAt) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}&refresh={checkedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}");

    private static string Link(InstallationViewOptions options) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}{(string.IsNullOrEmpty(options.Selected) ? "" : $"&selected={Uri.EscapeDataString(options.Selected)}")}");

    private static string RuntimeName(string name) => name switch
    {
        "Microsoft.NETCore.App" => ".NET Runtime",
        "Microsoft.AspNetCore.App" => "ASP.NET Core Runtime",
        "Microsoft.WindowsDesktop.App" => "Windows Desktop Runtime",
        _ => name
    };
}

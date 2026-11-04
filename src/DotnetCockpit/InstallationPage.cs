using System.Globalization;
using System.Net;
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
    internal sealed record Row(
        string Kind,
        string Name,
        string Description,
        string Version,
        string DisplayVersion,
        IReadOnlyList<string> Paths,
        bool CanRemove,
        string Channel,
        SupportStatus Support,
        bool IsPreview,
        string? LatestPatchVersion = null,
        bool LatestPatchInstalled = false,
        string? PackageVersion = null,
        string? PackageDate = null,
        string? PackageRuntime = null,
        string? PackageUrl = null,
        string? PackageSha512 = null,
        IReadOnlyList<SdkCve>? Cves = null,
        string? IncludedRuntime = null,
        string? ReleasedDate = null,
        string? EquivalentCommand = null)
    {
        public string Path => string.Join(" ", Paths);
        public bool IsLts => Support.IsLts;
        public bool IsEol => Support.IsEol;
        public bool HasPatch => !IsEol && !string.IsNullOrEmpty(LatestPatchVersion) && LatestPatchVersion != Version && LatestPatchVersion != DisplayVersion;
        public bool HasPackageInfo => !string.IsNullOrEmpty(PackageUrl) || !string.IsNullOrEmpty(PackageSha512);
    }

    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create((left, right) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));

    public static string Render(InstallationSnapshot snapshot, InstallationViewOptions options) =>
        Render(snapshot, null, options);

    public static string Render(InstallationSnapshot snapshot, SdkDownloadSnapshot? sdkSnapshot, InstallationViewOptions options, DateOnly? today = null)
    {
        var currentDate = today ?? SupportLifecycle.Today;
        var health = EnvironmentHealth.Evaluate(snapshot, sdkSnapshot, currentDate);
        var allRows = BuildRows(snapshot, sdkSnapshot, currentDate);
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

        var sdkCount = allRows.Count(row => row.Kind == "sdk");
        var isFiltered = options.Kind != "all" || !string.IsNullOrWhiteSpace(options.Query);
        var filterNavHtml = $"""
            <div class="segmented">
              {KindSegment("All", "all", total, options, " versions")}
              {KindSegment("SDKs", "sdks", sdkCount, options)}
              {KindSegment("Runtimes", "runtimes", runtimeCount, options)}
            </div>
            <a href="{Link(options with { Kind = options.Kind == "previews" ? "all" : "previews" })}" class="toggle-chip {(options.Kind == "previews" ? "active" : "")}" data-version-kind="previews"{(options.Kind == "previews" ? " aria-current=\"page\"" : "")}><span class="chip-box" aria-hidden="true">{Icon("check", "xs")}</span>Previews</a>
            """;

        var hasSuperseded = allRows
            .Where(r => r.CanRemove)
            .GroupBy(r => (r.Channel, r.Kind))
            .Any(g => g.Count() > 1);
        var hasRemovablePreviews = allRows.Any(r => r.CanRemove && r.IsPreview);
        var hasRemovableEol = allRows.Any(r => r.CanRemove && r.IsEol);
        var canSmartCleanup = hasSuperseded || hasRemovablePreviews || hasRemovableEol;
        var defaultCleanupMode = hasSuperseded
            ? "superseded"
            : hasRemovablePreviews
                ? "previews"
                : "eol";

        return PageLayout.Render($$"""
                  <div class="page-heading">
                    <div>
                      <h1>Versions</h1>
                      <p class="muted intro">Installed SDKs and runtimes, organised by release channel.</p>
                    </div>
                    <div class="overview-header-actions">
                      {{(canSmartCleanup ? $"<button type=\"button\" class=\"button\" id=\"open-cleanup-btn\" hidden data-js-only data-cleanup-action=\"{defaultCleanupMode}\">{Icon("sparkle", "xs")} Clean up…</button>" : "")}}
                      <a class="button primary" id="open-install-dotnet-btn" href="/?install=1">{{Icon("download", "xs")}} Install .NET</a>
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
                    <div class="filter-bar">
                      <nav class="filter-group" aria-label="Installation type">
                        {{filterNavHtml}}
                      </nav>
                      <span class="filter-count" data-versions-count {{(isFiltered ? "" : "hidden")}}>{{rows.Length}} of {{total}} installations shown</span>
                      <a class="filter-reset" href="/" data-filter-reset {{(isFiltered ? "" : "hidden")}}>Reset</a>
                      <span class="spacer"></span>
                      {{SortMenu(options)}}
                      <form method="get" action="/" class="filter-form" role="search" data-versions-filter>
                        <label class="search-field">
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
                  </section>
                  {{RenderInstallDotnetSheet(sdkSnapshot ?? new("ready", []), snapshot, options.OpenInstall, currentDate)}}
                  <p class="scope-note muted">Locations include the version subfolder. When dotnet-core-uninstall is installed, only versions it can remove are shown; runtime frameworks that share a version are grouped together. Separate installations and other architectures may not appear here.</p>
                  """, "/", ".NET Cockpit", health.Count, total, refreshHref: WebUtility.HtmlDecode(RefreshLink(options, snapshot.CheckedAt)));
    }

    private static string RenderVersionsHub(Row[] allRows, Row[] rows, InstallationViewOptions options, Row? selectedRow)
    {
        var showActions = allRows.Any(row => row.CanRemove);

        // Preserve the sorted order of `rows` from FilterRows when grouping by channel
        var channelGroups = rows
            .GroupBy(r => r.Channel)
            .ToArray();

        var tableBody = string.Concat(channelGroups.Select(group =>
        {
            var channel = group.Key;
            var support = group.First().Support;
            var isEol = support.IsEol;
            var isPreviewChannel = support.State == SupportState.Preview || group.All(r => r.IsPreview);
            var lifeTextClass = isEol ? "bad" : support.IsEndingSoon ? "warn" : "";
            var tagClass = isPreviewChannel ? "preview" : (support.IsLts ? "lts" : "sts");
            var tagLabel = isPreviewChannel ? "Preview" : support.ReleaseType;

            var removeAll = group.Any(r => r.CanRemove)
                ? $"<a class=\"button sm {(isEol ? "danger" : "remove")}\" href=\"/confirm?action=channel-remove&amp;channel={channel}\" data-remove-channel=\"{channel}\" aria-label=\"Remove all of .NET {channel}\">Remove all</a>"
                : "";

            var headerRow = $"""
                <tr class="channel-header-row" role="row" data-channel-header="{channel}">
                  <td role="cell">
                    <div class="channel-header-card">
                      <span class="chevron-slot"><button type="button" class="button icon sm ghost channel-collapse-btn" data-collapse-channel="{channel}" aria-label="Toggle .NET {channel} channel" aria-expanded="true" hidden data-js-only>{Icon("chevd", "sm")}</button></span>
                      <div class="channel-title-row">
                        <strong class="channel-title">.NET {channel}</strong>
                        {(tagLabel.Length > 0 ? $"<span class=\"channel-tag {tagClass}\">{tagLabel}</span>" : "")}
                      </div>
                      <span class="life-status {lifeTextClass}">{Encode(support.Label)}</span>
                      <span class="channel-actions">{removeAll}</span>
                    </div>
                  </td>
                </tr>
                """;

            var rowsHtml = string.Concat(group.Select(row =>
            {
                var isSelected = selectedRow?.Version == row.Version;
                var fullVersion = $"<span class=\"sr-only\" data-full-version>{Encode(row.Version)}</span>";
                var versionCell = row.HasPatch
                    ? $"""<div class="version-patch-flow">{fullVersion}<span class="v-current">{Encode(row.DisplayVersion)}</span><span class="v-arrow" aria-hidden="true">→</span><span class="sr-only">, patch available: {Encode(row.LatestPatchVersion!)}</span><span class="v-patch" aria-hidden="true">{Encode(ShortPatchTarget(row.DisplayVersion, row.LatestPatchVersion!))}</span></div>"""
                    : row.IsEol || row.Support.State == SupportState.Unknown
                        ? $"""<div class="version-patch-flow">{fullVersion}<span class="v-current">{Encode(row.DisplayVersion)}</span></div>"""
                        : $"""<div class="version-patch-flow">{fullVersion}<span class="v-current">{Encode(row.DisplayVersion)}</span><span class="v-ok" title="Latest patch">{Icon("check", "xs")}<span class="sr-only">Latest patch</span></span></div>""";

                var primaryPath = row.Paths.FirstOrDefault() ?? "";
                var extraPaths = row.Paths.Count > 1 ? $"<span class=\"sr-only\"> {Encode(string.Join(" ", row.Paths.Skip(1)))}</span>" : "";
                return $"""
                    <tr data-kind="{row.Kind}" role="row" data-drawer-key="{Encode(row.Version)}" data-preview="{(row.IsPreview ? "true" : "false")}"{(isSelected ? " class=\"row-selected\"" : "")}>
                      <td role="cell" class="cell-name"><strong>{Encode(row.Name)}</strong><span class="muted framework-name">{Encode(ShortFrameworkSubtitle(row.Description))}<span class="sr-only"> {Encode(row.Description)}</span></span></td>
                      <td role="cell" class="cell-path"><code class="installation-path" title="{Encode(primaryPath)}"><bdi>{Encode(primaryPath)}</bdi>{extraPaths}</code></td>
                      <td role="cell" class="cell-version">{versionCell}</td>
                      <td role="cell" class="cell-actions">
                        <a href="{Link(options with { Selected = row.Version })}" class="button sm ghost inspect-btn" data-inspect="{Encode(row.Version)}" aria-label="Inspect {Encode(row.Version)}">Inspect</a>
                        {RemoveAction(row, options)}
                      </td>
                    </tr>
                    """;
            }));

            var cardState = isEol ? "eol" : support.IsEndingSoon ? "ending" : "ok";
            return $"""
                <tbody class="channel-card" role="rowgroup" data-channel-card="{channel}" data-state="{cardState}">
                  {headerRow}
                  {rowsHtml}
                </tbody>
                """;
        }));

        var emptyRow = $"""<tr class="empty-row" role="row" {(rows.Length == 0 ? "" : "hidden")}><td role="cell"><div class="empty-state"><h3>No matching versions</h3><p class="muted">Try another filter or switch to All versions.</p><a class="button" href="/">Clear filters</a></div></td></tr>""";

        var drawerHtml = RenderDrawer(selectedRow, options, allRows);
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
                  <table class="versions-table" role="table" aria-label="Installed .NET versions">
                    <thead class="sr-only" role="rowgroup"><tr role="row"><th scope="col" role="columnheader">Installation</th><th scope="col" role="columnheader">Location</th><th scope="col" role="columnheader">Version</th><th scope="col" role="columnheader">Actions</th></tr></thead>
                    {{tableBody}}
                    <tbody role="rowgroup">
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
                  <button type="button" class="cleanup-strategy-card" data-remove-mode="superseded">
                    <strong>Older versions</strong>
                    <span class="muted text-xs">Keeps the newest SDK and runtime in each channel.</span>
                  </button>
                  <button type="button" class="cleanup-strategy-card" data-remove-mode="previews">
                    <strong>Previews</strong>
                    <span class="muted text-xs">Removes preview and RC installations. Stable releases stay.</span>
                  </button>
                  <button type="button" class="cleanup-strategy-card" data-remove-mode="eol">
                    <strong>Out of support</strong>
                    <span class="muted text-xs">Removes versions that no longer receive security fixes.</span>
                  </button>
                </div>
                <div id="remove-dialog-items" class="remove-modal-items"></div>
                <div class="remove-modal-notice">
                  <span class="notice-lock">{{Icon("lock", "sm")}}</span>
                  <div>
                    <strong>{{ConfirmActions.AdminNote}}</strong>
                    <p id="remove-dialog-warn" class="muted">Removal runs through Microsoft's dotnet-core-uninstall. {{ConfirmActions.YesFlagNote}}</p>
                  </div>
                </div>
                <div class="drawer-command-box">
                  <span class="cmd-prompt">$</span>
                  <code id="remove-dialog-cmd">sudo dotnet-core-uninstall remove</code>
                  <button class="button icon ghost copy-btn" id="remove-dialog-copy" type="button" title="Copy command" aria-label="Copy command">{{Icon("clipboard")}}</button>
                </div>
                {{multiRemoveForm}}
                <div class="remove-modal-foot">
                  <button type="button" class="button" id="remove-dialog-cancel" autofocus>Cancel</button>
                  <button type="button" class="button danger" id="remove-dialog-confirm">{{Icon("trash", "xs")}} <span id="remove-dialog-confirm-text">Remove</span></button>
                </div>
              </div>
            </dialog>
            <script id="versions-data" type="application/json">
            {{jsonDictionary}}
            </script>
            """;
    }

    /// <summary>One server-rendered detail panel per row. Without JavaScript the selected one is rendered directly; with it, app.js swaps them in from templates.</summary>
    private static string RenderDrawer(Row? selected, InstallationViewOptions options, Row[] allRows)
    {
        var templates = string.Concat(allRows.Select(row =>
            $"<template data-drawer-content=\"{Encode(row.Version)}\">{RenderDrawerContent(row, options, allRows)}</template>"));
        return $"""
            <aside id="version-drawer" class="detail-drawer" {(selected is null ? "hidden" : "")} aria-label="Version details">{(selected is null ? "" : RenderDrawerContent(selected, options, allRows))}</aside>
            {templates}
            """;
    }

    private static string RenderDrawerContent(Row row, InstallationViewOptions options, Row[] allRows)
    {
        var support = row.Support;
        var channelType = row.IsPreview
            ? "Preview release"
            : support.IsLts
                ? "Long Term Support"
                : support.ReleaseType == "STS"
                    ? "Standard Term Support"
                    : "Support type unavailable";

        var badgeHtml = row.IsEol
            ? "<span class=\"badge bad\">End of life</span>"
            : row.HasPatch
                ? "<span class=\"badge warn\">Patch available</span>"
                : "";

        var hasSdkInChannel = allRows.Any(r => r.Channel == row.Channel && r.Kind == "sdk");
        var patchAlertText = row.Kind == "runtime" && hasSdkInChannel
            ? $"A newer update in the .NET {row.Channel} channel includes security and reliability fixes. The matching SDK package updates this runtime too."
            : $"A newer update in the .NET {row.Channel} channel includes security and reliability fixes.";

        var supportedChannel = allRows
            .Where(r => r.Channel != row.Channel && r.Support.State == SupportState.Supported && !r.IsPreview)
            .OrderByDescending(r => r.Version, SdkVersionComparer.Instance)
            .FirstOrDefault();
        var upgradeHint = supportedChannel is null
            ? "We recommend moving to a supported release."
            : $".NET {supportedChannel.Channel} is already installed and supported.";

        var alertHtml = row.IsEol
            ? $"""
              <div class="drawer-alert drawer-alert-bad">
                <strong>.NET {row.Channel} no longer receives security fixes</strong>
                <p class="muted">{Encode(support.Label)}. {Encode(upgradeHint)}</p>
              </div>
              """
            : support.IsEndingSoon
                ? $"""
                  <div class="drawer-alert drawer-alert-warn">
                    <strong>.NET {row.Channel} support ends {SupportLifecycle.RelativeDays(support.DaysLeft ?? 0)}</strong>
                    <p class="muted">No more security fixes after {Encode(support.EndDate)}. {Encode(upgradeHint)}</p>
                  </div>
                  """
                : row.HasPatch && row.LatestPatchInstalled
                    ? $"""
                      <div class="drawer-alert drawer-alert-warn">
                        <strong>Newer release {Encode(row.LatestPatchVersion!)} is already installed</strong>
                        <p class="muted">You can remove {Encode(row.DisplayVersion)} if no global.json pins it.</p>
                      </div>
                      """
                    : row.HasPatch
                        ? $"""
                          <div class="drawer-alert drawer-alert-warn">
                            <strong>Newer release {Encode(row.LatestPatchVersion!)} available</strong>
                            <p class="muted">{Encode(patchAlertText)}</p>
                          </div>
                          """
                        : $"""<p class="drawer-status-line">{Icon("check", "xs")} {Encode(support.State == SupportState.Unknown ? "Patch and support status unavailable" : $"Latest patch installed. {support.Label}.")}</p>""";

        var pkgTitle = row.HasPatch ? "Newest patch" : "Release package & security";
        var pkgVer = row.PackageVersion ?? row.DisplayVersion;
        var pkgDate = row.PackageDate ?? row.ReleasedDate;
        var pkgRuntime = row.PackageRuntime is { Length: > 0 } packageRuntime ? $"Runtime {ShortDisplayVersion(packageRuntime)}" : "";
        var pkgMeta = string.Join(" · ", new[] { pkgDate, pkgRuntime }.Where(value => !string.IsNullOrEmpty(value)));
        var cveBadgeHtml = row.Cves is { Count: > 0 }
            ? $"<span class=\"badge sm\">{row.Cves.Count} security {(row.Cves.Count == 1 ? "fix" : "fixes")}</span>"
            : "";
        var cvePillsHtml = string.Concat((row.Cves ?? []).Take(6).Select(c =>
            $"<a class=\"cve-pill\" href=\"{Encode(c.Url ?? "#")}\" target=\"_blank\" rel=\"noopener\">{Encode(c.Id)}</a>"));

        var patchSection = row.HasPackageInfo
            ? $"""
              <div class="drawer-section">
                <div class="drawer-patch-box">
                  <h4 class="drawer-section-title">{pkgTitle}</h4>
                  <div class="drawer-patch-header">
                    <strong>{Encode(pkgVer)}</strong>
                    <span class="muted text-xs">{Encode(pkgMeta)}</span>
                  </div>
                  {(cveBadgeHtml.Length + cvePillsHtml.Length > 0 ? $"<div class=\"drawer-cve-list\">{cveBadgeHtml}{cvePillsHtml}</div>" : "")}
                  <div class="drawer-code-box">
                    <span class="drawer-pkg-meta">{Icon("package", "xs")} <span class="drawer-pkg-name">{Encode(GetInstallerFileName(row.PackageUrl))}</span> · SHA-512 <code class="drawer-sha">{Encode(ShortSha(row.PackageSha512))}</code></span>
                    <button class="button icon ghost copy-btn" data-copy="{Encode(row.PackageSha512 ?? "")}" type="button" title="Copy SHA-512" aria-label="Copy SHA-512">{Icon("clipboard")}</button>
                  </div>
                </div>
              </div>
              """
            : "";

        var command = ConfirmActions.RemovalCommand(row.Version, row.Kind);
        var needsPatchDownload = row.HasPatch && !row.LatestPatchInstalled;
        var downloadBtn = string.IsNullOrEmpty(row.PackageUrl)
            ? ""
            : needsPatchDownload
                ? $"<a class=\"button primary drawer-primary-btn\" href=\"{Encode(row.PackageUrl)}\" download>{Icon("download", "sm")} Download {Encode(row.LatestPatchVersion!)}</a>"
                : $"<a class=\"button ghost drawer-primary-btn\" href=\"{Encode(row.PackageUrl)}\" download>{Icon("download", "sm")} Download again</a>";

        var removeAction = row.CanRemove
            ? ConfirmActions.Trigger(ConfirmActions.VersionRemove(row, options), $"<button class=\"button remove drawer-remove-btn\" type=\"submit\">{Icon("trash", "xs")} Remove</button>")
            : "";

        var runtimeLabel = row.Kind == "sdk" ? "Included runtime" : "Frameworks";
        var runtimeValue = row.IncludedRuntime ?? ShortFrameworkSubtitle(row.Description);
        var location = row.Paths.FirstOrDefault();

        var drawerTitleHtml = row.Version.StartsWith(row.DisplayVersion, StringComparison.Ordinal) && row.Version.Length > row.DisplayVersion.Length
            ? $"{Encode(row.DisplayVersion)}<span class=\"sr-only\">{Encode(row.Version[row.DisplayVersion.Length..])}</span>"
            : Encode(row.Version);

        return $$"""
              <div class="drawer-header">
                <div class="drawer-eyebrow-row">
                  <span id="drawer-eyebrow">.NET {{row.Channel}} · {{(row.Kind == "sdk" ? "SDK" : "Runtime")}}</span>
                  <a href="{{Link(options with { Selected = "" })}}" id="drawer-close" data-drawer-close class="button icon ghost drawer-close-btn" aria-label="Close details">{{Icon("x", "sm")}}</a>
                </div>
                <div class="drawer-title-row">
                  <h3 id="drawer-title" class="drawer-title">{{drawerTitleHtml}}</h3>
                  {{badgeHtml}}
                </div>
              </div>
              <div class="drawer-body">
                {{alertHtml}}
                <div class="drawer-section">
                  <h4 class="drawer-section-title">Installed</h4>
                  <dl class="drawer-dl">
                    <dt>Channel</dt>
                    <dd>{{row.Channel}}.0 · {{channelType}}</dd>
                    <dt>{{runtimeLabel}}</dt>
                    <dd>{{Encode(runtimeValue)}}</dd>
                    {{(row.ReleasedDate is { Length: > 0 } released ? $"<dt>Released</dt><dd>{Encode(released)}</dd>" : "")}}
                    {{(location is null ? "" : $$"""
                    <dt>Location</dt>
                    <dd>
                      <div class="drawer-code-box">
                        <code class="drawer-path">{{Encode(location)}}</code>
                        <button class="button icon ghost copy-btn" data-copy="{{Encode(location)}}" type="button" title="Copy path" aria-label="Copy path">{{Icon("clipboard")}}</button>
                      </div>
                    </dd>
                    """)}}
                  </dl>
                </div>
                {{patchSection}}
                {{(row.CanRemove ? $$"""
                <div class="drawer-section">
                  <h4 class="drawer-section-title">Command to remove this version</h4>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="drawer-command">{{Encode(command)}}</code>
                    <button class="button icon ghost copy-btn" data-copy="{{Encode(command)}}" type="button" title="Copy command" aria-label="Copy command">{{Icon("clipboard")}}</button>
                  </div>
                </div>
                """ : "")}}
              </div>
              {{(downloadBtn.Length + removeAction.Length > 0 ? $"<div class=\"drawer-footer\">{downloadBtn}{removeAction}</div>" : "")}}
            """;
    }

    private static string RenderInstallDotnetSheet(SdkDownloadSnapshot sdkSnapshot, InstallationSnapshot installation, bool open, DateOnly today)
    {
        static bool IsDownloadEol(SdkDownload download, DateOnly date) =>
            string.Equals(download.SupportPhase, "EOL", StringComparison.OrdinalIgnoreCase) ||
            SupportLifecycle.ParseDate(download.EndOfSupportDate) is { } end && end < date;

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
            !IsDownloadEol(d, today) &&
            !d.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) &&
            !d.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase))
            ?? curated.FirstOrDefault();

        var initialCmd = firstDefault != null
            ? FormatVerifyInstallCommand(firstDefault.Sha512, GetInstallerFileName(firstDefault.Url))
            : "";

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
                var chIsEol = orderedItems.All(d => IsDownloadEol(d, today));
                var chIsLts = orderedItems.Any(d => string.Equals(d.ReleaseType, "LTS", StringComparison.OrdinalIgnoreCase));
                var chTagClass = chIsPreviewOnly ? "preview" : (chIsLts ? "lts" : "sts");
                var chTagLabel = chIsPreviewOnly ? "Preview" : (chIsLts ? "LTS" : "STS");
                var chPhaseBadge = chIsEol ? "<span class=\"badge bad sm\">End of life</span>" : "";

                var initialVisibleCount = 0;
                var rowsHtml = string.Concat(orderedItems.Select(d =>
                {
                    var isPreview = d.Version.Contains("-preview", StringComparison.OrdinalIgnoreCase) || d.Version.Contains("-rc", StringComparison.OrdinalIgnoreCase);
                    var isEol = IsDownloadEol(d, today);
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
                        ? $"<span class=\"v-ok\">{Icon("check", "xs")} Installed</span>"
                        : isEol
                            ? "<span class=\"badge bad sm\">End of life</span>"
                            : "";

                    var relDate = FormatReleaseDate(d.ReleaseDate) ?? d.ReleaseDate;
                    var inclRt = ShortDisplayVersion(d.IncludedRuntimeVersion ?? $"{major}.0");
                    var subtitle = d.Product == "SDK"
                        ? $"Includes .NET Runtime {inclRt} · Released {relDate}"
                        : $"Shared framework · Released {relDate}";

                    var fileName = GetInstallerFileName(d.Url);
                    var shortSha = ShortSha(d.Sha512);
                    var verifyCmd = FormatVerifyInstallCommand(d.Sha512, fileName);

                    var cveMeta = d.Cves.Length > 0
                        ? $"<span>{Icon("shield", "xs")} {d.Cves.Length} {(d.Cves.Length == 1 ? "CVE fix" : "CVE fixes")}</span>"
                        : "";

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
                          <div class="sheet-result-body">
                            <div class="sheet-result-top">
                              <strong>.NET {Encode(d.Product == "SDK" ? "SDK" : "Runtime")} {Encode(d.Version)}</strong>
                              <span class="channel-tag {tagClass}">{tagLabel}</span>
                              {statusBadge}
                            </div>
                            <span class="muted text-xs">{Encode(subtitle)}</span>
                            <div class="sheet-result-meta muted text-xs">
                              {(string.IsNullOrEmpty(shortSha) ? "" : $"<span>SHA-512 <code>{Encode(shortSha)}</code></span>")}
                              {cveMeta}
                            </div>
                          </div>
                          <div class="sheet-result-actions">
                            {(string.IsNullOrEmpty(d.Sha512) ? "" : $"<button type=\"button\" class=\"button icon sm ghost copy-btn\" data-copy=\"{Encode(d.Sha512)}\" title=\"Copy SHA-512 checksum\">{Icon("clipboard", "xs")}</button>")}
                            <a class="button {(isInstalled ? "ghost" : "")} sm" href="{Encode(d.Url)}" download>{Icon("download", "xs")} {(isInstalled ? "Download again" : "Download")}</a>
                          </div>
                        </div>
                        """;
                }));

                var countLabel = initialVisibleCount == 1 ? "1 release" : $"{initialVisibleCount} releases";
                return $"""
                    <section class="sheet-channel-group" data-install-channel-group="{Encode(major)}" {(initialVisibleCount == 0 ? "hidden" : "")}>
                      <div class="sheet-channel-head">
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
            <dialog id="install-dotnet-dialog" class="add-tool-sheet install-dotnet-sheet" closedby="any" aria-labelledby="install-dotnet-sheet-title"{{(open ? " open data-open-on-load" : "")}}>
              <div class="sheet-inner">
                <div class="sheet-head">
                  <div class="sheet-title-group">
                    <h2 id="install-dotnet-sheet-title">Install .NET</h2>
                    <span class="search-source">builds.dotnet.microsoft.com</span>
                  </div>
                  <a class="button icon ghost" id="close-install-dotnet-btn" href="/" aria-label="Close install .NET panel">{{Icon("x", "sm")}}</a>
                </div>

                <div class="sheet-controls">
                  <label class="search-field wide" for="install-dotnet-search-input">
                    {{Icon("search", "xs")}}
                    <input id="install-dotnet-search-input" type="search" placeholder="Search by version, channel or CVE" autocomplete="off">
                  </label>
                  <div class="filter-bar">
                    <div class="segmented" role="group" aria-label="Product">
                      <button type="button" class="seg active" data-install-product="SDK" aria-pressed="true">SDK</button>
                      <button type="button" class="seg" data-install-product=".NET Runtime" aria-pressed="false">Runtime</button>
                    </div>
                    <div class="segmented" role="group" aria-label="Releases to show">
                      <button type="button" class="seg active" data-install-lifecycle="latest" aria-pressed="true">Latest</button>
                      <button type="button" class="seg" data-install-lifecycle="supported" aria-pressed="false">All patches</button>
                      <button type="button" class="seg" data-install-lifecycle="lts" aria-pressed="false">LTS</button>
                      <button type="button" class="seg" data-install-lifecycle="all" aria-pressed="false">Everything</button>
                    </div>
                    <button type="button" class="toggle-chip" data-install-previews aria-pressed="false"><span class="chip-box" aria-hidden="true">{{Icon("check", "xs")}}</span>Previews</button>
                  </div>
                </div>

                <div id="install-dotnet-results" class="sheet-results">
                  {{resultsHtml}}
                </div>

                <div class="sheet-foot">
                  <span class="drawer-section-title">Verify checksum and install</span>
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

    /// <summary>Only what the removal dialog needs. Details live in the server-rendered drawer templates.</summary>
    private static string SerializeRowsJson(Row[] rows)
    {
        var newest = rows
            .GroupBy(row => (row.Channel, row.Kind))
            .Select(group => group.OrderByDescending(row => row.Version, SdkVersionComparer.Instance).First())
            .ToHashSet();
        var entries = rows.Where(row => row.CanRemove).Select(row =>
            $"{{\"version\": \"{EscapeJson(row.Version)}\"," +
            $"\"displayVersion\": \"{EscapeJson(row.DisplayVersion)}\"," +
            $"\"kind\": \"{row.Kind}\"," +
            $"\"name\": \"{EscapeJson(row.Name)}\"," +
            $"\"channel\": \"{EscapeJson(row.Channel)}\"," +
            $"\"isEol\": {(row.IsEol ? "true" : "false")}," +
            $"\"isPreview\": {(row.IsPreview ? "true" : "false")}," +
            $"\"isSuperseded\": {(newest.Contains(row) ? "false" : "true")}," +
            $"\"location\": \"{EscapeJson(row.Paths.FirstOrDefault() ?? "")}\"," +
            $"\"command\": \"{EscapeJson(row.EquivalentCommand ?? "")}\"}}");
        return "[" + string.Join(",\n", entries) + "]";
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("\r", "").Replace("\n", "\\n");

    internal static Row[] BuildRows(InstallationSnapshot snapshot, SdkDownloadSnapshot? sdkSnapshot, DateOnly today)
    {
        var downloads = sdkSnapshot?.Downloads ?? [];
        var installedSdkSet = snapshot.Sdks
            .SelectMany(s => new[] { s.Version, ShortDisplayVersion(s.Version) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedRuntimeSet = snapshot.Runtimes
            .SelectMany(r => new[] { r.Version, ShortDisplayVersion(r.Version) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sdkRows = snapshot.Sdks.Select(sdk =>
        {
            var major = sdk.Version.Split('.')[0];
            var support = SupportLifecycle.For(sdkSnapshot, sdk.Version, today);
            var isEol = support.IsEol;
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

            var cmd = ConfirmActions.RemovalCommand(sdk.Version, "sdk");

            return new Row(
                Kind: "sdk",
                Name: ".NET SDK",
                Description: $"incl. Runtime {inclRt}",
                Version: sdk.Version,
                DisplayVersion: shortVer,
                Paths: string.IsNullOrWhiteSpace(sdk.InstallationPath) ? [] : [sdk.InstallationPath],
                CanRemove: sdk.CanRemove,
                Channel: major,
                Support: support,
                IsPreview: isPreview,
                LatestPatchVersion: hasNewerPatch ? patchTarget!.Version : null,
                LatestPatchInstalled: patchAlreadyInstalled,
                PackageVersion: targetPkg?.Version ?? shortVer,
                PackageDate: FormatReleaseDate(targetPkg?.ReleaseDate),
                PackageRuntime: targetPkg?.IncludedRuntimeVersion ?? inclRt,
                PackageUrl: targetPkg?.Url,
                PackageSha512: targetPkg?.Sha512,
                Cves: targetPkg?.Cves ?? [],
                IncludedRuntime: $"{inclRt} + ASP.NET Core",
                ReleasedDate: FormatReleaseDate(catalogEntry?.ReleaseDate),
                EquivalentCommand: cmd);
        });

        var runtimeRows = snapshot.Runtimes
            .GroupBy(runtime => runtime.Version)
            .Select(group =>
            {
                var runtimes = group.ToArray();
                var sample = runtimes[0];
                var major = sample.Version.Split('.')[0];
                var support = SupportLifecycle.For(sdkSnapshot, sample.Version, today);
                var isEol = support.IsEol;
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

                var cmd = ConfirmActions.RemovalCommand(sample.Version, "runtime");
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
                    Support: support,
                    IsPreview: isPreview,
                    LatestPatchVersion: hasNewerPatch ? latest!.Version : null,
                    LatestPatchInstalled: patchAlreadyInstalled,
                    PackageVersion: targetPkg?.Version ?? shortVer,
                    PackageDate: FormatReleaseDate(targetPkg?.ReleaseDate),
                    PackageRuntime: targetPkg?.Version,
                    PackageUrl: targetPkg?.Url,
                    PackageSha512: targetPkg?.Sha512,
                    Cves: targetPkg?.Cves ?? [],
                    IncludedRuntime: ShortFrameworkSubtitle(description),
                    ReleasedDate: FormatReleaseDate(catalogEntry?.ReleaseDate),
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

    /// <summary>"11.0.100-preview.7" → "11.0.100-rc.3" reads better as "rc.3" once the shared core is on screen.</summary>
    private static string ShortPatchTarget(string current, string target)
    {
        var dash = target.IndexOf('-');
        return dash > 0 && current.StartsWith(target[..dash], StringComparison.Ordinal) ? target[(dash + 1)..] : target;
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrEmpty(sha)
            ? ""
            : sha.Length > 12
                ? $"{sha[..4]}…{sha[^4..]}"
                : sha;

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

    private static string ShortFrameworkSubtitle(string description) =>
        description
            .Replace("Microsoft.NETCore.App", ".NET", StringComparison.Ordinal)
            .Replace("Microsoft.AspNetCore.App", "ASP.NET Core", StringComparison.Ordinal)
            .Replace("Microsoft.WindowsDesktop.App", "Desktop", StringComparison.Ordinal);

    private static string GetInstallerFileName(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? System.IO.Path.GetFileName(uri.LocalPath) : "";

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
        : ConfirmActions.Trigger(ConfirmActions.VersionRemove(row, options), $"<button class=\"button sm remove\" type=\"submit\" aria-label=\"Remove {Encode(row.Name)} {Encode(row.DisplayVersion)}\">Remove</button>");

    private static string RenderNotice(string notice) => notice switch
    {
        "removed" => "<div class=\"action-notice\" role=\"status\">The selected .NET installation was removed.</div>",
        "remove-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The selected .NET installation could not be removed. Check the permissions and try again.</div>",
        _ => ""
    };

    private static string KindSegment(string label, string kind, int count, InstallationViewOptions options, string srSuffix = "")
    {
        var active = options.Kind == kind;
        var target = options with { Kind = active ? "all" : kind };
        return $"<a href=\"{Link(target)}\" class=\"seg {(active ? "active" : "")}\" data-version-kind=\"{kind}\"{(active ? " aria-current=\"page\"" : "")}>{label}{(srSuffix.Length > 0 ? $"<span class=\"sr-only\">{srSuffix}</span>" : "")}<span class=\"seg-count\" aria-hidden=\"true\">{count}</span></a>";
    }

    private static string SortMenu(InstallationViewOptions options)
    {
        var current = (options.Sort, options.Ascending) switch
        {
            ("name", false) => "Name, Z to A",
            ("name", true) => "Name, A to Z",
            ("path", false) => "Location, Z to A",
            ("path", true) => "Location, A to Z",
            (_, true) => "Oldest version",
            _ => "Newest version"
        };
        string Option(string label, string sort, bool ascending) =>
            $"<a class=\"menu-item {(label == current ? "active" : "")}\" href=\"{Link(options with { Sort = sort, Ascending = ascending })}\"{(label == current ? " aria-current=\"true\"" : "")}>{label}</a>";
        return $"""
            <details class="menu sort-menu">
              <summary class="button sm ghost" aria-label="Sort: {current}">{current} {Icon("chevd", "xs")}</summary>
              <div class="menu-panel">
                {Option("Newest version", "version", false)}{Option("Oldest version", "version", true)}{Option("Name, A to Z", "name", true)}{Option("Name, Z to A", "name", false)}{Option("Location, A to Z", "path", true)}{Option("Location, Z to A", "path", false)}
              </div>
            </details>
            """;
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

using System.Globalization;
using System.Runtime.InteropServices;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using static DotnetCockpit.PageLayout;
using static DotnetCockpit.SdkDownloadsSection;

namespace DotnetCockpit;

internal sealed record InstallationViewOptions(string Query = "", string Kind = "all", string Sort = "version", bool Ascending = false, string Notice = "", string Selected = "", string Tab = "installed", SdkDownloadsViewOptions? CatalogOptions = null)
{
    public static InstallationViewOptions From(IQueryCollection query) => new(
        query["q"].ToString(),
        query["kind"].ToString() is "sdks" or "runtimes" ? query["kind"].ToString() : "all",
        query["sort"].ToString() is "name" or "path" ? query["sort"].ToString() : "version",
        query["direction"] == "asc",
        query["notice"].ToString(),
        query["selected"].ToString(),
        query["tab"].ToString() is "all-releases" or "all" ? "all-releases" : "installed",
        SdkDownloadsViewOptions.From(SetupViewOptions.From(query)));
}

internal static class InstallationPage
{
    private sealed record Row(
        string Kind,
        string Name,
        string Description,
        string Version,
        IReadOnlyList<string> Paths,
        bool CanRemove,
        string Channel,
        bool IsLts,
        bool IsEol,
        string? LatestPatchVersion = null,
        string? LatestPatchDate = null,
        string? LatestPatchUrl = null,
        string? LatestPatchSha512 = null,
        IReadOnlyList<SdkCve>? Cves = null,
        string? IncludedRuntime = null,
        string? ReleasedDate = null,
        string? EquivalentCommand = null)
    {
        public string Path => string.Join(" ", Paths);
        public bool HasPatch => !string.IsNullOrEmpty(LatestPatchVersion) && LatestPatchVersion != Version;
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
        var inventoryDescription = snapshot.ExecutablePath is null
            ? "Versions reported by dotnet-core-uninstall."
            : "The .NET installation available on your PATH.";

        var selectedRow = !string.IsNullOrWhiteSpace(options.Selected)
            ? allRows.FirstOrDefault(r => r.Version == options.Selected)
            : null;

        string content;
        if (isAllReleases)
        {
            var catOpts = options.CatalogOptions ?? new SdkDownloadsViewOptions(Query: options.Query, Product: options.Kind == "runtimes" ? "runtime" : "sdk");
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
                        <a class="pill-badge {(!isAllReleases ? "active" : "")}" href="{{Link(options with { Tab = "installed" })}}">Installed <strong class="summary-count">{{total}}</strong></a>
                        <a class="pill-badge {(isAllReleases ? "active" : "")}" href="{{Link(options with { Tab = "all-releases" })}}">All releases <strong class="summary-count">{{catalogTotal}}</strong></a>
                      </div>
                      <div class="version-filter-controls">
                        <nav class="tool-tabs" aria-label="Installation type">
                          <a href="{{Link(options with { Kind = "all" })}}" class="sr-only" {(!isAllReleases && options.Kind == "all" ? " aria-current=\"page\"" : "")}>All versions</a>
                          {{TypeLink("SDKs", "sdks", options, showCheck: true)}}
                          {{TypeLink("Runtimes", "runtimes", options, showCheck: true)}}
                          <a href="/?tab=all-releases&amp;preview=true&amp;rc=true" class="filter-pill">Previews</a>
                        </nav>
                        {{(!isAllReleases ? $"""
                        <form method="get" action="/" class="filter-form version-search-compact" role="search" data-versions-filter>
                          <label class="filter-input-compact">
                            {Icon("search", "xs")}
                            <input id="installed-version-filter" type="search" name="q" value="{Encode(options.Query)}" placeholder="Search versions..." aria-label="Filter installed versions">
                          </label>
                          <input type="hidden" name="kind" value="{options.Kind}">
                          <input type="hidden" name="sort" value="{options.Sort}">
                          <input type="hidden" name="direction" value="{(options.Ascending ? "asc" : "desc")}">
                          <input type="hidden" name="tab" value="{options.Tab}">
                          <noscript><button class="button sm" type="submit">Filter</button></noscript>
                        </form>
                        """ : "")}}
                      </div>
                    </div>
                    {{content}}
                    <div class="inventory-footer sr-only">
                      <span><span data-versions-count>{{rows.Length}} of {{total}} installations shown</span></span>
                      <span>Checked <time datetime="{{snapshot.CheckedAt.ToString("O", CultureInfo.InvariantCulture)}}">{{snapshot.CheckedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}}</time></span>
                    </div>
                  </section>
                  <p class="scope-note muted">Locations include the version subfolder. When dotnet-core-uninstall is installed, only versions it can remove are shown; runtime frameworks that share a version are grouped together. Separate installations and other architectures may not appear here.</p>
                  """, "/", ".NET Cockpit", snapshot.IssueCount);
    }

    private static string RenderVersionsHub(Row[] allRows, Row[] rows, InstallationViewOptions options, Row? selectedRow, int catalogTotal)
    {
        var showActions = allRows.Any(row => row.CanRemove);
        var columnCount = showActions ? 4 : 3;

        // Group rows by channel
        var channelGroups = rows
            .GroupBy(r => r.Channel)
            .OrderByDescending(g => int.TryParse(g.Key, out var v) ? v : 0)
            .ToArray();

        var tableBody = string.Concat(channelGroups.Select(group =>
        {
            var channel = group.Key;
            var isLts = channel is "10" or "8" or "6";
            var isEol = InstallationSnapshot.IsEol(channel);
            var patchCount = group.Count(r => r.HasPatch);

            var channelStatusBadge = isEol
                ? "<span class=\"badge bad sm\">🛡 End of life</span>"
                : patchCount > 0
                    ? $"<span class=\"badge warn sm\">⚠ {patchCount} {(patchCount == 1 ? "patch" : "patches")}</span>"
                    : "<span class=\"badge ok sm\">✓ Up to date</span>";

            var channelSubtitle = isEol
                ? "No security fixes since May 14, 2024"
                : group.Any(r => r.LatestPatchVersion != null)
                    ? $"Latest patch {group.FirstOrDefault(r => r.Kind == "sdk")?.LatestPatchVersion ?? group.First().LatestPatchVersion} · Runtime {channel}.0"
                    : $"Active .NET {channel} channel";

            var lifeState = isEol ? "bad" : (channel is "9" or "8" ? "warn" : "ok");
            var lifeLeftLabel = isEol ? "Unsupported" : (channel is "9" or "8" ? "Ends soon" : "Active support");
            var lifeRightLabel = isEol ? "Ended May 14, 2024" : (channel is "9" or "8" ? "Nov 10, 2026" : "until Nov 14, 2028");
            var lifeWidth = isEol ? "100%" : (channel is "9" or "8" ? "95%" : "92%");

            var headerRow = $"""
                <tr class="channel-header-row" data-channel-header="{channel}">
                  <td colspan="{columnCount}">
                    <div class="channel-header-card">
                      <div class="channel-avatar channel-avatar-{channel}">{channel}</div>
                      <div class="channel-header-body">
                        <div class="channel-title-row">
                          <strong class="channel-title">.NET {channel}</strong>
                          <span class="channel-tag {(isLts ? "lts" : "sts")}">{ChannelTagLabel(channel)}</span>
                          {channelStatusBadge}
                        </div>
                        <span class="channel-subtitle muted">{channelSubtitle}</span>
                      </div>
                      <div class="channel-life">
                        <div class="channel-life-top">
                          <span class="life-status {lifeState}">{lifeLeftLabel}</span>
                          <span>{lifeRightLabel}</span>
                        </div>
                        <div class="channel-life-bar {lifeState}"><i style="width:{lifeWidth}"></i></div>
                      </div>
                      <button type="button" class="button icon sm ghost channel-collapse-btn" data-collapse-channel="{channel}" aria-label="Toggle .NET {channel} channel">{Icon("chevd", "sm")}</button>
                    </div>
                  </td>
                </tr>
                """;

            var rowsHtml = string.Concat(group.Select(row =>
            {
                var isSelected = selectedRow?.Version == row.Version;
                var versionCell = row.HasPatch
                    ? $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="brand" class="version-badge sr-only">{Encode(row.Version)}</fluent-badge><span class="v-current">{Encode(row.Version)}</span><span class="v-arrow">→</span><strong class="v-patch">{Encode(row.LatestPatchVersion!)}</strong></div>"""
                    : row.IsEol
                        ? $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="brand" class="version-badge sr-only">{Encode(row.Version)}</fluent-badge><span class="v-current">{Encode(row.Version)}</span><span class="badge bad sm">EOL</span></div>"""
                        : $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="brand" class="version-badge sr-only">{Encode(row.Version)}</fluent-badge><span class="v-current">{Encode(row.Version)}</span><span class="badge ok sm">Latest</span></div>""";

                return $"""
                    <tr data-kind="{row.Kind}"{(isSelected ? " class=\"row-selected\"" : "")}>
                      <td class="cell-name"><div class="installation-name"><span class="installation-icon">{Icon(row.Kind == "sdk" ? "versions" : "server")}</span><div><div class="installation-header"><strong>{Encode(row.Name)}</strong></div><span class="muted framework-name">{Encode(row.Description)}</span></div></div></td>
                      <td class="cell-version">{versionCell}</td>
                      <td class="cell-path"><code class="installation-path installation-path-clean">{Encode(row.Path)}</code></td>
                      <td class="cell-actions">
                        <a href="{Link(options with { Selected = row.Version })}" class="button sm ghost inspect-btn" data-inspect="{row.Version}" data-channel="{row.Channel}" aria-label="Inspect {row.Version}">Inspect</a>
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
                    <p id="remove-dialog-warn" class="muted">Removal runs through Microsoft's dotnet-core-uninstall. Projects pinned to this version will no longer build on this machine.</p>
                  </div>
                </div>
                <div class="drawer-command-box">
                  <span class="cmd-prompt">$</span>
                  <code id="remove-dialog-cmd">sudo dotnet-core-uninstall remove</code>
                  <button class="button icon ghost copy-btn" id="remove-dialog-copy" type="button" title="Copy command">{{Icon("clipboard")}}</button>
                </div>
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

        var isLts = row.IsLts;
        var channelType = isLts ? "Long Term Support (LTS)" : "Standard Term Support (STS)";
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
                    <strong>⏱ .NET {row.Channel} patch available</strong>
                    <p class="muted">Newer patch {Encode(row.LatestPatchVersion!)} includes security and reliability fixes.</p>
                  </div>
                  """
                : $"""
                  <div class="drawer-alert drawer-alert-ok">
                    <strong>✓ Running latest release for .NET {row.Channel}</strong>
                    <p class="muted">Your active installation is fully patched and up to date.</p>
                  </div>
                  """;

        var patchSection = row.HasPatch
            ? $"""
              <div id="drawer-patch-section" class="drawer-section">
                <h4 class="drawer-section-title">Newest patch</h4>
                <div class="drawer-patch-box">
                  <div class="drawer-patch-header">
                    <strong id="drawer-patch-version">{Encode(row.LatestPatchVersion!)}</strong>
                    <span id="drawer-patch-date" class="muted text-xs">{Encode(row.LatestPatchDate ?? "")} · Runtime {row.Channel}.0</span>
                  </div>
                  <div id="drawer-cves" class="drawer-cve-list">
                    {(row.Cves is { Count: > 0 } ? $"<span class=\"badge warn sm\">🛡 {row.Cves.Count} security fixes</span>" : "")}
                    {string.Concat((row.Cves ?? []).Select(c => $"<a class=\"cve-pill\" href=\"{Encode(c.Url ?? "")}\" target=\"_blank\" rel=\"noopener\">{Encode(c.Id)}</a>"))}
                  </div>
                  <div class="drawer-code-box">
                    <code id="drawer-patch-sha" class="drawer-sha">{Encode(row.LatestPatchSha512?.Length > 32 ? row.LatestPatchSha512[..32] + "..." : row.LatestPatchSha512 ?? "")}</code>
                    <button class="button icon ghost copy-btn" data-copy="{Encode(row.LatestPatchSha512 ?? "")}" type="button" title="Copy SHA-512">{Icon("clipboard")}</button>
                  </div>
                </div>
              </div>
              """
            : $"""<div id="drawer-patch-section" class="drawer-section" hidden></div>""";

        var command = row.EquivalentCommand ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"dotnet-core-uninstall remove {row.Version} --{(row.Kind == "sdk" ? "sdk" : "runtime")}"
            : $"sudo dotnet-core-uninstall remove {row.Version} --{(row.Kind == "sdk" ? "sdk" : "runtime")}");

        var downloadBtn = row.HasPatch && !string.IsNullOrEmpty(row.LatestPatchUrl)
            ? $"<a id=\"drawer-download-btn\" class=\"button primary drawer-primary-btn\" href=\"{Encode(row.LatestPatchUrl)}\" download>{Icon("download", "sm")} Download & open installer</a>"
            : $"<a id=\"drawer-download-btn\" class=\"button primary drawer-primary-btn\" href=\"/setup#sdk-downloads-title\" hidden>{Icon("download", "sm")} Download & open installer</a>";

        var removeAction = row.CanRemove
            ? $"""
              <form method="post" action="/installation/remove" id="drawer-remove-form">
                <input type="hidden" name="version" value="{Encode(row.Version)}">
                <input type="hidden" name="kind" value="{row.Kind}">
                <input type="hidden" name="viewKind" value="{options.Kind}">
                <input type="hidden" name="q" value="{Encode(options.Query)}">
                <input type="hidden" name="sort" value="{options.Sort}">
                <input type="hidden" name="direction" value="{(options.Ascending ? "asc" : "desc")}">
                <button class="button danger drawer-remove-btn" type="submit">{Icon("trash", "xs")} Remove</button>
              </form>
              """
            : "<div id=\"drawer-remove-form\"></div>";

        return $$"""
            <aside id="version-drawer" class="detail-drawer" {{hiddenAttr}} aria-label="Version details">
              <div class="drawer-header">
                <div class="drawer-eyebrow-row">
                  <span id="drawer-eyebrow">.NET {{row.Channel}} · {{(row.Kind == "sdk" ? "SDK" : "Runtime")}}</span>
                  <a href="{{Link(options with { Selected = "" })}}" id="drawer-close" class="button icon ghost drawer-close-btn" aria-label="Close details">✕</a>
                </div>
                <div class="drawer-title-row">
                  <h3 id="drawer-title" class="drawer-title">{{Encode(row.Version)}}</h3>
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
                    <dt>Included runtime</dt>
                    <dd id="drawer-runtime">{{Encode(row.IncludedRuntime ?? (row.Kind == "sdk" ? $"{row.Channel}.0.9 + ASP.NET Core" : "Included"))}}</dd>
                    <dt>Released</dt>
                    <dd id="drawer-released">{{Encode(row.ReleasedDate ?? "Sep 9, 2025")}}</dd>
                    <dt>Location</dt>
                    <dd>
                      <div class="drawer-code-box">
                        <code id="drawer-location" class="drawer-path">{{Encode(row.Path)}}</code>
                        <button class="button icon ghost copy-btn" data-copy="{{Encode(row.Path)}}" type="button" title="Copy path">{{Icon("clipboard")}}</button>
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
            $"\"kind\": \"{row.Kind}\"," +
            $"\"name\": \"{EscapeJson(row.Name)}\"," +
            $"\"description\": \"{EscapeJson(row.Description)}\"," +
            $"\"channel\": \"{EscapeJson(row.Channel)}\"," +
            $"\"isLts\": {(row.IsLts ? "true" : "false")}," +
            $"\"isEol\": {(row.IsEol ? "true" : "false")}," +
            $"\"hasPatch\": {(row.HasPatch ? "true" : "false")}," +
            $"\"patchVersion\": \"{EscapeJson(row.LatestPatchVersion ?? "")}\"," +
            $"\"patchDate\": \"{EscapeJson(row.LatestPatchDate ?? "")}\"," +
            $"\"patchUrl\": \"{EscapeJson(row.LatestPatchUrl ?? "")}\"," +
            $"\"patchSha\": \"{EscapeJson(row.LatestPatchSha512 ?? "")}\"," +
            $"\"includedRuntime\": \"{EscapeJson(row.IncludedRuntime ?? (row.Kind == "sdk" ? $"{row.Channel}.0.9 + ASP.NET Core" : ""))}\"," +
            $"\"released\": \"{EscapeJson(row.ReleasedDate ?? "Sep 9, 2025")}\"," +
            $"\"location\": \"{EscapeJson(row.Path)}\"," +
            $"\"command\": \"{EscapeJson(row.EquivalentCommand ?? "")}\"," +
            $"\"canRemove\": {(row.CanRemove ? "true" : "false")}," +
            $"\"cves\": [{(row.Cves == null ? "" : string.Join(", ", row.Cves.Select(c => $"{{\"id\": \"{EscapeJson(c.Id)}\", \"url\": \"{EscapeJson(c.Url ?? "")}\"}}")))}]" +
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

            var catalogEntry = downloads.FirstOrDefault(d => d.Product == "SDK" && d.Version == sdk.Version);

            var channelDownloads = downloads
                .Where(d => d.Product == "SDK" && d.Channel == major && !d.Version.Contains("-preview") && !d.Version.Contains("-rc"))
                .OrderByDescending(d => d.Version, SdkVersionComparer.Instance)
                .ToArray();
            var latest = channelDownloads.FirstOrDefault();
            var hasNewerPatch = latest != null && SdkVersionComparer.Instance.Compare(latest.Version, sdk.Version) > 0;

            var cmd = $"{uninstallPrefix} remove {sdk.Version} --sdk";

            return new Row(
                Kind: "sdk",
                Name: ".NET SDK",
                Description: $"incl. Runtime {catalogEntry?.IncludedRuntimeVersion ?? $"{major}.0"}",
                Version: sdk.Version,
                Paths: string.IsNullOrWhiteSpace(sdk.InstallationPath) ? [] : [sdk.InstallationPath],
                CanRemove: sdk.CanRemove,
                Channel: major,
                IsLts: isLts,
                IsEol: isEol,
                LatestPatchVersion: hasNewerPatch ? latest!.Version : null,
                LatestPatchDate: hasNewerPatch ? latest!.ReleaseDate : null,
                LatestPatchUrl: hasNewerPatch ? latest!.Url : null,
                LatestPatchSha512: hasNewerPatch ? latest!.Sha512 : null,
                Cves: hasNewerPatch ? latest!.Cves : [],
                IncludedRuntime: catalogEntry?.IncludedRuntimeVersion ?? $"{major}.0.9 + ASP.NET Core",
                ReleasedDate: catalogEntry?.ReleaseDate ?? "Sep 9, 2025",
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

                var catalogEntry = downloads.FirstOrDefault(d => d.Product == ".NET Runtime" && d.Version == sample.Version);

                var channelDownloads = downloads
                    .Where(d => d.Product == ".NET Runtime" && d.Channel == major && !d.Version.Contains("-preview") && !d.Version.Contains("-rc"))
                    .OrderByDescending(d => d.Version, SdkVersionComparer.Instance)
                    .ToArray();
                var latest = channelDownloads.FirstOrDefault();
                var hasNewerPatch = latest != null && SdkVersionComparer.Instance.Compare(latest.Version, sample.Version) > 0;

                var cmd = $"{uninstallPrefix} remove {sample.Version} --runtime";

                return new Row(
                    Kind: "runtime",
                    Name: runtimes.Length == 1 ? RuntimeName(sample.Name) : ".NET Runtime",
                    Description: string.Join(" · ", runtimes.Select(runtime => runtime.Name)),
                    Version: sample.Version,
                    Paths: runtimes.Select(runtime => runtime.InstallationPath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray(),
                    CanRemove: runtimes.Any(runtime => runtime.CanRemove),
                    Channel: major,
                    IsLts: isLts,
                    IsEol: isEol,
                    LatestPatchVersion: hasNewerPatch ? latest!.Version : null,
                    LatestPatchDate: hasNewerPatch ? latest!.ReleaseDate : null,
                    LatestPatchUrl: hasNewerPatch ? latest!.Url : null,
                    LatestPatchSha512: hasNewerPatch ? latest!.Sha512 : null,
                    Cves: hasNewerPatch ? latest!.Cves : [],
                    IncludedRuntime: null,
                    ReleasedDate: catalogEntry?.ReleaseDate ?? "Sep 9, 2025",
                    EquivalentCommand: cmd);
            });

        return [.. sdkRows, .. runtimeRows];
    }

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
        _ => true
    };

    private static bool MatchesQuery(Row row, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var searchable = $"{row.Name} {row.Description} {row.Version} {row.Path} {row.Channel}";
        return terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string ChannelTagLabel(string channel) => channel is "10" or "8" or "6" ? "LTS" : "STS";

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

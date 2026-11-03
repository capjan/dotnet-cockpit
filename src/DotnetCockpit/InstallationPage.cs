using System.Globalization;
using System.Runtime.InteropServices;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using static DotnetCockpit.PageLayout;
using static DotnetCockpit.SdkDownloadsSection;

namespace DotnetCockpit;

internal sealed record InstallationViewOptions(string Query = "", string Kind = "all", string Sort = "version", bool Ascending = false, string Notice = "", string Selected = "")
{
    public static InstallationViewOptions From(IQueryCollection query) => new(
        query["q"].ToString(),
        query["kind"].ToString() is "sdks" or "runtimes" ? query["kind"].ToString() : "all",
        query["sort"].ToString() is "name" or "path" ? query["sort"].ToString() : "version",
        query["direction"] == "asc",
        query["notice"].ToString(),
        query["selected"].ToString());
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
        var inventoryDescription = snapshot.ExecutablePath is null
            ? "Versions reported by dotnet-core-uninstall."
            : "The .NET installation available on your PATH.";

        var selectedRow = !string.IsNullOrWhiteSpace(options.Selected)
            ? allRows.FirstOrDefault(r => r.Version == options.Selected)
            : null;

        var content = snapshot.Status switch
        {
            "notFound" => """
                <div class="empty-state"><h3>No .NET installation found</h3><p>The dotnet command is not available on PATH.</p><p class="muted">.NET Cockpit runs independently. After installing .NET, restart Cockpit if PATH has changed.</p></div>
                """,
            "error" => $"<div class=\"empty-state error-state\" role=\"alert\"><h3>Installation information is unavailable</h3><p>{Encode(snapshot.Error ?? "The installation could not be read.")}</p><a class=\"button\" href=\"{RefreshLink(options, snapshot.CheckedAt)}\">Try again</a></div>",
            _ when total == 0 => $"<div class=\"empty-state\"><h3>No SDKs or runtimes found</h3><p class=\"muted\">{(snapshot.ExecutablePath is null ? "dotnet-core-uninstall reported no removable SDKs or runtimes." : "The dotnet host reported no installed SDKs or runtimes.")}</p></div>",
            _ => RenderVersionsHub(allRows, rows, options, selectedRow, catalogTotal)
        };

        var preflightBanner = options.Query.Length == 0
            ? $"""
              <div id="preflight-banner" class="preflight-banner" hidden>
                <div class="pf-banner-content">
                  <span class="pf-banner-icon">{Icon("sparkle", "sm")}</span>
                  <div><strong>First launch? Run the Pre-flight Check</strong><p class="muted">10-second diagnostics to verify your .NET host, installed SDKs, and workspace.</p></div>
                </div>
                <div class="pf-banner-actions">
                  <a class="button sm primary" href="/preflight">Run Pre-flight</a>
                  <button id="dismiss-preflight-btn" class="button sm ghost" type="button">Dismiss</button>
                </div>
              </div>
              """
            : "";

        return PageLayout.Render($$"""
                  {{preflightBanner}}
                  <div class="page-heading">
                    <div><h1>Your .NET environment</h1><p class="muted intro">Installed SDKs and runtimes, in one place.</p></div>
                    <a class="button primary" href="{{RefreshLink(options, snapshot.CheckedAt)}}">{{Icon("refresh")}} Refresh</a>
                  </div>
                  {{RenderNotice(options.Notice)}}
                  <section class="summary-grid" aria-label="Installation summary">
                    {{SummaryCard("Installed SDKs", snapshot.Sdks.Length.ToString(CultureInfo.InvariantCulture), "Build and develop applications", "code")}}
                    {{SummaryCard("Installed runtimes", runtimeCount.ToString(CultureInfo.InvariantCulture), "Run .NET applications", "server")}}
                    {{SummaryCard("This machine", snapshot.Architecture.ToUpperInvariant(), snapshot.OperatingSystem, "desktop")}}
                  </section>
                  <section class="inventory" id="inventory" aria-labelledby="inventory-title">
                    <div class="inventory-heading">
                      <div><h2 id="inventory-title">Installed versions</h2><p class="muted">{{inventoryDescription}}</p></div>
                      <fluent-badge appearance="outline" color="brand" class="inventory-count">{{total}} installed</fluent-badge>
                    </div>
                    <div class="inventory-scope-bar">
                      <div class="version-scope-pills">
                        <span class="pill-badge active">Installed <strong class="summary-count">{{total}}</strong></span>
                        <a class="pill-badge" href="/setup#sdk-downloads-title">All releases <span class="muted">{{catalogTotal}}</span></a>
                      </div>
                      <nav class="tool-tabs" aria-label="Installation type">
                        {{TypeLink("All versions", "all", options)}}
                        {{TypeLink("SDKs", "sdks", options)}}
                        {{TypeLink("Runtimes", "runtimes", options)}}
                      </nav>
                    </div>
                    <div class="inventory-toolbar">
                      <form method="get" action="/" class="filter-form" role="search" data-versions-filter>
                        <label class="filter-input">
                          {{Icon("search")}}
                          <input id="installed-version-filter" type="search" name="q" value="{{Encode(options.Query)}}" placeholder="Filter by version, name, or path" aria-label="Filter installed versions">
                        </label>
                        <input type="hidden" name="kind" value="{{options.Kind}}">
                        <input type="hidden" name="sort" value="{{options.Sort}}">
                        <input type="hidden" name="direction" value="{{(options.Ascending ? "asc" : "desc")}}">
                        <noscript><button class="button" type="submit">Filter</button></noscript>
                      </form>
                      <div class="inventory-results"><span class="muted"><span data-versions-count>{{rows.Length}} of {{total}} installations shown</span></span></div>
                    </div>
                    {{content}}
                    <div class="inventory-footer">
                      <span><span data-versions-count>{{rows.Length}}</span> of {{total}} installations shown</span>
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
                    ? $"<span class=\"badge warn sm\">↑ {patchCount} {(patchCount == 1 ? "patch" : "patches")}</span>"
                    : "<span class=\"badge ok sm\">✓ Up to date</span>";

            var channelSubtitle = isEol
                ? "No security fixes since end of support date"
                : group.Any(r => r.LatestPatchVersion != null)
                    ? $"Latest patch {group.FirstOrDefault(r => r.Kind == "sdk")?.LatestPatchVersion ?? group.First().LatestPatchVersion} · Runtime {channel}.0"
                    : $"Active .NET {channel} channel";

            var headerRow = $"""
                <tr class="channel-group-header-row" data-channel-header="{channel}">
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
                    </div>
                  </td>
                </tr>
                """;

            var rowsHtml = string.Concat(group.Select(row =>
            {
                var isSelected = selectedRow?.Version == row.Version;
                var versionCell = row.HasPatch
                    ? $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="{(row.Kind == "sdk" ? "brand" : "informative")}" class="version-badge">{Encode(row.Version)}</fluent-badge><span class="ver-arrow">→</span><fluent-badge appearance="tint" color="warning" class="version-badge patch-badge">{Encode(row.LatestPatchVersion!)}</fluent-badge></div>"""
                    : row.IsEol
                        ? $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="{(row.Kind == "sdk" ? "brand" : "informative")}" class="version-badge">{Encode(row.Version)}</fluent-badge><span class="badge bad sm">EOL</span></div>"""
                        : $"""<div class="version-patch-flow"><fluent-badge appearance="tint" color="{(row.Kind == "sdk" ? "brand" : "informative")}" class="version-badge">{Encode(row.Version)}</fluent-badge><span class="badge ok sm">Latest</span></div>""";

                return $"""
                    <tr data-kind="{row.Kind}">
                      <td><div class="installation-name"><span class="installation-icon">{Icon(row.Kind == "sdk" ? "code" : "server")}</span><div><div class="installation-header"><strong>{Encode(row.Name)}</strong></div><span class="muted framework-name">{Encode(row.Description)}</span></div></div></td>
                      <td>{versionCell}</td>
                      <td><div class="installation-paths">{string.Concat(row.Paths.Select(path => $"<code class=\"installation-path\" title=\"Click to copy\">{Encode(path)}</code>"))}</div></td>
                      {(showActions ? $"<td class=\"installation-actions\">{RemoveAction(row, options)}<a href=\"{Link(options with { Selected = row.Version })}\" class=\"button sm ghost inspect-btn\" data-inspect=\"{row.Version}\" data-channel=\"{row.Channel}\" aria-label=\"Inspect {row.Version}\">Inspect</a></td>" : $"<td class=\"installation-actions\"><a href=\"{Link(options with { Selected = row.Version })}\" class=\"button sm ghost inspect-btn\" data-inspect=\"{row.Version}\" data-channel=\"{row.Channel}\" aria-label=\"Inspect {row.Version}\">Inspect</a></td>")}
                    </tr>
                    """;
            }));

            return headerRow + rowsHtml;
        }));

        var emptyRow = $"""<tr class="empty-row" {(rows.Length == 0 ? "" : "hidden")}><td colspan="{columnCount}"><div class="empty-state"><h3>No matching versions</h3><p class="muted">Try another filter or switch to All versions.</p><a class="button" href="/#inventory">Clear filters</a></div></td></tr>""";

        var drawerHtml = RenderDrawer(selectedRow ?? allRows.FirstOrDefault(), selectedRow != null, options);
        var jsonDictionary = SerializeRowsJson(allRows);

        return $$"""
            <div class="versions-hub" id="versions-hub">
              <div class="versions-list">
                <div class="table-scroll" role="region" aria-label="Installed .NET versions" tabindex="0">
                  <table class="versions-table" aria-label="Installed .NET versions">
                    <thead><tr>{{SortHeader("Installation", "name", options)}}{{SortHeader("Version", "version", options)}}{{SortHeader("Location", "path", options)}}<th scope="col">Actions</th></tr></thead>
                    <tbody>
                      {{tableBody}}
                      {{emptyRow}}
                    </tbody>
                  </table>
                </div>
              </div>
              {{drawerHtml}}
            </div>
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
                <p class="muted">Security updates are no longer released for this channel. We recommend upgrading to a supported LTS release.</p>
              </div>
              """
            : row.HasPatch
                ? $"""
                  <div class="drawer-alert drawer-alert-warn">
                    <strong>⏱ Newer patch {Encode(row.LatestPatchVersion!)} available</strong>
                    <p class="muted">A newer servicing update is available with security and reliability fixes.</p>
                  </div>
                  """
                : $"""
                  <div class="drawer-alert drawer-alert-ok">
                    <strong>✓ Running latest release for .NET {row.Channel}</strong>
                    <p class="muted">This version is actively supported and up to date.</p>
                  </div>
                  """;

        var patchSection = row.HasPatch
            ? $"""
              <div id="drawer-patch-section" class="drawer-section">
                <h4 class="drawer-section-title">Newest patch</h4>
                <div class="drawer-patch-box">
                  <div class="drawer-patch-header">
                    <strong id="drawer-patch-version">{Encode(row.LatestPatchVersion!)}</strong>
                    <span id="drawer-patch-date" class="muted text-xs">{Encode(row.LatestPatchDate ?? "")}</span>
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
            ? $"<a id=\"drawer-download-btn\" class=\"button primary\" href=\"{Encode(row.LatestPatchUrl)}\" download>Download & open installer</a>"
            : $"<a id=\"drawer-download-btn\" class=\"button primary\" href=\"/setup#sdk-downloads-title\" hidden>Download & open installer</a>";

        var removeAction = row.CanRemove
            ? $"""
              <form method="post" action="/installation/remove" id="drawer-remove-form">
                <input type="hidden" name="version" value="{Encode(row.Version)}">
                <input type="hidden" name="kind" value="{row.Kind}">
                <input type="hidden" name="viewKind" value="{options.Kind}">
                <input type="hidden" name="q" value="{Encode(options.Query)}">
                <input type="hidden" name="sort" value="{options.Sort}">
                <input type="hidden" name="direction" value="{(options.Ascending ? "asc" : "desc")}">
                <button class="button danger ghost" type="submit">Remove</button>
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
                    <dd id="drawer-channel">.NET {{row.Channel}} · {{channelType}}</dd>
                    <dt>Included runtime</dt>
                    <dd id="drawer-runtime">{{Encode(row.IncludedRuntime ?? (row.Kind == "sdk" ? $"{row.Channel}.0" : "Included"))}}</dd>
                    <dt>Released</dt>
                    <dd id="drawer-released">{{Encode(row.ReleasedDate ?? "Official release")}}</dd>
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
            $"\"includedRuntime\": \"{EscapeJson(row.IncludedRuntime ?? (row.Kind == "sdk" ? $"{row.Channel}.0" : ""))}\"," +
            $"\"released\": \"{EscapeJson(row.ReleasedDate ?? "Official release")}\"," +
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

            // Find matching catalog entry for release date
            var catalogEntry = downloads.FirstOrDefault(d => d.Product == "SDK" && d.Version == sdk.Version);

            // Find newest patch in this channel
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
                Description: "Software development kit",
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
                IncludedRuntime: catalogEntry?.IncludedRuntimeVersion ?? $"{major}.0",
                ReleasedDate: catalogEntry?.ReleaseDate,
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
                    ReleasedDate: catalogEntry?.ReleaseDate,
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

    private static string SummaryCard(string label, string value, string description, string icon) =>
        $"<div class=\"summary-card\"><div class=\"summary-top\"><span>{Encode(label)}</span>{Icon(icon)}</div><strong class=\"summary-number\">{Encode(value)}</strong><span class=\"muted\">{Encode(description)}</span></div>";

    private static string RemoveAction(Row row, InstallationViewOptions options) => !row.CanRemove
        ? ""
        : $"<form method=\"post\" action=\"/installation/remove\"><input type=\"hidden\" name=\"version\" value=\"{Encode(row.Version)}\"><input type=\"hidden\" name=\"kind\" value=\"{row.Kind}\"><input type=\"hidden\" name=\"viewKind\" value=\"{options.Kind}\"><input type=\"hidden\" name=\"q\" value=\"{Encode(options.Query)}\"><input type=\"hidden\" name=\"sort\" value=\"{options.Sort}\"><input type=\"hidden\" name=\"direction\" value=\"{(options.Ascending ? "asc" : "desc")}\"><button class=\"button danger\" type=\"submit\">Remove</button></form>";

    private static string RenderNotice(string notice) => notice switch
    {
        "removed" => "<div class=\"action-notice\" role=\"status\">The selected .NET installation was removed.</div>",
        "remove-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The selected .NET installation could not be removed. Check the permissions and try again.</div>",
        _ => ""
    };

    private static string TypeLink(string label, string kind, InstallationViewOptions options) =>
        $"<a href=\"{Link(options with { Kind = kind })}\"{(options.Kind == kind ? " aria-current=\"page\"" : "")}>{label}</a>";

    private static string SortHeader(string label, string column, InstallationViewOptions options)
    {
        var active = options.Sort == column;
        var direction = active ? options.Ascending ? "ascending" : "descending" : "none";
        return $"<th scope=\"col\" aria-sort=\"{direction}\"><a href=\"{Link(options with { Sort = column, Ascending = !active || !options.Ascending })}\">{label}{(active ? options.Ascending ? " ↑" : " ↓" : "")}</a></th>";
    }

    private static string RefreshLink(InstallationViewOptions options, DateTimeOffset checkedAt) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}&refresh={checkedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}#inventory");

    private static string Link(InstallationViewOptions options) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}{(string.IsNullOrEmpty(options.Selected) ? "" : $"&selected={Uri.EscapeDataString(options.Selected)}")}#inventory");

    private static string RuntimeName(string name) => name switch
    {
        "Microsoft.NETCore.App" => ".NET Runtime",
        "Microsoft.AspNetCore.App" => "ASP.NET Core Runtime",
        "Microsoft.WindowsDesktop.App" => "Windows Desktop Runtime",
        _ => name
    };
}

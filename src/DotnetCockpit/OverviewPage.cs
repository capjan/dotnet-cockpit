using System.Globalization;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Setup;
using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal static class OverviewPage
{
    public static string Render(InstallationSnapshot snapshot, ToolsSnapshot tools, SetupSnapshot setup, SdkDownloadSnapshot sdkSnapshot, ActiveSdk? activeSdk = null, DateOnly? today = null)
    {
        var currentDate = today ?? SupportLifecycle.Today;
        var health = EnvironmentHealth.Evaluate(snapshot, sdkSnapshot, currentDate);
        var rows = InstallationPage.BuildRows(snapshot, sdkSnapshot, currentDate);
        var runtimeCount = snapshot.Runtimes.Select(r => r.Version).Distinct().Count();
        var issues = health.Count;
        var eolRuntimes = rows.Count(row => row.Kind == "runtime" && row.IsEol);
        var patchableSdks = rows.Count(row => row.Kind == "sdk" && row.HasPatch && !row.LatestPatchInstalled);
        var isHealthy = issues == 0 && health.SupportKnown;

        var newestStableSdk = snapshot.Sdks
            .Where(sdk => !sdk.Version.Contains('-'))
            .OrderByDescending(sdk => sdk.Version, SdkDownloadsSection.SdkVersionComparer.Instance)
            .FirstOrDefault()?.Version;

        var healthBadgeClass = isHealthy ? "ok" : "warn";
        var healthStatus = snapshot.Status == "error"
            ? "Not available"
            : isHealthy ? "Healthy" : issues == 0 ? "Partly checked" : "Attention needed";

        var actions = health.Actions.Select(action => $"""
            <div class="overview-action-item">
              <div class="action-icon {action.Severity}">{Icon(action.Icon, "sm")}</div>
              <div class="action-text">
                <strong>{Encode(action.Title)}</strong>
                <p class="muted">{Encode(action.Detail)}</p>
              </div>
              <div class="action-buttons">
                {string.Concat(action.Links.Select(link => $"<a class=\"{link.Class}\" href=\"{Encode(link.Href)}\"{(link.Download ? " download" : "")}>{(link.Icon is null ? "" : Icon(link.Icon, "xs"))} {Encode(link.Label)}</a>"))}
              </div>
            </div>
            """).ToArray();
        var actionsHtml = actions.Length > 0
            ? string.Join("\n", actions)
            : $"""<div class="overview-all-clear">{Icon("check", "sm")}<div><strong>{(health.SupportKnown ? "Nothing to do" : "No known actions")}</strong><p class="muted">{(health.SupportKnown ? "Every installed version is supported and fully patched." : "Support dates could not be loaded, so end-of-support risks are not listed. Refresh to try again.")}</p></div></div>""";

        var sdkBadge = patchableSdks > 0
            ? $"<span class=\"badge warn sm\">{patchableSdks} {(patchableSdks == 1 ? "patch" : "patches")}</span>"
            : "";

        var runtimeBadge = eolRuntimes > 0
            ? $"<span class=\"badge bad sm\">{eolRuntimes} end of life</span>"
            : "";

        var toolsAvailable = tools.Status == "ready";
        string ToolCount(ToolList list) => toolsAvailable && list.Error is null ? list.Tools.Length.ToString(CultureInfo.InvariantCulture) : "–";
        var toolsNote = toolsAvailable ? null : "Not available";

        var sdkInUse = activeSdk?.Version is { Length: > 0 } activeVersion
            ? $"<strong>{Encode(activeVersion)}</strong> <span class=\"muted\">· {(activeSdk.GlobalJsonPath is null ? "no global.json" : $"pinned by <code>{Encode(ShortenPath(activeSdk.GlobalJsonPath))}</code>")}</span>"
            : "<span class=\"muted\">Not available</span>";

        var manifestSummary = !tools.LocalManifestInitialized
            ? "<span class=\"muted\">No local manifest</span>"
            : tools.LocalManifestOwned
                ? "<strong>Local</strong> <span class=\"muted\">in .config/dotnet-tools.json</span>"
                : $"<strong>Inherited</strong> <span class=\"muted\">from parent .config</span>";

        var localToolPills = tools.Local.Tools.Length > 0
            ? string.Concat(tools.Local.Tools.Select(t => $"<a class=\"ws-tool-pill-link\" href=\"/tools?scope=local&amp;selected={Uri.EscapeDataString(t.PackageId)}\" title=\"Inspect {Encode(t.PackageId)} in .NET Tools\"><code class=\"ws-tool-pill\">{Encode(t.PackageId)} {Encode(t.Version)}</code></a>"))
            : "<span class=\"muted\">None declared</span>";

        return PageLayout.Render($$"""
            <div class="page-heading">
              <div>
                <h1>Overview</h1>
                <p class="muted intro">Here's the state of .NET on this machine.</p>
              </div>
              <div class="overview-header-actions">
                <a class="button" href="/tools?add=1">{{Icon("plus", "xs")}} Add tool</a>
                <a class="button primary" href="/?install=1">{{Icon("download", "xs")}} Install .NET</a>
              </div>
            </div>

            <section class="overview-hero-card" aria-label="System health and summary">
              <div class="hero-health-pane">
                <div class="hero-ring" style="--ring: {{health.RingGradient}}">
                  <div class="hero-ring-inner">
                    <strong>{{issues}}</strong>
                    <span>to review</span>
                  </div>
                </div>
                <div class="hero-health-copy">
                  <div class="hero-eyebrow-row">
                    <span class="sr-only">System Health</span>
                    <span class="badge {{healthBadgeClass}}">{{healthStatus}}</span>
                  </div>
                  <h2 class="hero-headline">{{Encode(health.Headline)}}</h2>
                  <div class="hero-meta-row">
                    <span>{{Icon("cpu", "xs")}} {{Encode(snapshot.OperatingSystem)}} · {{Encode(snapshot.Architecture.ToLowerInvariant())}}</span>
                    <span>{{Icon("code", "xs")}} {{(snapshot.ExecutablePath is { } hostPath ? $"<code>{Encode(hostPath)}</code>" : "No dotnet host on PATH")}}</span>
                    {{(newestStableSdk is null ? "" : $"<span>{Icon("shield", "xs")} Newest stable SDK {Encode(newestStableSdk)}</span>")}}
                  </div>
                </div>
              </div>

              <div class="hero-stats-grid" aria-label="Installation summary">
                <div class="hero-stat-cell">
                  <div class="stat-top">{{Icon("versions", "xs")}} <span>Installed SDKs</span></div>
                  <div class="stat-val-row">
                    <strong class="summary-number">{{snapshot.Sdks.Length.ToString(CultureInfo.InvariantCulture)}}</strong>
                    {{sdkBadge}}
                  </div>
                </div>
                <div class="hero-stat-cell">
                  <div class="stat-top">{{Icon("server", "xs")}} <span>Installed runtimes</span></div>
                  <div class="stat-val-row">
                    <strong class="summary-number">{{runtimeCount.ToString(CultureInfo.InvariantCulture)}}</strong>
                    {{runtimeBadge}}
                  </div>
                </div>
                <div class="hero-stat-cell">
                  <div class="stat-top">{{Icon("tools", "xs")}} <span>Global tools</span></div>
                  <div class="stat-val-row">
                    <strong class="summary-number">{{ToolCount(tools.Global)}}</strong>
                    <span class="muted text-xs">{{Encode(toolsNote ?? $"for {Environment.UserName}")}}</span>
                  </div>
                </div>
                <div class="hero-stat-cell">
                  <div class="stat-top">{{Icon("folder", "xs")}} <span>Local tools</span></div>
                  <div class="stat-val-row">
                    <strong class="summary-number">{{ToolCount(tools.Local)}}</strong>
                    <span class="muted text-xs">{{Encode(toolsNote ?? "this workspace")}}</span>
                  </div>
                </div>
              </div>
            </section>

            <section class="overview-main-grid" aria-label="Recommendations and workspace">
              <div class="overview-card actions-card">
                <div class="card-top">
                  <div>
                    <h2>Recommended actions</h2>
                    <p class="muted text-xs">Sorted by risk. Every change asks for confirmation first.</p>
                  </div>
                </div>
                <div class="action-list">
                  {{actionsHtml}}
                </div>
              </div>

              <div class="overview-side-stack">
                <div class="overview-card workspace-card">
                  <div class="ws-widget-head">
                    <div class="ws-icon-box">{{Icon("folder", "sm")}}</div>
                    <div class="ws-head-copy">
                      <h3>This workspace</h3>
                      <code>{{Encode(ShortenPath(tools.WorkingDirectory))}}</code>
                    </div>
                  </div>
                  <dl class="ws-dl">
                    <dt>SDK in use</dt>
                    <dd>{{sdkInUse}}</dd>
                    <dt>Tool manifest</dt>
                    <dd>{{manifestSummary}}</dd>
                    <dt>Local tools</dt>
                    <dd class="ws-tools-dd">{{localToolPills}}</dd>
                  </dl>
                </div>

                <div class="overview-card shortcuts-card">
                  <h3>Shortcuts</h3>
                  <div class="shortcuts-list">
                    <a class="shortcut-row" href="/?install=1">
                      <span>{{Icon("download", "xs")}} Install .NET</span>
                    </a>
                    <a class="shortcut-row" href="/tools?add=1">
                      <span>{{Icon("plus", "xs")}} Find a .NET tool on NuGet</span>
                      <kbd>T</kbd>
                    </a>
                    {{(setup.UninstallTool.Installed || setup.Status == "unsupported" ? "" : $"<a class=\"shortcut-row\" href=\"/setup#uninstall-tool-title\"><span>{Icon("trash", "xs")} Set up removal support</span></a>")}}
                    <a class="shortcut-row" href="/preflight">
                      <span>{{Icon("pulse", "xs")}} Pre-flight diagnostics</span>
                      <kbd>P</kbd>
                    </a>
                  </div>
                </div>
              </div>
            </section>

            <section class="inventory timeline-section" aria-labelledby="timeline-title">
              <div class="inventory-heading">
                <div>
                  <h2 id="timeline-title">Support Lifecycle Timeline</h2>
                  <p class="muted">Official Microsoft lifecycle dates. Ringed bars are installed, pale bars are available to install.</p>
                </div>
                <div class="tl-legend">
                  <span class="tl-leg-item"><i class="tl-dot lts"></i> LTS</span>
                  <span class="tl-leg-item"><i class="tl-dot sts"></i> STS</span>
                  <span class="tl-leg-item"><i class="tl-dot preview"></i> Preview</span>
                </div>
              </div>
              {{RenderTimeline(snapshot, sdkSnapshot, currentDate)}}
            </section>
            """, "/overview", ".NET Cockpit · Overview", issues, snapshot.InstallationCount, toolsAvailable ? tools.Global.Tools.Length + tools.Local.Tools.Length : null, inventorySignature: snapshot.Signature);
    }

    private static string RenderTimeline(InstallationSnapshot snapshot, SdkDownloadSnapshot sdkSnapshot, DateOnly today)
    {
        var channels = sdkSnapshot.Channels ?? [];
        if (channels.Length == 0)
            return """<div class="empty-state"><h3>Support dates are unavailable</h3><p class="muted">The official release metadata could not be loaded. Refresh to try again.</p></div>""";

        var installedVersions = snapshot.Sdks.Select(sdk => sdk.Version)
            .Concat(snapshot.Runtimes.Select(runtime => runtime.Version))
            .ToArray();
        var installed = installedVersions.Select(SupportLifecycle.ChannelVersion).ToHashSet();
        var newestSdkByChannel = snapshot.Sdks
            .GroupBy(sdk => SupportLifecycle.ChannelVersion(sdk.Version))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(sdk => sdk.Version, SdkDownloadsSection.SdkVersionComparer.Instance).First().Version);

        var visible = channels
            .Select(channel => (Channel: channel, Status: SupportLifecycle.Evaluate(channel, today)))
            .Where(entry => (!entry.Status.IsEol || installed.Contains(entry.Channel.Channel)) && entry.Channel.FirstRelease is not null)
            .OrderByDescending(entry => entry.Channel.FirstRelease)
            .ToArray();
        if (visible.Length == 0)
            return """<div class="empty-state"><h3>Support dates are unavailable</h3><p class="muted">The release metadata contains no dates for these channels.</p></div>""";

        var startYear = visible.Min(entry => entry.Channel.FirstRelease!.Value.Year);
        var endYear = Math.Max(visible.Max(entry => (entry.Channel.EndOfSupport ?? today).Year), today.Year) + 1;
        var rangeStart = new DateOnly(startYear, 1, 1).DayNumber;
        var rangeDays = (double)(new DateOnly(endYear, 1, 1).DayNumber - rangeStart);
        string Percent(DateOnly date) => (Math.Clamp((date.DayNumber - rangeStart) / rangeDays, 0, 1) * 100).ToString("0.##", CultureInfo.InvariantCulture);
        var todayFraction = ((today.DayNumber - rangeStart) / rangeDays).ToString("0.####", CultureInfo.InvariantCulture);

        var years = string.Concat(Enumerable.Range(startYear, endYear - startYear).Select(year => $"<span>{year}</span>"));
        var rows = string.Concat(visible.Select(entry =>
        {
            var (channel, status) = entry;
            var major = channel.Channel.EndsWith(".0", StringComparison.Ordinal) ? channel.Channel[..^2] : channel.Channel;
            var isPreview = status.State == SupportState.Preview;
            var isInstalled = installed.Contains(channel.Channel);
            var tag = isPreview ? "Preview" : channel.ReleaseType;
            var tagClass = isPreview ? "preview" : channel.ReleaseType.ToLowerInvariant();
            var barClass = status.IsEol ? "eol" : isPreview ? "preview" : tagClass;
            var left = channel.FirstRelease!.Value;
            var right = channel.EndOfSupport ?? today;
            var width = Math.Max(Math.Clamp((right.DayNumber - left.DayNumber) / rangeDays, 0, 1) * 100, 1).ToString("0.##", CultureInfo.InvariantCulture);
            var barLabel = newestSdkByChannel.TryGetValue(channel.Channel, out var sdk)
                ? $"{InstallationPage.ShortDisplayVersion(sdk)} installed"
                : isInstalled ? "Runtime installed" : "";
            var (statusClass, statusText) = status.State switch
            {
                SupportState.EndOfLife => ("bad", channel.EndOfSupport is { } end ? $"ended {end.Year}" : "ended"),
                SupportState.EndingSoon => ("warn", $"ends {SupportLifecycle.RelativeDays(status.DaysLeft ?? 0)}"),
                SupportState.Preview => ("muted", "preview"),
                _ => ("muted", channel.EndOfSupport is { } until ? $"until {until.ToString("MMM yyyy", CultureInfo.InvariantCulture)}" : "supported")
            };
            return $"""
                <div class="timeline-row {(isInstalled ? "" : "not-installed")}">
                  <div class="tl-channel">
                    <b>.NET {Encode(major)}</b>
                    <span class="channel-tag {tagClass}">{Encode(tag)}</span>
                  </div>
                  <div class="tl-track">
                    <div class="tl-bar {barClass} {(isInstalled ? "installed" : "")}" style="left: {Percent(left)}%; width: {width}%;" title="{Encode(status.Label)}">{Encode(barLabel)}</div>
                  </div>
                  <span class="tl-status {statusClass}">{Encode(statusText)}</span>
                </div>
                """;
        }));

        return $"""
            <div class="timeline-chart">
              <div class="tl-years">
                <span></span>
                <div class="tl-years-track">{years}</div>
                <span></span>
              </div>
              <div class="tl-rows">
                <div class="tl-today-marker" style="left: calc(130px + 16px + (100% - 272px) * {todayFraction});">
                  <span class="tl-today-pill">Today</span>
                </div>
                {rows}
              </div>
            </div>
            """;
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3) return path;
        return "../" + string.Join("/", parts[^3..]);
    }
}

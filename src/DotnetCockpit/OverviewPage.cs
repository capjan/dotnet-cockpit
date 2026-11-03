using System.Globalization;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Setup;
using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal static class OverviewPage
{
    public static string Render(InstallationSnapshot snapshot, ToolsSnapshot tools, SetupSnapshot setup, SdkDownloadSnapshot sdkSnapshot)
    {
        var runtimeCount = snapshot.Runtimes.Select(r => r.Version).Distinct().Count();
        var issues = snapshot.IssueCount;
        var eolSdks = snapshot.Sdks.Where(s => InstallationSnapshot.IsEol(s.Version)).ToArray();
        var eolRuntimes = snapshot.Runtimes.Where(r => InstallationSnapshot.IsEol(r.Version)).Select(r => r.Version).Distinct().ToArray();
        var eolCount = eolSdks.Length + eolRuntimes.Length;
        var hasEol = eolCount > 0;

        var downloads = sdkSnapshot.Downloads ?? [];
        var patchableSdks = snapshot.Sdks.Where(sdk =>
        {
            var major = sdk.Version.Split('.')[0];
            var latest = downloads
                .Where(d => d.Product == "SDK" && d.Channel == major && !d.Version.Contains("-preview") && !d.Version.Contains("-rc"))
                .OrderByDescending(d => d.Version, SdkDownloadsSection.SdkVersionComparer.Instance)
                .FirstOrDefault();
            return latest != null && SdkDownloadsSection.SdkVersionComparer.Instance.Compare(latest.Version, sdk.Version) > 0;
        }).ToArray();

        var latestInstalledSdk = snapshot.Sdks
            .OrderByDescending(s => s.Version, SdkDownloadsSection.SdkVersionComparer.Instance)
            .FirstOrDefault()?.Version;

        var installedChannels = snapshot.Sdks.Select(s => s.Version.Split('.')[0])
            .Concat(snapshot.Runtimes.Select(r => r.Version.Split('.')[0]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var healthBadgeClass = issues == 0 ? "ok" : "warn";
        var healthStatus = issues == 0 ? "Healthy" : "Attention needed";
        var healthHeadline = issues == 0
            ? "All installed versions are supported and up to date."
            : hasEol
                ? $"{(eolCount == 1 ? "One version is" : $"{eolCount} versions are")} out of support{(patchableSdks.Length > 0 ? $", {patchableSdks.Length} patch available" : "")}."
                : "Your .NET environment needs attention.";

        var actions = new List<string>();
        if (hasEol)
        {
            var eolChannels = snapshot.Sdks.Where(s => InstallationSnapshot.IsEol(s.Version)).Select(s => s.Version.Split('.')[0])
                .Concat(snapshot.Runtimes.Where(r => InstallationSnapshot.IsEol(r.Version)).Select(r => r.Version.Split('.')[0]))
                .Distinct()
                .OrderBy(c => c)
                .Select(c => $".NET {c}");
            var channelLabel = string.Join(", ", eolChannels);
            actions.Add($"""
                <div class="overview-action-item">
                  <div class="action-icon bad">{Icon("shield", "sm")}</div>
                  <div class="action-text">
                    <strong>{Encode(channelLabel)} is out of support since May 14, 2024</strong>
                    <p class="muted">{eolCount} installed {(eolCount == 1 ? "item no longer receives" : "items no longer receive")} security fixes.</p>
                  </div>
                  <div class="action-buttons">
                    <a class="button sm ghost" href="/">Details</a>
                    <a class="button sm danger-outline" href="/">{Icon("trash", "xs")} Remove {eolCount} {(eolCount == 1 ? "item" : "items")}</a>
                  </div>
                </div>
                """);
        }

        if (patchableSdks.Length > 0)
        {
            var firstSdk = patchableSdks[0];
            var major = firstSdk.Version.Split('.')[0];
            var latest = downloads
                .Where(d => d.Product == "SDK" && d.Channel == major && !d.Version.Contains("-preview") && !d.Version.Contains("-rc"))
                .OrderByDescending(d => d.Version, SdkDownloadsSection.SdkVersionComparer.Instance)
                .First();
            actions.Add($"""
                <div class="overview-action-item">
                  <div class="action-icon info">{Icon("download", "sm")}</div>
                  <div class="action-text">
                    <strong>Patch available: SDK {Encode(firstSdk.Version)} → {Encode(latest.Version)}</strong>
                    <p class="muted">Includes Runtime {Encode(latest.IncludedRuntimeVersion ?? $"{major}.0")} and latest security fixes.</p>
                  </div>
                  <div class="action-buttons">
                    <a class="button sm" href="{Encode(latest.Url)}" download>{Icon("download", "xs")} Download .pkg</a>
                  </div>
                </div>
                """);
        }

        if (!setup.UninstallTool.Installed)
        {
            actions.Add($"""
                <div class="overview-action-item">
                  <div class="action-icon warn">{Icon("warn", "sm")}</div>
                  <div class="action-text">
                    <strong>Removal support is not installed</strong>
                    <p class="muted">Install Microsoft's dotnet-core-uninstall to cleanly remove old SDKs and runtimes.</p>
                  </div>
                  <div class="action-buttons">
                    <a class="button sm" href="/setup">Set up now</a>
                  </div>
                </div>
                """);
        }

        if (snapshot.Sdks.Length == 0)
        {
            actions.Add($"""
                <div class="overview-action-item">
                  <div class="action-icon warn">{Icon("code", "sm")}</div>
                  <div class="action-text">
                    <strong>Install .NET SDK</strong>
                    <p class="muted">No SDK was found on this machine. An SDK is required to build applications.</p>
                  </div>
                  <div class="action-buttons">
                    <a class="button sm primary" href="/?tab=all-releases">Install SDK</a>
                  </div>
                </div>
                """);
        }

        actions.Add($"""
            <div class="overview-action-item">
              <div class="action-icon brand">{Icon("sparkle", "sm")}</div>
              <div class="action-text">
                <strong>Official .NET release catalog available</strong>
                <p class="muted">Browse LTS, STS, and preview installers side-by-side without affecting pinned projects.</p>
              </div>
              <div class="action-buttons">
                <a class="button sm ghost" href="/?tab=all-releases">View releases</a>
              </div>
            </div>
            """);

        var sdkBadge = patchableSdks.Length > 0
            ? $"<span class=\"badge warn sm\">{patchableSdks.Length} {(patchableSdks.Length == 1 ? "patch" : "patches")} available</span>"
            : "<span class=\"badge ok sm\">Up to date</span>";

        var runtimeBadge = eolRuntimes.Length > 0
            ? $"<span class=\"badge bad sm\">{eolRuntimes.Length} end of life</span>"
            : "<span class=\"badge ok sm\">Supported</span>";

        var manifestSummary = !tools.LocalManifestInitialized
            ? "<span class=\"muted\">No local manifest</span>"
            : tools.LocalManifestOwned
                ? "<strong>Local</strong> <span class=\"muted\">in .config/dotnet-tools.json</span>"
                : $"<strong>Inherited</strong> <span class=\"muted\">from parent .config</span>";

        var localToolPills = tools.Local.Tools.Length > 0
            ? string.Concat(tools.Local.Tools.Select(t => $"<code class=\"ws-tool-pill\">{Encode(t.PackageId)} {Encode(t.Version)}</code>"))
            : "<span class=\"muted\">None declared</span>";

        var hasNet10 = installedChannels.Contains("10");

        return PageLayout.Render($$"""
            <div class="page-heading">
              <div>
                <h1>Overview</h1>
                <p class="muted intro">Here's the state of .NET on this machine.</p>
              </div>
              <div class="overview-header-actions">
                <a class="button" href="/tools#tool-search-title">{{Icon("plus", "xs")}} Add tool</a>
                <a class="button primary" href="/?tab=all-releases">{{Icon("download", "xs")}} Install .NET</a>
                <a class="button icon ghost" href="/overview" title="Refresh" aria-label="Refresh">{{Icon("refresh", "sm")}}</a>
              </div>
            </div>

            <section class="overview-hero-card" aria-label="System health and summary">
              <div class="hero-health-pane">
                <div class="hero-ring {{(issues == 0 ? "ring-ok" : "ring-warn")}}" aria-hidden="true">
                  <div class="hero-ring-inner">
                    <strong>{{issues}}</strong>
                    <span>{{(issues == 0 ? "issues" : "to review")}}</span>
                  </div>
                </div>
                <div class="hero-health-copy">
                  <div class="hero-eyebrow-row">
                    <span class="sr-only">System Health</span>
                    <span class="badge {{healthBadgeClass}}">● {{healthStatus}}</span>
                  </div>
                  <h2 class="hero-headline">{{Encode(healthHeadline)}}</h2>
                  <div class="hero-meta-row">
                    <span>{{Icon("cpu", "xs")}} {{Encode(snapshot.OperatingSystem)}} · {{Encode(snapshot.Architecture.ToLowerInvariant())}}</span>
                    <span>{{Icon("code", "xs")}} <code>{{Encode(snapshot.ExecutablePath ?? "dotnet on PATH")}}</code></span>
                    <span>{{Icon("shield", "xs")}} {{(latestInstalledSdk != null ? $"Latest SDK {Encode(latestInstalledSdk)} installed" : "No SDK installed")}}</span>
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
                    <strong class="summary-number">{{tools.Global.Tools.Length.ToString(CultureInfo.InvariantCulture)}}</strong>
                    <span class="muted text-xs">for {{Encode(Environment.UserName)}}</span>
                  </div>
                </div>
                <div class="hero-stat-cell">
                  <div class="stat-top">{{Icon("folder", "xs")}} <span>Local tools</span></div>
                  <div class="stat-val-row">
                    <strong class="summary-number">{{tools.Local.Tools.Length.ToString(CultureInfo.InvariantCulture)}}</strong>
                    <span class="muted text-xs">this workspace</span>
                  </div>
                </div>
              </div>
            </section>

            <section class="overview-main-grid" aria-label="Recommendations and workspace">
              <div class="overview-card actions-card">
                <div class="card-top">
                  <div>
                    <h2>Recommended actions</h2>
                    <p class="muted text-xs">Sorted by risk. Every action asks for confirmation first.</p>
                  </div>
                  <span class="count-box">{{actions.Count}}</span>
                </div>
                <div class="action-list">
                  {{string.Join("\n", actions)}}
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
                    <dd><strong>{{Encode(latestInstalledSdk ?? "None")}}</strong> <span class="muted">· no global.json</span></dd>
                    <dt>Tool manifest</dt>
                    <dd>{{manifestSummary}}</dd>
                    <dt>Local tools</dt>
                    <dd class="ws-tools-dd">{{localToolPills}}</dd>
                  </dl>
                </div>

                <div class="overview-card shortcuts-card">
                  <h3>Shortcuts</h3>
                  <div class="shortcuts-list">
                    <a class="shortcut-row" href="/?tab=all-releases">
                      <span>{{Icon("download", "xs")}} Install the latest LTS SDK</span>
                      <span class="badge {{(hasNet10 ? "ok" : "brand")}} sm">{{(hasNet10 ? "Installed" : "Download")}}</span>
                    </a>
                    <a class="shortcut-row" href="/tools#tool-search-title">
                      <span>{{Icon("plus", "xs")}} Find a .NET tool on NuGet</span>
                      <kbd>T</kbd>
                    </a>
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
                  <p class="muted">Official Microsoft lifecycle. Ringed bars are installed on this machine.</p>
                </div>
                <div class="tl-legend">
                  <span class="tl-leg-item"><i class="tl-dot lts"></i> LTS</span>
                  <span class="tl-leg-item"><i class="tl-dot sts"></i> STS</span>
                  <span class="tl-leg-item"><i class="tl-dot preview"></i> Preview</span>
                </div>
              </div>
              <div class="timeline-chart">
                <div class="tl-years">
                  <span></span>
                  <div class="tl-years-track">
                    <span>2023</span>
                    <span>2024</span>
                    <span>2025</span>
                    <span>2026</span>
                    <span>2027</span>
                    <span>2028</span>
                    <div class="tl-today-marker" style="left: 64%;">
                      <span class="tl-today-pill">Today</span>
                    </div>
                  </div>
                  <span></span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <b>.NET 11</b>
                    <span class="channel-tag preview">Preview</span>
                  </div>
                  <div class="tl-track">
                    <div class="tl-bar preview" style="left: 55%; width: 32%;"></div>
                  </div>
                  <span class="tl-status muted">GA Nov 2026</span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <b>.NET 10</b>
                    <span class="channel-tag lts">LTS</span>
                  </div>
                  <div class="tl-track">
                    <div class="tl-bar lts {{(installedChannels.Contains("10") ? "installed" : "")}}" style="left: 48%; width: 48%;"></div>
                  </div>
                  <span class="tl-status ok">until Nov 2028</span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <b>.NET 9</b>
                    <span class="channel-tag sts">STS</span>
                  </div>
                  <div class="tl-track">
                    <div class="tl-bar sts {{(installedChannels.Contains("9") ? "installed" : "")}}" style="left: 31%; width: 34%;"></div>
                  </div>
                  <span class="tl-status warn">until Nov 2026</span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <b>.NET 8</b>
                    <span class="channel-tag lts">LTS</span>
                  </div>
                  <div class="tl-track">
                    <div class="tl-bar lts {{(installedChannels.Contains("8") ? "installed" : "")}}" style="left: 15%; width: 50%;"></div>
                  </div>
                  <span class="tl-status warn">until Nov 2026</span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <b>.NET 7</b>
                    <span class="channel-tag sts">STS</span>
                  </div>
                  <div class="tl-track">
                    <div class="tl-bar eol {{(installedChannels.Contains("7") ? "installed" : "")}}" style="left: 2%; width: 24%;"></div>
                  </div>
                  <span class="tl-status bad">ended 2024</span>
                </div>
              </div>
            </section>
            """, "/overview", ".NET Cockpit · Overview", issues);
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3) return path;
        return "../" + string.Join("/", parts[^3..]);
    }
}

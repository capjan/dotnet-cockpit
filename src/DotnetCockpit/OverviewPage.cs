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
        var hasEol = snapshot.Sdks.Any(s => InstallationSnapshot.IsEol(s.Version)) || snapshot.Runtimes.Any(r => InstallationSnapshot.IsEol(r.Version));
        var eolCount = snapshot.Sdks.Count(s => InstallationSnapshot.IsEol(s.Version)) + snapshot.Runtimes.Count(r => InstallationSnapshot.IsEol(r.Version));

        var healthScore = issues switch
        {
            0 => 100,
            1 => 85,
            2 => 70,
            _ => 55
        };

        var healthBadgeClass = issues == 0 ? "ok" : "warn";
        var healthStatus = issues == 0 ? "Healthy" : $"{issues} Attention Needed";
        var healthSummary = issues == 0 ? "All systems optimal" : "Review highlighted items";
        var healthDetail = issues == 0
            ? "Your .NET host, installed SDKs, and workspace are fully supported and up to date."
            : $"{issues} item{(issues == 1 ? " requires" : "s require")} attention: {(hasEol ? $"{eolCount} out-of-support version(s) detected." : "missing SDK or runtime.")}";

        var actions = new List<string>();
        if (hasEol)
        {
            actions.Add($"""
                <div class="overview-action-item">
                  <div class="action-icon warn">{Icon("warn", "sm")}</div>
                  <div class="action-text">
                    <strong>Remove EOL versions</strong>
                    <p class="muted">Older versions no longer receive security fixes. Use Cockpit to remove them cleanly.</p>
                  </div>
                  <a class="button sm ghost" href="/setup">Review EOL</a>
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
                  <a class="button sm primary" href="/setup#sdk-downloads-title">Install SDK</a>
                </div>
                """);
        }
        if (actions.Count == 0)
        {
            actions.Add($"""
                <div class="overview-action-item">
                  <div class="action-icon ok">{Icon("check", "sm")}</div>
                  <div class="action-text">
                    <strong>All recommendations completed</strong>
                    <p class="muted">Your .NET installations are healthy, secure, and up to date.</p>
                  </div>
                  <a class="button sm ghost" href="/">View Versions</a>
                </div>
                """);
        }

        return PageLayout.Render($$"""
            <div class="page-heading">
              <div>
                <h1>Overview</h1>
                <p class="muted intro">Health, recommendations and support lifecycle for your .NET environment.</p>
              </div>
              <a class="button primary" href="/overview">{{Icon("refresh")}} Refresh</a>
            </div>

            <section class="summary-grid" aria-label="Installation summary">
              {{SummaryCard("Installed SDKs", snapshot.Sdks.Length.ToString(CultureInfo.InvariantCulture), "Build and develop applications", "code")}}
              {{SummaryCard("Installed runtimes", runtimeCount.ToString(CultureInfo.InvariantCulture), "Run .NET applications", "server")}}
              {{SummaryCard("This machine", snapshot.Architecture.ToUpperInvariant(), snapshot.OperatingSystem, "desktop")}}
            </section>

            <section class="overview-grid" aria-label="System health and actions">
              <div class="overview-card health-card">
                <div class="card-top">
                  <span class="card-k">System Health</span>
                  <span class="badge {{healthBadgeClass}}">{{healthStatus}}</span>
                </div>
                <div class="gauge-display">
                  <div class="gauge-value">{{healthScore}}%</div>
                  <div class="gauge-label">{{healthSummary}}</div>
                </div>
                <p class="muted text-sm">{{healthDetail}}</p>
                <div class="card-foot-links">
                  <a class="button sm ghost" href="/preflight">{{Icon("pulse", "xs")}} Run diagnostics</a>
                </div>
              </div>

              <div class="overview-card actions-card">
                <div class="card-top">
                  <span class="card-k">Recommended Actions</span>
                  <span class="badge info">{{actions.Count}} available</span>
                </div>
                <div class="action-list">
                  {{string.Join("\n", actions)}}
                </div>
              </div>
            </section>

            <section class="inventory" aria-labelledby="timeline-title">
              <div class="inventory-heading">
                <div>
                  <h2 id="timeline-title">Support Lifecycle Timeline</h2>
                  <p class="muted">Official Microsoft support windows for current .NET release channels.</p>
                </div>
              </div>
              <div class="timeline-body">
                <div class="timeline-row">
                  <div class="tl-channel">
                    <span class="channel-avatar channel-avatar-10 sm">10</span>
                    <b>.NET 10</b>
                    <span class="channel-tag lts">LTS</span>
                  </div>
                  <div class="tl-bar-wrapper">
                    <div class="tl-bar lts" style="width: 85%;">Supported · Nov 2025 – Nov 2028</div>
                  </div>
                  <span class="badge ok sm">Active</span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <span class="channel-avatar channel-avatar-9 sm">9</span>
                    <b>.NET 9</b>
                    <span class="channel-tag sts">STS</span>
                  </div>
                  <div class="tl-bar-wrapper">
                    <div class="tl-bar sts" style="width: 50%;">Supported · Nov 2024 – May 2026</div>
                  </div>
                  <span class="badge ok sm">Active</span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <span class="channel-avatar channel-avatar-8 sm">8</span>
                    <b>.NET 8</b>
                    <span class="channel-tag lts">LTS</span>
                  </div>
                  <div class="tl-bar-wrapper">
                    <div class="tl-bar lts" style="width: 65%;">Supported · Nov 2023 – Nov 2026</div>
                  </div>
                  <span class="badge ok sm">Active</span>
                </div>

                <div class="timeline-row">
                  <div class="tl-channel">
                    <span class="channel-avatar channel-avatar-7 sm">7</span>
                    <b>.NET 7</b>
                    <span class="channel-tag sts">STS</span>
                  </div>
                  <div class="tl-bar-wrapper">
                    <div class="tl-bar eol" style="width: 30%;">Ended · May 14, 2024</div>
                  </div>
                  <span class="badge bad sm">End of Life</span>
                </div>
              </div>
            </section>
            """, "/overview", ".NET Cockpit · Overview", issues);
    }

    private static string SummaryCard(string label, string value, string description, string icon) =>
        $"<div class=\"summary-card\"><div class=\"summary-top\"><span>{Encode(label)}</span>{Icon(icon)}</div><strong class=\"summary-number\">{Encode(value)}</strong><span class=\"muted\">{Encode(description)}</span></div>";
}

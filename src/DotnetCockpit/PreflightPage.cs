using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Setup;
using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal static class PreflightPage
{
    public static string Render(InstallationSnapshot installation, ToolsSnapshot tools, SetupSnapshot setup, SdkDownloadSnapshot sdkDownloads)
    {
        var hostOk = installation.Status != "notFound" && !string.IsNullOrWhiteSpace(installation.ExecutablePath);
        var hostTitle = hostOk ? "dotnet host found" : "No dotnet host found";
        var hostDetail = hostOk ? $"{installation.ExecutablePath} · active dotnet host on PATH" : "The dotnet executable was not found on PATH. Install an SDK or configure PATH.";
        var hostBadge = hostOk
            ? "<span class=\"badge ok\">Ready</span>"
            : "<span class=\"badge bad\">Missing</span>";

        var sdks = installation.Sdks;
        var runtimes = installation.Runtimes;
        var channels = sdks.Select(s => s.Version.Split('.')[0])
            .Concat(runtimes.Select(r => r.Version.Split('.')[0]))
            .Distinct()
            .OrderByDescending(c => int.TryParse(c, out var v) ? v : 0)
            .ToArray();
        var instOk = sdks.Length > 0 || runtimes.Length > 0;
        var instTitle = instOk
            ? $"{sdks.Length} SDK{(sdks.Length == 1 ? "" : "s")} and {runtimes.Length} runtime{(runtimes.Length == 1 ? "" : "s")} detected"
            : "No .NET installations detected";
        var instDetail = instOk
            ? $"Grouped into {channels.Length} release channel{(channels.Length == 1 ? "" : "s")}: {string.Join(", ", channels.Select(c => $".NET {c}"))}"
            : "No installed SDKs or runtimes found. Official installers are available in Setup.";
        var instBadge = instOk
            ? "<span class=\"badge ok\">Ready</span>"
            : "<span class=\"badge warn\">Empty</span>";

        var catalogOk = sdkDownloads.Status == "ready";
        var catalogTitle = catalogOk ? "Official release catalog loaded" : "Release catalog offline";
        var catalogDetail = catalogOk
            ? $"{sdkDownloads.Downloads.Length} official packages available · lifecycle dates, patches & security CVEs"
            : Encode(sdkDownloads.Error ?? "Could not reach Microsoft release servers. Offline mode active.");
        var catalogBadge = catalogOk
            ? "<span class=\"badge ok\">Ready</span>"
            : "<span class=\"badge warn\">Offline</span>";

        var wsInitialized = tools.LocalManifestInitialized;
        var wsOwned = tools.LocalManifestOwned;
        var wsTitle = "Workspace detected";
        var wsDetail = wsInitialized
            ? $"{tools.WorkingDirectory} · {(wsOwned ? "Local manifest" : "Inherited manifest")} ({tools.Local.Tools.Length} local tool{(tools.Local.Tools.Length == 1 ? "" : "s")} declared)"
            : $"{tools.WorkingDirectory} · No local tool manifest";

        var uninstallInstalled = setup.UninstallTool.Installed;
        var uninstallTitle = uninstallInstalled ? "Uninstall tool is installed" : "Removal support is optional";
        var uninstallDetail = uninstallInstalled
            ? $"Version {Encode(setup.UninstallTool.Version ?? "detected")} at <code>{Encode(setup.UninstallTool.Path ?? "")}</code>"
            : "Optional. Cockpit uses Microsoft’s official dotnet-core-uninstall to safely remove installations.";
        var uninstallAction = uninstallInstalled
            ? "<span class=\"badge ok\">Ready</span>"
            : "<a class=\"button sm\" href=\"/setup\">Set up now</a>";

        var eolSdks = sdks.Where(s => s.Version.StartsWith("7.") || s.Version.StartsWith("6.") || s.Version.StartsWith("5.") || s.Version.StartsWith("3.") || s.Version.StartsWith("2.") || s.Version.StartsWith("1.")).ToArray();
        var eolRuntimes = runtimes.Where(r => r.Version.StartsWith("7.") || r.Version.StartsWith("6.") || r.Version.StartsWith("5.") || r.Version.StartsWith("3.") || r.Version.StartsWith("2.") || r.Version.StartsWith("1.")).ToArray();
        var eolCount = eolSdks.Length + eolRuntimes.Length;

        string attentionBanner;
        if (eolCount > 0)
        {
            attentionBanner = $"""
                <div class="card pf-attention" role="status">
                  <span class="pf-attention-icon warn">{Icon("warn", "sm")}</span>
                  <div>
                    <strong>Attention: {eolCount} installation{(eolCount == 1 ? " is" : "s are")} out of support (EOL)</strong>
                    <p class="muted">Outdated .NET versions no longer receive security updates. You can safely remove them via Installations or Setup.</p>
                  </div>
                  <a class="button sm danger" href="/#inventory">Review items</a>
                </div>
                """;
        }
        else if (!hostOk)
        {
            attentionBanner = $"""
                <div class="card pf-attention" role="alert">
                  <span class="pf-attention-icon bad">{Icon("warn", "sm")}</span>
                  <div>
                    <strong>No .NET host on PATH</strong>
                    <p class="muted">A .NET SDK or runtime installation is required to execute .NET applications and tools.</p>
                  </div>
                  <a class="button sm primary" href="/setup#sdk-downloads-title">Install SDK</a>
                </div>
                """;
        }
        else
        {
            attentionBanner = $"""
                <div class="card pf-attention" role="status">
                  <span class="pf-attention-icon ok">{Icon("check", "sm")}</span>
                  <div>
                    <strong>All systems are ready</strong>
                    <p class="muted">Your active .NET host, installations, and workspace are detected and healthy.</p>
                  </div>
                </div>
                """;
        }

        return PageLayout.Render($$"""
            <div class="preflight-container">
              <div class="pf">
                <div class="pf-brand">
                  <img src="/assets/logo.png" alt=".NET Cockpit">
                  <div>
                    <div class="muted" style="font-weight:600;font-size:13px">.NET Cockpit · First launch &amp; Diagnostics</div>
                    <h1 class="pf-title">Pre-flight <span class="grad-text">check</span></h1>
                  </div>
                </div>
                <p class="sub" style="font-size:15px;max-width:680px;margin:-8px 0 24px">Cockpit inspects the .NET environment on this machine. It only reads — nothing is installed or removed without your explicit confirmation.</p>

                <div class="card steps">
                  <div class="step">
                    <span class="st {{(hostOk ? "ok" : "warn")}}">{{Icon(hostOk ? "check" : "warn", "sm")}}</span>
                    <div><b>{{hostTitle}}</b><p><code>{{Encode(hostDetail)}}</code></p></div>
                    {{hostBadge}}
                  </div>

                  <div class="step">
                    <span class="st {{(instOk ? "ok" : "warn")}}">{{Icon(instOk ? "check" : "warn", "sm")}}</span>
                    <div><b>{{instTitle}}</b><p>{{Encode(instDetail)}}</p></div>
                    {{instBadge}}
                  </div>

                  <div class="step">
                    <span class="st {{(catalogOk ? "ok" : "warn")}}">{{Icon(catalogOk ? "check" : "warn", "sm")}}</span>
                    <div><b>{{catalogTitle}}</b><p>{{catalogDetail}}</p></div>
                    {{catalogBadge}}
                  </div>

                  <div class="step">
                    <span class="st ok">{{Icon("check", "sm")}}</span>
                    <div><b>{{wsTitle}}</b><p><code>{{Encode(wsDetail)}}</code></p></div>
                    <span class="badge ok">Ready</span>
                  </div>

                  <div class="step">
                    <span class="st {{(uninstallInstalled ? "ok" : "warn")}}">{{Icon(uninstallInstalled ? "check" : "warn", "sm")}}</span>
                    <div><b>{{uninstallTitle}}</b><p>{{uninstallDetail}}</p></div>
                    {{uninstallAction}}
                  </div>
                </div>

                {{attentionBanner}}

                <div class="pf-foot">
                  <a class="button primary lg" href="/">Open Cockpit {{Icon("chevron-right", "sm")}}</a>
                  <a class="button lg" href="/tools">View .NET Tools</a>
                  <span class="spacer"></span>
                  <a class="button ghost" href="/setup">Setup &amp; Downloads</a>
                </div>

                <div class="trust">
                  <span>{{Icon("lock", "xs")}} Runs only on 127.0.0.1</span>
                  <span>{{Icon("shield", "xs")}} Zero telemetry, zero accounts</span>
                  <span>{{Icon("code", "xs")}} Single native binary — no .NET runtime needed</span>
                  <span>{{Icon("cloud", "xs")}} Talks only to Microsoft, NuGet and GitHub</span>
                </div>
              </div>
            </div>
            """, "/preflight", "Pre-flight Check | .NET Cockpit");
    }
}

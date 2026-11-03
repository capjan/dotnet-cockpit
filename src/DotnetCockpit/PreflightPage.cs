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
        var hostDetail = hostOk
            ? $"{installation.ExecutablePath} · first dotnet on PATH"
            : "The dotnet executable was not found on PATH. Install an SDK or configure PATH.";
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
            ? "Microsoft release metadata · support dates, patches, security fixes"
            : Encode(sdkDownloads.Error ?? "Could not reach Microsoft release servers. Offline mode active.");
        var catalogBadge = catalogOk
            ? "<span class=\"badge ok\">Ready</span>"
            : "<span class=\"badge warn\">Offline</span>";

        var wsInitialized = tools.LocalManifestInitialized;
        var wsOwned = tools.LocalManifestOwned;
        var wsTitle = "Workspace detected";
        var shortenedWsPath = ShortenPath(tools.WorkingDirectory);
        var wsDetail = wsInitialized
            ? $"<code>{Encode(shortenedWsPath)}</code> · {(wsOwned ? $"{tools.Local.Tools.Length} local tool declared" : $"{tools.Local.Tools.Length} local tool from an inherited manifest")}"
            : $"<code>{Encode(shortenedWsPath)}</code> · No local tool manifest";

        var uninstallInstalled = setup.UninstallTool.Installed;
        var uninstallTitle = uninstallInstalled ? "Uninstall tool is installed" : "Removal support is not installed";
        var uninstallDetail = uninstallInstalled
            ? $"Ready to list and remove installations · version {Encode(setup.UninstallTool.Version ?? "detected")}"
            : "Optional. Cockpit needs Microsoft’s dotnet-core-uninstall to remove old versions for you.";
        var uninstallAction = uninstallInstalled
            ? "<span class=\"badge ok\">Ready</span>"
            : "<a class=\"button sm\" href=\"/setup\">Set up now</a>";

        var eolSdks = sdks.Where(s => s.Version.StartsWith("7.") || s.Version.StartsWith("6.") || s.Version.StartsWith("5.") || s.Version.StartsWith("3.") || s.Version.StartsWith("2.") || s.Version.StartsWith("1.")).ToArray();
        var eolRuntimes = runtimes.Where(r => r.Version.StartsWith("7.") || r.Version.StartsWith("6.") || r.Version.StartsWith("5.") || r.Version.StartsWith("3.") || r.Version.StartsWith("2.") || r.Version.StartsWith("1.")).ToArray();
        var eolCount = eolSdks.Length + eolRuntimes.Length;

        string attentionTitle;
        string attentionSubtitle;
        string attentionBadgeClass;
        if (eolCount > 0)
        {
            attentionBadgeClass = "warn";
            attentionTitle = $"First result: {eolCount} {(eolCount == 1 ? "thing needs" : "things need")} your attention";
            attentionSubtitle = ".NET 7 is out of support, and older versions no longer receive security fixes.";
        }
        else if (!hostOk)
        {
            attentionBadgeClass = "bad";
            attentionTitle = "First result: No .NET host on PATH";
            attentionSubtitle = "A .NET SDK or runtime installation is required to execute .NET applications.";
        }
        else
        {
            attentionBadgeClass = "ok";
            attentionTitle = "First result: All systems look healthy";
            attentionSubtitle = "Your active .NET host and workspace are ready for development.";
        }

        return $$"""
            <!doctype html>
            <html lang="en" data-theme="dark">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Pre-flight Check | .NET Cockpit</title>
              <link rel="icon" type="image/png" sizes="32x32" href="/assets/logo-32.png">
              <link rel="stylesheet" href="/assets/app.css">
              <script type="module" src="/assets/app.js"></script>
            </head>
            <body class="app-body preflight-body">
              <button id="theme-toggle" class="button icon ghost pf-theme-btn" type="button" aria-label="Switch theme" title="Switch theme" hidden>
                <span aria-hidden="true">◐</span>
              </button>

              <div class="preflight">
                <div class="pf">
                  <div class="pf-brand">
                    <img src="/assets/logo.png" alt=".NET Cockpit logo">
                    <div>
                      <div class="muted pf-kicker">.NET Cockpit · first launch</div>
                      <h1 class="pf-title">Pre-flight <span class="grad-text">check</span></h1>
                    </div>
                  </div>
                  <p class="sub pf-desc">Cockpit looks at the .NET installation on this machine. It only reads — nothing is installed or removed without your explicit confirmation.</p>

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
                      <div><b>{{wsTitle}}</b><p>{{wsDetail}}</p></div>
                      <span class="badge ok">Ready</span>
                    </div>

                    <div class="step">
                      <span class="st {{(uninstallInstalled ? "ok" : "warn")}}">{{Icon(uninstallInstalled ? "check" : "warn", "sm")}}</span>
                      <div><b>{{uninstallTitle}}</b><p>{{uninstallDetail}}</p></div>
                      {{uninstallAction}}
                    </div>
                  </div>

                  <div class="card pf-attention">
                    <span class="pf-attention-icon {{attentionBadgeClass}}">{{Icon("sparkle", "sm")}}</span>
                    <div class="pf-attention-text">
                      <b>{{attentionTitle}}</b>
                      <p class="muted">{{attentionSubtitle}}</p>
                    </div>
                  </div>

                  <div class="pf-foot">
                    <a class="button primary lg pf-open-btn" href="/">Open cockpit {{Icon("chevron-right", "sm")}}</a>
                    <a class="button lg ghost" href="/setup">Take a 60-second tour</a>
                    <span class="spacer"></span>
                    <label class="pf-skip-label">
                      <input type="checkbox" id="skip-preflight-checkbox" checked>
                      <span>Don’t show on next launch</span>
                    </label>
                  </div>

                  <div class="trust">
                    <span>{{Icon("lock", "xs")}} Runs only on 127.0.0.1</span>
                    <span>{{Icon("shield", "xs")}} No telemetry, no accounts</span>
                    <span>{{Icon("code", "xs")}} Single native binary — no .NET or Node required</span>
                    <span>{{Icon("cloud", "xs")}} Talks only to Microsoft, NuGet and GitHub</span>
                  </div>
                </div>
              </div>
            </body>
            </html>
            """;
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            path = "~" + path[home.Length..];
        }
        else if (path.StartsWith("/Volumes/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                path = "~/" + string.Join('/', parts.Skip(2));
            }
        }
        return path;
    }
}

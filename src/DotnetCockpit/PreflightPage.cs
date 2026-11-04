using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Setup;
using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal static class PreflightPage
{
    public static string Render(InstallationSnapshot installation, ToolsSnapshot tools, SetupSnapshot setup, SdkDownloadSnapshot sdkDownloads, bool showOnLaunch = false, DateOnly? today = null)
    {
        var currentDate = today ?? SupportLifecycle.Today;
        var rows = InstallationPage.BuildRows(installation, sdkDownloads, currentDate);
        var health = EnvironmentHealth.Evaluate(installation, sdkDownloads, currentDate);

        var hostOk = installation.Status != "notFound" && !string.IsNullOrWhiteSpace(installation.ExecutablePath);
        var hostTitle = hostOk ? "dotnet host found" : "No dotnet host found";
        var hostDetail = hostOk
            ? $"<code>{Encode(installation.ExecutablePath!)}</code> · first dotnet on PATH"
            : Encode("The dotnet executable was not found on PATH. Install an SDK or configure PATH.");
        var hostAction = hostOk
            ? "<span class=\"badge ok\">Ready</span>"
            : "<a class=\"button sm\" href=\"/?install=1\">Install .NET</a>";

        var sdks = installation.Sdks;
        var eolSdks = rows.Where(row => row.Kind == "sdk" && row.IsEol).ToArray();
        var sdkChannels = sdks.Select(s => s.Version.Split('.')[0]).Distinct().OrderByDescending(c => int.TryParse(c, out var v) ? v : 0).ToArray();
        var sdkOk = sdks.Length > 0;
        var sdkStepState = !sdkOk || eolSdks.Length > 0 ? "warn" : "ok";
        var sdkTitle = sdkOk
            ? $"{sdks.Length} SDK{(sdks.Length == 1 ? "" : "s")} detected"
            : "No .NET SDK installed";
        var sdkDetail = sdkOk
            ? $"Installed: {string.Join(", ", sdks.Select(s => s.Version))} ({string.Join(", ", sdkChannels.Select(c => $".NET {c}"))})"
            : "Required to build projects and use dotnet CLI tools.";
        var sdkAction = !sdkOk
            ? "<a class=\"button sm\" href=\"/?install=1\">Install .NET</a>"
            : eolSdks.Length > 0
                ? $"<span class=\"badge warn\">{eolSdks.Length} EOL</span>"
                : "<span class=\"badge ok\">Ready</span>";

        var runtimes = installation.Runtimes;
        var distinctRuntimeVersions = runtimes.Select(r => r.Version).Distinct().ToArray();
        var eolRuntimes = rows.Where(row => row.Kind == "runtime" && row.IsEol).ToArray();
        var runtimeOk = runtimes.Length > 0;
        var runtimeStepState = !runtimeOk || eolRuntimes.Length > 0 ? "warn" : "ok";
        var runtimeTitle = runtimeOk
            ? $"{distinctRuntimeVersions.Length} runtime{(distinctRuntimeVersions.Length == 1 ? "" : "s")} detected"
            : "No .NET runtime installed";
        var runtimeDetail = runtimeOk
            ? $"Installed: {string.Join(", ", distinctRuntimeVersions)} (ASP.NET Core & .NET Runtime)"
            : "Required to execute compiled .NET applications.";
        var runtimeAction = !runtimeOk
            ? "<a class=\"button sm\" href=\"/?install=1\">Install .NET</a>"
            : eolRuntimes.Length > 0
                ? $"<span class=\"badge warn\">{eolRuntimes.Length} EOL</span>"
                : "<span class=\"badge ok\">Ready</span>";

        var catalogOk = sdkDownloads.Status == "ready";
        var catalogTitle = catalogOk ? "Official release catalog loaded" : "Release catalog offline";
        var catalogDetail = catalogOk
            ? "Microsoft release metadata · support dates, patches, security fixes"
            : Encode(sdkDownloads.Error ?? "Could not reach Microsoft release servers. Support status cannot be checked.");
        var catalogBadge = catalogOk
            ? "<span class=\"badge ok\">Ready</span>"
            : "<span class=\"badge warn\">Offline</span>";

        var wsInitialized = tools.LocalManifestInitialized;
        var wsOwned = tools.LocalManifestOwned;
        var wsTitle = "Workspace detected";
        var shortenedWsPath = ShortenPath(tools.WorkingDirectory);
        var wsToolWord = tools.Local.Tools.Length == 1 ? "local tool" : "local tools";
        var wsDetail = wsInitialized
            ? $"<code>{Encode(shortenedWsPath)}</code> · {(wsOwned ? $"{tools.Local.Tools.Length} {wsToolWord} declared" : $"{tools.Local.Tools.Length} {wsToolWord} from an inherited manifest")}"
            : $"<code>{Encode(shortenedWsPath)}</code> · No local tool manifest";

        var uninstallInstalled = setup.UninstallTool.Installed;
        var uninstallTitle = uninstallInstalled ? "Uninstall tool is installed" : "Removal support is not installed";
        var uninstallDetail = uninstallInstalled
            ? $"Ready to list and remove installations · version {Encode(setup.UninstallTool.Version ?? "detected")}"
            : "Optional. Cockpit needs Microsoft’s dotnet-core-uninstall to remove old versions for you.";
        var uninstallAction = uninstallInstalled
            ? "<span class=\"badge ok\">Ready</span>"
            : "<a class=\"button sm\" href=\"/setup\">Set up now</a>";

        var issues = health.Count;
        var attentionBadgeClass = issues > 0 || !health.SupportKnown ? "warn" : "ok";
        var attentionTitle = issues switch
        {
            0 when health.SupportKnown => "All systems look healthy",
            0 => "Support status could not be checked",
            1 => "1 thing needs your attention",
            _ => $"{issues} things need your attention"
        };
        var attentionSubtitle = Encode(health.Headline);

        return $$"""
            <!doctype html>
            <html lang="en" data-theme="{{SettingsTheme}}" data-palette="{{PageLayout.CurrentSettings().Palette}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="color-scheme" content="light dark">
              <title>Pre-flight Check | .NET Cockpit</title>
              <link rel="icon" type="image/png" sizes="32x32" href="/assets/logo-32.png">
              <link rel="stylesheet" href="/assets/app.css">
              <script type="module" src="/assets/app.js"></script>
            </head>
            <body class="app-body preflight-body">
              {{PageLayout.ThemeSwitch(extraClass: "pf-theme-btn")}}

              <div class="preflight">
                <div class="pf">
                  <div class="pf-brand">
                    <img src="/assets/logo.png" alt=".NET Cockpit logo">
                    <div>
                      <div class="muted pf-kicker">.NET Cockpit</div>
                      <h1 class="pf-title">Pre-flight <span class="grad-text">check</span></h1>
                    </div>
                  </div>
                  <p class="sub pf-desc">Cockpit looks at the .NET installation on this machine. It only reads — nothing is installed or removed without your explicit confirmation.</p>

                  <div class="card steps">
                    <div class="step">
                      <span class="st {{(hostOk ? "ok" : "warn")}}">{{Icon(hostOk ? "check" : "warn", "sm")}}</span>
                      <div><b>{{hostTitle}}</b><p>{{hostDetail}}</p></div>
                      {{hostAction}}
                    </div>

                    <div class="step">
                      <span class="st {{sdkStepState}}">{{Icon(sdkStepState == "ok" ? "check" : "warn", "sm")}}</span>
                      <div><b>{{sdkTitle}}</b><p>{{Encode(sdkDetail)}}</p></div>
                      {{sdkAction}}
                    </div>

                    <div class="step">
                      <span class="st {{runtimeStepState}}">{{Icon(runtimeStepState == "ok" ? "check" : "warn", "sm")}}</span>
                      <div><b>{{runtimeTitle}}</b><p>{{Encode(runtimeDetail)}}</p></div>
                      {{runtimeAction}}
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

                  <form method="post" action="/settings/preflight" class="pf-foot">
                    <button class="button primary lg pf-open-btn" type="submit">Open cockpit {{Icon("chevron-right", "sm")}}</button>
                    <span class="spacer"></span>
                    <label class="pf-skip-label">
                      <input type="checkbox" id="skip-preflight-checkbox" name="skip" value="true"{{(showOnLaunch ? "" : " checked")}}>
                      <span>Don’t show on next launch</span>
                    </label>
                  </form>

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

    private static string SettingsTheme => PageLayout.CurrentSettings().Theme;

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

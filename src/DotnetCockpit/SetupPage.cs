using System.Globalization;
using DotnetCockpit.Setup;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Installation;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record SetupViewOptions(string Notice = "", string Query = "", string Product = "sdk", bool IncludePreview = false, bool IncludeRc = false, bool SupportedOnly = false, bool LatestThree = false, bool IncludeLts = true, bool IncludeSts = true)
{
    public static SetupViewOptions From(IQueryCollection query)
    {
        var lifecycle = query["lifecycle"];
        var hasLifecycleFilter = query["lifecycle-filter"].ToString() == "true";
        var includePreviews = IsEnabled(query["previews"]);
        return new(
            query["notice"].ToString(),
            query["q"].ToString().Trim() is { Length: <= 100 } search ? search : "",
            query["product"].ToString() is "runtime" ? "runtime" : "sdk",
            includePreviews || IsEnabled(query["preview"]),
            includePreviews || IsEnabled(query["rc"]),
            IsEnabled(query["supported"]),
            IsEnabled(query["latest3"]),
            !hasLifecycleFilter || lifecycle.Any(value => string.Equals(value, "LTS", StringComparison.OrdinalIgnoreCase)),
            !hasLifecycleFilter || lifecycle.Any(value => string.Equals(value, "STS", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsEnabled(string? value) => value is "true" or "1" or "on";
}

internal static class SetupPage
{
    private const string DocumentationUrl = "https://learn.microsoft.com/en-us/dotnet/core/additional-tools/uninstall-tool-overview?pivots=os-macos";

    public static string Render(SetupSnapshot snapshot, SetupViewOptions options)
        => Render(snapshot, new SdkDownloadSnapshot("ready", []), InstallationSnapshot.Create("notFound", null), options);

    public static string Render(SetupSnapshot snapshot, SdkDownloadSnapshot sdkDownloads, InstallationSnapshot installation, SetupViewOptions options, int? toolsCount = null)
    {
        var settings = PageLayout.CurrentSettings();
        var content = snapshot.Status switch
        {
            "unsupported" => """
                <section class="setup-card empty-state" aria-labelledby="setup-unavailable-title">
                  <h2 id="setup-unavailable-title">This platform is not supported</h2>
                  <p class="muted">The .NET Uninstall Tool supports Windows and macOS. Linux is not supported by the official release.</p>
                </section>
                """,
            "error" => $"""
                <section class="setup-card empty-state error-state" role="alert">
                  <h2>Removal support information is unavailable</h2>
                  <p>{Encode(snapshot.Error ?? "The setup information could not be read.")}</p>
                  <a class="button" href="/setup">Try again</a>
                </section>
                """,
            _ => RenderToolCard(snapshot)
        };
        var issues = EnvironmentHealth.Evaluate(installation, sdkDownloads).Count;

        return PageLayout.Render($"""
            <div class="page-heading">
              <div>
                <h1>Settings</h1>
                <p class="muted intro">How Cockpit behaves on this machine.</p>
              </div>
              <div class="inventory-heading-actions">
                <a class="button primary" href="/?install=1">{Icon("download", "xs")} Install .NET</a>
              </div>
            </div>
            {RenderNotice(options.Notice)}
            <div class="settings-layout">
              <nav class="settings-rail" aria-label="Settings sections">
                <a class="settings-nav-item active" href="#settings-general">General</a>
                <a class="settings-nav-item" href="#uninstall-tool-title">Removal support</a>
                <a class="settings-nav-item" href="#settings-environment">Environment</a>
                <a class="settings-nav-item" href="#settings-privacy">Privacy &amp; network</a>
                <a class="settings-nav-item" href="#settings-about">About</a>
              </nav>

              <div class="settings-main">
                <section class="setup-card settings-card" id="settings-general" aria-labelledby="settings-general-title">
                  <div class="settings-card-head">
                    <h2 id="settings-general-title">General</h2>
                  </div>
                  <div class="settings-row">
                    <div>
                      <strong>Appearance</strong>
                      <p class="muted">Follows your operating system by default.</p>
                    </div>
                    {PageLayout.ThemeSwitch(showLabels: true)}
                  </div>
                  <div class="settings-row">
                    <div>
                      <strong>Show pre-flight check on launch</strong>
                      <p class="muted">A short health summary before the dashboard opens.</p>
                    </div>
                    <form method="post" action="/settings/preflight" class="settings-toggle-form" data-settings-toggle>
                      <input type="hidden" name="return" value="settings">
                      <label class="toggle-switch">
                        <input type="checkbox" id="settings-preflight-toggle" name="show" value="true"{(settings.PreflightOnLaunch ? " checked" : "")} aria-label="Show pre-flight check on launch">
                        <span class="toggle-slider"></span>
                      </label>
                      <noscript><button class="button sm" type="submit">Save</button></noscript>
                    </form>
                  </div>
                </section>

                {content}

                <section class="setup-card settings-card" id="settings-environment" aria-labelledby="settings-env-title">
                  <div class="settings-card-head">
                    <h2 id="settings-env-title">Environment</h2>
                  </div>
                  <dl class="settings-dl">
                    <dt>Active dotnet host</dt>
                    <dd><code>{Encode(installation.ExecutablePath ?? "Not detected on PATH")}</code></dd>
                    <dt>Platform</dt>
                    <dd><strong>{Encode(snapshot.OperatingSystem)} · {Encode(snapshot.Architecture)}</strong></dd>
                    <dt>Startup workspace</dt>
                    <dd><code>{Encode(Environment.CurrentDirectory)}</code></dd>
                    <dt>Official installers</dt>
                    <dd><a href="/?install=1">Browse .NET SDK &amp; Runtime catalog →</a></dd>
                  </dl>
                </section>

                <section class="setup-card settings-card" id="settings-privacy" aria-labelledby="settings-privacy-title">
                  <div class="settings-card-head">
                    <h2 id="settings-privacy-title">Privacy &amp; network</h2>
                    <p class="muted">Cockpit listens on <code>127.0.0.1</code> only. These are the only outbound requests it ever makes:</p>
                  </div>
                  <div class="endpoint-list">
                    <div class="endpoint-row">
                      <div class="endpoint-left">
                        <span class="endpoint-icon">{Icon("globe", "sm")}</span>
                        <div>
                          <strong>Microsoft release metadata</strong>
                          <p class="muted text-xs">Official release index, lifecycle dates, CVEs, SHA-512 checksums, and .pkg installers.</p>
                        </div>
                      </div>
                      <code class="muted">builds.dotnet.microsoft.com</code>
                    </div>
                    <div class="endpoint-row">
                      <div class="endpoint-left">
                        <span class="endpoint-icon">{Icon("search", "sm")}</span>
                        <div>
                          <strong>NuGet search</strong>
                          <p class="muted text-xs">Queried only when you search in "Add tool".</p>
                        </div>
                      </div>
                      <code class="muted">api.nuget.org</code>
                    </div>
                    <div class="endpoint-row">
                      <div class="endpoint-left">
                        <span class="endpoint-icon">{Icon("package", "sm")}</span>
                        <div>
                          <strong>GitHub Releases</strong>
                          <p class="muted text-xs">Official <code>dotnet/cli-lab</code> releases for Microsoft's <code>dotnet-core-uninstall</code> tool.</p>
                        </div>
                      </div>
                      <code class="muted">api.github.com</code>
                    </div>
                    <div class="endpoint-row">
                      <div class="endpoint-left">
                        <span class="endpoint-icon ok">{Icon("shieldok", "sm")}</span>
                        <div>
                          <strong>No telemetry, no CDN, no accounts</strong>
                          <p class="muted text-xs">All fonts, icons and scripts are bundled into the binary.</p>
                        </div>
                      </div>
                      <span class="badge ok sm">Enforced</span>
                    </div>
                  </div>
                </section>

                <section class="setup-card settings-card" id="settings-about" aria-labelledby="settings-about-title">
                  <div class="settings-card-head">
                    <h2 id="settings-about-title">About .NET Cockpit</h2>
                  </div>
                  <p class="scope-note muted">Choose a package in Versions → Install .NET to download it, then open the .pkg file in macOS Installer to complete installation. The SDK also installs the ASP.NET Core Runtime; Microsoft provides the standalone .NET Runtime as a separate package. The .NET Uninstall Tool uses official dotnet/cli-lab release assets. Review the <a href="{DocumentationUrl}">Microsoft documentation</a> for the tool's limitations and commands.</p>
                </section>
              </div>
            </div>
            """, "/setup", "Settings | .NET Cockpit", issues, installation.InstallationCount, toolsCount, inventorySignature: installation.Signature);
    }

    private static string RenderToolCard(SetupSnapshot snapshot)
    {
        var installed = snapshot.UninstallTool.Installed;
        var headerActions = RenderHeaderActions(snapshot);
        var details = installed
            ? RenderInstalledDetails(snapshot)
            : RenderInstallDetails(snapshot);

        return $"""
            <section class="setup-card settings-card" aria-labelledby="uninstall-tool-title">
              <div class="setup-card-heading">
                <div class="setup-title">
                  <span class="setup-icon">{Icon("trash")}</span>
                  <div>
                    <div class="removal-support-title-row">
                      <h2 id="uninstall-tool-title">Removal support · .NET Uninstall Tool</h2>
                      {(installed ? $"<span class=\"badge ok sm\">{(snapshot.LatestVersion is null || IsUpdateAvailable(snapshot.UninstallTool.Version, snapshot.LatestVersion) ? "Installed" : "Up to date")}</span>" : "")}
                    </div>
                    <p class="muted">Microsoft's <code>dotnet-core-uninstall</code> lets Cockpit remove SDKs and runtimes that were installed by supported installers.</p>
                  </div>
                </div>
                {headerActions}
              </div>
              {details}
              <div class="remove-modal-notice setup-admin-box">
                <span class="notice-lock">{Icon("lock", "sm")}</span>
                <div>
                  <strong>Administrator approval</strong>
                  <p class="muted">Installing, updating and removing this tool — and removing .NET versions with it — asks for your {Encode(snapshot.OperatingSystem)} administrator password.</p>
                </div>
              </div>
              {RenderReleaseList(snapshot)}
              <div class="setup-note"><strong>{Encode(snapshot.OperatingSystem)} · {Encode(snapshot.Architecture)}</strong><span class="muted">The tool requires administrator privileges when it removes .NET installations.</span></div>
            </section>
            """;
    }

    private static string RenderHeaderActions(SetupSnapshot snapshot)
    {
        if (snapshot.UninstallTool.Installed)
        {
            if (snapshot.UninstallTool.Scope != "global") return "";
            var update = IsUpdateAvailable(snapshot.UninstallTool.Version, snapshot.LatestVersion)
                ? ConfirmActions.Trigger(ConfirmActions.SetupUpdate(snapshot.LatestVersion), $"<button class=\"button primary\" type=\"submit\">Update to {Encode(snapshot.LatestVersion!)}</button>")
                : "";
            return $"<div class=\"setup-card-actions\">{update}{ConfirmActions.Trigger(ConfirmActions.SetupUninstall(), "<button class=\"button remove\" type=\"submit\" aria-label=\"Remove .NET Uninstall Tool\" title=\"Remove .NET Uninstall Tool\">Remove</button>")}</div>";
        }

        return $"<div class=\"setup-card-actions\"><span class=\"badge sm\">Not installed</span>{RenderInstallControl(snapshot)}</div>";
    }

    private static string RenderInstallControl(SetupSnapshot snapshot)
    {
        if (snapshot.AvailableReleases is null) return "";
        var latestLabel = snapshot.LatestVersion is null ? "Latest" : $"Latest ({Encode(snapshot.LatestVersion)})";
        var options = $"<option value=\"latest\">{latestLabel}</option>" + string.Join("", snapshot.AvailableReleases
            .Where(release => !string.Equals(release.Version, snapshot.LatestVersion, StringComparison.OrdinalIgnoreCase))
            .Select(release => $"<option value=\"{Encode(release.Version)}\">{Encode(release.Version)}</option>"));
        return ConfirmActions.Trigger(ConfirmActions.SetupInstall(null, snapshot.GlobalInstallPath), $"<label class=\"setup-version-picker\" for=\"setup-version\"><span class=\"visually-hidden\">Version</span><select id=\"setup-version\" name=\"version\">{options}</select></label><button class=\"button primary setup-install-button\" type=\"submit\">{Icon("download", "xs")} Install</button>", "setup-version-form");
    }

    private static string RenderInstallDetails(SetupSnapshot snapshot)
    {
        if (snapshot.AvailableReleases is null)
            return "<div class=\"setup-install-summary\"><p class=\"muted\" role=\"alert\">Available releases could not be loaded. Refresh to try again.</p></div>";

        return $"""
                <div class="setup-install-summary">
                  <div><strong>Install globally</strong><p class="muted">Choose a release above. It will be available to all users at <code>{Encode(snapshot.GlobalInstallPath)}</code>. Administrator approval is required.</p></div>
                </div>
                """;
    }

    private static string RenderInstalledDetails(SetupSnapshot snapshot)
    {
        var installation = snapshot.UninstallTool;
        var version = installation.Version is null
            ? "Version could not be read from the executable."
            : $"Installed version <code>{Encode(installation.Version)}</code>.";
        var latest = snapshot.LatestVersion is null
            ? Encode(snapshot.ReleaseListError ?? "The latest release could not be checked right now.")
            : $"Latest official release <a href=\"{Encode(snapshot.LatestReleaseUrl ?? DocumentationUrl)}\"><code>{Encode(snapshot.LatestVersion)}</code></a>.";
        var actions = installation.Scope == "global" ? "" : "<span class=\"muted\">This installation is outside the managed global location and must be removed manually.</span>";

        return $"""
                <div class="setup-installed" role="status">
                  <dl class="settings-dl">
                    <dt>Installed version</dt>
                    <dd><span class="sr-only">{version}</span><strong><code>{Encode(installation.Version ?? "Unknown")}</code></strong> <span class="muted">· {latest}</span></dd>
                    <dt>Location</dt>
                    <dd><code>{Encode(installation.Path ?? "")}</code> <span class="badge sm">{Encode(installation.Scope ?? "path")}</span> <span class="sr-only">Detected at <code>{Encode(installation.Path ?? "")}</code> ({Encode(installation.Scope ?? "path")} installation).</span></dd>
                    <dt>Source</dt>
                    <dd><a href="https://github.com/dotnet/cli-lab/releases" target="_blank" rel="noopener">github.com/dotnet/cli-lab/releases ↗</a></dd>
                    <dt>Status</dt>
                    <dd><span class="setup-ready">{Icon("check", "xs")} Ready to list and remove .NET Installations</span></dd>
                  </dl>
                  {actions}
                </div>
                """;
    }

    private static string RenderReleaseList(SetupSnapshot snapshot)
    {
        if (snapshot.AvailableReleases is null)
            return "<div class=\"setup-release-list\"><p class=\"muted\" role=\"alert\">Available releases are currently unavailable.</p></div>";

        var releases = string.Join("", snapshot.AvailableReleases.Select(release =>
            $"<li><a href=\"{Encode(release.Url)}\">{Encode(release.Version)}</a>{(string.IsNullOrWhiteSpace(release.PublishedAt) ? "" : $"<span class=\"muted\">{Encode(FormatReleaseDate(release.PublishedAt!))}</span>")}</li>"));
        return $"<div class=\"setup-release-list\"><strong>Available releases</strong><ul>{releases}</ul></div>";
    }

    private static string FormatReleaseDate(string raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : raw;

    private static bool IsUpdateAvailable(string? installedVersion, string? latestVersion) =>
        Version.TryParse(installedVersion, out var installed) && Version.TryParse(latestVersion, out var latest) && latest > installed;

    private static string RenderNotice(string notice) => notice switch
    {
        "installed" => SuccessToast("The .NET Uninstall Tool was installed."),
        "failed" => "<div class=\"action-notice error-state\" role=\"alert\">The .NET Uninstall Tool could not be installed. Check the network connection and permissions, then try again.</div>",
        "updated" => SuccessToast("The .NET Uninstall Tool was updated."),
        "update-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The .NET Uninstall Tool could not be updated. Check the network connection and permissions, then try again.</div>",
        "uninstalled" => SuccessToast("The .NET Uninstall Tool was removed."),
        "uninstall-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The .NET Uninstall Tool could not be uninstalled. Check the permissions and try again.</div>",
        _ => ""
    };
}

using DotnetCockpit.Setup;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record SetupViewOptions(string Notice = "")
{
    public static SetupViewOptions From(IQueryCollection query) => new(query["notice"].ToString());
}

internal static class SetupPage
{
    private const string DocumentationUrl = "https://learn.microsoft.com/en-us/dotnet/core/additional-tools/uninstall-tool-overview?pivots=os-macos";

    public static string Render(SetupSnapshot snapshot, SetupViewOptions options)
    {
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
                  <h2>Setup information is unavailable</h2>
                  <p>{Encode(snapshot.Error ?? "The setup information could not be read.")}</p>
                  <a class="button" href="/setup">Try again</a>
                </section>
                """,
            _ => RenderToolCard(snapshot)
        };

        return PageLayout.Render($"""
            <div class="breadcrumb">Environment <span>/</span> Setup</div>
            <div class="page-heading">
              <div><h1>Setup</h1><p class="muted intro">Prepare the global tools used to manage your local .NET installation.</p></div>
              <a class="button primary" href="/setup">Refresh</a>
            </div>
            {RenderNotice(options.Notice)}
            {content}
            <p class="scope-note muted">Downloads use the official dotnet/cli-lab release assets. Review the <a href="{DocumentationUrl}">Microsoft documentation</a> for the tool's limitations and commands.</p>
            """, "/setup", "Setup | .NET Cockpit");
    }

    private static string RenderToolCard(SetupSnapshot snapshot)
    {
        var installed = snapshot.UninstallTool.Installed;
        var status = installed
            ? "<fluent-badge appearance=\"tint\" color=\"success\">Installed</fluent-badge>"
            : "<fluent-badge appearance=\"outline\" color=\"neutral\">Not installed</fluent-badge>";
        var details = installed
            ? RenderInstalledDetails(snapshot)
            : $"""
                <div class="setup-install-options">
                  <div class="setup-install-option">
                    <div><strong>Install globally</strong><p class="muted">Available to all users at <code>{Encode(snapshot.GlobalInstallPath)}</code>. Administrator approval is required.</p></div>
                    <form method="post" action="/setup/install"><button class="button primary" type="submit">Install globally</button></form>
                  </div>
                </div>
                """;

        return $"""
            <section class="setup-card" aria-labelledby="uninstall-tool-title">
              <div class="setup-card-heading">
                <div class="setup-title"><span class="setup-icon">{Icon("settings")}</span><div><h2 id="uninstall-tool-title">.NET Uninstall Tool</h2><p class="muted">Remove SDKs and runtimes that were installed by supported installers.</p></div></div>
                {status}
              </div>
              {details}
              <div class="setup-note"><strong>{Encode(snapshot.OperatingSystem)} · {Encode(snapshot.Architecture)}</strong><span class="muted">The tool requires administrator privileges when it removes .NET installations.</span></div>
            </section>
            """;
    }

    private static string RenderInstalledDetails(SetupSnapshot snapshot)
    {
        var installation = snapshot.UninstallTool;
        var version = installation.Version is null
            ? "Version could not be read from the executable."
            : $"Installed version <code>{Encode(installation.Version)}</code>.";
        var latest = snapshot.LatestVersion is null
            ? Encode(snapshot.UpdateCheckError ?? "The latest release could not be checked right now.")
            : $"Latest official release <code>{Encode(snapshot.LatestVersion)}</code>.";
        var update = IsUpdateAvailable(installation.Version, snapshot.LatestVersion)
            ? $"<form method=\"post\" action=\"/setup/update\"><button class=\"button primary\" type=\"submit\">Update to {Encode(snapshot.LatestVersion!)}</button></form>"
            : "";
        var actions = installation.Scope == "global"
            ? $"<div class=\"setup-installed-actions\">{update}<form method=\"post\" action=\"/setup/uninstall\"><button class=\"button danger\" type=\"submit\">Uninstall globally</button></form></div>"
            : "<span class=\"muted\">This installation is outside the managed global location and must be removed manually.</span>";

        return $"""
                <div class="setup-installed" role="status">
                  <strong>Ready to manage installed .NET versions</strong>
                  <span class="muted">Detected at <code>{Encode(installation.Path ?? "")}</code> ({Encode(installation.Scope ?? "path")} installation).</span>
                  <span class="muted">{version} {latest}</span>
                  {actions}
                </div>
                """;
    }

    private static bool IsUpdateAvailable(string? installedVersion, string? latestVersion) =>
        Version.TryParse(installedVersion, out var installed) && Version.TryParse(latestVersion, out var latest) && latest > installed;

    private static string RenderNotice(string notice) => notice switch
    {
        "installed" => "<div class=\"action-notice\" role=\"status\">The .NET Uninstall Tool was installed successfully.</div>",
        "failed" => "<div class=\"action-notice error-state\" role=\"alert\">The .NET Uninstall Tool could not be installed. Check the network connection and permissions, then try again.</div>",
        "updated" => "<div class=\"action-notice\" role=\"status\">The .NET Uninstall Tool was updated successfully.</div>",
        "update-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The .NET Uninstall Tool could not be updated. Check the network connection and permissions, then try again.</div>",
        "uninstalled" => "<div class=\"action-notice\" role=\"status\">The .NET Uninstall Tool was uninstalled successfully.</div>",
        "uninstall-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The .NET Uninstall Tool could not be uninstalled. Check the permissions and try again.</div>",
        _ => ""
    };
}

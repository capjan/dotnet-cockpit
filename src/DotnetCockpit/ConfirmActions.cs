using System.Runtime.InteropServices;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Installation;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

/// <summary>
/// Describes one change Cockpit can make. The same description drives the dialog (with JavaScript) and
/// the /confirm page (without it), so both show identical wording and the identical command.
/// </summary>
internal sealed record ConfirmSpec(
    string Action,
    string Title,
    string Body,
    string Command,
    string ConfirmLabel,
    string BusyLabel,
    string PostUrl,
    string CancelHref,
    (string Name, string Value)[] Fields,
    bool Danger = false,
    bool NeedsAdmin = false,
    string[]? Items = null);

internal static class ConfirmActions
{
    private static string UninstallPrefix => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet-core-uninstall" : "sudo dotnet-core-uninstall";

    public static string AdminNote => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "Windows will ask for administrator approval."
        : "macOS will ask for your administrator password.";

    public static string RemovalCommand(string version, string kind) =>
        $"{UninstallPrefix} remove {version} \\\n  --{(kind == "sdk" ? "sdk" : "runtime")} \\\n  --yes";

    public const string YesFlagNote = "The --yes flag skips the tool's own prompt, because you confirm here.";

    public static ConfirmSpec VersionRemove(InstallationPage.Row row, InstallationViewOptions? view = null) => new(
        "version-remove",
        $"Remove {row.Name} {row.DisplayVersion}?",
        $"Projects targeting net{row.Channel}.0 may stop building or running unless another compatible version is installed. {YesFlagNote}",
        RemovalCommand(row.Version, row.Kind),
        $"Remove {row.DisplayVersion}",
        $"Removing {row.DisplayVersion}…",
        "/installation/remove",
        "/",
        [("version", row.Version), ("kind", row.Kind), .. ViewFields(view)],
        Danger: true,
        NeedsAdmin: true);

    public static ConfirmSpec ChannelRemove(string channel, InstallationPage.Row[] rows)
    {
        var count = rows.Length;
        return new(
            "channel-remove",
            $"Remove .NET {channel}?",
            $"Projects targeting net{channel}.0 will no longer build on this machine. {YesFlagNote}",
            string.Join("\n\n", rows.Select(row => RemovalCommand(row.Version, row.Kind))),
            $"Remove {count} {(count == 1 ? "item" : "items")}",
            $"Removing .NET {channel}…",
            "/installation/remove",
            "/",
            [("channel", channel), .. rows.Select(row => ("items", $"{row.Version}|{row.Kind}"))],
            Danger: true,
            NeedsAdmin: true,
            Items: rows.Select(row => $"{row.Name} {row.DisplayVersion}").ToArray());
    }

    public static ConfirmSpec ToolUninstall(string packageId, string scope) => new(
        "tool-uninstall",
        $"Remove {packageId}?",
        scope == "local"
            ? "The tool is removed from this workspace's manifest."
            : "The tool is removed for your user account. Its commands stop working in every project.",
        $"dotnet tool uninstall --{Scope(scope)} {packageId}",
        "Remove tool",
        $"Removing {packageId}…",
        "/tools/uninstall",
        $"/tools?scope={Scope(scope)}",
        [("packageId", packageId), ("scope", Scope(scope))],
        Danger: true);

    public static ConfirmSpec ToolUpdate(string packageId, string scope, string? latestVersion) => new(
        "tool-update",
        $"Update {packageId}{(string.IsNullOrEmpty(latestVersion) ? "" : $" to {latestVersion}")}?",
        "The newest version from NuGet.org replaces the installed one.",
        $"dotnet tool update --{Scope(scope)} {packageId}",
        "Update tool",
        $"Updating {packageId}…",
        "/tools/update",
        $"/tools?scope={Scope(scope)}",
        [("packageId", packageId), ("scope", Scope(scope))]);

    public static ConfirmSpec ToolsUpdateAll(string scope, string query, string[] items)
    {
        var parsed = items.Select(item => item.Split('|', 2)).Where(parts => parts[0].Length > 0).ToArray();
        return new(
            "tools-update-all",
            $"Update {parsed.Length} {(parsed.Length == 1 ? "tool" : "tools")}?",
            "The newest versions from NuGet.org replace the installed ones.",
            string.Join("\n", parsed.Select(parts => $"dotnet tool update --{Scope(parts.Length > 1 ? parts[1] : scope)} {parts[0]}")),
            $"Update {parsed.Length} {(parsed.Length == 1 ? "tool" : "tools")}",
            "Updating tools…",
            "/tools/update-all",
            $"/tools?scope={scope}",
            [("scope", scope), ("q", query), .. items.Select(item => ("items", item))],
            Items: parsed.Select(parts => parts[0]).ToArray());
    }

    public static ConfirmSpec ManifestInit(string query) => new(
        "manifest-init",
        "Initialize local tools?",
        "Creates .config/dotnet-tools.json in this workspace, so tools can be installed for this project only.",
        "dotnet new tool-manifest --output .config",
        "Initialize",
        "Initializing…",
        "/tools/init",
        "/tools?scope=local",
        [("scope", "local"), ("q", query)]);

    public static ConfirmSpec ManifestRemove(string query) => new(
        "manifest-remove",
        "Remove local tools from this workspace?",
        "Deletes this workspace's .config/dotnet-tools.json. Cached packages are left untouched and inherited manifests are not affected.",
        "",
        "Remove local tools",
        "Removing local tools…",
        "/tools/remove-manifest",
        "/tools?scope=local",
        [("scope", "local"), ("q", query)],
        Danger: true);

    public static ConfirmSpec SetupInstall(string? version, string installPath) => new(
        "setup-install",
        "Install the .NET Uninstall Tool?",
        $"Downloads the official release from github.com/dotnet/cli-lab and installs it to {installPath}.",
        "",
        "Install",
        "Installing…",
        "/setup/install",
        "/setup",
        string.IsNullOrEmpty(version) ? [] : [("version", version)],
        NeedsAdmin: true);

    public static ConfirmSpec SetupUpdate(string? latestVersion) => new(
        "setup-update",
        $"Update the .NET Uninstall Tool{(string.IsNullOrEmpty(latestVersion) ? "" : $" to {latestVersion}")}?",
        "Downloads the official release from github.com/dotnet/cli-lab and replaces the installed tool.",
        "",
        "Update",
        "Updating…",
        "/setup/update",
        "/setup",
        [],
        NeedsAdmin: true);

    public static ConfirmSpec SetupUninstall() => new(
        "setup-uninstall",
        "Remove the .NET Uninstall Tool?",
        "Cockpit can no longer remove SDKs and runtimes for you until the tool is installed again. Installed .NET versions are not affected.",
        "",
        "Remove tool",
        "Removing…",
        "/setup/uninstall",
        "/setup",
        [],
        Danger: true,
        NeedsAdmin: true);

    /// <summary>A form that opens the dialog with JavaScript and the /confirm page without it.</summary>
    public static string Trigger(ConfirmSpec spec, string controlsHtml, string formClass = "") => $"""
        <form method="get" action="/confirm" class="{formClass}" data-confirm data-confirm-title="{Encode(spec.Title)}" data-confirm-body="{Encode(spec.Body)}" data-confirm-command="{Encode(spec.Command)}" data-confirm-label="{Encode(spec.ConfirmLabel)}" data-confirm-busy="{Encode(spec.BusyLabel)}" data-confirm-post="{Encode(spec.PostUrl)}" data-confirm-danger="{(spec.Danger ? "true" : "false")}" data-confirm-admin="{(spec.NeedsAdmin ? "true" : "false")}"><input type="hidden" name="action" value="{Encode(spec.Action)}">{string.Concat(spec.Fields.Select(field => $"<input type=\"hidden\" name=\"{Encode(field.Name)}\" value=\"{Encode(field.Value)}\">"))}{controlsHtml}</form>
        """;

    /// <summary>The shared dialog; app.js fills it from the data attributes of the submitted trigger.</summary>
    public static string Dialog => $"""
        <dialog id="confirm-dialog" class="remove-modal" closedby="any" aria-labelledby="confirm-dialog-title">
          <form method="post" id="confirm-dialog-form" class="remove-modal-inner">
            <div class="remove-modal-head">
              <div class="remove-modal-icon" id="confirm-dialog-icon">{Icon("trash", "sm")}</div>
              <div>
                <h2 id="confirm-dialog-title"></h2>
                <p id="confirm-dialog-body" class="muted"></p>
              </div>
            </div>
            <div id="confirm-dialog-fields" hidden></div>
            <div class="remove-modal-notice" id="confirm-dialog-admin" hidden>
              <span class="notice-lock">{Icon("lock", "sm")}</span>
              <div><strong>{AdminNote}</strong></div>
            </div>
            <div class="drawer-command-box" id="confirm-dialog-command-box" hidden>
              <span class="cmd-prompt">$</span>
              <code id="confirm-dialog-command"></code>
              <button class="button icon ghost copy-btn" id="confirm-dialog-copy" type="button" title="Copy command" aria-label="Copy command">{Icon("clipboard")}</button>
            </div>
            <div class="remove-modal-foot">
              <button type="button" class="button" id="confirm-dialog-cancel" autofocus>Cancel</button>
              <button type="submit" class="button" id="confirm-dialog-submit"></button>
            </div>
          </form>
        </dialog>
        """;

    /// <summary>The same confirmation as a page, for browsers without JavaScript and for links from other pages.</summary>
    public static string RenderPage(ConfirmSpec spec, int issueCount, int? versionsCount, int? toolsCount) => PageLayout.Render($"""
        <div class="page-heading"><div><h1>Confirm</h1></div></div>
        <section class="setup-card settings-card confirm-card" aria-labelledby="confirm-title">
          <h2 id="confirm-title">{Encode(spec.Title)}</h2>
          <p class="muted">{Encode(spec.Body)}</p>
          {(spec.Items is { Length: > 0 } items ? $"<ul class=\"confirm-items\">{string.Concat(items.Select(item => $"<li>{Encode(item)}</li>"))}</ul>" : "")}
          {(spec.NeedsAdmin ? $"<div class=\"remove-modal-notice\"><span class=\"notice-lock\">{Icon("lock", "sm")}</span><div><strong>{AdminNote}</strong></div></div>" : "")}
          {(spec.Command.Length > 0 ? $"<div class=\"drawer-command-box\"><span class=\"cmd-prompt\">$</span><code>{Encode(spec.Command)}</code><button class=\"button icon ghost copy-btn\" data-copy=\"{Encode(spec.Command)}\" type=\"button\" title=\"Copy command\" aria-label=\"Copy command\">{Icon("clipboard")}</button></div>" : "")}
          <form method="post" action="{Encode(spec.PostUrl)}" class="remove-modal-foot" data-busy="{Encode(spec.BusyLabel)}">
            {string.Concat(spec.Fields.Where(field => field.Name != "channel").Select(field => $"<input type=\"hidden\" name=\"{Encode(field.Name)}\" value=\"{Encode(field.Value)}\">"))}
            <a class="button" href="{Encode(spec.CancelHref)}">Cancel</a>
            <button type="submit" class="button {(spec.Danger ? "danger" : "primary")}">{Encode(spec.ConfirmLabel)}</button>
          </form>
        </section>
        """, "/confirm", "Confirm | .NET Cockpit", issueCount, versionsCount, toolsCount);

    private static string Scope(string scope) => scope == "local" ? "local" : "global";

    private static (string, string)[] ViewFields(InstallationViewOptions? view) => view is null
        ? []
        : [("viewKind", view.Kind), ("q", view.Query), ("sort", view.Sort), ("direction", view.Ascending ? "asc" : "desc")];
}

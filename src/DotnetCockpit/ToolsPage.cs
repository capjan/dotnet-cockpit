using System.Text;
using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record ToolsViewOptions(string Scope = "global", string SearchQuery = "", string Notice = "", string Selected = "")
{
    public static ToolsViewOptions From(IQueryCollection query) => new(
        query["scope"].ToString() is "local" ? "local" : query["scope"].ToString() is "all" ? "all" : "global",
        query["q"].ToString().Trim() is { Length: <= 100 } search ? search : "",
        query["notice"].ToString(),
        query["selected"].ToString().Trim() is { Length: <= 100 } selected ? selected : "");
}

internal static class ToolsPage
{
    public static string Render(ToolsSnapshot snapshot, ToolsViewOptions options) =>
        Render(snapshot, options, new(options.SearchQuery, []));

    public static string Render(ToolsSnapshot snapshot, ToolsViewOptions options, ToolSearch search)
    {
        var content = snapshot.Status switch
        {
            "notFound" => "<section class=\"inventory empty-state\"><h2>No .NET installation found</h2><p class=\"muted\">The dotnet command is not available on PATH. A .NET SDK is required to list tools.</p></section>",
            "sdkMissing" => "<section class=\"inventory empty-state\"><h2>A .NET SDK is required</h2><p class=\"muted\">The dotnet host is available, but no SDK is installed. Install an SDK to list global and local tools.</p></section>",
            "error" => $"<section class=\"inventory empty-state error-state\" role=\"alert\"><h2>Tool information is unavailable</h2><p>{Encode(snapshot.Error ?? "The tools could not be read.")}</p><a class=\"button\" href=\"/tools\">Try again</a></section>",
            _ => RenderTabs(snapshot, options)
        };
        var issues = snapshot.Status is "notFound" or "sdkMissing" or "error" ? 1 : 0;

        var manifestSubtitle = snapshot.LocalManifestInitialized
            ? $"Local tools come from {(snapshot.LocalManifestOwned ? "this workspace manifest" : "an inherited manifest")}: {Encode(snapshot.LocalManifestPath ?? ".config/dotnet-tools.json")}"
            : "Local tools use manifests from this directory and its parents.";

        var manifestBadge = snapshot.LocalManifestInitialized
            ? (snapshot.LocalManifestOwned ? "Local manifest" : "Inherited · read-only here")
            : "Current workspace";

        return PageLayout.Render($$"""
            <div class="page-heading">
              <div>
                <h1>.NET Tools</h1>
                <p class="muted intro">Global tools for your account and local tools declared for this workspace.</p>
              </div>
              <div class="tools-header-actions">
                <a class="button sm ghost refresh-btn" href="{{RefreshLink(options)}}">{{Icon("refresh", "xs")}} Refresh</a>
                {{(snapshot.Status == "ready" ? $"<button type=\"button\" class=\"button primary\" id=\"open-add-tool-btn\">{Icon("plus", "xs")} Add tool</button>" : "")}}
              </div>
            </div>
            <div class="working-directory">
              <div class="working-directory-heading">
                <span class="context-icon">{{Icon("folder")}}</span>
                <div>
                  <div class="ws-banner-line"><strong>Workspace</strong> <code>{{Encode(snapshot.WorkingDirectory)}}</code></div>
                  <span class="muted">{{manifestSubtitle}}</span>
                </div>
                <span class="context-badge">{{manifestBadge}}</span>
              </div>
            </div>
            {{RenderNotice(options.Notice)}}
            {{content}}
            {{(snapshot.Status == "ready" ? RenderSearch(snapshot, options, search) : "")}}
            {{(snapshot.Status == "ready" ? RenderAddToolSheet(snapshot, options, search) : "")}}
            <p class="scope-note muted">Local tools are declared in manifests for this working directory or its parents. This view does not check whether they have been restored — run <code>dotnet tool restore</code> in the workspace.</p>
            """, "/tools", ".NET Tools | .NET Cockpit", issues, toolsCount: snapshot.Global.Tools.Length + snapshot.Local.Tools.Length);
    }

    private static string RenderTabs(ToolsSnapshot snapshot, ToolsViewOptions options)
    {
        var isLocal = options.Scope == "local";
        var isAll = options.Scope == "all";
        var isGlobal = !isLocal && !isAll;
        var allCountHtml = (snapshot.Global.Error is null && snapshot.Local.Error is null)
            ? $"<span class=\"tool-tab-count summary-count\">{snapshot.Global.Tools.Length + snapshot.Local.Tools.Length}</span>"
            : "";
        var tabsHtml = $"""
            <div class="tool-tabs version-scope-pills" role="tablist" aria-label="Tool scope">
              <a class="tool-tab pill-badge {(isAll ? "active" : "")}" href="/tools?scope=all" role="tab" aria-selected="{isAll.ToString().ToLowerInvariant()}" aria-controls="all-tools-panel">All{allCountHtml}</a>
              {RenderTab("global", "Global", snapshot.Global, isGlobal)}
              {RenderTab("local", "Local", snapshot.Local, isLocal)}
            </div>
            """;
        if (isAll)
        {
            var combined = snapshot.Global.Tools.Concat(snapshot.Local.Tools).ToArray();
            var allList = new ToolList(combined, snapshot.Global.Error ?? snapshot.Local.Error);
            return RenderList("All tools", "all-tools", allList, false, snapshot.LocalManifestInitialized, snapshot.LocalManifestOwned, options, tabsHtml);
        }
        var selected = isLocal ? snapshot.Local : snapshot.Global;
        return RenderList(isLocal ? "Local tools" : "Global tools", isLocal ? "local-tools" : "global-tools", selected, isLocal, snapshot.LocalManifestInitialized, snapshot.LocalManifestOwned, options, tabsHtml);
    }

    private static string RenderTab(string scope, string label, ToolList list, bool selected)
    {
        var count = list.Error is null ? $"<span class=\"tool-tab-count summary-count\">{list.Tools.Length}</span>" : "";
        return $"<a class=\"tool-tab pill-badge {(selected ? "active" : "")}\" href=\"/tools?scope={scope}\" role=\"tab\" aria-selected=\"{selected.ToString().ToLowerInvariant()}\" aria-controls=\"{scope}-tools-panel\">{label}{count}</a>";
    }

    private static string RefreshLink(ToolsViewOptions options) =>
        options.Scope == "local" ? "/tools?scope=local" : "/tools";

    private static string RenderList(string title, string id, ToolList list, bool local, bool localInitialized, bool localOwned, ToolsViewOptions options, string tabsHtml)
    {
        var orderedTools = list.Tools.OrderBy(t => t.PackageId, StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedTool = !string.IsNullOrEmpty(options.Selected)
            ? orderedTools.FirstOrDefault(t => string.Equals(t.PackageId, options.Selected, StringComparison.OrdinalIgnoreCase))
            : null;

        var content = list.Error is not null
            ? $"<div class=\"empty-state error-state\" role=\"alert\"><h3>{title} could not be read</h3><p>{Encode(list.Error)}</p><a class=\"button\" href=\"{(options.Scope == "local" ? "/tools?scope=local" : "/tools")}\">Try again</a></div>"
            : orderedTools.Length == 0
                ? $"<div class=\"empty-state\"><h3>{(local ? (localInitialized ? "No local tools found" : "No local tools initialized") : "No global tools installed")}</h3><p class=\"muted\">{(local ? (localInitialized ? "No tools are declared in the tool manifests for this working directory." : "Initialize local tools to add a manifest in this working directory.") : "No global tools were reported for this user account.")}</p></div>"
                : RenderTable(orderedTools, local, title, localOwned, options, selectedTool);

        var filterInput = orderedTools.Length > 0
            ? $"<label class=\"filter-input\">{Icon("search", "xs")}<input type=\"search\" data-tools-table-filter placeholder=\"Filter {title.ToLowerInvariant()}...\" aria-label=\"Filter {title.ToLowerInvariant()}\"></label>"
            : "";
        var localAction = local ? RenderLocalManifestAction(localInitialized, localOwned, options) : "";
        var drawerHtml = RenderToolDrawer(selectedTool ?? orderedTools.FirstOrDefault(), selectedTool is not null, local, localOwned);
        var toolsJson = SerializeToolsJson(orderedTools, local, localOwned);

        return $$"""
            <section class="tool-panel-wrapper" role="tabpanel" aria-labelledby="{{id}}" id="{{id}}-panel">
              <div class="sr-only"><h2 id="{{id}}">{{title}}</h2></div>
              <div class="tools-toolbar">
                {{tabsHtml}}
                <div class="inventory-heading-actions">{{filterInput}}{{localAction}}</div>
              </div>
              <div class="tools-hub" id="tools-hub">
                <div class="tools-list">
                  <div class="inventory tool-panel">
                    {{content}}
                  </div>
                </div>
                {{drawerHtml}}
              </div>
              <script id="tools-data" type="application/json">
              {{toolsJson}}
              </script>
            </section>
            """;
    }

    private static string RenderTable(DotnetTool[] orderedTools, bool local, string title, bool localOwned, ToolsViewOptions options, DotnetTool? selectedTool) => $$"""
        <div class="table-scroll" role="region" aria-label="{{title}}" tabindex="0">
          <table class="tools-table" aria-label="{{title}}">
            <thead><tr><th scope="col">Tool</th><th scope="col">Command</th><th scope="col">Version</th><th scope="col">{{(local ? "Manifest" : "Scope")}}</th><th scope="col">Actions</th></tr></thead>
            <tbody>{{string.Concat(orderedTools.Select(tool =>
            {
                var isToolLocal = local || tool.Manifest is not null;
                var isSelected = selectedTool is not null && string.Equals(selectedTool.PackageId, tool.PackageId, StringComparison.OrdinalIgnoreCase);
                var monogram = GetMonogram(tool.PackageId);
                var colorIdx = GetColorIndex(tool.PackageId);
                var meta = ToolMetadataCatalog.Resolve(tool.PackageId, tool.Version, isToolLocal, tool.Manifest);
                var logoHtml = ToolMetadataCatalog.RenderLogo(meta, colorIdx, monogram);
                var scopeBadge = isToolLocal
                    ? $"<span class=\"tool-scope-pill local\">{Icon("folder", "xs")} {(localOwned ? "Local" : "Local · inherited")}</span>"
                    : $"<span class=\"tool-scope-pill global\">{Icon("tools", "xs")} Global</span>";
                var primaryCmd = tool.Commands.FirstOrDefault() ?? tool.PackageId;
                var copyCmd = isToolLocal ? $"dotnet {primaryCmd}" : primaryCmd;
                var cliEquiv = $"dotnet tool update {(isToolLocal ? "--local" : "--global")} {tool.PackageId}";
                var toolScopeStr = isToolLocal ? "local" : "global";
                var toolKey = $"{toolScopeStr}:{tool.PackageId}";
                var inspectHref = $"/tools?scope={Uri.EscapeDataString(options.Scope)}&selected={Uri.EscapeDataString(tool.PackageId)}";
                var versionCellHtml = meta.HasUpdate && !string.IsNullOrEmpty(meta.LatestVersion)
                    ? $"<div class=\"version-patch-flow\"><fluent-badge appearance=\"tint\" color=\"brand\" class=\"version-badge sr-only\">{Encode(tool.Version)}</fluent-badge><span class=\"v-current\">{Encode(tool.Version)}</span><span class=\"v-arrow\" title=\"Newer version available on NuGet.org\">→</span><span class=\"v-patch\" title=\"Latest on NuGet.org: {Encode(meta.LatestVersion)}\">{Encode(meta.LatestVersion)}</span></div>"
                    : $"<fluent-badge appearance=\"tint\" color=\"brand\" class=\"version-badge sr-only\">{Encode(tool.Version)}</fluent-badge><span class=\"v-current\">{Encode(tool.Version)}</span>";

                return $"""
                  <tr data-tool-scope="{toolScopeStr}" data-tool-row data-tool-id="{Encode(tool.PackageId)}" data-tool-key="{Encode(toolKey)}" data-search="{Encode($"{tool.PackageId} {tool.Version} {string.Join(' ', tool.Commands)} {tool.Manifest} {meta.Summary} {meta.Authors}")}"{(isSelected ? " class=\"row-selected\"" : "")}>
                    <td class="tool-cell-main">
                      <div class="tool-name">
                        {logoHtml}
                        <div class="tool-name-body">
                          <div class="tool-title-line">
                            <span class="tool-id-group">
                              <strong>{Encode(tool.PackageId)}</strong>
                              {(meta.IsVerified ? "<span class=\"tool-verified-dot\" title=\"Verified publisher on NuGet.org\">✓</span>" : "")}
                            </span>
                            <span class="tool-author-chip muted text-xs">by {Encode(meta.Authors)}</span>
                            {(string.IsNullOrEmpty(meta.DownloadsDisplay) ? "" : $"<span class=\"tool-downloads-chip\" title=\"{Encode(meta.DownloadsFullDisplay)} total downloads on NuGet.org\">↓ {Encode(meta.DownloadsDisplay)}</span>")}
                          </div>
                          <span class="tool-desc muted text-xs">{Encode(meta.Summary)}</span>
                        </div>
                      </div>
                    </td>
                    <td class="tool-cell-cmd">
                      <div class="row" style="gap:6px;flex-wrap:wrap">
                        {string.Concat(tool.Commands.Select(command => $"<span class=\"tool-command-pill\" data-copy=\"{Encode(isToolLocal ? $"dotnet {command}" : command)}\" title=\"Click to copy command\"><code>{Encode(command)}</code>{Icon("clipboard", "xs")}</span>"))}
                      </div>
                    </td>
                    <td class="tool-cell-ver">{versionCellHtml}</td>
                    <td class="tool-cell-scope">
                      <div class="tool-scope-cell">
                        {scopeBadge}
                        {(isToolLocal ? $"<code class=\"tool-manifest\">{Encode(tool.Manifest ?? "")}</code>" : "")}
                      </div>
                    </td>
                    <td class="tool-cell-actions">
                      <div class="tool-row-actions">
                        <a class="button sm ghost tool-inspect-btn" href="{inspectHref}" data-tool-inspect="{Encode(toolKey)}" aria-label="Inspect {Encode(tool.PackageId)}">Inspect</a>
                        {(meta.HasUpdate ? $"<form method=\"post\" action=\"/tools/update\" class=\"tool-action tool-update-form\"><input type=\"hidden\" name=\"packageId\" value=\"{Encode(tool.PackageId)}\"><input type=\"hidden\" name=\"scope\" value=\"{toolScopeStr}\"><button class=\"button primary sm\" type=\"submit\" title=\"Update {Encode(tool.PackageId)} to {Encode(meta.LatestVersion ?? "latest")}\">Update</button></form>" : "")}
                        <form method="post" action="/tools/uninstall" class="tool-action tool-uninstall-form">
                          <input type="hidden" name="packageId" value="{Encode(tool.PackageId)}">
                          <input type="hidden" name="scope" value="{toolScopeStr}">
                          <button class="button danger sm" type="submit">Uninstall</button>
                        </form>
                        <div class="tool-more-wrap">
                          <button type="button" class="button icon sm ghost tool-menu-trigger" data-tool-menu-btn aria-label="More actions for {Encode(tool.PackageId)}" aria-expanded="false">{Icon("more", "sm")}</button>
                          <div class="tool-context-menu" hidden>
                            <button type="button" class="tool-menu-item" data-tool-inspect="{Encode(toolKey)}">{Icon("search", "sm")} <span>Inspect tool details</span></button>
                            {(meta.HasUpdate ? $"<button type=\"button\" class=\"tool-menu-item\" data-tool-menu-update>{Icon("refresh", "sm")} <span>Update to {Encode(meta.LatestVersion ?? "latest")}</span></button>" : "")}
                            <button type="button" class="tool-menu-item copy-btn" data-copy="{Encode(copyCmd)}">{Icon("copy", "sm")} <span>Copy command</span> <kbd>C</kbd></button>
                            <a class="tool-menu-item" href="https://www.nuget.org/packages/{Encode(tool.PackageId)}" target="_blank" rel="noopener">{Icon("external", "sm")} <span>Open on NuGet.org</span></a>
                            <button type="button" class="tool-menu-item copy-btn" data-copy="{Encode(cliEquiv)}">{Icon("terminal", "sm")} <span>Copy CLI update command</span></button>
                            <hr>
                            <button type="button" class="tool-menu-item danger" data-tool-menu-uninstall>{Icon("trash", "sm")} <span>Uninstall package…</span></button>
                          </div>
                        </div>
                      </div>
                    </td>
                  </tr>
                  """;
            }))}}</tbody>
          </table>
        </div>
        """;

    private static string RenderToolDrawer(DotnetTool? tool, bool isOpen, bool local, bool localOwned)
    {
        var hiddenAttr = isOpen ? "" : "hidden";
        if (tool is null)
        {
            return $"""
                <aside id="tool-drawer" class="detail-drawer" {hiddenAttr} aria-label="Tool details">
                  <div class="drawer-header">
                    <div class="drawer-eyebrow-row"><span id="tool-drawer-eyebrow">.NET Tool Details</span><button id="tool-drawer-close" class="button icon ghost drawer-close-btn" type="button" aria-label="Close tool details">✕</button></div>
                    <div class="drawer-title-row"><h3 id="tool-drawer-title" class="drawer-title">Select a tool</h3></div>
                  </div>
                  <div class="drawer-body"><p class="muted">Click any tool in the table to inspect its commands, package details, and CLI equivalents.</p></div>
                </aside>
                """;
        }

        var isToolLocal = local || tool.Manifest is not null;
        var monogram = GetMonogram(tool.PackageId);
        var colorIdx = GetColorIndex(tool.PackageId);
        var meta = ToolMetadataCatalog.Resolve(tool.PackageId, tool.Version, isToolLocal, tool.Manifest);
        var logoHtml = ToolMetadataCatalog.RenderLogo(meta, colorIdx, monogram, "drawer-logo-lg");
        var scopeLabel = isToolLocal
            ? (localOwned ? "Local workspace manifest" : "Inherited workspace manifest")
            : "Global user tool (~/.dotnet/tools)";
        var eyebrow = isToolLocal ? "LOCAL WORKSPACE TOOL" : "GLOBAL .NET TOOL";
        var updateCmd = $"dotnet tool update {(isToolLocal ? "--local" : "--global")} {tool.PackageId}";
        var uninstallCmd = $"dotnet tool uninstall {(isToolLocal ? "--local" : "--global")} {tool.PackageId}";
        var nugetUrl = $"https://www.nuget.org/packages/{Encode(tool.PackageId)}";

        var commandsHtml = string.Concat(tool.Commands.Select(cmd =>
        {
            var invoke = isToolLocal ? $"dotnet {cmd}" : cmd;
            return $"""
                <div class="drawer-code-box tool-drawer-cmd-item">
                  <div class="tool-drawer-cmd-main">
                    <code>{Encode(invoke)}</code>
                    <span class="muted text-xs">CLI command</span>
                  </div>
                  <button class="button icon ghost copy-btn" data-copy="{Encode(invoke)}" type="button" title="Copy command">{Icon("clipboard")}</button>
                </div>
                """;
        }));

        var tagsHtml = string.Concat(meta.Tags.Select(t => $"<span class=\"tool-tag-pill\">{Encode(t)}</span>"));

        return $"""
            <aside id="tool-drawer" class="detail-drawer tool-detail-drawer" {hiddenAttr} aria-label="Tool details">
              <div class="drawer-header">
                <div class="drawer-eyebrow-row">
                  <span id="tool-drawer-eyebrow">{eyebrow}</span>
                  <button id="tool-drawer-close" class="button icon ghost drawer-close-btn" type="button" aria-label="Close tool details">✕</button>
                </div>
                <div class="tool-drawer-hero">
                  <div id="tool-drawer-logo">{logoHtml}</div>
                  <div class="tool-drawer-hero-text">
                    <h3 id="tool-drawer-title" class="drawer-title">{Encode(tool.PackageId)}</h3>
                    <div id="tool-drawer-badges" class="tool-drawer-badges">
                      <span class="v-current">{Encode(tool.Version)}</span>
                      <span class="badge {(isToolLocal ? "" : "ok")} sm">{(isToolLocal ? "Local" : "Global")}</span>
                      {(meta.IsVerified ? "<span class=\"badge ok sm\">✓ Verified</span>" : "")}
                      {(meta.HasUpdate && !string.IsNullOrEmpty(meta.LatestVersion) ? $"<span class=\"badge warn sm\">↑ {Encode(meta.LatestVersion)} available</span>" : "")}
                    </div>
                  </div>
                </div>
              </div>
              <div class="drawer-body">
                <div class="tool-drawer-about">
                  <p id="tool-drawer-description" class="tool-drawer-desc">{Encode(meta.FullDescription)}</p>
                  <div id="tool-drawer-tags" class="tool-drawer-tags">{tagsHtml}</div>
                </div>

                <div class="drawer-section">
                  <h4 class="drawer-section-title">Exposed commands</h4>
                  <div id="tool-drawer-commands" class="tool-drawer-cmd-list">
                    {commandsHtml}
                  </div>
                </div>

                <div class="drawer-section">
                  <h4 class="drawer-section-title">Package &amp; installation</h4>
                  <dl class="drawer-dl">
                    <dt>Package ID</dt><dd><code id="tool-drawer-pkg">{Encode(tool.PackageId)}</code></dd>
                    <dt>Version</dt><dd><code id="tool-drawer-ver">{Encode(tool.Version)}</code>{(meta.HasUpdate && !string.IsNullOrEmpty(meta.LatestVersion) ? $" <span class=\"muted text-xs\">(latest: <code>{Encode(meta.LatestVersion)}</code>)</span>" : "")}</dd>
                    <dt>Downloads</dt><dd id="tool-drawer-downloads">{Encode(meta.DownloadsFullDisplay)}</dd>
                    <dt>Author</dt><dd id="tool-drawer-authors">{Encode(meta.Authors)}</dd>
                    <dt>Publisher</dt><dd id="tool-drawer-verified">{(meta.IsVerified ? "✓ Verified prefix on NuGet.org" : "Standard NuGet package")}</dd>
                    <dt>License</dt><dd id="tool-drawer-license">{Encode(meta.License)}</dd>
                    <dt>Scope</dt><dd id="tool-drawer-scope">{Encode(scopeLabel)}</dd>
                    <dt>Project</dt><dd id="tool-drawer-project"><a href="{Encode(meta.ProjectUrl)}" target="_blank" rel="noopener">{Encode(ShortUrlDisplay(meta.ProjectUrl))} ↗</a></dd>
                  </dl>
                </div>

                <div class="drawer-section">
                  <h4 id="tool-drawer-path-title" class="drawer-section-title">{(isToolLocal ? "Manifest file" : "Package store location")}</h4>
                  <div class="drawer-code-box">
                    <code id="tool-drawer-path">{Encode(meta.StorePath)}</code>
                    <button class="button icon ghost copy-btn" id="tool-drawer-path-copy" data-copy="{Encode(meta.StorePath)}" type="button" title="Copy path">{Icon("clipboard")}</button>
                  </div>
                </div>

                <div class="drawer-section">
                  <h4 class="drawer-section-title">Update command</h4>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="tool-drawer-update-cmd">{Encode(updateCmd)}</code>
                    <button class="button icon ghost copy-btn" id="tool-drawer-update-copy" data-copy="{Encode(updateCmd)}" type="button" title="Copy update command">{Icon("clipboard")}</button>
                  </div>
                </div>

                <div class="drawer-section">
                  <h4 class="drawer-section-title">Uninstall command</h4>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="tool-drawer-uninstall-cmd">{Encode(uninstallCmd)}</code>
                    <button class="button icon ghost copy-btn" id="tool-drawer-uninstall-copy" data-copy="{Encode(uninstallCmd)}" type="button" title="Copy uninstall command">{Icon("clipboard")}</button>
                  </div>
                </div>
              </div>
              <div class="drawer-footer">
                <a id="tool-drawer-nuget-btn" class="button drawer-download-btn" href="{nugetUrl}" target="_blank" rel="noopener">{Icon("external", "xs")} <span id="tool-drawer-nuget-label">Open on NuGet.org</span></a>
                <div id="tool-drawer-update-form">
                  {(meta.HasUpdate ? $"<form method=\"post\" action=\"/tools/update\"><input type=\"hidden\" name=\"packageId\" value=\"{Encode(tool.PackageId)}\"><input type=\"hidden\" name=\"scope\" value=\"{(isToolLocal ? "local" : "global")}\"><button class=\"button primary\" type=\"submit\">{Icon("refresh", "xs")} Update to {Encode(meta.LatestVersion ?? "latest")}</button></form>" : "")}
                </div>
                <div id="tool-drawer-remove-form">
                  <form method="post" action="/tools/uninstall">
                    <input type="hidden" name="packageId" value="{Encode(tool.PackageId)}">
                    <input type="hidden" name="scope" value="{(isToolLocal ? "local" : "global")}">
                    <button class="button danger drawer-remove-btn" type="submit">{Icon("trash", "xs")} Uninstall tool</button>
                  </form>
                </div>
              </div>
            </aside>
            """;
    }

    private static string SerializeToolsJson(DotnetTool[] tools, bool local, bool localOwned)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var tool in tools)
        {
            if (!first) sb.Append(',');
            first = false;
            var isToolLocal = local || tool.Manifest is not null;
            var monogram = GetMonogram(tool.PackageId);
            var colorIdx = GetColorIndex(tool.PackageId);
            var meta = ToolMetadataCatalog.Resolve(tool.PackageId, tool.Version, isToolLocal, tool.Manifest);
            var logoHtml = ToolMetadataCatalog.RenderLogo(meta, colorIdx, monogram, "drawer-logo-lg");
            var scopeLabel = isToolLocal
                ? (localOwned ? "Local workspace manifest" : "Inherited workspace manifest")
                : "Global user tool (~/.dotnet/tools)";
            var eyebrow = isToolLocal ? "LOCAL WORKSPACE TOOL" : "GLOBAL .NET TOOL";
            var updateCmd = $"dotnet tool update {(isToolLocal ? "--local" : "--global")} {tool.PackageId}";
            var uninstallCmd = $"dotnet tool uninstall {(isToolLocal ? "--local" : "--global")} {tool.PackageId}";
            var toolKey = $"{(isToolLocal ? "local" : "global")}:{tool.PackageId}";

            sb.Append($"\"{JsonEscape(toolKey)}\":{{");
            sb.Append($"\"key\":\"{JsonEscape(toolKey)}\",");
            sb.Append($"\"packageId\":\"{JsonEscape(tool.PackageId)}\",");
            sb.Append($"\"version\":\"{JsonEscape(tool.Version)}\",");
            sb.Append($"\"scope\":\"{(isToolLocal ? "local" : "global")}\",");
            sb.Append($"\"eyebrow\":\"{JsonEscape(eyebrow)}\",");
            sb.Append($"\"scopeLabel\":\"{JsonEscape(scopeLabel)}\",");
            sb.Append($"\"authors\":\"{JsonEscape(meta.Authors)}\",");
            sb.Append($"\"license\":\"{JsonEscape(meta.License)}\",");
            sb.Append($"\"summary\":\"{JsonEscape(meta.Summary)}\",");
            sb.Append($"\"fullDescription\":\"{JsonEscape(meta.FullDescription)}\",");
            sb.Append($"\"storePath\":\"{JsonEscape(meta.StorePath)}\",");
            sb.Append($"\"pathTitle\":\"{(isToolLocal ? "Manifest file" : "Package store location")}\",");
            sb.Append($"\"projectUrl\":\"{JsonEscape(meta.ProjectUrl)}\",");
            sb.Append($"\"projectLabel\":\"{JsonEscape(ShortUrlDisplay(meta.ProjectUrl))}\",");
            sb.Append($"\"nugetUrl\":\"https://www.nuget.org/packages/{JsonEscape(tool.PackageId)}\",");
            sb.Append($"\"updateCmd\":\"{JsonEscape(updateCmd)}\",");
            sb.Append($"\"uninstallCmd\":\"{JsonEscape(uninstallCmd)}\",");
            sb.Append($"\"isVerified\":{(meta.IsVerified ? "true" : "false")},");
            sb.Append($"\"hasUpdate\":{(meta.HasUpdate ? "true" : "false")},");
            sb.Append($"\"latestVersion\":\"{JsonEscape(meta.LatestVersion)}\",");
            sb.Append($"\"downloads\":\"{JsonEscape(meta.DownloadsDisplay)}\",");
            sb.Append($"\"downloadsFull\":\"{JsonEscape(meta.DownloadsFullDisplay)}\",");
            sb.Append($"\"logoHtml\":\"{JsonEscape(logoHtml)}\",");
            sb.Append($"\"commands\":[{string.Join(',', tool.Commands.Select(c => $"\"{JsonEscape(isToolLocal ? $"dotnet {c}" : c)}\""))}],");
            sb.Append($"\"tags\":[{string.Join(',', meta.Tags.Select(t => $"\"{JsonEscape(t)}\""))}]");
            sb.Append('}');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string ShortUrlDisplay(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "NuGet.org";
        var trimmed = url.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        return trimmed.Length <= 38 ? trimmed : trimmed[..35] + "…";
    }

    private static string JsonEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("<", "\\u003c")
            .Replace(">", "\\u003e")
            .Replace("&", "\\u0026");
    }

    private static string RenderLocalManifestAction(bool initialized, bool owned, ToolsViewOptions options)
    {
        if (!initialized)
            return $$"""
                <form method="post" action="/tools/init" class="manifest-action"><input type="hidden" name="scope" value="local"><input type="hidden" name="q" value="{{Encode(options.SearchQuery)}}"><button class="button" type="submit">Initialize local tools</button></form>
                """;

        if (!owned)
            return $$"""
                <span class="manifest-status muted">Using an inherited manifest</span>
                """;

        return $$"""
            <form method="post" action="/tools/remove-manifest" class="manifest-action"><input type="hidden" name="scope" value="local"><input type="hidden" name="q" value="{{Encode(options.SearchQuery)}}"><button class="button danger" type="submit">Remove local tools</button></form>
            """;
    }

    private static string RenderSearch(ToolsSnapshot snapshot, ToolsViewOptions options, ToolSearch search)
    {
        var localChoice = snapshot.LocalManifestInitialized
            ? "<button class=\"button\" type=\"submit\" name=\"scope\" value=\"local\">Install locally</button>"
            : "";
        var results = search.Error is not null
            ? $"<div class=\"empty-state error-state\" role=\"alert\"><h3>Search failed</h3><p>{Encode(search.Error)}</p></div>"
            : options.SearchQuery.Length == 0
                ? ""
                : search.Results.Length == 0
                    ? $"<div class=\"empty-state\"><h3>No tools found</h3><p class=\"muted\">No tools matched <strong>{Encode(options.SearchQuery)}</strong>.</p></div>"
                    : $"<div class=\"search-results\"><h3>Search results for <strong>{Encode(options.SearchQuery)}</strong></h3><div class=\"search-result-list\">{string.Concat(search.Results.Select(result => RenderSearchResult(result, options.SearchQuery, localChoice)))}</div></div>";

        return $$"""
            <section class="tool-search" id="tool-search-section" data-has-query="{{(options.SearchQuery.Length > 0 ? "true" : "false")}}" aria-labelledby="tool-search-title">
              <div class="tool-search-heading">
                <div class="tool-search-title">
                  <span class="search-icon">{{Icon("search")}}</span>
                  <div>
                    <label id="tool-search-title" for="tool-search-input">Search .NET tools</label>
                    <p class="muted">Find packages on NuGet.org by ID or description.</p>
                  </div>
                </div>
                <span class="search-source">NuGet.org</span>
              </div>
              <form method="get" action="/tools" class="tool-search-form">
                <div class="tool-search-controls">
                  <input id="tool-search-input" name="q" value="{{Encode(options.SearchQuery)}}" placeholder="Package ID or description (e.g. dotnet-ef, coverage)">
                  <input type="hidden" name="scope" value="{{options.Scope}}">
                  <button class="button primary" type="submit">Search</button>
                </div>
              </form>
              {{results}}
            </section>
            """;
    }

    private static string RenderSearchResult(ToolSearchResult result, string query, string localChoice)
    {
        var monogram = GetMonogram(result.PackageId);
        var colorIdx = GetColorIndex(result.PackageId);
        var meta = ToolMetadataCatalog.Resolve(result.PackageId, result.Version, false);
        var logoHtml = ToolMetadataCatalog.RenderLogo(meta, colorIdx, monogram);
        return $$"""
            <article class="search-result">
              <div class="search-result-main">
                {{logoHtml}}
                <div>
                  <div class="search-result-title-row">
                    <strong>{{Encode(result.PackageId)}}</strong>
                    <code class="muted">{{Encode(result.Version)}}</code>
                    {{(result.Verified ? "<span class=\"badge ok sm verified\">✓ Verified</span>" : "")}}
                  </div>
                  <p class="tool-desc muted text-xs">{{Encode(meta.Summary)}}</p>
                  <div class="search-result-meta">
                    <span>{{Encode(result.Authors)}}</span>
                    <span>· {{Encode(result.Downloads)}} downloads</span>
                  </div>
                  <div class="tool-cli-preview"><span class="cli-label">CLI</span><code>dotnet tool install --global {{Encode(result.PackageId)}} --version {{Encode(result.Version)}}</code></div>
                </div>
              </div>
              <form method="post" action="/tools/install" class="tool-install-actions">
                <input type="hidden" name="packageId" value="{{Encode(result.PackageId)}}">
                <input type="hidden" name="version" value="{{Encode(result.Version)}}">
                <input type="hidden" name="q" value="{{Encode(query)}}">
                <button class="button primary sm" type="submit" name="scope" value="global">Install globally</button>
                {{localChoice}}
              </form>
            </article>
            """;
    }

    private static string RenderAddToolSheet(ToolsSnapshot snapshot, ToolsViewOptions options, ToolSearch search)
    {
        var canInstallLocal = snapshot.LocalManifestInitialized;
        var featured = new (string Id, string DefaultVer, string Cmd)[]
        {
            ("dotnet-coverage", "18.1.0", "dotnet-coverage"),
            ("dotnet-reportgenerator-globaltool", "5.4.18", "reportgenerator"),
            ("coverlet.console", "6.0.4", "coverlet"),
            ("dotnet-ef", "10.0.0", "dotnet-ef"),
            ("dotnet-outdated-tool", "4.6.8", "dotnet-outdated")
        };

        var itemsHtml = string.Concat(featured.Select((item, idx) =>
        {
            var meta = ToolMetadataCatalog.Resolve(item.Id, item.DefaultVer, false);
            var ver = !string.IsNullOrWhiteSpace(meta.LatestVersion) ? meta.LatestVersion : item.DefaultVer;
            var logoHtml = ToolMetadataCatalog.RenderLogo(meta);
            var downloads = !string.IsNullOrWhiteSpace(meta.DownloadsDisplay) ? meta.DownloadsDisplay : "10K+";
            return $"""
                <div class="sheet-result-item {(idx == 0 ? "active" : "")}" data-pkg-id="{Encode(item.Id)}" data-pkg-ver="{Encode(ver)}">
                  {logoHtml}
                  <div class="sheet-result-body">
                    <div class="sheet-result-top">
                      <strong>{Encode(item.Id)}</strong>
                      <code class="muted">{Encode(ver)}</code>
                      {(meta.IsVerified ? "<span class=\"badge ok sm\">✓ Verified</span>" : "")}
                    </div>
                    <span class="muted text-xs">{Encode(meta.Summary)}</span>
                    <div class="sheet-result-meta muted text-xs">
                      <span>👤 {Encode(meta.Authors)}</span>
                      <span>↓ {Encode(downloads)}</span>
                      <span>⌘ {Encode(item.Cmd)}</span>
                    </div>
                  </div>
                  <form method="post" action="/tools/install">
                    <input type="hidden" name="packageId" value="{Encode(item.Id)}">
                    <input type="hidden" name="version" value="{Encode(ver)}">
                    <input type="hidden" name="scope" value="global" data-sheet-form-scope>
                    <button type="submit" class="button {(idx == 0 ? "primary" : "")} sm">+ Install</button>
                  </form>
                </div>
                """;
        }));

        return $$"""
            <dialog id="add-tool-dialog" class="add-tool-sheet" closedby="any" aria-labelledby="add-tool-sheet-title" data-can-local="{{canInstallLocal.ToString().ToLowerInvariant()}}">
              <div class="sheet-inner">
                <div class="sheet-head">
                  <div class="sheet-title-group">
                    <h2 id="add-tool-sheet-title">Add a .NET tool</h2>
                    <span class="search-source">NuGet.org</span>
                  </div>
                  <button type="button" class="button icon ghost" id="close-add-tool-btn" aria-label="Close add tool panel">✕</button>
                </div>

                <div class="sheet-controls">
                  <label class="sheet-search-box" for="sheet-search-input">
                    {{Icon("search", "sm")}}
                    <input id="sheet-search-input" type="search" placeholder="Search NuGet tools (e.g. coverage, dotnet-ef, format)..." value="{{Encode(options.SearchQuery)}}" autocomplete="off">
                    <kbd>↵</kbd>
                  </label>

                  <div class="sheet-scope-bar">
                    <span class="muted text-xs">Install to</span>
                    <div class="version-scope-pills" role="group" aria-label="Installation target scope">
                      <button type="button" class="pill-badge active" data-sheet-scope="global">{{Icon("tools", "xs")}} Global <span class="muted text-xs">· User</span></button>
                      {{(canInstallLocal ? $"<button type=\"button\" class=\"pill-badge\" data-sheet-scope=\"local\">{Icon("folder", "xs")} Local <span class=\"muted text-xs\">· Workspace</span></button>" : "<button type=\"button\" class=\"pill-badge\" disabled title=\"A local manifest is required to install into this workspace\">Local <span class=\"muted text-xs\">· Workspace</span></button>")}}
                    </div>
                  </div>
                </div>

                <div id="sheet-results" class="sheet-results">
                  {{itemsHtml}}
                </div>

                <div class="sheet-foot">
                  <span class="drawer-section-title">WILL RUN</span>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="sheet-cli-preview">dotnet tool install --global dotnet-coverage --version 18.1.0</code>
                    <button class="button icon ghost copy-btn" id="sheet-cli-copy" data-copy="dotnet tool install --global dotnet-coverage --version 18.1.0" type="button" title="Copy command">{{Icon("clipboard")}}</button>
                  </div>
                </div>
              </div>
            </dialog>
            """;
    }

    internal static string GetMonogram(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return "TL";
        var parts = packageId.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var filtered = parts.Where(p => !string.Equals(p, "dotnet", StringComparison.OrdinalIgnoreCase) && !string.Equals(p, "tool", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (filtered.Length >= 2)
                return $"{char.ToUpperInvariant(filtered[0][0])}{char.ToUpperInvariant(filtered[1][0])}";
            if (filtered.Length == 1 && filtered[0].Length >= 2)
                return filtered[0][..2].ToUpperInvariant();
        }
        return packageId.Length >= 2 ? packageId[..2].ToUpperInvariant() : packageId.ToUpperInvariant();
    }

    internal static int GetColorIndex(string packageId)
    {
        var hash = 0;
        foreach (var ch in packageId) hash = (hash * 31) + char.ToLowerInvariant(ch);
        return Math.Abs(hash) % 5;
    }

    private static string RenderNotice(string notice) => notice switch
    {
        "installed" => "<div class=\"action-notice\" role=\"status\">Tool installed successfully.</div>",
        "updated" => "<div class=\"action-notice\" role=\"status\">Tool updated successfully.</div>",
        "uninstalled" => "<div class=\"action-notice\" role=\"status\">Tool uninstalled successfully.</div>",
        "initialized" => "<div class=\"action-notice\" role=\"status\">Local tools are now initialized in this working directory.</div>",
        "removed" => "<div class=\"action-notice\" role=\"status\">The local tool manifest was removed. Cached packages were left untouched.</div>",
        "failed" => "<div class=\"action-notice error-state\" role=\"alert\">The requested tool action failed. Check the SDK and package sources, then try again.</div>",
        _ => ""
    };
}

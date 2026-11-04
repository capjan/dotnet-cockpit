using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record ToolsViewOptions(string Scope = "global", string SearchQuery = "", string Notice = "", string Selected = "", string Changed = "")
{
    public static ToolsViewOptions From(IQueryCollection query) => new(
        query["scope"].ToString() is "local" ? "local" : query["scope"].ToString() is "all" ? "all" : "global",
        query["q"].ToString().Trim() is { Length: <= 100 } search ? search : "",
        query["notice"].ToString(),
        query["selected"].ToString().Trim() is { Length: <= 100 } selected ? selected : "",
        query["changed"].ToString().Trim());
}

internal static class ToolsPage
{
    public static string Render(ToolsSnapshot snapshot, ToolsViewOptions options) =>
        Render(snapshot, options, new(options.SearchQuery, []));

    public static string Render(ToolsSnapshot snapshot, ToolsViewOptions options, ToolSearch search, int navIssues = 0, int? navVersions = null)
    {
        var content = snapshot.Status switch
        {
            "notFound" => "<section class=\"inventory empty-state\"><h2>No .NET installation found</h2><p class=\"muted\">The dotnet command is not available on PATH. A .NET SDK is required to list tools.</p></section>",
            "sdkMissing" => "<section class=\"inventory empty-state\"><h2>A .NET SDK is required</h2><p class=\"muted\">The dotnet host is available, but no SDK is installed. Install an SDK to list global and local tools.</p></section>",
            "error" => $"<section class=\"inventory empty-state error-state\" role=\"alert\"><h2>Tool information is unavailable</h2><p>{Encode(snapshot.Error ?? "The tools could not be read.")}</p><a class=\"button\" href=\"/tools\">Try again</a></section>",
            _ => RenderTabs(snapshot, options)
        };

        var manifestBadge = snapshot.LocalManifestInitialized
            ? (snapshot.LocalManifestOwned ? "Local manifest in this workspace" : "Local manifest inherited from a parent folder")
            : "No local manifest";

        var activeTools = options.Scope == "local"
            ? snapshot.Local.Tools
            : options.Scope == "all"
                ? snapshot.Global.Tools.Concat(snapshot.Local.Tools).ToArray()
                : snapshot.Global.Tools;
        var updatableTools = snapshot.Status == "ready"
            ? activeTools
                .Select(t =>
                {
                    var isToolLocal = options.Scope == "local" || t.Manifest is not null;
                    var meta = ToolMetadataCatalog.Resolve(t.PackageId, t.Version, isToolLocal, t.Manifest);
                    return (Tool: t, Scope: isToolLocal ? "local" : "global", Meta: meta);
                })
                .Where(x => x.Meta.HasUpdate)
                .ToArray()
            : [];
        var updateAllHtml = updatableTools.Length > 0
            ? ConfirmActions.Trigger(
                ConfirmActions.ToolsUpdateAll(options.Scope, options.SearchQuery, updatableTools.Select(u => $"{u.Tool.PackageId}|{u.Scope}").ToArray()),
                $"<button type=\"submit\" class=\"button\" id=\"update-all-tools-btn\">{Icon("refresh", "xs")} Update all <span class=\"summary-count\">{updatableTools.Length}</span></button>",
                "inline-action-form")
            : "";

        return PageLayout.Render($$"""
            <div class="page-heading">
              <div>
                <h1>.NET Tools</h1>
                <p class="muted intro">Global tools for your account and local tools declared for this workspace.</p>
              </div>
              <div class="tools-header-actions">
                {{updateAllHtml}}
                {{(snapshot.Status == "ready" ? $"<a class=\"button primary\" id=\"open-add-tool-btn\" href=\"/tools?add=1#tool-search-section\">{Icon("plus", "xs")} Add tool</a>" : "")}}
              </div>
            </div>
            <div class="working-directory">
              {{Icon("folder", "xs")}}
              <code title="{{Encode(snapshot.LocalManifestPath ?? "")}}">{{Encode(snapshot.WorkingDirectory)}}</code>
              <span class="muted">{{manifestBadge}}</span>
            </div>
            {{RenderNotice(options.Notice)}}
            {{content}}
            {{(snapshot.Status == "ready" ? RenderSearch(snapshot, options, search) : "")}}
            {{(snapshot.Status == "ready" ? RenderAddToolSheet(snapshot, options, search) : "")}}
            <p class="scope-note muted">Local tools are declared in manifests for this working directory or its parents. This view does not check whether they have been restored — run <code>dotnet tool restore</code> in the workspace.</p>
            """, "/tools", ".NET Tools | .NET Cockpit", navIssues, navVersions, snapshot.Global.Tools.Length + snapshot.Local.Tools.Length, refreshHref: RefreshLink(options));
    }

    private static string RenderTabs(ToolsSnapshot snapshot, ToolsViewOptions options)
    {
        var isLocal = options.Scope == "local";
        var isAll = options.Scope == "all";
        var isGlobal = !isLocal && !isAll;
        var allCount = snapshot.Global.Error is null && snapshot.Local.Error is null ? snapshot.Global.Tools.Length + snapshot.Local.Tools.Length : (int?)null;
        var tabsHtml = $"""
            <div class="segmented tool-tabs" role="tablist" aria-label="Tool scope">
              {RenderTab("global", "Global", snapshot.Global.Error is null ? snapshot.Global.Tools.Length : null, isGlobal)}
              {RenderTab("local", "Local", snapshot.Local.Error is null ? snapshot.Local.Tools.Length : null, isLocal)}
              {RenderTab("all", "All", allCount, isAll)}
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

    private static string RenderTab(string scope, string label, int? count, bool selected)
    {
        var countHtml = count is null ? "" : $"<span class=\"seg-count\" aria-hidden=\"true\">{count}</span>";
        return $"<a class=\"seg tool-tab {(selected ? "active" : "")}\" href=\"/tools?scope={scope}\" role=\"tab\" aria-selected=\"{selected.ToString().ToLowerInvariant()}\" aria-controls=\"{scope}-tools-panel\">{label}{countHtml}</a>";
    }

    private static string RefreshLink(ToolsViewOptions options) =>
        options.Scope == "local" ? "/tools?scope=local&refresh=1" : options.Scope == "all" ? "/tools?scope=all&refresh=1" : "/tools?refresh=1";

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
            ? $"<span class=\"filter-count\" data-tools-count hidden></span><span class=\"spacer\"></span><label class=\"search-field\">{Icon("search", "xs")}<input type=\"search\" data-tools-table-filter placeholder=\"Filter by name or command\" aria-label=\"Filter {title.ToLowerInvariant()}\"></label>"
            : "";
        var localAction = local ? RenderLocalManifestAction(localInitialized, localOwned, options) : "";
        var drawerHtml = RenderToolDrawer(orderedTools, selectedTool, local, localOwned, options);

        return $$"""
            <section class="tool-panel-wrapper" role="tabpanel" aria-labelledby="{{id}}" id="{{id}}-panel">
              <div class="sr-only"><h2 id="{{id}}">{{title}}</h2></div>
              <div class="filter-bar">
                {{tabsHtml}}
                {{filterInput}}
                {{(filterInput.Length == 0 ? "<span class=\"spacer\"></span>" : "")}}
                {{localAction}}
              </div>
              <div class="tools-hub" id="tools-hub">
                <div class="tools-list">
                  <div class="inventory tool-panel">
                    {{content}}
                  </div>
                </div>
                {{drawerHtml}}
              </div>
            </section>
            """;
    }

    private static string RenderTable(DotnetTool[] orderedTools, bool local, string title, bool localOwned, ToolsViewOptions options, DotnetTool? selectedTool) => $$"""
        <div class="table-scroll" role="region" aria-label="{{title}}" tabindex="0">
          <table class="tools-table" aria-label="{{title}}">
            <thead><tr><th scope="col">Tool</th><th scope="col">Command</th><th scope="col">Version</th>{{(options.Scope == "global" ? "" : $"<th scope=\"col\">{(local ? "Manifest" : "Scope")}</th>")}}<th scope="col" class="th-actions">Actions</th></tr></thead>
            <tbody>{{string.Concat(orderedTools.Select(tool =>
            {
                var isToolLocal = local || tool.Manifest is not null;
                var isSelected = selectedTool is not null && string.Equals(selectedTool.PackageId, tool.PackageId, StringComparison.OrdinalIgnoreCase);
                var monogram = GetMonogram(tool.PackageId);
                var colorIdx = GetColorIndex(tool.PackageId);
                var meta = ToolMetadataCatalog.Resolve(tool.PackageId, tool.Version, isToolLocal, tool.Manifest);
                var logoHtml = ToolMetadataCatalog.RenderLogo(meta, colorIdx, monogram);
                var scopeBadge = isToolLocal
                    ? $"<span class=\"tool-scope-pill\">{(localOwned ? "Local" : "Local · inherited")}</span>"
                    : "<span class=\"tool-scope-pill\">Global</span>";
                var primaryCmd = tool.Commands.FirstOrDefault() ?? tool.PackageId;
                var copyCmd = isToolLocal ? $"dotnet {primaryCmd}" : primaryCmd;
                var cliEquiv = $"dotnet tool update {(isToolLocal ? "--local" : "--global")} {tool.PackageId}";
                var toolScopeStr = isToolLocal ? "local" : "global";
                var toolKey = $"{toolScopeStr}:{tool.PackageId}";
                var inspectHref = $"/tools?scope={Uri.EscapeDataString(options.Scope)}&selected={Uri.EscapeDataString(tool.PackageId)}";
                var versionCellHtml = meta.HasUpdate && !string.IsNullOrEmpty(meta.LatestVersion)
                    ? $"<div class=\"version-patch-flow\"><span class=\"v-current\">{Encode(tool.Version)}</span><span class=\"v-arrow\" aria-hidden=\"true\">→</span><span class=\"sr-only\">, update available: </span><span class=\"v-patch\" title=\"Latest on NuGet.org: {Encode(meta.LatestVersion)}\">{Encode(meta.LatestVersion)}</span></div>"
                    : $"<span class=\"v-current\">{Encode(tool.Version)}</span>";

                return $"""
                  <tr data-tool-scope="{toolScopeStr}" data-tool-row data-drawer-key="{Encode(toolKey)}" data-search="{Encode($"{tool.PackageId} {tool.Version} {string.Join(' ', tool.Commands)} {tool.Manifest} {meta.Summary} {meta.Authors}")}"{(isSelected ? " class=\"row-selected\"" : string.Equals(options.Changed, tool.PackageId, StringComparison.OrdinalIgnoreCase) ? " class=\"row-changed\"" : "")}>
                    <td class="tool-cell-main">
                      <div class="tool-name">
                        {logoHtml}
                        <div class="tool-name-body">
                          <div class="tool-title-line">
                            <span class="tool-id-group">
                              <strong>{Encode(tool.PackageId)}</strong>
                              {(meta.IsVerified ? $"<span class=\"v-ok\" title=\"Verified publisher on NuGet.org\">{Icon("check", "xs")}<span class=\"sr-only\">Verified publisher</span></span>" : "")}
                            </span>
                            <span class="tool-author-chip muted text-xs">by {Encode(meta.Authors)}</span>
                          </div>
                          <span class="tool-desc muted text-xs">{Encode(meta.Summary)}</span>
                        </div>
                      </div>
                    </td>
                    <td class="tool-cell-cmd">
                      <div class="row">
                        {string.Concat(tool.Commands.Select(command => $"<span class=\"tool-command-pill\" data-copy=\"{Encode(isToolLocal ? $"dotnet {command}" : command)}\" title=\"Click to copy command\"><code>{Encode(command)}</code>{Icon("clipboard", "xs")}</span>"))}
                      </div>
                    </td>
                    <td class="tool-cell-ver">{versionCellHtml}</td>
                    {(options.Scope == "global" ? "" : $"<td class=\"tool-cell-scope\"><div class=\"tool-scope-cell\">{scopeBadge}{(isToolLocal ? $"<code class=\"tool-manifest\">{Encode(tool.Manifest ?? "")}</code>" : "")}</div></td>")}
                    <td class="tool-cell-actions">
                      <div class="tool-row-actions">
                        <a class="button sm ghost tool-inspect-btn" href="{inspectHref}" data-inspect="{Encode(toolKey)}" aria-label="Inspect {Encode(tool.PackageId)}">Inspect</a>
                        {(meta.HasUpdate ? ConfirmActions.Trigger(ConfirmActions.ToolUpdate(tool.PackageId, toolScopeStr, meta.LatestVersion), $"<button class=\"button sm\" type=\"submit\" aria-label=\"Update {Encode(tool.PackageId)}\">Update</button>", "tool-action tool-update-form") : "<span class=\"tool-update-slot\" aria-hidden=\"true\"></span>")}
                        {ConfirmActions.Trigger(ConfirmActions.ToolUninstall(tool.PackageId, toolScopeStr), $"<button class=\"button remove sm\" type=\"submit\" aria-label=\"Remove {Encode(tool.PackageId)}\">Remove</button>", "tool-action tool-uninstall-form")}
                        <div class="tool-more-wrap">
                          <button type="button" class="button icon sm ghost tool-menu-trigger" hidden data-js-only data-tool-menu-btn aria-label="More actions for {Encode(tool.PackageId)}" aria-expanded="false">{Icon("more", "sm")}</button>
                          <div class="tool-context-menu" hidden>
                            <button type="button" class="tool-menu-item" data-inspect="{Encode(toolKey)}">{Icon("search", "sm")} <span>Inspect tool details</span></button>
                            {(meta.HasUpdate ? $"<button type=\"button\" class=\"tool-menu-item\" data-tool-menu-update>{Icon("refresh", "sm")} <span>Update to {Encode(meta.LatestVersion ?? "latest")}</span></button>" : "")}
                            <button type="button" class="tool-menu-item copy-btn" data-copy="{Encode(copyCmd)}">{Icon("copy", "sm")} <span>Copy command</span> <kbd>C</kbd></button>
                            <a class="tool-menu-item" href="https://www.nuget.org/packages/{Encode(tool.PackageId)}" target="_blank" rel="noopener">{Icon("external", "sm")} <span>Open on NuGet.org</span></a>
                            <button type="button" class="tool-menu-item copy-btn" data-copy="{Encode(cliEquiv)}">{Icon("terminal", "sm")} <span>Copy CLI update command</span></button>
                            <hr>
                            <button type="button" class="tool-menu-item danger" data-tool-menu-uninstall>{Icon("trash", "sm")} <span>Remove tool…</span></button>
                          </div>
                        </div>
                      </div>
                    </td>
                  </tr>
                  """;
            }))}}</tbody>
          </table>
          <div class="empty-state client-tools-empty" data-tools-empty hidden><h3>No matching tools</h3><p class="muted">No tools match your filter.</p></div>
        </div>
        """;

    private static string RenderToolDrawer(DotnetTool[] tools, DotnetTool? selected, bool local, bool localOwned, ToolsViewOptions options)
    {
        var templates = string.Concat(tools.Select(tool =>
        {
            var isToolLocal = local || tool.Manifest is not null;
            var key = $"{(isToolLocal ? "local" : "global")}:{tool.PackageId}";
            return $"<template data-drawer-content=\"{Encode(key)}\">{RenderToolDrawerContent(tool, local, localOwned, options)}</template>";
        }));
        return $"""
            <aside id="tool-drawer" class="detail-drawer tool-detail-drawer" {(selected is null ? "hidden" : "")} aria-label="Tool details">{(selected is null ? "" : RenderToolDrawerContent(selected, local, localOwned, options))}</aside>
            {templates}
            """;
    }

    private static string RenderToolDrawerContent(DotnetTool tool, bool local, bool localOwned, ToolsViewOptions options)
    {
        var isToolLocal = local || tool.Manifest is not null;
        var scope = isToolLocal ? "local" : "global";
        var monogram = GetMonogram(tool.PackageId);
        var colorIdx = GetColorIndex(tool.PackageId);
        var meta = ToolMetadataCatalog.Resolve(tool.PackageId, tool.Version, isToolLocal, tool.Manifest);
        var logoHtml = ToolMetadataCatalog.RenderLogo(meta, colorIdx, monogram, "drawer-logo-lg");
        var scopeLabel = isToolLocal
            ? (localOwned ? "Local workspace manifest" : "Inherited workspace manifest")
            : "Global user tool (~/.dotnet/tools)";
        var eyebrow = isToolLocal ? "Local workspace tool" : "Global .NET tool";
        var updateCmd = $"dotnet tool update --{scope} \\\n  {tool.PackageId}";
        var uninstallCmd = $"dotnet tool uninstall --{scope} \\\n  {tool.PackageId}";
        var nugetUrl = $"https://www.nuget.org/packages/{Encode(tool.PackageId)}";

        var commandsHtml = string.Concat(tool.Commands.Select(cmd =>
        {
            var invoke = isToolLocal ? $"dotnet {cmd}" : cmd;
            return $"""
                <div class="drawer-code-box tool-drawer-cmd-item">
                  <code>{Encode(invoke)}</code>
                  <button class="button icon ghost copy-btn" data-copy="{Encode(invoke)}" type="button" title="Copy command" aria-label="Copy command {Encode(invoke)}">{Icon("clipboard")}</button>
                </div>
                """;
        }));

        var tagsHtml = string.Concat(meta.Tags.Select(t => $"<span class=\"tool-tag-pill\">{Encode(t)}</span>"));
        var closeHref = $"/tools?scope={Uri.EscapeDataString(options.Scope)}";

        return $"""
              <div class="drawer-header">
                <div class="drawer-eyebrow-row">
                  <span>{eyebrow}</span>
                  <a id="tool-drawer-close" data-drawer-close class="button icon ghost drawer-close-btn" href="{closeHref}" aria-label="Close tool details">{Icon("x", "sm")}</a>
                </div>
                <div class="tool-drawer-hero">
                  <div>{logoHtml}</div>
                  <div class="tool-drawer-hero-text">
                    <h3 id="tool-drawer-title" class="drawer-title">{Encode(tool.PackageId)}</h3>
                    <div class="tool-drawer-badges">
                      <span class="v-current">{Encode(tool.Version)}</span>
                      {(meta.IsVerified ? $"<span class=\"tool-verified\">{Icon("check", "xs")} Verified</span>" : "")}
                      {(meta.HasUpdate && !string.IsNullOrEmpty(meta.LatestVersion) ? $"<span class=\"badge warn sm\">{Encode(meta.LatestVersion)} available</span>" : "")}
                    </div>
                  </div>
                </div>
              </div>
              <div class="drawer-body">
                <div class="tool-drawer-about">
                  <p class="tool-drawer-desc">{Encode(meta.FullDescription)}</p>
                  {(tagsHtml.Length > 0 ? $"<div class=\"tool-drawer-tags\">{tagsHtml}</div>" : "")}
                </div>

                <div class="drawer-section">
                  <h4 class="drawer-section-title">Exposed commands</h4>
                  <div class="tool-drawer-cmd-list">{commandsHtml}</div>
                </div>

                <div class="drawer-section">
                  <h4 class="drawer-section-title">Package &amp; installation</h4>
                  <dl class="drawer-dl">
                    <dt>Package ID</dt><dd><code>{Encode(tool.PackageId)}</code></dd>
                    <dt>Version</dt><dd><code>{Encode(tool.Version)}</code>{(meta.HasUpdate && !string.IsNullOrEmpty(meta.LatestVersion) ? $" <span class=\"muted text-xs\">(latest: <code>{Encode(meta.LatestVersion)}</code>)</span>" : "")}</dd>
                    {(string.IsNullOrEmpty(meta.DownloadsFullDisplay) ? "" : $"<dt>Downloads</dt><dd id=\"tool-drawer-downloads\">{Encode(meta.DownloadsFullDisplay)}</dd>")}
                    <dt>Author</dt><dd>{Encode(meta.Authors)}</dd>
                    <dt>Publisher</dt><dd id="tool-drawer-verified">{(meta.IsVerified ? "Verified prefix on NuGet.org" : "Standard NuGet package")}</dd>
                    <dt>License</dt><dd>{Encode(meta.License)}</dd>
                    <dt>Scope</dt><dd>{Encode(scopeLabel)}</dd>
                    <dt>Project</dt><dd><a href="{Encode(meta.ProjectUrl)}" target="_blank" rel="noopener">{Encode(ShortUrlDisplay(meta.ProjectUrl))} {Icon("external", "xs")}</a></dd>
                  </dl>
                </div>

                <div class="drawer-section">
                  <h4 class="drawer-section-title">{(isToolLocal ? "Manifest file" : "Package store location")}</h4>
                  <div class="drawer-code-box">
                    <code class="drawer-path">{Encode(meta.StorePath)}</code>
                    <button class="button icon ghost copy-btn" data-copy="{Encode(meta.StorePath)}" type="button" title="Copy path" aria-label="Copy path">{Icon("clipboard")}</button>
                  </div>
                </div>

                {(meta.HasUpdate ? $"""
                <div class="drawer-section">
                  <h4 class="drawer-section-title">Update command</h4>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="tool-drawer-update-cmd">{Encode(updateCmd)}</code>
                    <button class="button icon ghost copy-btn" data-copy="{Encode(updateCmd)}" type="button" title="Copy update command" aria-label="Copy update command">{Icon("clipboard")}</button>
                  </div>
                </div>
                """ : "")}

                <div class="drawer-section">
                  <h4 class="drawer-section-title">Remove command</h4>
                  <div class="drawer-command-box">
                    <span class="cmd-prompt">$</span>
                    <code id="tool-drawer-uninstall-cmd">{Encode(uninstallCmd)}</code>
                    <button class="button icon ghost copy-btn" data-copy="{Encode(uninstallCmd)}" type="button" title="Copy remove command" aria-label="Copy remove command">{Icon("clipboard")}</button>
                  </div>
                </div>
              </div>
              <div class="drawer-footer">
                <a class="button ghost" href="{nugetUrl}" target="_blank" rel="noopener">{Icon("external", "xs")} NuGet.org</a>
                <span class="spacer"></span>
                {(meta.HasUpdate ? ConfirmActions.Trigger(ConfirmActions.ToolUpdate(tool.PackageId, scope, meta.LatestVersion), $"<button class=\"button primary\" type=\"submit\">{Icon("refresh", "xs")} Update to {Encode(meta.LatestVersion ?? "latest")}</button>") : "")}
                {ConfirmActions.Trigger(ConfirmActions.ToolUninstall(tool.PackageId, scope), $"<button class=\"button remove drawer-remove-btn\" type=\"submit\">{Icon("trash", "xs")} Remove</button>")}
              </div>
            """;
    }

    private static string ShortUrlDisplay(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "NuGet.org";
        var trimmed = url.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        return trimmed.Length <= 38 ? trimmed : trimmed[..35] + "…";
    }

    private static string RenderLocalManifestAction(bool initialized, bool owned, ToolsViewOptions options)
    {
        if (!initialized)
            return ConfirmActions.Trigger(ConfirmActions.ManifestInit(options.SearchQuery), "<button class=\"button\" type=\"submit\">Initialize local tools</button>", "manifest-action");

        if (!owned)
            return "<span class=\"manifest-status muted\">Using an inherited manifest</span>";

        return ConfirmActions.Trigger(ConfirmActions.ManifestRemove(options.SearchQuery), "<button class=\"button remove\" type=\"submit\">Remove local tools</button>", "manifest-action");
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
                  <div class="tool-cli-preview"><span class="cli-label">CLI</span><code>dotnet tool install --global \&#10;  {{Encode(result.PackageId)}} \&#10;  --version {{Encode(result.Version)}}</code></div>
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

        var itemsHtml = string.Concat(featured.Select(item =>
        {
            var meta = ToolMetadataCatalog.Resolve(item.Id, item.DefaultVer, false);
            var ver = !string.IsNullOrWhiteSpace(meta.LatestVersion) ? meta.LatestVersion : item.DefaultVer;
            var logoHtml = ToolMetadataCatalog.RenderLogo(meta);
            return $"""
                <div class="sheet-result-item" data-pkg-id="{Encode(item.Id)}">
                  {logoHtml}
                  <div class="sheet-result-body">
                    <div class="sheet-result-top">
                      <strong>{Encode(item.Id)}</strong>
                      <code class="muted">{Encode(ver)}</code>
                      {(meta.IsVerified ? "<span class=\"badge ok sm\">✓ Verified</span>" : "")}
                    </div>
                    <span class="muted text-xs">{Encode(meta.Summary)}</span>
                    <div class="sheet-result-meta muted text-xs">
                      <span>by {Encode(meta.Authors)}</span>
                      {(string.IsNullOrWhiteSpace(meta.DownloadsDisplay) ? "" : $"<span>{Encode(meta.DownloadsDisplay)} downloads</span>")}
                      <span><code>{Encode(item.Cmd)}</code></span>
                    </div>
                  </div>
                  {ConfirmActions.Trigger(ConfirmActions.ToolInstall(item.Id, ver, "global"), "<button type=\"submit\" class=\"button sm\">Install</button>")}
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
                  <button type="button" class="button icon ghost" id="close-add-tool-btn" aria-label="Close add tool panel">{{Icon("x", "sm")}}</button>
                </div>

                <div class="sheet-controls">
                  <label class="search-field wide" for="sheet-search-input">
                    {{Icon("search", "xs")}}
                    <input id="sheet-search-input" type="search" placeholder="Search NuGet.org, for example coverage or dotnet-ef" value="{{Encode(options.SearchQuery)}}" autocomplete="off">
                  </label>

                  <div class="filter-bar">
                    <span class="muted">Install to</span>
                    <div class="segmented" role="group" aria-label="Installation target">
                      <button type="button" class="seg active" data-sheet-scope="global">Global · User</button>
                      {{(canInstallLocal ? "<button type=\"button\" class=\"seg\" data-sheet-scope=\"local\">Local · Workspace</button>" : "<button type=\"button\" class=\"seg\" disabled title=\"A local manifest is required to install into this workspace\">Local · Workspace</button>")}}
                    </div>
                  </div>
                </div>

                <div id="sheet-results" class="sheet-results" data-body-global="{{Encode(ConfirmActions.ToolInstallBody("global"))}}" data-body-local="{{Encode(ConfirmActions.ToolInstallBody("local"))}}">
                  {{itemsHtml}}
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
        "installed" => SuccessToast("Tool installed."),
        "updated" => SuccessToast("Tool updated."),
        "uninstalled" => SuccessToast("Tool removed."),
        "initialized" => SuccessToast("Local tools are initialized in this workspace."),
        "removed" => SuccessToast("Local manifest removed. Cached packages were left untouched."),
        "failed" => "<div class=\"action-notice error-state\" role=\"alert\">The requested tool action failed. Check the SDK and package sources, then try again.</div>",
        _ => ""
    };
}

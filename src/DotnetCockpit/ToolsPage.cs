using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record ToolsViewOptions(string Scope = "global", string SearchQuery = "", string Notice = "")
{
    public static ToolsViewOptions From(IQueryCollection query) => new(
        query["scope"].ToString() is "local" ? "local" : query["scope"].ToString() is "all" ? "all" : "global",
        query["q"].ToString().Trim() is { Length: <= 100 } search ? search : "",
        query["notice"].ToString());
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
        var content = list.Error is not null
            ? $"<div class=\"empty-state error-state\" role=\"alert\"><h3>{title} could not be read</h3><p>{Encode(list.Error)}</p><a class=\"button\" href=\"{(options.Scope == "local" ? "/tools?scope=local" : "/tools")}\">Try again</a></div>"
            : list.Tools.Length == 0
                ? $"<div class=\"empty-state\"><h3>{(local ? (localInitialized ? "No local tools found" : "No local tools initialized") : "No global tools installed")}</h3><p class=\"muted\">{(local ? (localInitialized ? "No tools are declared in the tool manifests for this working directory." : "Initialize local tools to add a manifest in this working directory.") : "No global tools were reported for this user account.")}</p></div>"
                : RenderTable(list.Tools, local, title, localOwned);
        var filterInput = list.Tools.Length > 0
            ? $"<label class=\"filter-input\">{Icon("search", "xs")}<input type=\"search\" data-tools-table-filter placeholder=\"Filter {title.ToLowerInvariant()}...\" aria-label=\"Filter {title.ToLowerInvariant()}\"></label>"
            : "";
        var localAction = local ? RenderLocalManifestAction(localInitialized, localOwned, options) : "";
        return $$"""
            <section class="tool-panel-wrapper" role="tabpanel" aria-labelledby="{{id}}" id="{{id}}-panel">
              <div class="sr-only"><h2 id="{{id}}">{{title}}</h2></div>
              <div class="tools-toolbar">
                {{tabsHtml}}
                <div class="inventory-heading-actions">{{filterInput}}{{localAction}}</div>
              </div>
              <div class="inventory tool-panel">
                {{content}}
              </div>
            </section>
            """;
    }

    private static string RenderTable(DotnetTool[] tools, bool local, string title, bool localOwned) => $$"""
        <div class="table-scroll" role="region" aria-label="{{title}}" tabindex="0">
          <table class="tools-table" aria-label="{{title}}">
            <thead><tr><th scope="col">Tool</th><th scope="col">Command</th><th scope="col">Version</th><th scope="col">{{(local ? "Manifest" : "Scope")}}</th><th scope="col">Actions</th></tr></thead>
            <tbody>{{string.Concat(tools.OrderBy(tool => tool.PackageId, StringComparer.OrdinalIgnoreCase).Select(tool =>
            {
                var isToolLocal = local || tool.Manifest is not null;
                var monogram = GetMonogram(tool.PackageId);
                var colorIdx = GetColorIndex(tool.PackageId);
                var scopeBadge = isToolLocal
                    ? $"<span class=\"tool-scope-pill local\">{Icon("folder", "xs")} {(localOwned ? "Local" : "Local · inherited")}</span>"
                    : $"<span class=\"tool-scope-pill global\">{Icon("tools", "xs")} Global</span>";
                var primaryCmd = tool.Commands.FirstOrDefault() ?? tool.PackageId;
                var copyCmd = isToolLocal ? $"dotnet {primaryCmd}" : primaryCmd;
                var cliEquiv = $"dotnet tool update {(isToolLocal ? "--local" : "--global")} {tool.PackageId}";

                return $"""
                  <tr data-tool-scope="{(isToolLocal ? "local" : "global")}" data-tool-row data-search="{Encode($"{tool.PackageId} {tool.Version} {string.Join(' ', tool.Commands)} {tool.Manifest}")}">
                    <td>
                      <div class="tool-name">
                        <span class="tool-monogram monogram-{colorIdx}">{Encode(monogram)}</span>
                        <div>
                          <strong>{Encode(tool.PackageId)}</strong>
                          <span class="muted text-xs">{(isToolLocal ? "Workspace tool declaration" : "Installed CLI tool")}</span>
                        </div>
                      </div>
                    </td>
                    <td>
                      <div class="row" style="gap:6px;flex-wrap:wrap">
                        {string.Concat(tool.Commands.Select(command => $"<span class=\"tool-command-pill\" data-copy=\"{Encode(isToolLocal ? $"dotnet {command}" : command)}\" title=\"Click to copy command\"><code>{Encode(command)}</code>{Icon("clipboard", "xs")}</span>"))}
                      </div>
                    </td>
                    <td><fluent-badge appearance="tint" color="brand" class="version-badge sr-only">{Encode(tool.Version)}</fluent-badge><span class="v-current">{Encode(tool.Version)}</span></td>
                    <td>
                      <div class="tool-scope-cell">
                        {scopeBadge}
                        {(isToolLocal ? $"<code class=\"tool-manifest\">{Encode(tool.Manifest ?? "")}</code>" : "")}
                      </div>
                    </td>
                    <td>
                      <div class="tool-row-actions">
                        <a class="button sm ghost" href="https://www.nuget.org/packages/{Encode(tool.PackageId)}" target="_blank" rel="noopener" title="Open {Encode(tool.PackageId)} on NuGet.org">NuGet ↗</a>
                        <form method="post" action="/tools/uninstall" class="tool-action">
                          <input type="hidden" name="packageId" value="{Encode(tool.PackageId)}">
                          <input type="hidden" name="scope" value="{(isToolLocal ? "local" : "global")}">
                          <button class="button danger sm" type="submit">Uninstall</button>
                        </form>
                        <div class="tool-more-wrap">
                          <button type="button" class="button icon sm ghost tool-menu-trigger" data-tool-menu-btn aria-label="More actions for {Encode(tool.PackageId)}" aria-expanded="false">{Icon("more", "sm")}</button>
                          <div class="tool-context-menu" hidden>
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
        return $$"""
            <article class="search-result">
              <div class="search-result-main">
                <span class="tool-monogram monogram-{{colorIdx}}">{{Encode(monogram)}}</span>
                <div>
                  <div class="search-result-title-row">
                    <strong>{{Encode(result.PackageId)}}</strong>
                    <code class="muted">{{Encode(result.Version)}}</code>
                    {{(result.Verified ? "<span class=\"badge ok sm verified\">✓ Verified</span>" : "")}}
                  </div>
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
                      <button type="button" class="pill-badge active" data-sheet-scope="global">{{Icon("tools", "xs")}} Global · {{Encode(Environment.UserName)}}</button>
                      {{(canInstallLocal ? $"<button type=\"button\" class=\"pill-badge\" data-sheet-scope=\"local\">{Icon("folder", "xs")} This workspace</button>" : "<button type=\"button\" class=\"pill-badge\" disabled title=\"A local manifest is required to install into this workspace\">This workspace</button>")}}
                    </div>
                  </div>
                </div>

                <div id="sheet-results" class="sheet-results">
                  <div class="sheet-result-item active" data-pkg-id="dotnet-coverage" data-pkg-ver="18.1.0">
                    <span class="tool-monogram monogram-0">DC</span>
                    <div class="sheet-result-body">
                      <div class="sheet-result-top">
                        <strong>dotnet-coverage</strong>
                        <code class="muted">18.1.0</code>
                        <span class="badge ok sm">✓ Verified</span>
                      </div>
                      <span class="muted text-xs">Cross-platform tool to collect code coverage and merge reports.</span>
                      <div class="sheet-result-meta muted text-xs">
                        <span>👤 Microsoft</span>
                        <span>↓ 18.2M</span>
                        <span>⌘ dotnet-coverage</span>
                      </div>
                    </div>
                    <form method="post" action="/tools/install">
                      <input type="hidden" name="packageId" value="dotnet-coverage">
                      <input type="hidden" name="version" value="18.1.0">
                      <input type="hidden" name="scope" value="global" data-sheet-form-scope>
                      <button type="submit" class="button primary sm">+ Install</button>
                    </form>
                  </div>
                  <div class="sheet-result-item" data-pkg-id="dotnet-reportgenerator-globaltool" data-pkg-ver="5.4.18">
                    <span class="tool-monogram monogram-2">RG</span>
                    <div class="sheet-result-body">
                      <div class="sheet-result-top">
                        <strong>dotnet-reportgenerator-globaltool</strong>
                        <code class="muted">5.4.18</code>
                      </div>
                      <span class="muted text-xs">Turns coverage results into human-readable HTML reports.</span>
                      <div class="sheet-result-meta muted text-xs">
                        <span>👤 danielpalme</span>
                        <span>↓ 48.6M</span>
                        <span>⌘ reportgenerator</span>
                      </div>
                    </div>
                    <form method="post" action="/tools/install">
                      <input type="hidden" name="packageId" value="dotnet-reportgenerator-globaltool">
                      <input type="hidden" name="version" value="5.4.18">
                      <input type="hidden" name="scope" value="global" data-sheet-form-scope>
                      <button type="submit" class="button sm">+ Install</button>
                    </form>
                  </div>
                  <div class="sheet-result-item" data-pkg-id="coverlet.console" data-pkg-ver="6.0.4">
                    <span class="tool-monogram monogram-3">CC</span>
                    <div class="sheet-result-body">
                      <div class="sheet-result-top">
                        <strong>coverlet.console</strong>
                        <code class="muted">6.0.4</code>
                      </div>
                      <span class="muted text-xs">Coverlet as a global or local tool for any test runner.</span>
                      <div class="sheet-result-meta muted text-xs">
                        <span>👤 tonerdo</span>
                        <span>↓ 12.1M</span>
                        <span>⌘ coverlet</span>
                      </div>
                    </div>
                    <form method="post" action="/tools/install">
                      <input type="hidden" name="packageId" value="coverlet.console">
                      <input type="hidden" name="version" value="6.0.4">
                      <input type="hidden" name="scope" value="global" data-sheet-form-scope>
                      <button type="submit" class="button sm">+ Install</button>
                    </form>
                  </div>
                  <div class="sheet-result-item" data-pkg-id="dotnet-ef" data-pkg-ver="10.0.0">
                    <span class="tool-monogram monogram-1">EF</span>
                    <div class="sheet-result-body">
                      <div class="sheet-result-top">
                        <strong>dotnet-ef</strong>
                        <code class="muted">10.0.0</code>
                        <span class="badge ok sm">✓ Verified</span>
                      </div>
                      <span class="muted text-xs">Entity Framework Core command-line migrations and scaffolding tools.</span>
                      <div class="sheet-result-meta muted text-xs">
                        <span>👤 Microsoft</span>
                        <span>↓ 142.5M</span>
                        <span>⌘ dotnet-ef</span>
                      </div>
                    </div>
                    <form method="post" action="/tools/install">
                      <input type="hidden" name="packageId" value="dotnet-ef">
                      <input type="hidden" name="version" value="10.0.0">
                      <input type="hidden" name="scope" value="global" data-sheet-form-scope>
                      <button type="submit" class="button sm">+ Install</button>
                    </form>
                  </div>
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
        "uninstalled" => "<div class=\"action-notice\" role=\"status\">Tool uninstalled successfully.</div>",
        "initialized" => "<div class=\"action-notice\" role=\"status\">Local tools are now initialized in this working directory.</div>",
        "removed" => "<div class=\"action-notice\" role=\"status\">The local tool manifest was removed. Cached packages were left untouched.</div>",
        "failed" => "<div class=\"action-notice error-state\" role=\"alert\">The requested tool action failed. Check the SDK and package sources, then try again.</div>",
        _ => ""
    };
}

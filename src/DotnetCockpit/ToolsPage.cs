using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record ToolsViewOptions(string Scope = "global", string SearchQuery = "", string Notice = "")
{
    public static ToolsViewOptions From(IQueryCollection query) => new(
        query["scope"].ToString() is "local" ? "local" : "global",
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
        return PageLayout.Render($$"""
            <div class="breadcrumb">Environment <span>/</span> .NET Tools</div>
            <div class="page-heading">
              <div><h1>.NET Tools</h1><p class="muted intro">Global tools for your account and local tools for this workspace.</p></div>
              <a class="button primary" href="{{RefreshLink(options)}}">{{Icon("refresh")}} Refresh</a>
            </div>
            <div class="working-directory"><div class="working-directory-heading"><span class="context-icon">{{Icon("desktop")}}</span><div><strong>Working directory</strong><span class="muted">Local tools use manifests from this directory and its parents.</span></div><span class="context-badge">Current workspace</span></div><code>{{Encode(snapshot.WorkingDirectory)}}</code></div>
            {{RenderNotice(options.Notice)}}
            {{(snapshot.Status == "ready" ? RenderSearch(snapshot, options, search) : "")}}
            {{content}}
            <p class="scope-note muted">Local tools are declared in manifests for this working directory or its parents. This view does not check whether they have been restored.</p>
            """, "/tools", ".NET Tools | .NET Cockpit");
    }

    private static string RenderTabs(ToolsSnapshot snapshot, ToolsViewOptions options)
    {
        var local = options.Scope == "local";
        var selected = local ? snapshot.Local : snapshot.Global;
        return $$"""
            <div class="tool-tabs" role="tablist" aria-label="Tool scope">
              {{RenderTab("global", "Global", snapshot.Global, !local)}}
              {{RenderTab("local", "Local", snapshot.Local, local)}}
            </div>
            {{RenderList(local ? "Local tools" : "Global tools", local ? "local-tools" : "global-tools", selected, local, snapshot.LocalManifestInitialized, snapshot.LocalManifestOwned, options)}}
            """;
    }

    private static string RenderTab(string scope, string label, ToolList list, bool selected)
    {
        var count = list.Error is null ? $"<span class=\"tool-tab-count\">{list.Tools.Length}</span>" : "";
        return $"<a class=\"tool-tab\" href=\"/tools?scope={scope}\" role=\"tab\" aria-selected=\"{selected.ToString().ToLowerInvariant()}\" aria-controls=\"{scope}-tools-panel\">{label}{count}</a>";
    }

    private static string RefreshLink(ToolsViewOptions options) =>
        options.Scope == "local" ? "/tools?scope=local" : "/tools";

    private static string RenderList(string title, string id, ToolList list, bool local, bool localInitialized, bool localOwned, ToolsViewOptions options)
    {
        var content = list.Error is not null
            ? $"<div class=\"empty-state error-state\" role=\"alert\"><h3>{title} could not be read</h3><p>{Encode(list.Error)}</p><a class=\"button\" href=\"{(options.Scope == "local" ? "/tools?scope=local" : "/tools")}\">Try again</a></div>"
            : list.Tools.Length == 0
                ? $"<div class=\"empty-state\"><h3>{(local ? (localInitialized ? "No local tools found" : "No local tools initialized") : "No global tools installed")}</h3><p class=\"muted\">{(local ? (localInitialized ? "No tools are declared in the tool manifests for this working directory." : "Initialize local tools to add a manifest in this working directory.") : "No global tools were reported for this user account.")}</p></div>"
                : RenderTable(list.Tools, local, title);
        var localAction = local ? RenderLocalManifestAction(localInitialized, localOwned, options) : "";
        return $$"""
            <section class="inventory tool-panel" role="tabpanel" aria-labelledby="{{id}}" id="{{id}}-panel">
              <div class="inventory-heading"><div><h2 id="{{id}}">{{title}}</h2><p class="muted">{{(local ? "Tools declared in the manifests for this workspace." : "Tools installed for the current user account.")}}</p></div><div class="inventory-heading-actions">{{(list.Error is null ? $"<fluent-badge appearance=\"outline\" color=\"brand\" class=\"inventory-count\">{list.Tools.Length} tools</fluent-badge>" : "")}}{{localAction}}</div></div>
              {{content}}
            </section>
            """;
    }

    private static string RenderTable(DotnetTool[] tools, bool local, string title) => $$"""
        <div class="table-scroll" role="region" aria-label="{{title}}" tabindex="0">
          <table class="tools-table" aria-label="{{title}}">
            <thead><tr><th scope="col">Package ID</th><th scope="col">Version</th><th scope="col">Commands</th>{{(local ? "<th scope=\"col\">Manifest</th>" : "")}}<th scope="col">Actions</th></tr></thead>
            <tbody>{{string.Concat(tools.OrderBy(tool => tool.PackageId, StringComparer.OrdinalIgnoreCase).Select(tool => $"""
              <tr data-tool-scope="{(local ? "local" : "global")}">
                <td><div class="tool-name"><span class="tool-icon">{Icon("code")}</span><strong>{Encode(tool.PackageId)}</strong></div></td>
                <td><fluent-badge appearance="tint" color="brand" class="version-badge">{Encode(tool.Version)}</fluent-badge></td>
                <td>{string.Join(", ", tool.Commands.Select(command => $"<code>{Encode(command)}</code>"))}</td>
                {(local ? $"<td><code class=\"tool-manifest\">{Encode(tool.Manifest ?? "")}</code></td>" : "")}
                <td><form method="post" action="/tools/uninstall" class="tool-action"><input type="hidden" name="packageId" value="{Encode(tool.PackageId)}"><input type="hidden" name="scope" value="{(local ? "local" : "global")}"><button class="button danger" type="submit">Uninstall</button></form></td>
              </tr>
              """))}}</tbody>
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
            <section class="tool-search" aria-labelledby="tool-search-title">
              <div class="tool-search-heading"><div class="tool-search-title"><span class="search-icon">{{Icon("search")}}</span><div><label id="tool-search-title" for="tool-search-input">Search .NET tools</label><p class="muted">Find packages by ID or description.</p></div></div><span class="search-source">NuGet</span></div>
              <form method="get" action="/tools" class="tool-search-form">
                <div class="tool-search-controls"><input id="tool-search-input" name="q" value="{{Encode(options.SearchQuery)}}" placeholder="Package ID or description"><input type="hidden" name="scope" value="{{options.Scope}}"><button class="button primary" type="submit">Search</button></div>
              </form>
              {{results}}
            </section>
            """;
    }

    private static string RenderSearchResult(ToolSearchResult result, string query, string localChoice) => $$"""
        <article class="search-result">
          <div><strong>{{Encode(result.PackageId)}}</strong><span class="muted">{{Encode(result.Version)}} · {{Encode(result.Authors)}}</span></div>
          <div class="search-result-meta"><span>{{Encode(result.Downloads)}} downloads</span>{{(result.Verified ? "<span class=\"verified\">Verified</span>" : "")}}</div>
          <form method="post" action="/tools/install" class="tool-install-actions"><input type="hidden" name="packageId" value="{{Encode(result.PackageId)}}"><input type="hidden" name="version" value="{{Encode(result.Version)}}"><input type="hidden" name="q" value="{{Encode(query)}}"><button class="button" type="submit" name="scope" value="global">Install globally</button>{{localChoice}}</form>
        </article>
        """;

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

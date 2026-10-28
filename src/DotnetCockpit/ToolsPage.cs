using DotnetCockpit.Tools;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal static class ToolsPage
{
    public static string Render(ToolsSnapshot snapshot)
    {
        var content = snapshot.Status switch
        {
            "notFound" => "<section class=\"inventory empty-state\"><h2>No .NET installation found</h2><p class=\"muted\">The dotnet command is not available on PATH. A .NET SDK is required to list tools.</p></section>",
            "sdkMissing" => "<section class=\"inventory empty-state\"><h2>A .NET SDK is required</h2><p class=\"muted\">The dotnet host is available, but no SDK is installed. Install an SDK to list global and local tools.</p></section>",
            "error" => $"<section class=\"inventory empty-state error-state\" role=\"alert\"><h2>Tool information is unavailable</h2><p>{Encode(snapshot.Error ?? "The tools could not be read.")}</p><a class=\"button\" href=\"/tools\">Try again</a></section>",
            _ => RenderList("Global tools", "global-tools", snapshot.Global, false) + RenderList("Local tools", "local-tools", snapshot.Local, true)
        };
        return PageLayout.Render($$"""
            <div class="breadcrumb">Environment <span>/</span> <a href="/">Installations</a> <span>/</span> .NET Tools</div>
            <div class="page-heading">
              <div><h1>.NET Tools</h1><p class="muted intro">Global tools for your account and local tools for this workspace.</p></div>
              <a class="button primary" href="/tools">{{Icon("refresh")}} Refresh</a>
            </div>
            <div class="working-directory"><strong>Working directory</strong><code>{{Encode(snapshot.WorkingDirectory)}}</code></div>
            {{content}}
            <p class="scope-note muted">Local tools are declared in manifests for this working directory or its parents. This view does not check whether they have been restored.</p>
            """, "/tools", ".NET Tools | .NET Cockpit");
    }

    private static string RenderList(string title, string id, ToolList list, bool local)
    {
        var content = list.Error is not null
            ? $"<div class=\"empty-state error-state\" role=\"alert\"><h3>{title} could not be read</h3><p>{Encode(list.Error)}</p><a class=\"button\" href=\"/tools\">Try again</a></div>"
            : list.Tools.Length == 0
                ? $"<div class=\"empty-state\"><h3>{(local ? "No local tools found" : "No global tools installed")}</h3><p class=\"muted\">{(local ? "No tools are declared in the tool manifests for this working directory." : "No global tools were reported for this user account.")}</p></div>"
                : RenderTable(list.Tools, local, title);
        return $$"""
            <section class="inventory" aria-labelledby="{{id}}">
              <div class="inventory-heading"><div><h2 id="{{id}}">{{title}}</h2><p class="muted">{{(local ? "Tools declared in the manifests for this workspace." : "Tools installed for the current user account.")}}</p></div>{{(list.Error is null ? $"<fluent-badge appearance=\"outline\" color=\"brand\" class=\"inventory-count\">{list.Tools.Length} tools</fluent-badge>" : "")}}</div>
              {{content}}
            </section>
            """;
    }

    private static string RenderTable(DotnetTool[] tools, bool local, string title) => $$"""
        <div class="table-scroll" role="region" aria-label="{{title}}" tabindex="0">
          <table class="tools-table" aria-label="{{title}}">
            <thead><tr><th scope="col">Package ID</th><th scope="col">Version</th><th scope="col">Commands</th>{{(local ? "<th scope=\"col\">Manifest</th>" : "")}}</tr></thead>
            <tbody>{{string.Concat(tools.OrderBy(tool => tool.PackageId, StringComparer.OrdinalIgnoreCase).Select(tool => $"""
              <tr data-tool-scope="{(local ? "local" : "global")}">
                <td><strong>{Encode(tool.PackageId)}</strong></td>
                <td><fluent-badge appearance="tint" color="brand" class="version-badge">{Encode(tool.Version)}</fluent-badge></td>
                <td>{string.Join(", ", tool.Commands.Select(command => $"<code>{Encode(command)}</code>"))}</td>
                {(local ? $"<td><code class=\"tool-manifest\">{Encode(tool.Manifest ?? "")}</code></td>" : "")}
              </tr>
              """))}}</tbody>
          </table>
        </div>
        """;
}

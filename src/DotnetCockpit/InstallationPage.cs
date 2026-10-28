using System.Globalization;
using System.Text.Encodings.Web;
using DotnetCockpit.Installation;

namespace DotnetCockpit;

internal sealed record InstallationViewOptions(string Query = "", string Kind = "all", string Sort = "version", bool Ascending = false)
{
    public static InstallationViewOptions From(IQueryCollection query) => new(
        query["q"].ToString(),
        query["kind"].ToString() is "sdks" or "runtimes" ? query["kind"].ToString() : "all",
        query["sort"].ToString() is "name" or "path" ? query["sort"].ToString() : "version",
        query["direction"] == "asc");
}

internal static class InstallationPage
{
    private sealed record Row(string Kind, string Name, string Description, string Version, string Path);
    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create((left, right) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));

    public static string Render(InstallationSnapshot snapshot, InstallationViewOptions options)
    {
        var rows = FilterRows(snapshot, options);
        var total = snapshot.Sdks.Length + snapshot.Runtimes.Length;
        var content = snapshot.Status switch
        {
            "notFound" => """
                <div class="empty-state"><h3>No .NET installation found</h3><p>The dotnet command is not available on PATH.</p><p class="muted">.NET Cockpit runs independently. After installing .NET, restart Cockpit if PATH has changed.</p></div>
                """,
            "error" => $"<div class=\"empty-state error-state\" role=\"alert\"><h3>Installation information is unavailable</h3><p>{Encode(snapshot.Error ?? "The installation could not be read.")}</p><a class=\"button\" href=\"{Link(options)}\">Try again</a></div>",
            _ when total == 0 => "<div class=\"empty-state\"><h3>No SDKs or runtimes found</h3><p class=\"muted\">The dotnet host is available, but it reported no installed SDKs or runtimes.</p></div>",
            _ => RenderTable(rows, options)
        };

        return $$"""
            <!doctype html>
            <html lang="en" data-theme="auto">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="description" content="Your local .NET SDKs and runtimes, in one place.">
              <title>.NET Cockpit</title>
              <link rel="stylesheet" href="/assets/app.css">
              <script type="module" src="/assets/app.js"></script>
            </head>
            <body class="app">
              <a class="skip-link" href="#main">Skip to content</a>
              <header class="app-header">
                <div class="brand"><span class="brand-symbol">.NET</span><strong>.NET Cockpit</strong></div>
                <div class="header-actions">
                  <fluent-badge appearance="tint" color="success">Local workspace</fluent-badge>
                  <fluent-button id="theme-toggle" role="button" type="button" appearance="subtle" aria-label="Switch to dark mode" title="Switch to dark mode" hidden><span aria-hidden="true">◐</span></fluent-button>
                </div>
              </header>
              <div class="workspace">
                <aside class="sidebar" aria-label="Workspace navigation">
                  <span class="sidebar-label">ENVIRONMENT</span>
                  <a href="/" class="nav-current" aria-current="page">{{Icon("desktop")}} Installations</a>
                  <div class="sidebar-footer"><span class="status-dot"></span>Running on your machine</div>
                </aside>
                <main id="main" tabindex="-1">
                  <div class="breadcrumb">Environment <span>/</span> Installations</div>
                  <div class="page-heading">
                    <div><h1>Your .NET environment</h1><p class="muted intro">Installed SDKs and runtimes, in one place.</p></div>
                    <a class="button primary" href="{{Link(options)}}">{{Icon("refresh")}} Refresh</a>
                  </div>
                  <section class="summary-grid" aria-label="Installation summary">
                    {{SummaryCard("Installed SDKs", snapshot.Sdks.Length.ToString(CultureInfo.InvariantCulture), "Build and develop applications", "code")}}
                    {{SummaryCard("Installed runtimes", snapshot.Runtimes.Length.ToString(CultureInfo.InvariantCulture), "Run .NET applications", "server")}}
                    {{SummaryCard("This machine", snapshot.Architecture.ToUpperInvariant(), snapshot.OperatingSystem, "desktop")}}
                  </section>
                  <section class="inventory" id="inventory" aria-labelledby="inventory-title">
                    <div class="inventory-heading"><div><h2 id="inventory-title">Installed versions</h2><p class="muted">The .NET installation available on your PATH.</p></div><fluent-badge appearance="outline" color="brand" class="inventory-count">{{total}} installed</fluent-badge></div>
                    <div class="inventory-toolbar">
                      <nav class="type-filter" aria-label="Installation type">
                        {{TypeLink("All versions", "all", options)}}{{TypeLink("SDKs", "sdks", options)}}{{TypeLink("Runtimes", "runtimes", options)}}
                      </nav>
                      <form class="filter-form" method="get" action="/#inventory" role="search">
                        <input type="hidden" name="kind" value="{{Encode(options.Kind)}}">
                        <input type="hidden" name="sort" value="{{Encode(options.Sort)}}">
                        <input type="hidden" name="direction" value="{{(options.Ascending ? "asc" : "desc")}}">
                        <label class="filter-input">{{Icon("search")}}<input type="search" name="q" value="{{Encode(options.Query)}}" placeholder="Filter versions, names, or paths" aria-label="Filter installed versions"></label>
                        <button class="button" type="submit">Filter</button>
                      </form>
                    </div>
                    {{content}}
                    <div class="inventory-footer"><span>{{rows.Length}} of {{total}} installations shown</span><span>Checked <time datetime="{{snapshot.CheckedAt.ToString("O", CultureInfo.InvariantCulture)}}">{{snapshot.CheckedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}}</time></span></div>
                  </section>
                  {{(snapshot.ExecutablePath is null ? "" : $"<div class=\"host-info\"><span class=\"status-dot\"></span><span>dotnet host</span><code>{Encode(snapshot.ExecutablePath)}</code></div>")}}
                  <p class="scope-note muted">Locations include the version subfolder. This view lists the SDKs and runtimes reported by the selected dotnet host. Separate installations and other architectures may not appear here.</p>
                </main>
              </div>
            </body>
            </html>
            """;
    }

    private static Row[] FilterRows(InstallationSnapshot snapshot, InstallationViewOptions options)
    {
        var rows = snapshot.Sdks.Select(sdk => new Row("sdk", ".NET SDK", "Software development kit", sdk.Version, sdk.InstallationPath))
            .Concat(snapshot.Runtimes.Select(runtime => new Row("runtime", RuntimeName(runtime.Name), runtime.Name, runtime.Version, runtime.InstallationPath)));
        var terms = options.Query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        rows = rows.Where(row => (options.Kind == "all" || (options.Kind == "sdks" ? row.Kind == "sdk" : row.Kind == "runtime")) &&
            terms.All(term => $"{row.Name} {row.Description} {row.Version} {row.Path}".Contains(term, StringComparison.OrdinalIgnoreCase)));
        Func<Row, string> key = options.Sort switch { "name" => row => row.Name, "path" => row => row.Path, _ => row => row.Version };
        var comparer = options.Sort == "version" ? VersionComparer : StringComparer.OrdinalIgnoreCase;
        return (options.Ascending ? rows.OrderBy(key, comparer) : rows.OrderByDescending(key, comparer))
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string RenderTable(Row[] rows, InstallationViewOptions options) => $$"""
        <div class="table-scroll" role="region" aria-label="Installed .NET versions" tabindex="0">
          <table aria-label="Installed .NET versions">
            <thead><tr>{{SortHeader("Installation", "name", options)}}{{SortHeader("Version", "version", options)}}{{SortHeader("Location", "path", options)}}</tr></thead>
            <tbody>{{string.Concat(rows.Select(row => $"""
              <tr data-kind="{row.Kind}">
                <td><div class="installation-name"><span class="installation-icon">{Icon(row.Kind == "sdk" ? "code" : "server")}</span><div><strong>{Encode(row.Name)}</strong><span class="muted framework-name">{Encode(row.Description)}</span></div></div></td>
                <td><fluent-badge appearance="tint" color="{(row.Kind == "sdk" ? "brand" : "informative")}" class="version-badge">{Encode(row.Version)}</fluent-badge></td>
                <td><code class="installation-path">{Encode(row.Path)}</code></td>
              </tr>
              """))}}</tbody>
          </table>
          {{(rows.Length == 0 ? "<div class=\"empty-state\"><h3>No matching versions</h3><p class=\"muted\">Try another filter or switch to All versions.</p><a class=\"button\" href=\"/#inventory\">Clear filters</a></div>" : "")}}
        </div>
        """;

    private static string SummaryCard(string label, string value, string description, string icon) =>
        $"<div class=\"summary-card\"><div class=\"summary-top\"><span>{Encode(label)}</span>{Icon(icon)}</div><strong class=\"summary-number\">{Encode(value)}</strong><span class=\"muted\">{Encode(description)}</span></div>";

    private static string TypeLink(string label, string kind, InstallationViewOptions options) =>
        $"<a href=\"{Link(options with { Kind = kind })}\"{(options.Kind == kind ? " aria-current=\"page\"" : "")}>{label}</a>";

    private static string SortHeader(string label, string column, InstallationViewOptions options)
    {
        var active = options.Sort == column;
        var direction = active ? options.Ascending ? "ascending" : "descending" : "none";
        return $"<th scope=\"col\" aria-sort=\"{direction}\"><a href=\"{Link(options with { Sort = column, Ascending = !active || !options.Ascending })}\">{label}{(active ? options.Ascending ? " ↑" : " ↓" : "")}</a></th>";
    }

    private static string Link(InstallationViewOptions options) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}#inventory");

    private static string RuntimeName(string name) => name switch
    {
        "Microsoft.NETCore.App" => ".NET Runtime",
        "Microsoft.AspNetCore.App" => "ASP.NET Core Runtime",
        "Microsoft.WindowsDesktop.App" => "Windows Desktop Runtime",
        _ => name
    };

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);

    private static string Icon(string name)
    {
        var path = name switch
        {
            "code" => "<path d=\"m7 5-5 5 5 5m6-10 5 5-5 5M12 2 8 18\"/>",
            "server" => "<rect x=\"4\" y=\"2\" width=\"12\" height=\"16\" rx=\"2\"/><path d=\"M7 6h6M7 9h6M7 14h2\"/>",
            "refresh" => "<path d=\"M17 7a7 7 0 1 0 0 6M17 2v5h-5\"/>",
            "search" => "<circle cx=\"8.5\" cy=\"8.5\" r=\"5.5\"/><path d=\"m13 13 5 5\"/>",
            _ => "<rect x=\"2\" y=\"3\" width=\"16\" height=\"11\" rx=\"1\"/><path d=\"M7 18h6M10 14v4\"/>"
        };
        return $"<svg viewBox=\"0 0 20 20\" aria-hidden=\"true\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{path}</svg>";
    }
}

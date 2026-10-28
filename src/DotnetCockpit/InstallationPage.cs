using System.Globalization;
using DotnetCockpit.Installation;
using static DotnetCockpit.PageLayout;

namespace DotnetCockpit;

internal sealed record InstallationViewOptions(string Query = "", string Kind = "all", string Sort = "version", bool Ascending = false, string Notice = "")
{
    public static InstallationViewOptions From(IQueryCollection query) => new(
        query["q"].ToString(),
        query["kind"].ToString() is "sdks" or "runtimes" ? query["kind"].ToString() : "all",
        query["sort"].ToString() is "name" or "path" ? query["sort"].ToString() : "version",
        query["direction"] == "asc",
        query["notice"].ToString());
}

internal static class InstallationPage
{
    private sealed record Row(string Kind, string Name, string Description, string Version, IReadOnlyList<string> Paths, bool CanRemove)
    {
        public string Path => string.Join(" ", Paths);
    }

    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create((left, right) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));

    public static string Render(InstallationSnapshot snapshot, InstallationViewOptions options)
    {
        var allRows = BuildRows(snapshot);
        var rows = FilterRows(allRows, options);
        var total = allRows.Length;
        var runtimeCount = allRows.Count(row => row.Kind == "runtime");
        var inventoryDescription = snapshot.ExecutablePath is null
            ? "Versions reported by dotnet-core-uninstall."
            : "The .NET installation available on your PATH.";
        var content = snapshot.Status switch
        {
            "notFound" => """
                <div class="empty-state"><h3>No .NET installation found</h3><p>The dotnet command is not available on PATH.</p><p class="muted">.NET Cockpit runs independently. After installing .NET, restart Cockpit if PATH has changed.</p></div>
                """,
            "error" => $"<div class=\"empty-state error-state\" role=\"alert\"><h3>Installation information is unavailable</h3><p>{Encode(snapshot.Error ?? "The installation could not be read.")}</p><a class=\"button\" href=\"{RefreshLink(options, snapshot.CheckedAt)}\">Try again</a></div>",
            _ when total == 0 => $"<div class=\"empty-state\"><h3>No SDKs or runtimes found</h3><p class=\"muted\">{(snapshot.ExecutablePath is null ? "dotnet-core-uninstall reported no removable SDKs or runtimes." : "The dotnet host reported no installed SDKs or runtimes.")}</p></div>",
            _ => RenderTable(rows, options)
        };

        return PageLayout.Render($$"""
                  <div class="breadcrumb">Environment <span>/</span> Installations</div>
                  <div class="page-heading">
                    <div><h1>Your .NET environment</h1><p class="muted intro">Installed SDKs and runtimes, in one place.</p></div>
                    <a class="button primary" href="{{RefreshLink(options, snapshot.CheckedAt)}}">{{Icon("refresh")}} Refresh</a>
                  </div>
                  {{RenderNotice(options.Notice)}}
                  <section class="summary-grid" aria-label="Installation summary">
                    {{SummaryCard("Installed SDKs", snapshot.Sdks.Length.ToString(CultureInfo.InvariantCulture), "Build and develop applications", "code")}}
                    {{SummaryCard("Installed runtimes", runtimeCount.ToString(CultureInfo.InvariantCulture), "Run .NET applications", "server")}}
                    {{SummaryCard("This machine", snapshot.Architecture.ToUpperInvariant(), snapshot.OperatingSystem, "desktop")}}
                  </section>
                  <section class="inventory" id="inventory" aria-labelledby="inventory-title">
                    <div class="inventory-heading"><div><h2 id="inventory-title">Installed versions</h2><p class="muted">{{inventoryDescription}}</p></div><fluent-badge appearance="outline" color="brand" class="inventory-count">{{total}} installed</fluent-badge></div>
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
                  <p class="scope-note muted">Locations include the version subfolder. When dotnet-core-uninstall is installed, only versions it can remove are shown; runtime frameworks that share a version are grouped together. Separate installations and other architectures may not appear here.</p>
            """, "/");
    }

    private static Row[] BuildRows(InstallationSnapshot snapshot)
    {
        var sdkRows = snapshot.Sdks.Select(sdk =>
        {
            var path = sdk.InstallationPath;
            return new Row("sdk", ".NET SDK", "Software development kit", sdk.Version,
                string.IsNullOrWhiteSpace(path) ? [] : [path], sdk.CanRemove);
        });
        var runtimeRows = snapshot.Runtimes
            .GroupBy(runtime => runtime.Version, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var runtimes = group.ToArray();
                var names = runtimes.Select(runtime => RuntimeName(runtime.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var descriptions = runtimes.Select(runtime => runtime.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var paths = runtimes.Select(runtime => runtime.InstallationPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return new Row("runtime", names.Length == 1 ? names[0] : ".NET Runtime", string.Join(" · ", descriptions), group.Key, paths, runtimes.Any(runtime => runtime.CanRemove));
            });
        return sdkRows.Concat(runtimeRows).ToArray();
    }

    private static Row[] FilterRows(Row[] allRows, InstallationViewOptions options)
    {
        var rows = allRows.AsEnumerable();
        var terms = options.Query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        rows = rows.Where(row => (options.Kind == "all" || (options.Kind == "sdks" ? row.Kind == "sdk" : row.Kind == "runtime")) &&
            terms.All(term => $"{row.Name} {row.Description} {row.Version} {row.Path}".Contains(term, StringComparison.OrdinalIgnoreCase)));
        Func<Row, string> key = options.Sort switch { "name" => row => row.Name, "path" => row => row.Path, _ => row => row.Version };
        var comparer = options.Sort == "version" ? VersionComparer : StringComparer.OrdinalIgnoreCase;
        return (options.Ascending ? rows.OrderBy(key, comparer) : rows.OrderByDescending(key, comparer))
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string RenderTable(Row[] rows, InstallationViewOptions options)
    {
        var showActions = rows.Any(row => row.CanRemove);
        return $$"""
            <div class="table-scroll" role="region" aria-label="Installed .NET versions" tabindex="0">
              <table aria-label="Installed .NET versions">
                <thead><tr>{{SortHeader("Installation", "name", options)}}{{SortHeader("Version", "version", options)}}{{SortHeader("Location", "path", options)}}{{(showActions ? "<th scope=\"col\">Actions</th>" : "")}}</tr></thead>
                <tbody>{{string.Concat(rows.Select(row => $"""
                  <tr data-kind="{row.Kind}">
                    <td><div class="installation-name"><span class="installation-icon">{Icon(row.Kind == "sdk" ? "code" : "server")}</span><div><strong>{Encode(row.Name)}</strong><span class="muted framework-name">{Encode(row.Description)}</span></div></div></td>
                    <td><fluent-badge appearance="tint" color="{(row.Kind == "sdk" ? "brand" : "informative")}" class="version-badge">{Encode(row.Version)}</fluent-badge></td>
                    <td><div class="installation-paths">{string.Concat(row.Paths.Select(path => $"<code class=\"installation-path\">{Encode(path)}</code>"))}</div></td>
                    {(showActions ? $"<td class=\"installation-actions\">{RemoveAction(row, options)}</td>" : "")}
                  </tr>
                  """))}}</tbody>
              </table>
              {{(rows.Length == 0 ? "<div class=\"empty-state\"><h3>No matching versions</h3><p class=\"muted\">Try another filter or switch to All versions.</p><a class=\"button\" href=\"/#inventory\">Clear filters</a></div>" : "")}}
            </div>
            """;
    }

    private static string SummaryCard(string label, string value, string description, string icon) =>
        $"<div class=\"summary-card\"><div class=\"summary-top\"><span>{Encode(label)}</span>{Icon(icon)}</div><strong class=\"summary-number\">{Encode(value)}</strong><span class=\"muted\">{Encode(description)}</span></div>";

    private static string RemoveAction(Row row, InstallationViewOptions options) => !row.CanRemove
        ? ""
        : $"<form method=\"post\" action=\"/installation/remove\"><input type=\"hidden\" name=\"version\" value=\"{Encode(row.Version)}\"><input type=\"hidden\" name=\"kind\" value=\"{row.Kind}\"><input type=\"hidden\" name=\"viewKind\" value=\"{options.Kind}\"><input type=\"hidden\" name=\"q\" value=\"{Encode(options.Query)}\"><input type=\"hidden\" name=\"sort\" value=\"{options.Sort}\"><input type=\"hidden\" name=\"direction\" value=\"{(options.Ascending ? "asc" : "desc")}\"><button class=\"button danger\" type=\"submit\">Remove</button></form>";

    private static string RenderNotice(string notice) => notice switch
    {
        "removed" => "<div class=\"action-notice\" role=\"status\">The selected .NET installation was removed.</div>",
        "remove-failed" => "<div class=\"action-notice error-state\" role=\"alert\">The selected .NET installation could not be removed. Check the permissions and try again.</div>",
        _ => ""
    };

    private static string TypeLink(string label, string kind, InstallationViewOptions options) =>
        $"<a href=\"{Link(options with { Kind = kind })}\"{(options.Kind == kind ? " aria-current=\"page\"" : "")}>{label}</a>";

    private static string SortHeader(string label, string column, InstallationViewOptions options)
    {
        var active = options.Sort == column;
        var direction = active ? options.Ascending ? "ascending" : "descending" : "none";
        return $"<th scope=\"col\" aria-sort=\"{direction}\"><a href=\"{Link(options with { Sort = column, Ascending = !active || !options.Ascending })}\">{label}{(active ? options.Ascending ? " ↑" : " ↓" : "")}</a></th>";
    }

    private static string RefreshLink(InstallationViewOptions options, DateTimeOffset checkedAt) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}&refresh={checkedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}#inventory");

    private static string Link(InstallationViewOptions options) => Encode(
        $"/?q={Uri.EscapeDataString(options.Query)}&kind={options.Kind}&sort={options.Sort}&direction={(options.Ascending ? "asc" : "desc")}#inventory");

    private static string RuntimeName(string name) => name switch
    {
        "Microsoft.NETCore.App" => ".NET Runtime",
        "Microsoft.AspNetCore.App" => "ASP.NET Core Runtime",
        "Microsoft.WindowsDesktop.App" => "Windows Desktop Runtime",
        _ => name
    };
}

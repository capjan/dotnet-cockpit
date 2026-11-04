using System.Text.Encodings.Web;

namespace DotnetCockpit;

internal static class PageLayout
{
    /// <summary>Set at startup from the settings file. Tests and tools that render pages directly get the defaults.</summary>
    public static Func<AppSettings> CurrentSettings { get; set; } = () => new();

    private static string WorkspaceName =>
        Path.GetFileName(Environment.CurrentDirectory.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : Environment.CurrentDirectory;

    public static string Render(string content, string currentPath, string title = ".NET Cockpit", int issueCount = 0, int? versionsCount = null, int? toolsCount = null, string? refreshHref = null) => $$"""
        <!doctype html>
        <html lang="en" data-theme="{{CurrentSettings().Theme}}">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="color-scheme" content="light dark">
          <meta name="description" content="Your local .NET environment, in one place.">
          <title>{{Encode(title)}}</title>
          <link rel="icon" type="image/png" sizes="32x32" href="/assets/logo-32.png">
          <link rel="stylesheet" href="/assets/app.css">
          <script type="module" src="/assets/app.js"></script>
        </head>
        <body class="app-body">
          <a class="skip-link" href="#main">Skip to content</a>
          <div class="app">
            <aside class="rail" aria-label="Workspace navigation">
              <div class="brand">
                <img src="/assets/logo.png" alt=".NET Cockpit logo">
                <div>
                  <b>.NET Cockpit</b>
                  <small>Runs on this machine</small>
                </div>
              </div>

              <div class="rail-label">Environment</div>
              <a href="/overview" class="nav-link"{{(currentPath == "/overview" ? " aria-current=\"page\"" : "")}}>{{Icon("dashboard")}} Overview {{(issueCount > 0 ? $"<span class=\"badge warn sm nav-badge\"><span class=\"sr-only\">, </span>{issueCount}<span class=\"sr-only\"> to review</span></span>" : "")}}</a>
              <a href="/" class="nav-link"{{(currentPath is "/" or "/versions" ? " aria-current=\"page\"" : "")}} >{{Icon("versions")}} Versions{{(versionsCount is not null ? $"<span class=\"nav-count\" aria-hidden=\"true\">{versionsCount}</span>" : "")}}</a>
              <a href="/tools" class="nav-link"{{(currentPath == "/tools" ? " aria-current=\"page\"" : "")}} >{{Icon("tools")}} Tools{{(toolsCount is not null ? $"<span class=\"nav-count\" aria-hidden=\"true\">{toolsCount}</span>" : "")}}</a>

              <div class="rail-label">Cockpit</div>
              <a href="/setup" class="nav-link"{{(currentPath == "/setup" ? " aria-current=\"page\"" : "")}}>{{Icon("settings")}} Settings</a>

              <div class="rail-foot">
                <div class="ws-card">
                  <div class="k">{{Icon("folder", "xs")}} Workspace</div>
                  <b>{{Encode(WorkspaceName)}}</b>
                  <code title="{{Encode(Environment.CurrentDirectory)}}"><bdi>{{Encode(Environment.CurrentDirectory)}}</bdi></code>
                </div>
              </div>
            </aside>

            <div class="main">
              <header class="topbar">
                <div class="crumbs">
                  <b>{{GetPageName(currentPath)}}</b>
                </div>
                <button type="button" id="cmdk-trigger" class="cmdk" aria-label="Open command palette" hidden data-js-only>
                  {{Icon("search", "sm")}}
                  <span>Search versions, tools and actions</span>
                  <kbd>⌘K</kbd>
                </button>
                <a class="checked" href="{{Encode(refreshHref ?? (currentPath == "/confirm" ? "/overview" : currentPath))}}" title="Check again" aria-label="Refresh. Checked {{DateTime.Now.ToString("HH:mm")}}">
                  {{Icon("refresh", "xs")}}
                  <span>Checked <time datetime="{{DateTimeOffset.Now:O}}">{{DateTime.Now.ToString("HH:mm")}}</time></span>
                </a>
                {{ThemeSwitch()}}
              </header>

              <main id="main" class="content" tabindex="-1">
                {{content}}
              </main>
            </div>
          </div>

          <div id="cmdk-scrim" class="scrim" hidden></div>
          <div id="cmdk-dialog" class="palette" role="dialog" aria-modal="true" aria-label="Command palette" hidden>
            <div class="q">
              {{Icon("search", "sm")}}
              <input id="cmdk-input" type="text" placeholder="Search actions, versions and tools" autocomplete="off" aria-label="Search actions, versions and tools">
              <kbd>ESC</kbd>
            </div>
            <div id="cmdk-dynamic-grp" class="grp" hidden>
              <div class="grp-k">Installed</div>
              <div id="cmdk-dynamic-list"></div>
            </div>
            <div class="grp">
              <div class="grp-k">Actions</div>
              <a class="opt" href="/tools?add=1">
                {{Icon("plus", "sm")}}
                <div><b>Add a .NET tool</b> <small>· Search and install from NuGet.org</small></div>
                <span class="badge">NuGet</span>
              </a>
              <a class="opt" href="/?install=1">
                {{Icon("download", "sm")}}
                <div><b>Install new .NET SDK or Runtime</b> <small>· Official macOS .pkg catalog</small></div>
                <span class="badge brand">Download</span>
              </a>
              <a class="opt" href="/preflight">
                {{Icon("sparkle", "sm")}}
                <div><b>Run Pre-flight Diagnostics</b> <small>· Verify .NET host, SDKs and workspace</small></div>
                <span class="badge brand">Check</span>
              </a>
              <a class="opt" href="/?cleanup=1">
                {{Icon("sparkle", "sm")}}
                <div><b>Clean up installations</b> <small>· Remove older versions, previews or unsupported releases</small></div>
              </a>
              <a class="opt" href="/setup#uninstall-tool-title">
                {{Icon("trash", "sm")}}
                <div><b>Settings → Removal support</b> <small>· Manage dotnet-core-uninstall</small></div>
              </a>
            </div>
            <div class="grp">
              <div class="grp-k">Navigation</div>
              <a class="opt" href="/overview">
                {{Icon("dashboard", "sm")}}
                <div><b>Go to Overview</b> <small>· Environment status, workspace &amp; lifecycle timeline</small></div>
                <kbd>G O</kbd>
              </a>
              <a class="opt" href="/">
                {{Icon("versions", "sm")}}
                <div><b>Go to Versions</b> <small>· Installed SDKs and Runtimes</small></div>
                <kbd>G V</kbd>
              </a>
              <a class="opt" href="/tools">
                {{Icon("tools", "sm")}}
                <div><b>Go to .NET Tools</b> <small>· Global &amp; workspace local tools</small></div>
                <kbd>G T</kbd>
              </a>
              <a class="opt" href="/setup">
                {{Icon("settings", "sm")}}
                <div><b>Go to Settings</b> <small>· Appearance, removal support &amp; privacy policy</small></div>
                <kbd>G S</kbd>
              </a>
              <a class="opt" href="/preflight">
                {{Icon("pulse", "sm")}}
                <div><b>Go to Pre-flight Check</b> <small>· System diagnostics</small></div>
                <kbd>G P</kbd>
              </a>
            </div>
            <div id="cmdk-empty" class="cmdk-empty" hidden>
              <p>No matches.</p>
              <a class="button sm" id="cmdk-nuget" href="/tools?add=1">Search NuGet.org</a>
            </div>
            <div class="foot">
              <span><kbd>↑</kbd> <kbd>↓</kbd> navigate</span>
              <span><kbd>↵</kbd> select</span>
              <span><kbd>ESC</kbd> close</span>
            </div>
          </div>
          {{ConfirmActions.Dialog}}
        </body>
        </html>
        """;

    private static string GetPageName(string path) => path switch
    {
        "/overview" => "Overview",
        "/tools" => "Tools",
        "/setup" => "Settings",
        "/confirm" => "Confirm",
        "/preflight" => "Pre-flight Check",
        _ => "Versions"
    };

    public static string Encode(string value) => HtmlEncoder.Default.Encode(value);

    /// <summary>Three-state appearance switch. A plain form, so it also works without JavaScript.</summary>
    public static string ThemeSwitch(bool showLabels = false, string extraClass = "")
    {
        var current = CurrentSettings().Theme;
        string Option(string value, string label, string title) =>
            $"<button type=\"submit\" name=\"theme\" value=\"{value}\" class=\"theme-switch-btn{(current == value ? " active" : "")}\" data-theme-pref=\"{value}\" aria-pressed=\"{(current == value ? "true" : "false")}\" title=\"{title}\">{Icon($"theme-{value}", "sm")}<span class=\"{(showLabels ? "" : "sr-only")}\">{label}</span></button>";
        return $"""
            <form method="post" action="/settings/theme" class="theme-switch {extraClass}" role="group" aria-label="Appearance" data-theme-switch>
              {Option("auto", "Auto", "Automatic (follow system)")}{Option("light", "Light", "Light")}{Option("dark", "Dark", "Dark")}
            </form>
            """;
    }

    public static string Icon(string name, string cls = "")
    {
        var extraCls = string.IsNullOrEmpty(cls) ? "" : $" {cls}";
        var path = name switch
        {
            "dashboard" => "<path d=\"M12 14v4M3.34 17a10 10 0 1 1 17.32 0\"/><path d=\"m4.93 4.93 4.24 4.24\"/>",
            "versions" => "<path d=\"m12 3 9 5-9 5-9-5 9-5Z\"/><path d=\"m3 13 9 5 9-5\"/>",
            "tools" or "code" => "<path d=\"M14.7 6.3a4 4 0 0 0-5.4 5.2L3.6 17.2a1.6 1.6 0 0 0 2.3 2.3l5.7-5.7a4 4 0 0 0 5.2-5.4l-2.5 2.5-2.1-.5-.5-2.1 2.5-2.5Z\"/>",
            "server" or "cpu" => "<rect x=\"4\" y=\"2\" width=\"12\" height=\"16\" rx=\"2\"/><path d=\"M7 6h6M7 9h6M7 14h2\"/>",
            "refresh" => "<path d=\"M21 12a9 9 0 1 1-2.6-6.4L21 8\"/><path d=\"M21 3v5h-5\"/>",
            "download" => "<path d=\"M12 4v11m0 0 4.5-4.5M12 15l-4.5-4.5\"/><path d=\"M4 17v2a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-2\"/>",
            "search" => "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-3.5-3.5\"/>",
            "settings" => "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z\"/>",
            "folder" => "<path d=\"M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z\"/>",
            "clock" => "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>",
            "moon" => "<path d=\"M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5Z\"/>",
            "sun" => "<circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2v2M12 20v2m-7.1-14.1 1.4 1.4m11.4 11.4 1.4 1.4M2 12h2m16 0h2M4.9 19.1l1.4-1.4m11.4-11.4 1.4-1.4\"/>",
            "desktop" => "<rect x=\"2\" y=\"3\" width=\"16\" height=\"11\" rx=\"1\"/><path d=\"M7 18h6M10 14v4\"/>",
            "pulse" => "<polyline points=\"22 12 18 12 15 21 9 3 6 12 2 12\"/>",
            "check" => "<polyline points=\"20 6 9 17 4 12\"/>",
            "warn" => "<path d=\"m21.7 18-8-14a2 2 0 0 0-3.4 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3Z\"/><path d=\"M12 9v4M12 17h.01\"/>",
            "sparkle" => "<path d=\"m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3L12 3Z\"/>",
            "lock" => "<rect x=\"3\" y=\"11\" width=\"18\" height=\"11\" rx=\"2\" ry=\"2\"/><path d=\"M7 11V7a5 5 0 0 1 10 0v4\"/>",
            "trash" => "<path d=\"M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2\"/>",
            "clipboard" or "copy" => "<rect width=\"14\" height=\"14\" x=\"8\" y=\"8\" rx=\"2\" ry=\"2\"/><path d=\"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2\"/>",
            "shield" => "<path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\"/>",
            "shieldok" => "<path d=\"M12 3 4 6v6c0 5 3.5 8.5 8 9 4.5-.5 8-4 8-9V6l-8-3Z\"/><path d=\"m9 12 2 2 4-4\"/>",
            "globe" => "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18\"/>",
            "package" => "<path d=\"m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z\"/><path d=\"m12 12 8-4.5M12 12v9M12 12 4 7.5\"/>",
            "chevron-right" => "<polyline points=\"9 18 15 12 9 6\"/>",
            "chevd" => "<path d=\"m6 9 6 6 6-6\"/>",
            "more" => "<circle cx=\"5\" cy=\"12\" r=\"1.4\" fill=\"currentColor\"/><circle cx=\"12\" cy=\"12\" r=\"1.4\" fill=\"currentColor\"/><circle cx=\"19\" cy=\"12\" r=\"1.4\" fill=\"currentColor\"/>",
            "external" => "<path d=\"M14 4h6v6M20 4l-9 9\"/><path d=\"M10 6H5a1 1 0 0 0-1 1v12a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-5\"/>",
            "terminal" => "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"m7 9 3 3-3 3M12 15h5\"/>",
            "cloud" => "<path d=\"M17.5 19H9a7 7 0 1 1 6.7-9h.8a5.5 5.5 0 0 1 1 11Z\"/>",
            "plus" => "<path d=\"M12 5v14M5 12h14\"/>",
            "x" => "<path d=\"M6 6l12 12M18 6 6 18\"/>",
            "theme-auto" => "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 3a9 9 0 0 1 0 18Z\" fill=\"currentColor\"/>",
            "theme-light" => "<circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2v2.5M12 19.5V22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M2 12h2.5M19.5 12H22M4.9 19.1l1.8-1.8M17.3 6.7l1.8-1.8\"/>",
            "theme-dark" => "<path d=\"M20.5 14.6A8.5 8.5 0 0 1 9.4 3.5a8.5 8.5 0 1 0 11.1 11.1Z\" fill=\"currentColor\"/>",
            _ => "<circle cx=\"12\" cy=\"12\" r=\"9\"/>"
        };
        return $"<svg class=\"i{extraCls}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{path}</svg>";
    }
}

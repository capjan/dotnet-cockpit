using System.Text.Encodings.Web;

namespace DotnetCockpit;

internal static class PageLayout
{
    public static string Render(string content, string currentPath, string title = ".NET Cockpit", int issueCount = 0) => $$"""
        <!doctype html>
        <html lang="en" data-theme="auto">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
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
                  <small>v2.0 · local</small>
                </div>
              </div>

              <div class="rail-label">Environment</div>
              <a href="/overview" class="nav-link"{{(currentPath == "/overview" ? " aria-current=\"page\"" : "")}}>{{Icon("dashboard")}} Overview <span class="badge warn sm">3</span></a>
              <a href="/" class="nav-link"{{(currentPath is "/" or "/versions" ? " aria-current=\"page\"" : "")}} aria-label="Installations">{{Icon("versions")}} Versions <span class="badge info sm">8</span></a>
              <a href="/tools" class="nav-link"{{(currentPath == "/tools" ? " aria-current=\"page\"" : "")}} aria-label=".NET Tools">{{Icon("tools")}} Tools <span class="badge info sm">7</span></a>

              <div class="rail-label">Cockpit</div>
              <a href="/setup" class="nav-link"{{(currentPath == "/setup" ? " aria-current=\"page\"" : "")}}>{{Icon("settings")}} Setup</a>
              {{(issueCount > 0 ? $"<a href=\"/preflight\" class=\"nav-link pf-nav-link\">{Icon("pulse")} Pre-flight <span class=\"badge warn pf-issue-badge\">{issueCount}</span></a>" : "")}}

              <div class="rail-foot">
                <div class="ws-card">
                  <div class="k">{{Icon("folder", "xs")}} Workspace</div>
                  <b>DotnetCockpit</b>
                  <code>{{Encode(Environment.CurrentDirectory)}}</code>
                </div>
                <div class="host">
                  <span class="status-dot"></span>
                  <span>Running on your machine</span>
                </div>
              </div>
            </aside>

            <div class="main">
              <header class="topbar">
                <div class="crumbs">
                  <b>{{GetPageName(currentPath)}}</b>
                </div>
                <div id="cmdk-trigger" class="cmdk" role="button" tabindex="0" aria-label="Open command palette">
                  {{Icon("search", "sm")}}
                  <span>Search versions, tools and actions</span>
                  <kbd>⌘K</kbd>
                </div>
                <div class="checked">
                  {{Icon("clock", "xs")}}
                  <span>Checked {{DateTime.Now.ToString("HH:mm:ss")}}</span>
                </div>
                <fluent-button id="theme-toggle" role="button" type="button" appearance="subtle" aria-label="Switch to dark mode" title="Switch to dark mode" hidden>
                  <span aria-hidden="true">◐</span>
                </fluent-button>
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
              <input id="cmdk-input" type="text" placeholder="Type a command or search..." autocomplete="off">
              <kbd>ESC</kbd>
            </div>
            <div class="grp">
              <div class="grp-k">Navigation</div>
              <a class="opt" href="/">
                {{Icon("versions", "sm")}}
                <div><b>Installations</b> <small>· Installed SDKs and Runtimes</small></div>
                <kbd>1</kbd>
              </a>
              <a class="opt" href="/tools">
                {{Icon("tools", "sm")}}
                <div><b>.NET Tools</b> <small>· Global &amp; local tools</small></div>
                <kbd>2</kbd>
              </a>
              <a class="opt" href="/setup">
                {{Icon("settings", "sm")}}
                <div><b>Setup &amp; Downloads</b> <small>· SDK download catalog &amp; uninstaller</small></div>
                <kbd>3</kbd>
              </a>
              <a class="opt" href="/preflight">
                {{Icon("pulse", "sm")}}
                <div><b>Pre-flight Check</b> <small>· First launch &amp; system diagnostics</small></div>
                <kbd>4</kbd>
              </a>
            </div>
            <div class="grp">
              <div class="grp-k">Quick Actions</div>
              <a class="opt" href="/preflight">
                {{Icon("sparkle", "sm")}}
                <div><b>Run Pre-flight Diagnostics</b> <small>· Verify .NET host, SDKs and workspace</small></div>
                <span class="badge brand">Check</span>
              </a>
              <a class="opt" href="/setup#sdk-downloads-title">
                {{Icon("download", "sm")}}
                <div><b>Install new SDK</b> <small>· macOS .pkg installer catalog</small></div>
                <span class="badge brand">Download</span>
              </a>
              <a class="opt" href="/tools#tool-search-title">
                {{Icon("search", "sm")}}
                <div><b>Search NuGet tools</b> <small>· Search package registry</small></div>
                <span class="badge">NuGet</span>
              </a>
            </div>
            <div class="foot">
              <span><kbd>↑</kbd> <kbd>↓</kbd> navigate</span>
              <span><kbd>↵</kbd> select</span>
              <span><kbd>ESC</kbd> close</span>
            </div>
          </div>
        </body>
        </html>
        """;

    private static string GetPageName(string path) => path switch
    {
        "/overview" => "Overview",
        "/tools" => "Tools",
        "/setup" => "Settings",
        "/preflight" => "Pre-flight Check",
        _ => "Versions"
    };

    public static string Encode(string value) => HtmlEncoder.Default.Encode(value);

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
            "desktop" => "<rect x=\"2\" y=\"3\" width=\"16\" height=\"11\" rx=\"1\"/><path d=\"M7 18h6M10 14v4\"/>",
            "pulse" => "<polyline points=\"22 12 18 12 15 21 9 3 6 12 2 12\"/>",
            "check" => "<polyline points=\"20 6 9 17 4 12\"/>",
            "warn" => "<path d=\"m21.7 18-8-14a2 2 0 0 0-3.4 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3Z\"/><path d=\"M12 9v4M12 17h.01\"/>",
            "sparkle" => "<path d=\"m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3L12 3Z\"/>",
            "lock" => "<rect x=\"3\" y=\"11\" width=\"18\" height=\"11\" rx=\"2\" ry=\"2\"/><path d=\"M7 11V7a5 5 0 0 1 10 0v4\"/>",
            "trash" => "<path d=\"M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2\"/>",
            "clipboard" => "<rect width=\"14\" height=\"14\" x=\"8\" y=\"8\" rx=\"2\" ry=\"2\"/><path d=\"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2\"/>",
            "shield" => "<path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\"/>",
            "chevron-right" => "<polyline points=\"9 18 15 12 9 6\"/>",
            "cloud" => "<path d=\"M17.5 19H9a7 7 0 1 1 6.7-9h.8a5.5 5.5 0 0 1 1 11Z\"/>",
            _ => "<circle cx=\"12\" cy=\"12\" r=\"9\"/>"
        };
        return $"<svg class=\"i{extraCls}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{path}</svg>";
    }
}

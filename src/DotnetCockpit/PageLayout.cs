using System.Text.Encodings.Web;

namespace DotnetCockpit;

internal static class PageLayout
{
    public static string Render(string content, string currentPath, string title = ".NET Cockpit") => $$"""
        <!doctype html>
        <html lang="en" data-theme="dark">
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
              <a href="/" class="nav-link"{{(currentPath == "/" ? " aria-current=\"page\"" : "")}}>{{Icon("versions")}} Installations</a>
              <a href="/tools" class="nav-link"{{(currentPath == "/tools" ? " aria-current=\"page\"" : "")}}>{{Icon("tools")}} .NET Tools</a>
              <a href="/setup" class="nav-link"{{(currentPath == "/setup" ? " aria-current=\"page\"" : "")}}>{{Icon("settings")}} Setup</a>

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
                  <span>Environment</span>
                  <span>/</span>
                  <b>{{GetPageName(currentPath)}}</b>
                </div>
                <div class="cmdk">
                  {{Icon("search", "sm")}}
                  <span>Search versions, tools and actions</span>
                  <kbd>⌘K</kbd>
                </div>
                <div class="checked">
                  {{Icon("clock", "xs")}}
                  <span>Checked {{DateTime.Now.ToString("HH:mm:ss")}}</span>
                </div>
                <fluent-button id="theme-toggle" role="button" type="button" appearance="subtle" aria-label="Switch to light mode" title="Switch to light mode">
                  <span aria-hidden="true">◐</span>
                </fluent-button>
              </header>

              <main id="main" class="content" tabindex="-1">
                {{content}}
              </main>
            </div>
          </div>
        </body>
        </html>
        """;

    private static string GetPageName(string path) => path switch
    {
        "/tools" => ".NET Tools",
        "/setup" => "Setup",
        _ => "Installations"
    };

    public static string Encode(string value) => HtmlEncoder.Default.Encode(value);

    public static string Icon(string name, string cls = "")
    {
        var extraCls = string.IsNullOrEmpty(cls) ? "" : $" {cls}";
        var path = name switch
        {
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
            _ => "<circle cx=\"12\" cy=\"12\" r=\"9\"/>"
        };
        return $"<svg class=\"i{extraCls}\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">{path}</svg>";
    }
}

using System.Text.Encodings.Web;

namespace DotnetCockpit;

internal static class PageLayout
{
    public static string Render(string content, string currentPath, string title = ".NET Cockpit") => $$"""
        <!doctype html>
        <html lang="en" data-theme="auto">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="description" content="Your local .NET environment, in one place.">
          <title>{{Encode(title)}}</title>
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
              <a href="/" class="nav-link"{{(currentPath == "/" ? " aria-current=\"page\"" : "")}}>{{Icon("desktop")}} Installations</a>
              <a href="/tools" class="nav-link"{{(currentPath == "/tools" ? " aria-current=\"page\"" : "")}}>{{Icon("code")}} .NET Tools</a>
              <a href="/setup" class="nav-link"{{(currentPath == "/setup" ? " aria-current=\"page\"" : "")}}>{{Icon("settings")}} Setup</a>
              <div class="sidebar-footer"><span class="status-dot"></span>Running on your machine</div>
            </aside>
            <main id="main" tabindex="-1">
              {{content}}
            </main>
          </div>
        </body>
        </html>
        """;

    public static string Encode(string value) => HtmlEncoder.Default.Encode(value);

    public static string Icon(string name)
    {
        var path = name switch
        {
            "code" => "<path d=\"m7 5-5 5 5 5m6-10 5 5-5 5M12 2 8 18\"/>",
            "server" => "<rect x=\"4\" y=\"2\" width=\"12\" height=\"16\" rx=\"2\"/><path d=\"M7 6h6M7 9h6M7 14h2\"/>",
            "refresh" => "<path d=\"M17 7a7 7 0 1 0 0 6M17 2v5h-5\"/>",
            "download" => "<path d=\"M10 2v10m0 0 4-4m-4 4-4-4M3 15v3h14v-3\"/>",
            "trash" => "<path d=\"M4 6h12m-9 0v-2h6v2m-7 3v5m4-5v5m4-5v5M6 6l.7 11h6.6L14 6\"/>",
            "search" => "<circle cx=\"8.5\" cy=\"8.5\" r=\"5.5\"/><path d=\"m13 13 5 5\"/>",
            "settings" => "<path d=\"M10 2.5v2M10 15.5v2M2.5 10h2M15.5 10h2M4.7 4.7l1.4 1.4M13.9 13.9l1.4 1.4M15.3 4.7l-1.4 1.4M6.1 13.9l-1.4 1.4\"/><circle cx=\"10\" cy=\"10\" r=\"3.2\"/>",
            _ => "<rect x=\"2\" y=\"3\" width=\"16\" height=\"11\" rx=\"1\"/><path d=\"M7 18h6M10 14v4\"/>"
        };
        return $"<svg viewBox=\"0 0 20 20\" aria-hidden=\"true\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{path}</svg>";
    }
}

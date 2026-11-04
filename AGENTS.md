# Repository guide

.NET Cockpit is a local browser application: a Native AOT ASP.NET Core Minimal API host renders HTML in C#. A small module script enhances the server-rendered page; no UI framework is used.

- Keep native startup independent of an installed .NET runtime or SDK. The optional managed .NET tool is an additional distribution.
- Build browser assets before compiling .NET: `npm --prefix ui ci` and `npm --prefix ui run build`. Node is only a build/test dependency.
- Keep inventory rendering, searching and sorting on the server. Core functionality must work with JavaScript disabled, using normal GET forms and links.
- Keep all frontend dependencies local to the distributed application; do not add CDN requests.
- Keep the server bound to IPv4 `127.0.0.1`. Browser and smoke-test URLs use this host.
- Use source-generated System.Text.Json metadata and resolve Native AOT warnings. Do not add server-side Blazor, MVC, or dynamic code generation.
- Inventory reflects the selected dotnet host on PATH. Distinguish a missing host from a failed query.
- `/tools` lists user-global tools and local manifest declarations for Cockpit's startup working directory. Do not scan arbitrary projects or imply that a listed local package has been restored. Keep the two scopes independent when a query fails.
- Tool mutations run only through explicit local forms: search results install globally until a local manifest exists, initialized workspaces offer global/local targets, installed rows offer uninstall, and the current workspace can remove its own local manifest. Keep POST actions scoped to the startup working directory and preserve server-rendered behavior without JavaScript; never remove an inherited parent manifest.
- Every mutation asks first through `ConfirmActions`: the shared dialog with JavaScript, the `/confirm` page without it.
- Support dates, end-of-life state and the timeline come only from the release catalog. When it is unavailable, say the status is unknown instead of guessing.
- Preferences live in `SettingsStore`, not browser storage, because the free port changes the origin on every launch.
- Styles use the tokens in `ui/src/styles/tokens.css`: six type sizes, three radii, semantic colors. Add no literal colors, other sizes or `!important`. Palettes in `palettes.css` override color roles only and must pass the contrast test in both themes.
- `/setup` may detect, install, update, and remove only the official global `dotnet-core-uninstall` installation for Windows and macOS. Global installs use the platform paths and must surface administrator approval. Release discovery must follow every page of the official GitHub Releases API, filter stable releases to those with a compatible asset, and treat an incomplete/failed response as unavailable rather than presenting a partial list. Do not download arbitrary URLs or claim Linux support.
- The CLI's bracketed path is a base directory. Display `InstallationPath` (base path plus version) for an individual SDK or runtime; retain `Path` in the JSON API for compatibility.
- Run `dotnet test DotnetCockpit.sln`, publish the native binary, run `node scripts/smoke.mjs <binary>`, and run the browser tests for UI changes. For visual changes also run `npm --prefix ui run test:visual`; after an intended change, update the baselines with `npm --prefix ui run test:visual:update` and review them.
- Use `gh` for GitHub operations. Commit messages follow Conventional Commits and contain no AI attribution.
- Keep changes focused and avoid speculative abstractions or functionality.

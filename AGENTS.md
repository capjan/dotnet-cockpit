# Repository guide

.NET Cockpit is a local browser application: a Native AOT ASP.NET Core Minimal API host and a React/TypeScript frontend using Fluent UI React v9 (`@fluentui/react-components`).

- Keep native startup independent of an installed .NET runtime or SDK. The optional managed .NET tool is an additional distribution.
- Build browser assets before compiling .NET: `npm --prefix ui ci` and `npm --prefix ui run build`.
- Keep all frontend dependencies local to the distributed application; do not add CDN requests.
- Keep the server bound to IPv4 `127.0.0.1`. Browser and smoke-test URLs use this host.
- Use source-generated System.Text.Json metadata and resolve Native AOT warnings. Do not add server-side Blazor, MVC, or dynamic code generation.
- Inventory reflects the selected dotnet host on PATH. Distinguish a missing host from a failed query.
- Run `dotnet test DotnetCockpit.sln`, publish the native binary, run `node scripts/smoke.mjs <binary>`, and run the browser tests for UI changes.
- Use `gh` for GitHub operations. Commit messages follow Conventional Commits and contain no AI attribution.
- Keep changes focused and avoid speculative abstractions or functionality.

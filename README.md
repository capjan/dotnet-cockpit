# .NET Cockpit

A local browser workspace for your .NET environment, built with **Fluent UI React v9** and an **ASP.NET Core Minimal API** backend.

The prototype lists installed .NET SDKs and runtimes, supports filtering and sorting, and refreshes the inventory without reloading the page. Light and dark themes and a responsive layout are included. Installation and tool-management actions are not implemented yet.

## Run without installing .NET

Download the native archive for your machine from a GitHub release or the **Build and test** workflow artifacts. Extract it and launch:

```sh
./dotnet-cockpit
```

On Windows, run `dotnet-cockpit.exe`. The program selects an available port on `127.0.0.1` and opens your browser. For manual browser startup or a fixed port:

```sh
./dotnet-cockpit --no-browser --port 5230
```

Native AOT builds include everything needed from .NET and embed all browser assets. No .NET installation, Node.js, CDN, or internet connection is needed to start the application. Separate binaries are provided for Windows x64, Linux x64, macOS Intel, and macOS Apple Silicon. Prototype macOS downloads are not Developer ID signed or notarized.

## Install as a .NET tool

If a .NET 10 SDK is already installed, download the `.nupkg` from a release or the **dotnet-tool** workflow artifact into a local folder, then install:

```sh
dotnet tool install --global dotnet-cockpit --add-source /absolute/path/to/packages --version 0.1.0
dotnet cockpit
```

The tool command is also directly available as `dotnet-cockpit`. Replace the version with the downloaded package version. Update an existing installation with `dotnet tool update` using the same source and version options.

The repository and GitHub releases are private. Packages are distributed as downloadable `.nupkg` files; nothing is published to NuGet.org. The tool package is a standard managed .NET tool requiring the SDK. The native downloads are the bootstrap option for machines without .NET.

## Inventory scope

Cockpit resolves the first `dotnet` executable in an absolute directory on its own `PATH`, then runs `dotnet --list-sdks` and `dotnet --list-runtimes`. It reports the installation and architecture visible to that host, not an exhaustive scan of every .NET installation on disk. Refresh repeats discovery. Restart Cockpit after changing its inherited environment.

If `dotnet` is absent, the application still starts and explains that no installation was found. A command failure is displayed as an error rather than an empty inventory.

## Development

Requirements: .NET 10 SDK, Node.js 24 or later, and the Native AOT build prerequisites for your OS (Xcode Command Line Tools on macOS; the C++ build tools on Windows; `clang` and `zlib1g-dev` on Ubuntu).

```sh
npm --prefix ui ci
npm --prefix ui run build
dotnet test DotnetCockpit.sln
dotnet run --project src/DotnetCockpit -- --no-browser --port 5230
```

The backend embeds the production UI at build time. Rebuild the UI and restart the backend after frontend changes. For frontend hot reload, keep the backend on port 5230 and run `npm --prefix ui run dev`; Vite proxies `/api` to the local backend.

Publish and test a native executable (replace the runtime identifier for your platform):

```sh
dotnet publish src/DotnetCockpit -c Release -r osx-arm64 --self-contained true -p:PublishAot=true -warnaserror:IL2026,IL2104,IL3050,IL3053 -o artifacts/native
node scripts/smoke.mjs artifacts/native/dotnet-cockpit
```

Test the UI against the published native executable on Linux or macOS:

```sh
cd ui
npx playwright install chromium
npm run test:browser
```

Build the tool package:

```sh
dotnet pack src/DotnetCockpit -c Release -o artifacts/packages
```

## Build and release workflows

Every push and pull request runs unit tests, builds Native AOT executables, and smoke-tests each published binary on the same runner matrix used by MDU's native releases:

| Runner | Runtime identifier |
| --- | --- |
| Ubuntu 22.04 | `linux-x64` |
| Windows latest | `win-x64` |
| macOS 15 Intel | `osx-x64` |
| macOS latest | `osx-arm64` |

The native smoke tests launch from a temporary directory, check the embedded HTML/JS/CSS and API, compare the returned inventory against the real .NET CLI, and repeat with an empty `PATH`. The latter verifies that the native application starts without finding an installed .NET host. It does not simulate an otherwise clean OS; ordinary native OS dependencies still apply. Linux additionally runs browser tests for filtering, refresh, theme switching, empty/error states, and a narrow viewport. A separate job packages, installs, invokes, and smoke-tests the .NET tool.

Run the **Release** workflow manually from `main` and supply a version to repeat these checks and create a private GitHub release with native archives, the tool package, and SHA-256 checksums. No publishing credentials are needed beyond the workflow's repository token.

## Structure

- `src/DotnetCockpit`: loopback-only Minimal API host, installation discovery, embedded browser assets, and tool packaging.
- `ui`: React, TypeScript, Fluent UI v9, and browser tests.
- `tests`: .NET inventory parsing, executable discovery, and command-line tests.
- `scripts/smoke.mjs`: cross-platform tests against the actual published application.

The API is read-only. The host accepts `127.0.0.1` requests and exposes `GET /api/health` and `GET /api/installation`. JSON serialization uses source-generated metadata for Native AOT.

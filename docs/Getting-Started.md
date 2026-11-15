---
navigation:
  order: 1
---

# Getting started

Choose a native download for the quickest start. If you already have a .NET 10 SDK, you can also install Cockpit as a global .NET tool.

## Choose how to run

| | Native application | .NET tool |
| --- | --- | --- |
| Requires | No installed .NET runtime or SDK | .NET 10 SDK |
| Get it from | [GitHub Releases](https://github.com/capjan/dotnet-cockpit/releases/latest) | NuGet.org via the CLI |
| Start it with | `dotnet-cockpit` executable | `dotnet cockpit` |

Node.js is only needed to build the project, never to run a download.

## Run the native application

1. [Open the latest release](https://github.com/capjan/dotnet-cockpit/releases/latest) and download the archive matching your operating system and processor. Choose Apple Silicon or Intel on macOS.
2. Extract the archive.
3. In a terminal, change to the workspace you want to inspect and launch the extracted executable. Use its full path if it is in another folder.

On macOS or Linux, from the extracted folder:

```sh
./dotnet-cockpit
```

On Windows, run `dotnet-cockpit.exe`. Cockpit selects an available local port and opens your browser.

> [!TIP]
> The directory where you launch Cockpit determines its local tool scope. Start it from your project folder to inspect that project’s tool manifest.

### Use a fixed port

To choose a port and open the browser yourself:

```sh
./dotnet-cockpit --no-browser --port 5230
```

Then open [127.0.0.1:5230](http://127.0.0.1:5230). Stop Cockpit with **Ctrl+C** in its terminal.

## Install as a .NET tool

With a .NET 10 SDK installed:

```sh
dotnet tool install --global dotnet-cockpit
dotnet cockpit
```

`dotnet-cockpit` is also available as a direct command. To update:

```sh
dotnet tool update --global dotnet-cockpit
```

## Your first walkthrough

1. **Overview:** review the active SDK, environment health and suggested actions.
2. **Versions:** inspect SDKs and runtimes, their paths and support status. Use **Install .NET** for official installers.
3. **Tools:** check global packages and local manifest declarations for your startup directory.
4. **Settings:** choose your appearance and configure removal support if you need it.

[Take a closer look at the workspace →](Features.md)

## If something looks wrong

| What you see | What to check |
| --- | --- |
| No .NET host found | The native app can start without .NET. Install a host if you want an inventory, then restart Cockpit. |
| Unexpected SDKs or runtimes | Cockpit uses its inherited `PATH`. Check `dotnet --list-sdks` and `dotnet --list-runtimes` in the launching terminal. |
| Unexpected local tools | Check the startup directory and any inherited parent manifest. |
| Unknown support status | The release catalog may be unavailable. Refresh when connectivity returns. |
| A query failed | Read the reported error; a failed query does not mean the scope is empty. |

For details about local scope and changes, read [Privacy and safety](Privacy-and-Safety.md).

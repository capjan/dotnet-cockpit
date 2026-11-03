---
navigation:
  order: 1
---

# Getting started

## Run the native application

Download the archive for your operating system from the project's GitHub release, extract it, and run the executable:

```sh
./dotnet-cockpit
```

On Windows, run `dotnet-cockpit.exe`. The native binary includes the .NET runtime and the browser assets, so no .NET SDK or Node.js installation is needed to start Cockpit.

Cockpit selects an available local port and opens your browser. To choose a port or prevent automatic browser launch:

```sh
./dotnet-cockpit --no-browser --port 5230
```

Open `http://127.0.0.1:5230` when using a fixed port.

## Install as a .NET tool

If a .NET 10 SDK is already installed, install Cockpit from NuGet.org:

```sh
dotnet tool install --global dotnet-cockpit
dotnet cockpit
```

`dotnet-cockpit` is also available as a direct command. Update a global installation with:

```sh
dotnet tool update --global dotnet-cockpit
```

The .NET tool is optional. Use the native download on machines without an SDK.

## First steps

1. Open **Installations** to inspect the SDKs and runtimes reported by the selected `dotnet` host.
2. Open **.NET Tools** to see global tools and local manifest declarations for the directory where Cockpit was launched.
3. Open **Setup** to manage the supported uninstall tool or download an official macOS SDK or runtime package.

Restart Cockpit after changing its inherited `PATH` or installing a new `dotnet` host.

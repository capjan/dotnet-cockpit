# .NET Cockpit

.NET Cockpit is a local browser workspace for understanding and managing the .NET installation that is active on your machine. It runs only on `127.0.0.1`, opens a browser tab, and keeps the inventory, filtering, sorting, and actions on the server so the important parts still work with JavaScript disabled.

```hero
title: See your .NET environment clearly
text: Inspect SDKs, runtimes, and tools in one local workspace—then install or remove only through explicit actions.
actions:
  - text: Get started
    link: Getting-Started.md
  - text: Explore features
    link: Features.md
```

## Start here

- [Getting started](Getting-Started.md) — Run the native application or install the optional .NET tool.
- [Features](Features.md) — Learn what Cockpit shows and which actions it can perform.
- [Privacy and safety](Privacy-and-Safety.md) — Understand local scope, network requests, and safeguards.
- [Release and deployment](Release.md) — Build, verify, publish, and host the documentation.

## What Cockpit is for

Cockpit helps you answer practical questions about one .NET host:

- Which SDKs and runtimes are visible to the `dotnet` command on my `PATH`?
- Which global and workspace-local tools are declared?
- Which supported macOS SDK and runtime installers are available from Microsoft?
- Is the official .NET Uninstall Tool installed, and can it be updated or removed safely?

It is not an exhaustive disk scanner or a replacement for the .NET CLI. Its inventory always reflects the `dotnet` host that Cockpit itself can resolve.

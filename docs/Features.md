---
navigation:
  order: 2
---

# Features

## Installations

Cockpit runs `dotnet --list-sdks` and `dotnet --list-runtimes` through the first absolute `dotnet` executable it finds on its own `PATH`. It separates a missing host from a failed query and displays the version-specific installation directory.

The inventory is server rendered. Search, sorting, refresh links, and filters work as ordinary GET requests; JavaScript only improves the interaction.

## .NET Tools

The **.NET Tools** page keeps the two scopes separate:

- Global tools come from `dotnet tool list --global` for the current user.
- Local tools come from `dotnet tool list --local` in Cockpit's startup directory, including applicable parent manifests.

A local declaration is not proof that its package has been restored. Cockpit never scans unrelated projects. Tool changes are deliberate POST actions: search results install globally until the current workspace has a manifest; initialized workspaces can choose global or local installation; installed rows can uninstall; and Cockpit only removes a manifest owned by its own startup directory.

## Setup

On macOS and Windows, Cockpit can detect, install, update, and remove the official global `dotnet-core-uninstall` installation. These operations request administrator approval where the platform requires it. Linux is not supported by the official tool.

On macOS, **Setup** also lists official `.pkg` installers for the host architecture. The catalog comes from Microsoft's release metadata and includes SDKs and standalone .NET Runtimes, release channels, support status, release dates, SHA-512 checksums, and release CVEs where Microsoft provides them. Downloading a package is separate from installing it: open the downloaded package in macOS Installer to complete installation.

The SDK installer includes the matching .NET Runtime and ASP.NET Core Runtime. Use the standalone Runtime package only when you do not need the SDK.

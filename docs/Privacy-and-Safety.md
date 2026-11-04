---
navigation:
  order: 3
---

# Privacy and safety

Cockpit serves its user interface on IPv4 loopback only: `127.0.0.1`. It does not listen on a LAN or public network interface.

## Local information

Cockpit reads the .NET host selected by its own `PATH`, the output of the standard .NET CLI, and the tool manifest visible from its startup directory. It does not search arbitrary project folders or scan every .NET installation on disk.

## Network requests

The application starts without an internet connection. The Settings page requests release metadata only when it needs current installer or uninstall-tool release information. Installer links target official Microsoft-hosted files; uninstall-tool release discovery uses the official GitHub releases API.

## Explicit changes

Cockpit never changes .NET installations or tool manifests during inventory. Mutations require a local form submission and are scoped to the startup directory. An inherited parent tool manifest is displayed but cannot be removed from Cockpit.

Before removing an installation, confirm that it is not needed by another application. Cockpit surfaces the command result, but the underlying platform installer and .NET tooling remain authoritative.

## Local files

Cockpit stores your appearance and pre-flight choices in `settings.json` in the `dotnet-cockpit` folder of your user configuration directory. Delete the file to reset them.

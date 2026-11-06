---
navigation:
  order: 3
---

# Privacy and safety

Cockpit serves its interface only on IPv4 loopback, `127.0.0.1`. It does not listen on a LAN or public network interface. Local inventory works offline; current release information and package discovery need network access.

## What Cockpit reads

| Source | Scope |
| --- | --- |
| .NET host | The executable selected by Cockpit’s inherited `PATH` |
| SDKs and runtimes | Standard .NET CLI inventory for that host |
| Global tools | Tools for the current user |
| Local tools | Manifest declarations visible from the startup directory, including parent manifests |

Cockpit does not scan arbitrary project folders or every .NET installation on disk. A local tool declaration does not prove that its package has been restored.

## What uses the network

| Service | Purpose |
| --- | --- |
| Microsoft release metadata | Available releases, lifecycle dates, CVEs and checksums |
| Official Microsoft downloads | SDK and runtime installer downloads |
| NuGet.org | Tool search, package descriptions, icons and update checks |
| GitHub Releases for `dotnet/cli-lab` | Official uninstall-tool release discovery and downloads |

Installing tools also invokes the .NET CLI, which uses its configured package sources. Current catalog data is cached locally; when support information is unavailable, Cockpit marks the status unknown.

## Before a change

Inventory does not change installations or tool manifests. Mutations require an explicit local form submission and confirmation: a dialog with JavaScript, or a confirmation page without it.

- Tool actions use the startup workspace. Cockpit never removes an inherited parent manifest.
- Installation removal uses the official uninstall tool and is limited to eligible installations.
- Managing the global uninstall tool on macOS or Windows surfaces administrator approval where required.
- Downloading an installer and running it are separate steps.

Before removing an SDK, runtime or tool, check whether another application or project needs it. Cockpit shows the command result; the underlying installer and .NET tooling remain authoritative.

## Local preferences and cache

Cockpit stores appearance and pre-flight preferences in `settings.json`, and the last official release catalog in `release-catalog.json`, inside the `dotnet-cockpit` folder in your user configuration directory. Delete these files to reset them. Preferences are stored on disk because the browser origin changes when Cockpit chooses another free port.

## Sharing screenshots

Your workspace path, user name, installation paths and tool names may identify you or your projects. Review them before sharing. This documentation uses fixed demonstration inventory and replaces machine-specific workspace, user and timestamp values before capturing its screenshots.

[Explore the workspace →](Features.md)

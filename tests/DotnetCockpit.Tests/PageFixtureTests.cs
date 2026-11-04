using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Setup;
using DotnetCockpit.Tools;
using Xunit;

namespace DotnetCockpit.Tests;

/// <summary>
/// Renders the pages with fixed data and a fixed date for the screenshot tests. It writes files only when
/// DOTNET_COCKPIT_FIXTURES names a directory, which `npm run test:visual` does.
/// </summary>
public sealed class PageFixtureTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static readonly SdkDownloadSnapshot Catalog = new("ready",
    [
        Download("SDK", "10.0.403", "10.0.14", "10.0", "LTS", "ACTIVE", "2028-11-14"),
        Download(".NET Runtime", "10.0.14", null, "10.0", "LTS", "ACTIVE", "2028-11-14"),
        Download("SDK", "9.0.320", "9.0.22", "9.0", "STS", "MAINTENANCE", "2026-11-10"),
        Download(".NET Runtime", "9.0.22", null, "9.0", "STS", "MAINTENANCE", "2026-11-10"),
        Download("SDK", "11.0.100-rc.3", "11.0.0-rc.3", "11.0", "STS", "GO-LIVE", null),
        Download(".NET Runtime", "11.0.0-rc.3", null, "11.0", "STS", "GO-LIVE", null),
    ], Channels:
    [
        new("11.0", "STS", "GO-LIVE", null, new(2026, 2, 10), null),
        new("10.0", "LTS", "ACTIVE", new(2028, 11, 14), new(2025, 2, 11), new(2025, 11, 11)),
        new("9.0", "STS", "MAINTENANCE", new(2026, 11, 10), new(2024, 2, 13), new(2024, 12, 3)),
        new("7.0", "STS", "EOL", new(2024, 5, 14), new(2022, 2, 17), new(2022, 11, 8)),
    ]);

    private static InstallationSnapshot Mixed() => InstallationSnapshot.Create("ready", "/usr/local/share/dotnet/dotnet",
    [
        new SdkInstallation("11.0.100-preview.7.26381.103", "/usr/local/share/dotnet/sdk") { CanRemove = true },
        new SdkInstallation("10.0.403", "/usr/local/share/dotnet/sdk") { CanRemove = true },
        new SdkInstallation("9.0.320", "/usr/local/share/dotnet/sdk") { CanRemove = true },
        new SdkInstallation("7.0.408", "/usr/local/share/dotnet/sdk") { CanRemove = true },
    ],
    [
        new RuntimeInstallation("Microsoft.NETCore.App", "10.0.14", "/usr/local/share/dotnet/shared/Microsoft.NETCore.App") { CanRemove = true },
        new RuntimeInstallation("Microsoft.AspNetCore.App", "10.0.14", "/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App") { CanRemove = true },
        new RuntimeInstallation("Microsoft.NETCore.App", "7.0.18", "/usr/local/share/dotnet/shared/Microsoft.NETCore.App") { CanRemove = true },
    ]);

    private static InstallationSnapshot Healthy() => InstallationSnapshot.Create("ready", "/usr/local/share/dotnet/dotnet",
        [new SdkInstallation("10.0.403", "/usr/local/share/dotnet/sdk") { CanRemove = true }],
        [new RuntimeInstallation("Microsoft.NETCore.App", "10.0.14", "/usr/local/share/dotnet/shared/Microsoft.NETCore.App") { CanRemove = true }]);

    private static ToolsSnapshot Tools(bool empty = false) => new("ready", "/Users/dev/project",
        new(empty ? [] :
        [
            new DotnetTool("fixture.formatter", "1.2.0", ["fixture-format"], null),
            new DotnetTool("fixture.analyzer", "4.0.1", ["fixture-analyze"], null),
        ]),
        new([]),
        LocalManifestInitialized: false);

    private static readonly SetupSnapshot Setup = new("ready", "macOS", "Arm64",
        new(true, "/usr/local/bin/dotnet-core-uninstall", "global", "1.7.661902"), "/usr/local/bin",
        LatestVersion: "1.7.661902",
        LatestReleaseUrl: "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902",
        AvailableReleases: [new SetupRelease("1.7.661902", "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902", "2026-01-08T18:51:08Z", "dotnet-core-uninstall-osx-arm64.tar.gz")]);

    private static readonly ActiveSdk Active = new("10.0.403", null);

    [Fact]
    public void WritesFixturePagesWhenRequested()
    {
        var directory = Environment.GetEnvironmentVariable("DOTNET_COCKPIT_FIXTURES");
        var empty = InstallationSnapshot.Create("notFound", null);
        var pages = new Dictionary<string, string>
        {
            ["mixed-overview"] = OverviewPage.Render(Mixed(), Tools(), Setup, Catalog, Active, Today),
            ["mixed-versions"] = InstallationPage.Render(Mixed(), Catalog, new(), Today),
            ["mixed-tools"] = ToolsPage.Render(Tools(), new()),
            ["mixed-settings"] = SetupPage.Render(Setup, Catalog, Mixed(), new()),
            ["mixed-preflight"] = PreflightPage.Render(Mixed(), Tools(), Setup, Catalog, false, Today),
            ["healthy-overview"] = OverviewPage.Render(Healthy(), Tools(), Setup, Catalog, Active, Today),
            ["healthy-versions"] = InstallationPage.Render(Healthy(), Catalog, new(), Today),
            ["empty-overview"] = OverviewPage.Render(empty, ToolsSnapshot.Empty("notFound", "/Users/dev/project"), Setup, new("error", [], "offline"), null, Today),
            ["empty-versions"] = InstallationPage.Render(empty, new("error", [], "offline"), new(), Today),
            ["empty-tools"] = ToolsPage.Render(Tools(empty: true), new()),
        };

        Assert.All(pages.Values, html => Assert.StartsWith("<!doctype html>", html.TrimStart()));
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, html) in pages) File.WriteAllText(Path.Combine(directory, $"{name}.html"), html);
    }

    private static SdkDownload Download(string product, string version, string? runtime, string channel, string type, string phase, string? end) =>
        new(product, version, version, runtime, channel, type, phase, end, "2026-10-25",
            [new("CVE-2026-0001", "https://msrc.microsoft.com/update-guide/vulnerability/CVE-2026-0001")],
            $"https://builds.dotnet.microsoft.com/dotnet/{(product == "SDK" ? "Sdk" : "Runtime")}/{version}/dotnet-{(product == "SDK" ? "sdk" : "runtime")}-{version}-osx-arm64.pkg",
            "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0");
}

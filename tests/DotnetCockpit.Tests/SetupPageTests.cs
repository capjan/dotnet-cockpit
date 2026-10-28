using DotnetCockpit.Setup;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class SetupPageTests
{
    [Fact]
    public void RendersInstallOptionsAndOfficialDocumentation()
    {
        var snapshot = new SetupSnapshot("ready", "macOS", "Arm64",
            new(false, null, null), "/usr/local/bin",
            LatestVersion: "1.7.661902",
            LatestReleaseUrl: "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902",
            AvailableReleases: [
                new SetupRelease("1.7.661902", "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902", "2026-01-08T18:51:08Z", "dotnet-core-uninstall-osx-arm64.tar.gz"),
                new SetupRelease("1.6.656206", "https://github.com/dotnet/cli-lab/releases/tag/1.6.656206", "2025-01-01T00:00:00Z", "dotnet-core-uninstall.tar.gz")]);
        var html = SetupPage.Render(snapshot, new());

        Assert.Contains(".NET Uninstall Tool", html);
        Assert.Contains("Install globally", html);
        Assert.Contains("/usr/local/bin", html);
        Assert.Contains("Latest (1.7.661902)", html);
        Assert.Contains("name=\"version\"", html);
        Assert.Contains("action=\"/setup/install\"", html);
        Assert.Contains("dotnet/cli-lab/releases/tag/1.6.656206", html);
        Assert.DoesNotContain("Install for current user", html);
        Assert.Contains("https://learn.microsoft.com/en-us/dotnet/core/additional-tools/uninstall-tool-overview", html);
    }

    [Fact]
    public void RendersInstalledPathAndDoesNotOfferDuplicateInstallActions()
    {
        var snapshot = new SetupSnapshot("ready", "macOS", "Arm64",
            new(true, "/usr/local/bin/dotnet-core-uninstall", "global", "1.7.661902"), "/usr/local/bin",
            LatestVersion: "1.7.661902",
            LatestReleaseUrl: "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902",
            AvailableReleases: [new SetupRelease("1.7.661902", "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902", null, "dotnet-core-uninstall-osx-arm64.tar.gz")]);
        var html = SetupPage.Render(snapshot, new());

        Assert.DoesNotContain("<fluent-badge appearance=\"tint\" color=\"success\">Installed</fluent-badge>", html);
        Assert.Contains("/usr/local/bin/dotnet-core-uninstall", html);
        Assert.Contains("Installed version <code>1.7.661902</code>", html);
        Assert.Contains("Ready to list and remove .NET Installations", html);
        Assert.Contains("aria-label=\"Uninstall .NET Uninstall Tool\"", html);
        Assert.Contains("title=\"Uninstall .NET Uninstall Tool\"", html);
        Assert.Contains("setup-icon-button", html);
        Assert.Contains("action=\"/setup/uninstall\"", html);
        Assert.DoesNotContain("Uninstall globally", html);
        Assert.DoesNotContain("action=\"/setup/update\"", html);
        Assert.DoesNotContain("action=\"/setup/install\"", html);
    }

    [Fact]
    public void OffersAnUpdateWhenTheOfficialReleaseIsNewer()
    {
        var snapshot = new SetupSnapshot("ready", "macOS", "Arm64",
            new(true, "/usr/local/bin/dotnet-core-uninstall", "global", "1.7.0"), "/usr/local/bin",
            LatestVersion: "1.7.661902",
            LatestReleaseUrl: "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902",
            AvailableReleases: [new SetupRelease("1.7.661902", "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902", null, "dotnet-core-uninstall-osx-arm64.tar.gz")]);
        var html = SetupPage.Render(snapshot, new());

        Assert.Contains("Latest official release <a href=\"https://github.com/dotnet/cli-lab/releases/tag/1.7.661902\"><code>1.7.661902</code>", html);
        Assert.Contains("action=\"/setup/update\"", html);
        Assert.Contains("Update to 1.7.661902", html);
    }

    [Fact]
    public void DoesNotOfferAutomaticRemovalForAnUnmanagedPathInstallation()
    {
        var snapshot = new SetupSnapshot("ready", "macOS", "Arm64",
            new(true, "/opt/tools/dotnet-core-uninstall", "path", "1.7.0"), "/usr/local/bin",
            LatestVersion: "1.7.661902",
            AvailableReleases: [new SetupRelease("1.7.661902", "https://github.com/dotnet/cli-lab/releases/tag/1.7.661902", null, "dotnet-core-uninstall-osx-arm64.tar.gz")]);
        var html = SetupPage.Render(snapshot, new());

        Assert.DoesNotContain("action=\"/setup/uninstall\"", html);
        Assert.Contains("must be removed manually", html);
    }

    [Fact]
    public void ExplainsUnsupportedPlatforms()
    {
        var snapshot = new SetupSnapshot("unsupported", "Linux", "X64", new(false, null, null), "/usr/local/bin");
        var html = SetupPage.Render(snapshot, new());

        Assert.Contains("This platform is not supported", html);
        Assert.DoesNotContain("Install globally", html);
    }
}

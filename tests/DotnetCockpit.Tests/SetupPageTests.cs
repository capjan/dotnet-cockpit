using DotnetCockpit.Setup;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class SetupPageTests
{
    [Fact]
    public void RendersInstallOptionsAndOfficialDocumentation()
    {
        var snapshot = new SetupSnapshot("ready", "macOS", "Arm64",
            new(false, null, null), "/Users/test/dotnet-core-uninstall", "/usr/local/bin");
        var html = SetupPage.Render(snapshot, new());

        Assert.Contains(".NET Uninstall Tool", html);
        Assert.Contains("Install for current user", html);
        Assert.Contains("Install globally", html);
        Assert.Contains("/Users/test/dotnet-core-uninstall", html);
        Assert.Contains("/usr/local/bin", html);
        Assert.Contains("https://learn.microsoft.com/en-us/dotnet/core/additional-tools/uninstall-tool-overview", html);
    }

    [Fact]
    public void RendersInstalledPathAndDoesNotOfferDuplicateInstallActions()
    {
        var snapshot = new SetupSnapshot("ready", "macOS", "Arm64",
            new(true, "/usr/local/bin/dotnet-core-uninstall", "global"), "/Users/test/dotnet-core-uninstall", "/usr/local/bin");
        var html = SetupPage.Render(snapshot, new());

        Assert.Contains("Installed", html);
        Assert.Contains("/usr/local/bin/dotnet-core-uninstall", html);
        Assert.Contains("action=\"/setup/uninstall\"", html);
        Assert.Contains("Uninstall globally", html);
        Assert.DoesNotContain("action=\"/setup/install\"", html);
    }

    [Fact]
    public void DoesNotOfferAutomaticRemovalForAnUnmanagedPathInstallation()
    {
        var snapshot = new SetupSnapshot("ready", "macOS", "Arm64",
            new(true, "/opt/tools/dotnet-core-uninstall", "path"), "/Users/test/dotnet-core-uninstall", "/usr/local/bin");
        var html = SetupPage.Render(snapshot, new());

        Assert.DoesNotContain("action=\"/setup/uninstall\"", html);
        Assert.Contains("must be removed manually", html);
    }

    [Fact]
    public void ExplainsUnsupportedPlatforms()
    {
        var snapshot = new SetupSnapshot("unsupported", "Linux", "X64", new(false, null, null), "/home/test/dotnet-core-uninstall", "/usr/local/bin");
        var html = SetupPage.Render(snapshot, new());

        Assert.Contains("This platform is not supported", html);
        Assert.DoesNotContain("Install globally", html);
    }
}

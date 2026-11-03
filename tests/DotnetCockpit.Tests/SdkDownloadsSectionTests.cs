using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class SdkDownloadsSectionTests
{
    [Fact]
    public void RendersOfficialMacPackagesWithIntegrityAndSecurityDetails()
    {
        var downloads = new[]
        {
            Download("SDK", "10.0.100", "10.0.0", "ACTIVE", "LTS"),
            Download("SDK", "10.0.100-preview.1", "10.0.0-preview.1", "ACTIVE", "LTS"),
            Download(".NET Runtime", "10.0.0", null, "EOL", "STS")
        };

        var html = SdkDownloadsSection.Render(new SdkDownloadSnapshot("ready", downloads), InstallationSnapshot.Create("notFound", null), new());

        Assert.Contains("Latest .NET SDK", html);
        Assert.Contains("10.0.100", html);
        Assert.Contains("Includes .NET Runtime <code>10.0.0</code>", html);
        Assert.Contains("SHA-512", html);
        Assert.Contains("CVE-2026-0001", html);
        Assert.Contains("data-preview=\"true\"", html);
        Assert.Contains("data-lifecycle=\"LTS\" hidden", html);
        Assert.Contains("Official macOS .pkg packages", html);
    }

    [Fact]
    public void ReportsAnUnavailableCatalogWithoutRenderingDownloads()
    {
        var html = SdkDownloadsSection.Render(new SdkDownloadSnapshot("error", [], "Timed out"), InstallationSnapshot.Create("notFound", null), new());

        Assert.Contains("SDK downloads are unavailable", html);
        Assert.Contains("Timed out", html);
        Assert.DoesNotContain("Download .pkg", html);
    }

    private static SdkDownload Download(string product, string version, string? includedRuntimeVersion, string supportPhase, string releaseType) =>
        new(product, version, version, includedRuntimeVersion, "10.0", releaseType, supportPhase, "2028-11-14", "2026-11-01",
            [new("CVE-2026-0001", "https://msrc.microsoft.com/update-guide/vulnerability/CVE-2026-0001")],
            "https://builds.dotnet.microsoft.com/dotnet/package.pkg", "abc123");
}

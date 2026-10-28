using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using DotnetCockpit.Setup;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class SetupReleaseTests
{
    [Fact]
    public async Task CombinesStableCompatibleReleasesAcrossPages()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return;

        var asset = OperatingSystem.IsWindows()
            ? "dotnet-core-uninstall.msi"
            : $"dotnet-core-uninstall-osx-{(RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64")}.tar.gz";
        var handler = new ReleaseHandler(asset, pageTwo: false);
        using var client = new HttpClient(handler);
        var service = new DotnetUninstallToolService(NullLogger<DotnetUninstallToolService>.Instance, client);

        var snapshot = await service.ReadAsync(CancellationToken.None);

        Assert.Null(snapshot.ReleaseListError);
        Assert.Equal(["1.7.0", "1.6.0", "1.2.0"], snapshot.AvailableReleases!.Select(release => release.Version));
        Assert.Equal("1.7.0", snapshot.LatestVersion);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotExposePartialReleasesWhenAPageFails()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return;

        var asset = OperatingSystem.IsWindows() ? "dotnet-core-uninstall.msi" : "dotnet-core-uninstall.tar.gz";
        var handler = new ReleaseHandler(asset, pageTwo: true);
        using var client = new HttpClient(handler);
        var service = new DotnetUninstallToolService(NullLogger<DotnetUninstallToolService>.Instance, client);

        var snapshot = await service.ReadAsync(CancellationToken.None);

        Assert.Null(snapshot.AvailableReleases);
        Assert.Contains("could not be loaded", snapshot.ReleaseListError);
    }

    private sealed class ReleaseHandler(string asset, bool pageTwo) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal))
            {
                return Task.FromResult(pageTwo
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : Json("""[{"tag_name":"1.2.0","html_url":"https://github.com/dotnet/cli-lab/releases/tag/1.2.0","published_at":"2022-01-01T00:00:00Z","draft":false,"prerelease":false,"assets":[{"name":"ASSET"}]}]""".Replace("ASSET", asset)));
            }

            var firstPage = """
            [
              {"tag_name":"1.6.0","html_url":"https://github.com/dotnet/cli-lab/releases/tag/1.6.0","published_at":"2023-01-01T00:00:00Z","draft":false,"prerelease":false,"assets":[{"name":"ASSET"}]},
              {"tag_name":"0.1.0-preview.1","html_url":"https://github.com/dotnet/cli-lab/releases/tag/0.1.0-preview.1","published_at":"2022-01-01T00:00:00Z","draft":false,"prerelease":true,"assets":[{"name":"ASSET"}]},
              {"tag_name":"1.7.0","html_url":"https://github.com/dotnet/cli-lab/releases/tag/1.7.0","published_at":"2024-01-01T00:00:00Z","draft":false,"prerelease":false,"assets":[{"name":"ASSET"}]},
              {"tag_name":"1.5.0","html_url":"https://github.com/dotnet/cli-lab/releases/tag/1.5.0","published_at":"2022-01-01T00:00:00Z","draft":false,"prerelease":false,"assets":[]}
            ]
            """.Replace("ASSET", asset);
            var response = Json(firstPage);
            response.Headers.TryAddWithoutValidation("Link", "<https://api.github.com/repos/dotnet/cli-lab/releases?per_page=100&page=2>; rel=\"next\"");
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

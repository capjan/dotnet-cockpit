using System.Net;
using System.Runtime.InteropServices;
using DotnetCockpit.SdkDownloads;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class SdkDownloadCatalogTests
{
    [Fact]
    public async Task ReadsOfficialPackagesForTheCurrentMacArchitecture()
    {
        using var client = new HttpClient(new CatalogHandler());
        var catalog = new SdkDownloadCatalog(NullLogger<SdkDownloadCatalog>.Instance, client);

        var snapshot = await catalog.ReadAsync(CancellationToken.None);

        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var sdk = Assert.Single(snapshot.Downloads, download => download.Product == "SDK");
        Assert.Equal("ready", snapshot.Status);
        Assert.Equal("10.0.100", sdk.Version);
        Assert.Equal("10.0.0", sdk.IncludedRuntimeVersion);
        Assert.Equal($"https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-osx-{architecture}.pkg", sdk.Url);
        Assert.Equal("CVE-2026-0001", Assert.Single(sdk.Cves).Id);
        Assert.Equal(2, snapshot.Downloads.Length);
    }

    [Fact]
    public async Task DoesNotUseAnUntrustedReleaseMetadataUrl()
    {
        using var client = new HttpClient(new UntrustedCatalogHandler());
        var catalog = new SdkDownloadCatalog(NullLogger<SdkDownloadCatalog>.Instance, client);

        var snapshot = await catalog.ReadAsync(CancellationToken.None);

        Assert.Equal("error", snapshot.Status);
        Assert.Empty(snapshot.Downloads);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith("releases-index.json", StringComparison.Ordinal)
                ? """
                  {"releases-index":[{"channel-version":"10.0","release-type":"lts","support-phase":"active","eol-date":"2028-11-14","releases.json":"https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"}]}
                  """
                : ReleaseJson();
            return Task.FromResult(Json(content));
        }

        private static string ReleaseJson()
        {
            var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            var otherArchitecture = architecture == "arm64" ? "x64" : "arm64";
            return """
                {"releases":[{"release-date":"2026-11-01","cve-list":[{"cve-id":"CVE-2026-0001","cve-url":"https://msrc.microsoft.com/update-guide/vulnerability/CVE-2026-0001"}],"sdk":{"version-display":"10.0.100","version":"10.0.100","runtime-version":"10.0.0","files":[{"name":"dotnet-sdk-osx-ARCH.pkg","url":"https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-osx-ARCH.pkg","hash":"sdk-hash"},{"name":"dotnet-sdk-osx-OTHER_ARCH.pkg","url":"https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-osx-OTHER_ARCH.pkg","hash":"other-hash"}]},"runtime":{"version-display":"10.0.0","version":"10.0.0","files":[{"name":"dotnet-runtime-osx-ARCH.pkg","url":"https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.0/dotnet-runtime-osx-ARCH.pkg","hash":"runtime-hash"}]}}]}
                """
                .Replace("OTHER_ARCH", otherArchitecture, StringComparison.Ordinal)
                .Replace("ARCH", architecture, StringComparison.Ordinal);
        }
    }

    private sealed class UntrustedCatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Json("""{"releases-index":[{"channel-version":"10.0","releases.json":"https://example.com/releases.json"}]}"""));
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
    };
}

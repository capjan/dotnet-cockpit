using DotnetCockpit.Installation;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class InstallationTests
{
    [Fact]
    public void ParsesWindowsSdkPathsAndPreviewVersions()
    {
        var sdks = DotnetOutputParser.ParseSdks("10.0.100 [C:\\Program Files\\dotnet\\sdk]\r\n11.0.100-preview.1 [C:\\Program Files\\dotnet\\sdk]\r\n");
        Assert.Equal(2, sdks.Length);
        Assert.Equal(new SdkInstallation("10.0.100", @"C:\Program Files\dotnet\sdk"), sdks[0]);
        Assert.Equal("11.0.100-preview.1", sdks[1].Version);
    }

    [Fact]
    public void PreservesRuntimeFamiliesAndUnixPaths()
    {
        var runtimes = DotnetOutputParser.ParseRuntimes("Microsoft.AspNetCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]\nMicrosoft.NETCore.App 10.0.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n");
        Assert.Equal(2, runtimes.Length);
        Assert.Equal(new RuntimeInstallation("Microsoft.NETCore.App", "10.0.1", "/usr/share/dotnet/shared/Microsoft.NETCore.App"), runtimes[1]);
    }

    [Fact]
    public void EmptySdkListIsValidForRuntimeOnlyInstallations()
    {
        Assert.Empty(DotnetOutputParser.ParseSdks("\r\n"));
        Assert.Single(DotnetOutputParser.ParseRuntimes("Microsoft.NETCore.App 10.0.0 [/opt/dotnet/shared/Microsoft.NETCore.App]"));
    }

    [Fact]
    public void MissingPathDoesNotRequireAnInstalledRuntime()
    {
        Assert.Null(DotnetLocator.Find(null));
        Assert.Null(DotnetLocator.Find(""));
        Assert.Null(DotnetLocator.Find("."));
    }

    [Fact]
    public void FindsExecutableInAnAbsolutePathContainingSpaces()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cockpit test " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            File.WriteAllText(executable, "fixture");
            Assert.Equal(executable, DotnetLocator.Find(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void RejectsInvalidPorts(string port) =>
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--port", port]));

    [Fact]
    public void SupportsEphemeralPortsAndHeadlessStartup() =>
        Assert.Equal(new CommandLineOptions(0, false), CommandLineOptions.Parse(["--no-browser", "--port", "0"]));
}

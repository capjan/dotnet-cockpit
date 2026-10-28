using DotnetCockpit.Tools;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class ToolsTests
{
    [Fact]
    public void ParsesGlobalToolsAndMultipleCommands()
    {
        var tools = ToolOutputParser.Parse("Package Id      Version      Commands\n--------------------------------------\nexample.tool    1.2.3        example, example-alt\n");
        var tool = Assert.Single(tools);
        Assert.Equal("example.tool", tool.PackageId);
        Assert.Equal("1.2.3", tool.Version);
        Assert.Equal(["example", "example-alt"], tool.Commands);
        Assert.Null(tool.Manifest);
    }

    [Fact]
    public void PreservesManifestPathsContainingSpaces()
    {
        var tools = ToolOutputParser.Parse("Package Id      Version      Commands      Manifest\r\n-----------------------------------------------------\r\nexample.tool    2.0.0        example       C:\\My  Projects\\.config\\dotnet-tools.json\r\n");
        Assert.Equal(@"C:\My  Projects\.config\dotnet-tools.json", Assert.Single(tools).Manifest);
    }

    [Fact]
    public void EmptyTableIsAnEmptyToolList() =>
        Assert.Empty(ToolOutputParser.Parse("Package Id      Version      Commands      Manifest\n-----------------------------------------------------\n"));

    [Fact]
    public void ParsesSearchResults()
    {
        var results = ToolOutputParser.ParseSearch("Package-ID      Aktuelle Version      Autoren      Downloads      Überprüft\n-------------------------------------------------------------------------------------\ndotnet-ef       10.0.0               Microsoft    12345          x\n");
        var result = Assert.Single(results);
        Assert.Equal("dotnet-ef", result.PackageId);
        Assert.Equal("10.0.0", result.Version);
        Assert.Equal("Microsoft", result.Authors);
        Assert.Equal("12345", result.Downloads);
        Assert.True(result.Verified);
    }

    [Theory]
    [InlineData("Unexpected output")]
    [InlineData("Package Id\n----------\nincomplete")]
    public void MalformedOutputIsNotReportedAsAnEmptyList(string output) =>
        Assert.Throws<FormatException>(() => ToolOutputParser.Parse(output));

    [Fact]
    public void RendersToolTabsAndEscapesToolData()
    {
        var snapshot = new ToolsSnapshot("ready", "/workspace/<project>",
            new([new("global.tool", "1.2.3", ["global-command"], null)]),
            new([new("local.tool", "2.0.0", ["<command>", "alternate"], "/workspace/<project>/.config/dotnet-tools.json")]));
        var html = ToolsPage.Render(snapshot, new("global"));
        Assert.Contains("data-tool-scope=\"global\"", html);
        Assert.Contains("global.tool", html);
        Assert.DoesNotContain("local.tool", html);
        Assert.Contains("href=\"/tools?scope=local\"", html);
        Assert.Contains("aria-selected=\"true\"", html);
        Assert.Contains("href=\"/tools\" class=\"nav-link\" aria-current=\"page\"", html);

        var localHtml = ToolsPage.Render(snapshot, new("local"));
        Assert.Contains("local.tool", localHtml);
        Assert.Contains("&lt;command&gt;", localHtml);
        Assert.Contains("/workspace/&lt;project&gt;/.config/dotnet-tools.json", localHtml);
        Assert.DoesNotContain("<project>", localHtml);
        Assert.DoesNotContain("global.tool", localHtml);
    }

    [Fact]
    public void AFailedLocalListDoesNotHideGlobalTools()
    {
        var snapshot = new ToolsSnapshot("ready", "/workspace",
            new([new("global.tool", "1.0.0", ["global-command"], null)]), new([], "Invalid <manifest>"));
        var globalHtml = ToolsPage.Render(snapshot, new("global"));
        Assert.Contains("global.tool", globalHtml);
        Assert.DoesNotContain("Local tools could not be read", globalHtml);

        var localHtml = ToolsPage.Render(snapshot, new("local"));
        Assert.Contains("Local tools could not be read", localHtml);
        Assert.Contains("Invalid &lt;manifest&gt;", localHtml);
        Assert.DoesNotContain("No local tools found", localHtml);
    }

    [Fact]
    public void DistinguishesEmptyListsFromMissingSdk()
    {
        var empty = ToolsPage.Render(ToolsSnapshot.Empty("ready", "/workspace"), new("global"));
        var emptyLocal = ToolsPage.Render(new ToolsSnapshot("ready", "/workspace", new([]), new([]), null, true, "/workspace/.config/dotnet-tools.json"), new("local"));
        var missingSdk = ToolsPage.Render(ToolsSnapshot.Empty("sdkMissing", "/workspace"), new("global"));
        var missingHost = ToolsPage.Render(ToolsSnapshot.Empty("notFound", "/workspace"), new("global"));
        Assert.Contains("No global tools installed", empty);
        Assert.Contains("No local tools found", emptyLocal);
        Assert.Contains("A .NET SDK is required", missingSdk);
        Assert.DoesNotContain("No global tools installed", missingSdk);
        Assert.Contains("No .NET installation found", missingHost);
    }

    [Fact]
    public void RendersSearchInstallTargetsAndUninstallActions()
    {
        var snapshot = new ToolsSnapshot("ready", "/workspace", new([new("global.tool", "1.0.0", ["global"], null)]), new([]), null, false);
        var search = new ToolSearch("format", [new("format.tool", "2.0.0", "Author", "123", true)]);
        var withoutManifest = ToolsPage.Render(snapshot, new("global", "format"), search);
        Assert.Contains("Initialize local tools", withoutManifest);
        Assert.Contains("Install globally", withoutManifest);
        Assert.DoesNotContain("Install locally", withoutManifest);
        Assert.Contains("action=\"/tools/uninstall\"", withoutManifest);

        var initialized = snapshot with { LocalManifestInitialized = true };
        var withManifest = ToolsPage.Render(initialized, new("global", "format"), search);
        Assert.Contains("Install locally", withManifest);
    }
}

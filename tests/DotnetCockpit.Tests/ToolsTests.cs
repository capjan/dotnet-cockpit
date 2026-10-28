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

    [Theory]
    [InlineData("Unexpected output")]
    [InlineData("Package Id\n----------\nincomplete")]
    public void MalformedOutputIsNotReportedAsAnEmptyList(string output) =>
        Assert.Throws<FormatException>(() => ToolOutputParser.Parse(output));

    [Fact]
    public void RendersBothToolScopesAndEscapesToolData()
    {
        var snapshot = new ToolsSnapshot("ready", "/workspace/<project>",
            new([new("global.tool", "1.2.3", ["global-command"], null)]),
            new([new("local.tool", "2.0.0", ["<command>", "alternate"], "/workspace/<project>/.config/dotnet-tools.json")]));
        var html = ToolsPage.Render(snapshot);
        Assert.Contains("data-tool-scope=\"global\"", html);
        Assert.Contains("data-tool-scope=\"local\"", html);
        Assert.Contains("global.tool", html);
        Assert.Contains("local.tool", html);
        Assert.Contains("&lt;command&gt;", html);
        Assert.Contains("/workspace/&lt;project&gt;/.config/dotnet-tools.json", html);
        Assert.DoesNotContain("<project>", html);
        Assert.Contains("href=\"/tools\" class=\"nav-link nav-child\" aria-current=\"page\"", html);
    }

    [Fact]
    public void AFailedLocalListDoesNotHideGlobalTools()
    {
        var snapshot = new ToolsSnapshot("ready", "/workspace",
            new([new("global.tool", "1.0.0", ["global-command"], null)]), new([], "Invalid <manifest>"));
        var html = ToolsPage.Render(snapshot);
        Assert.Contains("global.tool", html);
        Assert.Contains("Local tools could not be read", html);
        Assert.Contains("Invalid &lt;manifest&gt;", html);
        Assert.DoesNotContain("No local tools found", html);
    }

    [Fact]
    public void DistinguishesEmptyListsFromMissingSdk()
    {
        var empty = ToolsPage.Render(ToolsSnapshot.Empty("ready", "/workspace"));
        var missingSdk = ToolsPage.Render(ToolsSnapshot.Empty("sdkMissing", "/workspace"));
        var missingHost = ToolsPage.Render(ToolsSnapshot.Empty("notFound", "/workspace"));
        Assert.Contains("No global tools installed", empty);
        Assert.Contains("No local tools found", empty);
        Assert.Contains("A .NET SDK is required", missingSdk);
        Assert.DoesNotContain("No global tools installed", missingSdk);
        Assert.Contains("No .NET installation found", missingHost);
    }
}

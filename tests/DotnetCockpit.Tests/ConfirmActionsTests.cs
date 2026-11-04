using Xunit;

namespace DotnetCockpit.Tests;

public sealed class ConfirmActionsTests
{
    [Fact]
    public void ToolRemovalAsksFirstAndShowsTheCommand()
    {
        var spec = ConfirmActions.ToolUninstall("dotnet-ef", "global");
        var html = ConfirmActions.Trigger(spec, "<button type=\"submit\">Remove</button>");

        Assert.Contains("method=\"get\" action=\"/confirm\"", html);
        Assert.Contains("data-confirm-post=\"/tools/uninstall\"", html);
        Assert.Contains("dotnet tool uninstall --global dotnet-ef", html);
        Assert.DoesNotContain("method=\"post\"", html);
    }

    [Fact]
    public void TheConfirmPagePostsTheSameFieldsWithoutJavaScript()
    {
        var html = ConfirmActions.RenderPage(ConfirmActions.ToolUninstall("dotnet-ef", "local"), 0, null, null);

        Assert.Contains("method=\"post\" action=\"/tools/uninstall\"", html);
        Assert.Contains("name=\"packageId\" value=\"dotnet-ef\"", html);
        Assert.Contains("name=\"scope\" value=\"local\"", html);
        Assert.Contains("href=\"/tools?scope=local\"", html);
    }

    [Fact]
    public void EscapesUntrustedValues()
    {
        var html = ConfirmActions.Trigger(ConfirmActions.ToolUninstall("\"><script>", "global"), "");
        Assert.DoesNotContain("<script>", html);
    }

    [Theory]
    [InlineData("github", "github")]
    [InlineData("codex", "codex")]
    [InlineData("<script>", "cockpit")]
    [InlineData(null, "cockpit")]
    public void AcceptsOnlyKnownPalettes(string? value, string expected) =>
        Assert.Equal(expected, SettingsStore.NormalizePalette(value));

    [Fact]
    public void ASettingsFileFromBeforePalettesKeepsItsChoicesAndGetsTheDefault()
    {
        var directory = Directory.CreateTempSubdirectory("cockpit-settings-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "settings.json"), "{\"theme\":\"dark\",\"preflightOnLaunch\":true}");
            var settings = new SettingsStore(directory.FullName).Current;
            Assert.Equal("dark", settings.Theme);
            Assert.True(settings.PreflightOnLaunch);
            Assert.Equal("cockpit", settings.Palette);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("-1\n", "graphite")]
    [InlineData("0", "red")]
    [InlineData("1", "orange")]
    [InlineData("2", "yellow")]
    [InlineData("3", "green")]
    [InlineData("4", "blue")]
    [InlineData("5", "purple")]
    [InlineData("6", "pink")]
    [InlineData(null, "blue")]
    public void MapsTheMacOsAccentSetting(string? value, string expected) =>
        Assert.Equal(expected, SystemAccent.Parse(value));
}

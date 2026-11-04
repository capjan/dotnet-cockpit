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
}

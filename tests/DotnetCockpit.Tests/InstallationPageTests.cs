using System.Text.RegularExpressions;
using DotnetCockpit.Installation;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class InstallationPageTests
{
    private static InstallationSnapshot Snapshot() => InstallationSnapshot.Create("ready", "/opt/dotnet/dotnet",
        [new("10.0.9", "/opt/dotnet/sdk"), new("10.0.10", "/opt/dotnet/sdk")],
        [new("Microsoft.NETCore.App", "10.0.5", "/opt/dotnet/shared/Microsoft.NETCore.App"),
         new("Microsoft.AspNetCore.App", "10.0.7", "/opt/dotnet/shared/Microsoft.AspNetCore.App")]);

    [Fact]
    public void RendersInventoryAndVersionDirectoriesInTheInitialHtml()
    {
        var snapshot = Snapshot();
        var html = InstallationPage.Render(snapshot, new());
        Assert.Equal(4, Regex.Matches(html, "<tr data-kind=").Count);
        Assert.Contains(snapshot.Sdks[0].InstallationPath, html);
        Assert.Contains(snapshot.Runtimes[0].InstallationPath, html);
        Assert.Contains("data-full-version>10.0.5</span>", html);
        Assert.DoesNotContain("id=\"root\"", html);
    }

    [Fact]
    public void FiltersByTypeAndAllSearchTermsOnTheServer()
    {
        var html = InstallationPage.Render(Snapshot(), new(Query: "asp.net 10.0.7", Kind: "runtimes"));
        Assert.Single(Regex.Matches(html, "<tr data-kind="));
        Assert.Contains("ASP.NET Core Runtime</strong>", html);
        Assert.DoesNotContain("data-full-version>10.0.5</span>", html);
        Assert.Contains("1 of 4 installations shown", html);
    }

    [Fact]
    public void GroupsRuntimeFrameworksWithTheSameVersionInOneCard()
    {
        var snapshot = InstallationSnapshot.Create("ready", "/opt/dotnet/dotnet",
            runtimes: [
                new("Microsoft.NETCore.App", "10.0.7", "/opt/dotnet/shared/Microsoft.NETCore.App"),
                new("Microsoft.AspNetCore.App", "10.0.7", "/opt/dotnet/shared/Microsoft.AspNetCore.App")]);

        var html = InstallationPage.Render(snapshot, new());

        Assert.Single(Regex.Matches(html, "<tr data-kind=\"runtime\""));
        Assert.Contains("Microsoft.NETCore.App", html);
        Assert.Contains("Microsoft.AspNetCore.App", html);
        Assert.Contains(Path.Combine("/opt/dotnet/shared/Microsoft.NETCore.App", "10.0.7"), html);
        Assert.Contains(Path.Combine("/opt/dotnet/shared/Microsoft.AspNetCore.App", "10.0.7"), html);
        Assert.Contains("Installed runtimes", html);
        Assert.Contains("<strong class=\"summary-number\">1</strong>", html);
    }

    [Fact]
    public void RendersRemoveActionOnlyForUninstallableInstallations()
    {
        var snapshot = InstallationSnapshot.Create("ready", "/opt/dotnet/dotnet",
            [new SdkInstallation("10.0.9", "/opt/dotnet/sdk") { CanRemove = true }]);

        var html = InstallationPage.Render(snapshot, new(Kind: "sdks"));

        Assert.Contains("<th scope=\"col\">Actions</th>", html);
        Assert.Contains("data-confirm-post=\"/installation/remove\"", html);
        Assert.Contains("name=\"kind\" value=\"sdk\"", html);
        Assert.Contains("name=\"version\" value=\"10.0.9\"", html);
        Assert.DoesNotContain("data-confirm-post=\"/installation/remove\"", InstallationPage.Render(Snapshot(), new()));
    }

    [Fact]
    public void RendersUninstallListEntriesWithoutUnavailablePaths()
    {
        var snapshot = InstallationSnapshot.Create("ready", null,
            [new SdkInstallation("10.0.403", "") { CanRemove = true }],
            [new RuntimeInstallation("Microsoft.NETCore.App", "10.0.14", "") { CanRemove = true }]);

        var html = InstallationPage.Render(snapshot, new());

        Assert.Contains("data-full-version>10.0.403</span>", html);
        Assert.Contains("data-full-version>10.0.14</span>", html);
        Assert.Contains("name=\"version\" value=\"10.0.403\"", html);
        Assert.DoesNotContain("/10.0.403", html);
        Assert.DoesNotContain("/10.0.14", html);
        Assert.DoesNotContain("dotnet host", html);
    }

    [Fact]
    public void SortsVersionsNumericallyInBothDirections()
    {
        var descending = InstallationPage.Render(Snapshot(), new());
        var ascending = InstallationPage.Render(Snapshot(), new(Ascending: true));
        Assert.True(descending.IndexOf("data-full-version>10.0.10</span>", StringComparison.Ordinal) < descending.IndexOf("data-full-version>10.0.9</span>", StringComparison.Ordinal));
        Assert.True(ascending.IndexOf("data-full-version>10.0.10</span>", StringComparison.Ordinal) > ascending.IndexOf("data-full-version>10.0.9</span>", StringComparison.Ordinal));
    }

    [Fact]
    public void EscapesSearchTextAndInstallationPaths()
    {
        var snapshot = InstallationSnapshot.Create("ready", "/opt/<script>/dotnet", [new("10.0.0", "/opt/<img>/sdk")]);
        var page = InstallationPage.Render(snapshot, new());
        var filtered = InstallationPage.Render(snapshot, new(Query: "\"><script>alert(1)</script>"));
        Assert.Contains("&lt;img&gt;", page);
        Assert.Contains("&lt;script&gt;", filtered);
        Assert.DoesNotContain("<script>alert", filtered);
        Assert.DoesNotContain("/opt/<img>", page);
    }

    [Fact]
    public void RendersMissingAndFailedQueriesWithoutBrowserCode()
    {
        var missing = InstallationPage.Render(InstallationSnapshot.Create("notFound", null), new());
        var error = InstallationPage.Render(InstallationSnapshot.Create("error", null, error: "Cannot read <host>"), new());
        Assert.Contains("No .NET installation found", missing);
        Assert.Contains("role=\"alert\"", error);
        Assert.Contains("Cannot read &lt;host&gt;", error);
        Assert.Contains("refresh=", error);
    }
}

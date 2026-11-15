using Xunit;

namespace DotnetCockpit.Tests;

public sealed class LocalOriginTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5123", null)]
    [InlineData("http://127.0.0.1:5123", "same-origin")]
    [InlineData(null, "same-origin")]
    [InlineData(null, "none")]
    [InlineData(null, null)]
    public void AllowsChangesFromCockpitAndFromClientsThatAreNotBrowsers(string? origin, string? fetchSite) =>
        Assert.True(LocalOrigin.AllowsChange(origin, fetchSite, "127.0.0.1:5123"));

    [Theory]
    [InlineData("https://evil.example", "cross-site")]
    [InlineData("http://127.0.0.1:9999", "same-site")]
    [InlineData("http://localhost:5123", null)]
    [InlineData("null", "cross-site")]
    [InlineData(null, "cross-site")]
    [InlineData(null, "same-site")]
    public void RefusesChangesSentByOtherPages(string? origin, string? fetchSite) =>
        Assert.False(LocalOrigin.AllowsChange(origin, fetchSite, "127.0.0.1:5123"));
}

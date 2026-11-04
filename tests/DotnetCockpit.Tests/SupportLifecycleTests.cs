using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class SupportLifecycleTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static SdkDownloadSnapshot Catalog() => new("ready", [], Channels:
    [
        new("11.0", "STS", "GO-LIVE", null, new(2026, 2, 10), null),
        new("10.0", "LTS", "ACTIVE", new(2028, 11, 14), new(2025, 2, 11), new(2025, 11, 11)),
        new("9.0", "STS", "MAINTENANCE", new(2026, 11, 10), new(2024, 2, 13), new(2024, 12, 3)),
        new("7.0", "STS", "EOL", new(2024, 5, 14), new(2022, 2, 17), new(2022, 11, 8)),
    ]);

    [Theory]
    [InlineData("10.0.403", nameof(SupportState.Supported), "Supported until Nov 14, 2028")]
    [InlineData("9.0.320", nameof(SupportState.EndingSoon), "Support ends in 6 days (Nov 10, 2026)")]
    [InlineData("7.0.408", nameof(SupportState.EndOfLife), "Out of support since May 14, 2024")]
    [InlineData("11.0.100-rc.3.26501.103", nameof(SupportState.Preview), "Preview release")]
    [InlineData("12.0.100", nameof(SupportState.Unknown), "Support status unavailable")]
    public void DerivesTheStateFromCatalogDatesAndTheCurrentDate(string version, string state, string label)
    {
        var status = SupportLifecycle.For(Catalog(), version, Today);
        Assert.Equal(state, status.State.ToString());
        Assert.Equal(label, status.Label);
    }

    [Fact]
    public void TheRemainingTimeFollowsTheCurrentDateAndEndsInEndOfLife()
    {
        Assert.Equal("Support ends tomorrow (Nov 10, 2026)", SupportLifecycle.For(Catalog(), "9.0.320", new(2026, 11, 9)).Label);
        Assert.Equal("Support ends today (Nov 10, 2026)", SupportLifecycle.For(Catalog(), "9.0.320", new(2026, 11, 10)).Label);
        Assert.Equal(SupportState.EndOfLife, SupportLifecycle.For(Catalog(), "9.0.320", new(2026, 11, 11)).State);
        Assert.Equal(SupportState.Supported, SupportLifecycle.For(Catalog(), "9.0.320", new(2026, 9, 1)).State);
    }

    [Fact]
    public void AnUnavailableCatalogNeverClaimsASupportState()
    {
        Assert.Equal(SupportState.Unknown, SupportLifecycle.For(null, "7.0.408", Today).State);
        Assert.Equal(SupportState.Unknown, SupportLifecycle.For(new("error", [], "offline"), "7.0.408", Today).State);
    }

    [Fact]
    public void HealthCountsExactlyTheRecommendedActions()
    {
        var snapshot = InstallationSnapshot.Create("ready", "/opt/dotnet/dotnet",
            [new("10.0.403", "/sdk"), new("9.0.320", "/sdk"), new("7.0.408", "/sdk")],
            [new("Microsoft.NETCore.App", "7.0.18", "/shared")]);

        var health = EnvironmentHealth.Evaluate(snapshot, Catalog(), Today);

        Assert.Equal(2, health.Count);
        Assert.Contains("out of support", health.Actions[0].Title);
        Assert.Equal(".NET 9 support ends in 6 days", health.Actions[1].Title);
        Assert.Contains(".NET 7 is out of support", health.Headline);
        Assert.Contains(".NET 9 support ends in 6 days", health.Headline);
    }

    [Fact]
    public void AHealthySystemHasNoActionsButAnOfflineCatalogIsNotCalledHealthy()
    {
        var snapshot = InstallationSnapshot.Create("ready", "/opt/dotnet/dotnet", [new("10.0.403", "/sdk")]);

        var healthy = EnvironmentHealth.Evaluate(snapshot, Catalog(), Today);
        var offline = EnvironmentHealth.Evaluate(snapshot, new("error", [], "offline"), Today);

        Assert.Equal(0, healthy.Count);
        Assert.True(healthy.SupportKnown);
        Assert.Contains("supported and up to date", healthy.Headline);
        Assert.False(offline.SupportKnown);
        Assert.Contains("could not be checked", offline.Headline);
    }

    [Fact]
    public void TheRingShowsTheActionListInProportionMostSevereFirst()
    {
        HealthAction Action(string severity) => new(severity, "", "", "", []);
        var report = new HealthReport([Action("bad"), Action("bad"), Action("warn"), Action("info")], true, "");

        Assert.Equal("conic-gradient(var(--bad) 0% 48.5%, transparent 48.5% 50%, var(--warn) 50% 73.5%, transparent 73.5% 75%, var(--info) 75% 98.5%, transparent 98.5% 100%)", report.RingGradient);
        Assert.Equal("var(--ok)", new HealthReport([], true, "").RingGradient);
        Assert.Equal("var(--line-strong)", new HealthReport([], false, "").RingGradient);
    }

    [Fact]
    public void AFailedQueryIsNotReportedAsAMissingSdk()
    {
        var health = EnvironmentHealth.Evaluate(InstallationSnapshot.Create("error", null, error: "boom"), Catalog(), Today);
        Assert.Equal(0, health.Count);
        Assert.Contains("could not be read", health.Headline);
    }

    [Fact]
    public void VersionsPageShowsTheComputedDateInsteadOfAFixedText()
    {
        var snapshot = InstallationSnapshot.Create("ready", "/opt/dotnet/dotnet", [new("9.0.320", "/sdk")]);
        var html = InstallationPage.Render(snapshot, Catalog(), new(), Today);
        Assert.Contains("Support ends in 6 days (Nov 10, 2026)", html);
        Assert.DoesNotContain("Ends in 7 days", html);
    }
}

using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;

namespace DotnetCockpit;

internal sealed record HealthLink(string Label, string Href, string Class = "button sm", string? Icon = null, bool Download = false);

internal sealed record HealthAction(string Severity, string Icon, string Title, string Detail, HealthLink[] Links);

/// <summary>The single source for the number shown in the ring, the action list and the navigation badge.</summary>
internal sealed record HealthReport(HealthAction[] Actions, bool SupportKnown, string Headline)
{
    public int Count => Actions.Length;

    /// <summary>The ring shows the action list in proportion, most severe first, with a hairline between parts.</summary>
    public string RingGradient
    {
        get
        {
            if (Count == 0) return SupportKnown ? "var(--ok)" : "var(--line-strong)";
            string[] order = ["bad", "warn", "info"];
            var parts = order
                .Select(severity => (Severity: severity, Share: Actions.Count(action => action.Severity == severity) / (double)Count))
                .Where(part => part.Share > 0)
                .ToArray();
            var gap = parts.Length > 1 ? 1.5 : 0;
            var start = 0.0;
            var stops = new List<string>();
            foreach (var (severity, share) in parts)
            {
                var end = start + share * 100;
                stops.Add(FormattableString.Invariant($"var(--{severity}) {start:0.##}% {end - gap:0.##}%"));
                if (gap > 0) stops.Add(FormattableString.Invariant($"transparent {end - gap:0.##}% {end:0.##}%"));
                start = end;
            }
            return $"conic-gradient({string.Join(", ", stops)})";
        }
    }
}

internal static class EnvironmentHealth
{
    public static HealthReport Evaluate(InstallationSnapshot snapshot, SdkDownloadSnapshot? catalog, DateOnly? today = null)
    {
        if (snapshot.Status == "error")
            return new([], false, "The .NET installation could not be read.");

        var currentDate = today ?? SupportLifecycle.Today;
        var rows = InstallationPage.BuildRows(snapshot, catalog, currentDate);
        var supportKnown = catalog?.Channels is { Length: > 0 };
        var actions = new List<HealthAction>();

        if (snapshot.Sdks.Length == 0)
        {
            actions.Add(new("warn", "code", "Install a .NET SDK",
                snapshot.Status == "notFound" ? "No dotnet host was found on PATH." : "No SDK was found on this machine. An SDK is required to build applications.",
                [new("Install .NET", "/?install=1", "button sm primary")]));
        }

        var channels = rows.GroupBy(row => row.Channel)
            .OrderBy(group => group.First().Support.DaysLeft ?? int.MaxValue)
            .ToArray();
        var eol = channels.Where(group => group.First().Support.IsEol).ToArray();
        var endingSoon = channels.Where(group => group.First().Support.IsEndingSoon).ToArray();
        var patchable = channels
            .Where(group => group.Any(row => row.HasPatch && !row.LatestPatchInstalled))
            .OrderByDescending(group => group.Key.Length).ThenByDescending(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var group in eol)
        {
            var count = group.Count();
            var support = group.First().Support;
            actions.Add(new("bad", "shield",
                support.EndDate.Length > 0 ? $".NET {group.Key} has been out of support since {support.EndDate}" : $".NET {group.Key} is out of support",
                $"{count} installed {(count == 1 ? "item no longer receives" : "items no longer receive")} security fixes.",
                [
                    new("Details", $"/?selected={Uri.EscapeDataString(group.First().Version)}#inventory", "button sm ghost"),
                    .. group.Any(row => row.CanRemove)
                        ? [new HealthLink($"Remove {count} {(count == 1 ? "item" : "items")}", $"/confirm?action=channel-remove&channel={Uri.EscapeDataString(group.Key)}", "button sm", "trash")]
                        : Array.Empty<HealthLink>()
                ]));
        }

        foreach (var group in endingSoon)
        {
            var count = group.Count();
            var support = group.First().Support;
            actions.Add(new("warn", "clock",
                $".NET {group.Key} support ends {SupportLifecycle.RelativeDays(support.DaysLeft ?? 0)}",
                $"{support.EndDate}. {count} installed {(count == 1 ? "item stops" : "items stop")} receiving security fixes after that date.",
                [new("Details", $"/?selected={Uri.EscapeDataString(group.First().Version)}#inventory", "button sm ghost")]));
        }

        foreach (var group in patchable)
        {
            var row = group.First(candidate => candidate.HasPatch && !candidate.LatestPatchInstalled);
            var product = row.Kind == "sdk" ? "SDK" : "Runtime";
            actions.Add(new("info", "download",
                $"Patch available for .NET {group.Key}",
                $"{product} {row.DisplayVersion} → {row.LatestPatchVersion}.",
                [row.PackageUrl is { Length: > 0 } url
                    ? new("Download", url, "button sm", "download", Download: true)
                    : new("Details", $"/?selected={Uri.EscapeDataString(row.Version)}#inventory", "button sm ghost")]));
        }

        return new([.. actions], supportKnown, BuildHeadline(snapshot, eol, endingSoon, patchable.Length, supportKnown));
    }

    private static string BuildHeadline(InstallationSnapshot snapshot, IGrouping<string, InstallationPage.Row>[] eol, IGrouping<string, InstallationPage.Row>[] endingSoon, int patchCount, bool supportKnown)
    {
        var phrases = new List<string>();
        if (snapshot.Sdks.Length == 0) phrases.Add("No .NET SDK is installed");
        if (eol.Length > 0)
            phrases.Add($"{Names(eol)} {(eol.Length == 1 ? "is" : "are")} out of support");
        if (endingSoon.Length > 0)
            phrases.Add($"{Names(endingSoon)} support ends {SupportLifecycle.RelativeDays(endingSoon[0].First().Support.DaysLeft ?? 0)}");
        if (patchCount > 0)
            phrases.Add($"{patchCount} {(patchCount == 1 ? "patch" : "patches")} available");

        if (phrases.Count > 0) return string.Join(", ", phrases) + ".";
        return supportKnown
            ? "All installed versions are supported and up to date."
            : "Support status could not be checked because the release catalog is unavailable.";
    }

    private static string Names(IEnumerable<IGrouping<string, InstallationPage.Row>> groups) =>
        string.Join(" and ", groups.Select(group => $".NET {group.Key}"));
}

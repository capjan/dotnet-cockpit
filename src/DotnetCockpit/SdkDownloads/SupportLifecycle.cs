using System.Globalization;

namespace DotnetCockpit.SdkDownloads;

/// <summary>Official lifecycle data for one .NET channel, read from the Microsoft release metadata.</summary>
internal sealed record ChannelLifecycle(
    string Channel,
    string ReleaseType,
    string SupportPhase,
    DateOnly? EndOfSupport,
    DateOnly? FirstRelease,
    DateOnly? GeneralAvailability);

internal enum SupportState { Unknown, Preview, Supported, EndingSoon, EndOfLife }

internal sealed record SupportStatus(SupportState State, ChannelLifecycle? Channel, int? DaysLeft)
{
    public bool IsEol => State == SupportState.EndOfLife;
    public bool IsEndingSoon => State == SupportState.EndingSoon;
    public string ReleaseType => Channel?.ReleaseType ?? "";
    public bool IsLts => ReleaseType == "LTS";
    public string EndDate => Channel?.EndOfSupport is { } date ? SupportLifecycle.FormatDate(date) : "";

    /// <summary>Short status for a channel header, such as "Support ends in 6 days".</summary>
    public string Label => State switch
    {
        SupportState.EndOfLife => EndDate.Length > 0 ? $"Out of support since {EndDate}" : "Out of support",
        SupportState.EndingSoon => $"Support ends {SupportLifecycle.RelativeDays(DaysLeft ?? 0)} ({EndDate})",
        SupportState.Preview => "Preview release",
        SupportState.Supported => EndDate.Length > 0 ? $"Supported until {EndDate}" : "Supported",
        _ => "Support status unavailable"
    };
}

internal static class SupportLifecycle
{
    public const int EndingSoonDays = 30;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static SupportStatus For(SdkDownloadSnapshot? catalog, string version, DateOnly today)
    {
        var channel = ChannelVersion(version);
        var lifecycle = catalog?.Channels?.FirstOrDefault(entry => entry.Channel == channel);
        return lifecycle is null ? new(SupportState.Unknown, null, null) : Evaluate(lifecycle, today);
    }

    public static SupportStatus Evaluate(ChannelLifecycle lifecycle, DateOnly today)
    {
        var daysLeft = lifecycle.EndOfSupport is { } end ? end.DayNumber - today.DayNumber : (int?)null;
        var state = lifecycle.SupportPhase switch
        {
            "EOL" => SupportState.EndOfLife,
            _ when daysLeft < 0 => SupportState.EndOfLife,
            "PREVIEW" or "GO-LIVE" => SupportState.Preview,
            _ when daysLeft <= EndingSoonDays => SupportState.EndingSoon,
            _ => SupportState.Supported
        };
        return new(state, lifecycle, daysLeft);
    }

    /// <summary>"9.0.320" and "9.0.0-rc.1.2" both belong to the "9.0" channel.</summary>
    public static string ChannelVersion(string version)
    {
        var parts = version.Split('-', 2)[0].Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static string FormatDate(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string RelativeDays(int days) => days switch
    {
        <= 0 => "today",
        1 => "tomorrow",
        _ => $"in {days} days"
    };
}

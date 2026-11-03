using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace DotnetCockpit.Installation;

internal sealed record SdkInstallation(string Version, string Path)
{
    [JsonIgnore]
    public bool CanRemove { get; init; }

    public string InstallationPath => string.IsNullOrWhiteSpace(Path) ? "" : System.IO.Path.Combine(Path, Version);
}

internal sealed record RuntimeInstallation(string Name, string Version, string Path)
{
    [JsonIgnore]
    public bool CanRemove { get; init; }

    public string InstallationPath => string.IsNullOrWhiteSpace(Path) ? "" : System.IO.Path.Combine(Path, Version);
}

internal sealed record InstallationSnapshot(
    string Status,
    string? ExecutablePath,
    string OperatingSystem,
    string Architecture,
    DateTimeOffset CheckedAt,
    SdkInstallation[] Sdks,
    RuntimeInstallation[] Runtimes,
    string? Error = null)
{
    [JsonIgnore]
    public int IssueCount
    {
        get
        {
            var issues = 0;
            if (Status is "notFound" or "error") issues++;
            if (Sdks.Length == 0) issues++;
            if (Runtimes.Length == 0) issues++;
            issues += Sdks.Count(s => IsEol(s.Version));
            issues += Runtimes.Where(r => IsEol(r.Version)).Select(r => r.Version).Distinct().Count();
            return issues;
        }
    }

    public static bool IsEol(string version)
    {
        var major = version.Split('.')[0];
        return major is "7" or "6" or "5" or "3" or "2" or "1";
    }

    public static InstallationSnapshot Create(
        string status, string? executablePath,
        SdkInstallation[]? sdks = null, RuntimeInstallation[]? runtimes = null, string? error = null) =>
        new(status, executablePath, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            DateTimeOffset.UtcNow, sdks ?? [], runtimes ?? [], error);
}

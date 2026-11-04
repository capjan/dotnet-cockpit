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
    /// <summary>One row per SDK and one per runtime version, because frameworks sharing a version are grouped.</summary>
    [JsonIgnore]
    public int InstallationCount => Sdks.Length + Runtimes.Select(runtime => runtime.Version).Distinct().Count();

    /// <summary>Changes whenever a version appears or disappears, so an open page can notice without polling.</summary>
    public string Signature => string.Join('|', Sdks.Select(sdk => $"sdk:{sdk.Version}")
        .Concat(Runtimes.Select(runtime => $"{runtime.Name}:{runtime.Version}"))
        .Order(StringComparer.Ordinal));

    public static InstallationSnapshot Create(
        string status, string? executablePath,
        SdkInstallation[]? sdks = null, RuntimeInstallation[]? runtimes = null, string? error = null) =>
        new(status, executablePath, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            DateTimeOffset.UtcNow, sdks ?? [], runtimes ?? [], error);
}

using System.Runtime.InteropServices;

namespace DotnetCockpit.Installation;

internal sealed record SdkInstallation(string Version, string Path)
{
    public string InstallationPath => System.IO.Path.Combine(Path, Version);
}

internal sealed record RuntimeInstallation(string Name, string Version, string Path)
{
    public string InstallationPath => System.IO.Path.Combine(Path, Version);
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
    public static InstallationSnapshot Create(
        string status, string? executablePath,
        SdkInstallation[]? sdks = null, RuntimeInstallation[]? runtimes = null, string? error = null) =>
        new(status, executablePath, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            DateTimeOffset.UtcNow, sdks ?? [], runtimes ?? [], error);
}

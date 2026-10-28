namespace DotnetCockpit.Setup;

internal sealed record UninstallToolInstallation(bool Installed, string? Path, string? Scope, string? Version = null);

internal sealed record SetupSnapshot(
    string Status,
    string OperatingSystem,
    string Architecture,
    UninstallToolInstallation UninstallTool,
    string GlobalInstallPath,
    string? LatestVersion = null,
    string? UpdateCheckError = null,
    string? Error = null);

internal sealed record SetupActionResult(bool Success, string? Error = null);

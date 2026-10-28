namespace DotnetCockpit.Setup;

internal sealed record UninstallToolInstallation(bool Installed, string? Path, string? Scope);

internal sealed record SetupSnapshot(
    string Status,
    string OperatingSystem,
    string Architecture,
    UninstallToolInstallation UninstallTool,
    string UserInstallPath,
    string GlobalInstallPath,
    string? Error = null);

internal sealed record SetupActionResult(bool Success, string? Error = null);

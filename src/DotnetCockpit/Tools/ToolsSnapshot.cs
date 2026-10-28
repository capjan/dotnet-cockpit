namespace DotnetCockpit.Tools;

internal sealed record DotnetTool(string PackageId, string Version, string[] Commands, string? Manifest);
internal sealed record ToolList(DotnetTool[] Tools, string? Error = null);
internal sealed record ToolSearchResult(string PackageId, string Version, string Authors, string Downloads, bool Verified);
internal sealed record ToolSearch(string Query, ToolSearchResult[] Results, string? Error = null);
internal sealed record ToolActionResult(bool Success, string? Error = null);

internal sealed record ToolsSnapshot(
    string Status,
    string WorkingDirectory,
    ToolList Global,
    ToolList Local,
    string? Error = null,
    bool LocalManifestInitialized = false,
    string? LocalManifestPath = null,
    bool LocalManifestOwned = false)
{
    public static ToolsSnapshot Empty(string status, string workingDirectory, string? error = null) =>
        new(status, workingDirectory, new([]), new([]), error);
}

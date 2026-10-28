namespace DotnetCockpit.Tools;

internal sealed record DotnetTool(string PackageId, string Version, string[] Commands, string? Manifest);
internal sealed record ToolList(DotnetTool[] Tools, string? Error = null);

internal sealed record ToolsSnapshot(string Status, string WorkingDirectory, ToolList Global, ToolList Local, string? Error = null)
{
    public static ToolsSnapshot Empty(string status, string workingDirectory, string? error = null) =>
        new(status, workingDirectory, new([]), new([]), error);
}

namespace DotnetCockpit.Installation;

internal static class DotnetLocator
{
    public static string? Find(string? searchPath)
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var directory in (searchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory.Trim('"'), executable);
            // Only search absolute PATH entries; never execute a file from the current working directory.
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path;
        }
        return null;
    }
}

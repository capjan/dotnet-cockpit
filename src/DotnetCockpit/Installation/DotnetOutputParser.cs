using System.Text.RegularExpressions;

namespace DotnetCockpit.Installation;

internal static partial class DotnetOutputParser
{
    [GeneratedRegex(@"^(?<version>\S+)\s+\[(?<path>.+)\]$")]
    private static partial Regex SdkLine();

    [GeneratedRegex(@"^(?<name>\S+)\s+(?<version>\S+)\s+\[(?<path>.+)\]$")]
    private static partial Regex RuntimeLine();

    public static SdkInstallation[] ParseSdks(string output) =>
        Lines(output).Select(line => SdkLine().Match(line)).Where(match => match.Success)
            .Select(match => new SdkInstallation(match.Groups["version"].Value, match.Groups["path"].Value)).ToArray();

    public static RuntimeInstallation[] ParseRuntimes(string output) =>
        Lines(output).Select(line => RuntimeLine().Match(line)).Where(match => match.Success)
            .Select(match => new RuntimeInstallation(match.Groups["name"].Value,
                match.Groups["version"].Value, match.Groups["path"].Value)).ToArray();

    private static IEnumerable<string> Lines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

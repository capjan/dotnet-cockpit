using System.Text.RegularExpressions;

namespace DotnetCockpit.Installation;

internal static partial class DotnetOutputParser
{
    private enum UninstallSection
    {
        None,
        Sdk,
        Runtime,
        AspNetRuntime,
        WindowsDesktopRuntime
    }

    [GeneratedRegex(@"^(?<version>\S+)\s+\[(?<path>.+)\]$")]
    private static partial Regex SdkLine();

    [GeneratedRegex(@"^(?<name>\S+)\s+(?<version>\S+)\s+\[(?<path>.+)\]$")]
    private static partial Regex RuntimeLine();

    [GeneratedRegex(@"^\s*(?<version>\d+(?:\.\d+)*(?:[-+][^\s]+)?)\b")]
    private static partial Regex UninstallVersionLine();

    public static SdkInstallation[] ParseSdks(string output) =>
        Lines(output).Select(line => SdkLine().Match(line)).Where(match => match.Success)
            .Select(match => new SdkInstallation(match.Groups["version"].Value, match.Groups["path"].Value)).ToArray();

    public static RuntimeInstallation[] ParseRuntimes(string output) =>
        Lines(output).Select(line => RuntimeLine().Match(line)).Where(match => match.Success)
            .Select(match => new RuntimeInstallation(match.Groups["name"].Value,
                match.Groups["version"].Value, match.Groups["path"].Value)).ToArray();

    public static UninstallListInventory ParseUninstallList(string output)
    {
        var sdks = new List<string>();
        var runtimes = new List<UninstallableRuntime>();
        var section = UninstallSection.None;
        foreach (var line in Lines(output))
        {
            var heading = line.TrimEnd(':');
            if (heading.Contains("SDK", StringComparison.OrdinalIgnoreCase) && heading.EndsWith('s'))
            {
                section = UninstallSection.Sdk;
                continue;
            }

            if (heading.Contains("ASP.NET Core Runtime", StringComparison.OrdinalIgnoreCase) && heading.EndsWith('s'))
            {
                section = UninstallSection.AspNetRuntime;
                continue;
            }

            if (heading.Contains("Windows Desktop Runtime", StringComparison.OrdinalIgnoreCase) && heading.EndsWith('s'))
            {
                section = UninstallSection.WindowsDesktopRuntime;
                continue;
            }

            if (heading.Contains(".NET Core Runtimes", StringComparison.OrdinalIgnoreCase))
            {
                section = UninstallSection.Runtime;
                continue;
            }

            if (line.StartsWith(".", StringComparison.Ordinal) && line.EndsWith(':'))
            {
                section = UninstallSection.None;
                continue;
            }

            var match = UninstallVersionLine().Match(line);
            if (!match.Success) continue;
            var version = match.Groups["version"].Value;
            if (section == UninstallSection.Sdk && !sdks.Contains(version, StringComparer.OrdinalIgnoreCase)) sdks.Add(version);
            if (section is UninstallSection.Runtime or UninstallSection.AspNetRuntime or UninstallSection.WindowsDesktopRuntime)
            {
                var name = section switch
                {
                    UninstallSection.AspNetRuntime => "Microsoft.AspNetCore.App",
                    UninstallSection.WindowsDesktopRuntime => "Microsoft.WindowsDesktop.App",
                    _ => "Microsoft.NETCore.App"
                };
                if (!runtimes.Any(runtime => string.Equals(runtime.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(runtime.Version, version, StringComparison.OrdinalIgnoreCase)))
                    runtimes.Add(new(name, version));
            }
        }

        return new(sdks.ToArray(), runtimes.ToArray());
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed record UninstallListInventory(string[] SdkVersions, UninstallableRuntime[] Runtimes)
{
    public string[] RuntimeVersions => Runtimes.Select(runtime => runtime.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

internal sealed record UninstallableRuntime(string Name, string Version);

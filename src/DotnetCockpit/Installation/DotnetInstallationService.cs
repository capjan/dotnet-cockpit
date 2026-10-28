using System.ComponentModel;

namespace DotnetCockpit.Installation;

internal sealed class DotnetInstallationService
{
    public async Task<InstallationSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return InstallationSnapshot.Create("notFound", null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var infoTask = DotnetCommand.RunAsync(executable, ["--info"], timeout.Token);
        var uninstallTool = FindUninstallTool();
        if (uninstallTool is null)
        {
            var sdkTask = DotnetCommand.RunAsync(executable, ["--list-sdks"], timeout.Token);
            var runtimeTask = DotnetCommand.RunAsync(executable, ["--list-runtimes"], timeout.Token);
            await Task.WhenAll(sdkTask, runtimeTask, infoTask);
            return InstallationSnapshot.Create("ready", executable,
                DotnetOutputParser.ParseSdks(sdkTask.Result), DotnetOutputParser.ParseRuntimes(runtimeTask.Result));
        }

        var uninstallListTask = DotnetCommand.RunAsync(uninstallTool, ["list"], timeout.Token);
        try
        {
            await Task.WhenAll(infoTask, uninstallListTask);
        }
        catch (Exception exception) when ((exception is Win32Exception or InvalidOperationException) && infoTask.Status == TaskStatus.RanToCompletion)
        {
            return InstallationSnapshot.Create("ready", executable,
                DotnetOutputParser.ParseSdks(infoTask.Result), DotnetOutputParser.ParseRuntimes(infoTask.Result));
        }

        var uninstallable = DotnetOutputParser.ParseUninstallList(uninstallListTask.Result);
        var sdks = DotnetOutputParser.ParseSdks(infoTask.Result)
            .Where(sdk => uninstallable.SdkVersions.Contains(sdk.Version, StringComparer.OrdinalIgnoreCase))
            .Select(sdk => sdk with { CanRemove = true })
            .ToArray();
        var runtimes = DotnetOutputParser.ParseRuntimes(infoTask.Result)
            .Where(runtime => uninstallable.RuntimeVersions.Contains(runtime.Version, StringComparer.OrdinalIgnoreCase))
            .Select(runtime => runtime with
            {
                CanRemove = uninstallable.Runtimes.Any(candidate =>
                    string.Equals(candidate.Name, runtime.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Version, runtime.Version, StringComparison.OrdinalIgnoreCase))
            })
            .ToArray();
        return InstallationSnapshot.Create("ready", executable, sdks, runtimes);
    }

    private static string? FindUninstallTool()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet-core-uninstall.exe" : "dotnet-core-uninstall";
        var globalDirectory = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet-core-uninstall")
            : "/usr/local/bin";
        var candidates = new[] { Path.Combine(globalDirectory, executable) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory.Trim('"'), executable)));
        return candidates.FirstOrDefault(path => Path.IsPathFullyQualified(path) && File.Exists(path));
    }
}

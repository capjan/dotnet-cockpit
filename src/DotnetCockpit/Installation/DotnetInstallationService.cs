using System.ComponentModel;

namespace DotnetCockpit.Installation;

/// <summary>The SDK the dotnet host resolves for Cockpit's working directory, including a global.json pin.</summary>
internal sealed record ActiveSdk(string? Version, string? GlobalJsonPath);

internal sealed class DotnetInstallationService
{
    public async Task<ActiveSdk> ReadActiveSdkAsync(CancellationToken cancellationToken)
    {
        var globalJson = FindGlobalJson(Environment.CurrentDirectory);
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return new(null, globalJson);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var output = await DotnetCommand.RunAsync(executable, ["--version"], timeout.Token);
            return new(output.Trim(), globalJson);
        }
        catch (Exception exception) when (IsQueryFailure(exception) && !cancellationToken.IsCancellationRequested)
        {
            return new(null, globalJson);
        }
    }

    private static string? FindGlobalJson(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "global.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public async Task<InstallationSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        var uninstallTool = FindUninstallTool();
        if (executable is null && uninstallTool is null) return InstallationSnapshot.Create("notFound", null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        if (uninstallTool is not null)
        {
            var uninstallListTask = DotnetCommand.RunAsync(uninstallTool, ["list"], timeout.Token);
            var infoTask = executable is null ? null : DotnetCommand.RunAsync(executable, ["--info"], timeout.Token);

            try
            {
                var uninstallList = await uninstallListTask;
                if (infoTask is null) return FromUninstallList(executable, uninstallList);

                try
                {
                    return MergeUninstallListWithInfo(executable, uninstallList, await infoTask);
                }
                catch (Exception exception) when (IsQueryFailure(exception))
                {
                    return FromUninstallList(null, uninstallList);
                }
            }
            catch (Exception listException) when (IsQueryFailure(listException))
            {
                if (infoTask is null) throw;

                try
                {
                    return FromInfoFallback(executable, await infoTask);
                }
                catch (Exception infoException) when (IsQueryFailure(infoException))
                {
                    throw listException;
                }
            }
        }

        if (executable is null) return InstallationSnapshot.Create("notFound", null);

        if (uninstallTool is null)
        {
            var sdkTask = DotnetCommand.RunAsync(executable, ["--list-sdks"], timeout.Token);
            var runtimeTask = DotnetCommand.RunAsync(executable, ["--list-runtimes"], timeout.Token);
            await Task.WhenAll(sdkTask, runtimeTask);
            return InstallationSnapshot.Create("ready", executable,
                DotnetOutputParser.ParseSdks(sdkTask.Result), DotnetOutputParser.ParseRuntimes(runtimeTask.Result));
        }

        throw new InvalidOperationException("The .NET installation could not be read.");
    }

    private static InstallationSnapshot FromUninstallList(string? executable, string output)
    {
        var inventory = DotnetOutputParser.ParseUninstallList(output);
        var sdks = inventory.SdkVersions
            .Select(version => new SdkInstallation(version, "") { CanRemove = true })
            .ToArray();
        var runtimes = inventory.Runtimes
            .Select(runtime => new RuntimeInstallation(runtime.Name, runtime.Version, "") { CanRemove = true })
            .ToArray();
        return InstallationSnapshot.Create("ready", executable, sdks, runtimes);
    }

    private static InstallationSnapshot MergeUninstallListWithInfo(string? executable, string uninstallOutput, string infoOutput)
    {
        var inventory = DotnetOutputParser.ParseUninstallList(uninstallOutput);
        var infoSdks = DotnetOutputParser.ParseSdks(infoOutput);
        var infoRuntimes = DotnetOutputParser.ParseRuntimes(infoOutput);

        var sdks = infoSdks
            .Where(sdk => inventory.SdkVersions.Contains(sdk.Version, StringComparer.OrdinalIgnoreCase))
            .Select(sdk => sdk with { CanRemove = true })
            .ToList();
        foreach (var version in inventory.SdkVersions)
        {
            if (!sdks.Any(sdk => string.Equals(sdk.Version, version, StringComparison.OrdinalIgnoreCase)))
            {
                sdks.Add(new SdkInstallation(version, "") { CanRemove = true });
            }
        }

        var runtimes = infoRuntimes
            .Where(runtime => inventory.RuntimeVersions.Contains(runtime.Version, StringComparer.OrdinalIgnoreCase))
            .Select(runtime => runtime with
            {
                CanRemove = inventory.Runtimes.Any(candidate =>
                    string.Equals(candidate.Name, runtime.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Version, runtime.Version, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();
        foreach (var runtime in inventory.Runtimes)
        {
            if (!runtimes.Any(existing =>
                    string.Equals(existing.Name, runtime.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Version, runtime.Version, StringComparison.OrdinalIgnoreCase)))
            {
                runtimes.Add(new RuntimeInstallation(runtime.Name, runtime.Version, "") { CanRemove = true });
            }
        }

        return InstallationSnapshot.Create("ready", executable, sdks.ToArray(), runtimes.ToArray());
    }

    private static InstallationSnapshot FromInfoFallback(string? executable, string infoOutput) =>
        InstallationSnapshot.Create("ready", executable,
            DotnetOutputParser.ParseSdks(infoOutput), DotnetOutputParser.ParseRuntimes(infoOutput));

    private static bool IsQueryFailure(Exception exception) =>
        exception is Win32Exception or IOException or InvalidOperationException;

    private static string? FindUninstallTool()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet-core-uninstall.exe" : "dotnet-core-uninstall";
        var candidates = new[] { Path.Combine(Setup.DotnetUninstallToolService.GetGlobalDirectory(), executable) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory.Trim('"'), executable)));
        return candidates.FirstOrDefault(path => Path.IsPathFullyQualified(path) && File.Exists(path));
    }
}

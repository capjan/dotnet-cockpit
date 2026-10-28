namespace DotnetCockpit.Installation;

internal sealed class DotnetInstallationService
{
    public async Task<InstallationSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return InstallationSnapshot.Create("notFound", null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var sdkTask = DotnetCommand.RunAsync(executable, ["--list-sdks"], timeout.Token);
        var runtimeTask = DotnetCommand.RunAsync(executable, ["--list-runtimes"], timeout.Token);
        await Task.WhenAll(sdkTask, runtimeTask);
        return InstallationSnapshot.Create("ready", executable,
            DotnetOutputParser.ParseSdks(sdkTask.Result), DotnetOutputParser.ParseRuntimes(runtimeTask.Result));
    }
}

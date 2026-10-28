using System.Diagnostics;

namespace DotnetCockpit.Installation;

internal sealed class DotnetInstallationService
{
    public async Task<InstallationSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return InstallationSnapshot.Create("notFound", null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var sdkTask = RunAsync(executable, "--list-sdks", timeout.Token);
        var runtimeTask = RunAsync(executable, "--list-runtimes", timeout.Token);
        await Task.WhenAll(sdkTask, runtimeTask);
        return InstallationSnapshot.Create("ready", executable,
            DotnetOutputParser.ParseSdks(sdkTask.Result), DotnetOutputParser.ParseRuntimes(runtimeTask.Result));
    }

    private static async Task<string> RunAsync(string executable, string argument, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"dotnet {argument} exited with code {process.ExitCode}.");
            return stdout.Result;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}

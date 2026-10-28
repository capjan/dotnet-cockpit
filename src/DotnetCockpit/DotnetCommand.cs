using System.Diagnostics;

namespace DotnetCockpit;

internal static class DotnetCommand
{
    public static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken cancellationToken, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
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
                throw new InvalidOperationException($"dotnet {string.Join(' ', arguments)} exited with code {process.ExitCode}.");
            return stdout.Result;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}

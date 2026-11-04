using System.Diagnostics;
using DotnetCockpit.Installation;
using DotnetCockpit.Setup;
using Xunit;

namespace DotnetCockpit.Tests;

public sealed class BatchRemovalTests
{
    [Fact]
    public void BuildsOneCommandForAllRemovalsAndQuotesEveryValue()
    {
        var command = DotnetUninstallToolService.BuildBatchRemovalCommand("/opt/it's/tool", [("7.0.408", "--sdk"), ("7.0.18", "--aspnet-runtime")]);

        Assert.Contains("'/opt/it'\\''s/tool' 'remove' '7.0.408' '--sdk' '--yes' || status=1", command);
        Assert.Contains("'7.0.18' '--aspnet-runtime'", command);
        Assert.EndsWith("exit $status", command);
    }

    [Fact]
    public async Task AttemptsEveryItemAndReportsAFailureAtTheEnd()
    {
        if (OperatingSystem.IsWindows()) return;

        var directory = Directory.CreateTempSubdirectory("cockpit-batch-");
        try
        {
            var log = Path.Combine(directory.FullName, "log");
            var tool = Path.Combine(directory.FullName, "tool");
            await File.WriteAllTextAsync(tool, $"#!/bin/sh\necho \"$2\" >> '{log}'\n[ \"$2\" != bad ]\n");
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var command = DotnetUninstallToolService.BuildBatchRemovalCommand(tool, [("first", "--sdk"), ("bad", "--sdk"), ("last", "--runtime")]);
            using var process = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } })!;
            await process.WaitForExitAsync();

            Assert.Equal(1, process.ExitCode);
            Assert.Equal(["first", "bad", "last"], await File.ReadAllLinesAsync(log));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheShownCommandNamesEveryFrameworkOfARuntime()
    {
        var snapshot = InstallationSnapshot.Create("ready", "/opt/dotnet/dotnet", runtimes:
        [
            new RuntimeInstallation("Microsoft.NETCore.App", "7.0.18", "/shared/core") { CanRemove = true },
            new RuntimeInstallation("Microsoft.AspNetCore.App", "7.0.18", "/shared/asp") { CanRemove = true },
        ]);

        var row = Assert.Single(InstallationPage.BuildRows(snapshot, null, new(2026, 11, 4)));

        Assert.Contains("--runtime", row.EquivalentCommand);
        Assert.Contains("--aspnet-runtime", row.EquivalentCommand);
    }
}

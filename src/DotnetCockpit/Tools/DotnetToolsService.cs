using System.ComponentModel;
using DotnetCockpit.Installation;

namespace DotnetCockpit.Tools;

internal sealed class DotnetToolsService(ILogger<DotnetToolsService> logger)
{
    public async Task<ToolsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var directory = Environment.CurrentDirectory;
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return ToolsSnapshot.Empty("notFound", directory);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var sdks = await DotnetCommand.RunAsync(executable, ["--list-sdks"], timeout.Token, directory);
        if (DotnetOutputParser.ParseSdks(sdks).Length == 0) return ToolsSnapshot.Empty("sdkMissing", directory);

        var global = ReadListAsync(executable, "--global", directory, timeout.Token);
        var local = ReadListAsync(executable, "--local", directory, timeout.Token);
        await Task.WhenAll(global, local);
        return new("ready", directory, global.Result, local.Result);
    }

    private async Task<ToolList> ReadListAsync(string executable, string scope, string directory, CancellationToken cancellationToken)
    {
        try
        {
            var output = await DotnetCommand.RunAsync(executable, ["tool", "list", scope], cancellationToken, directory);
            return new(ToolOutputParser.Parse(output));
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(exception, "Could not query {Scope} tools in {Directory}.", scope, directory);
            return new([], scope == "--global"
                ? "Global tools could not be read. Check the .NET SDK available in this directory and try again."
                : "Local tools could not be read. Check the tool manifests and the .NET SDK available in this directory, then try again.");
        }
    }
}

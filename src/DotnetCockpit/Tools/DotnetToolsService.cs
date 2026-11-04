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

        var manifest = FindManifest(directory);
        var global = ReadListAsync(executable, "--global", directory, timeout.Token);
        var local = manifest is null
            ? Task.FromResult(new ToolList([]))
            : ReadListAsync(executable, "--local", directory, timeout.Token);
        await Task.WhenAll(global, local);
        return new("ready", directory, global.Result, local.Result, null, manifest is not null, manifest,
            manifest is not null && IsManifestInWorkingDirectory(manifest, directory));
    }

    public async Task<ToolSearch> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return new(query, [], "The dotnet command is not available on PATH.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var output = await DotnetCommand.RunAsync(executable, ["tool", "search", query, "--take", "20"], timeout.Token, Environment.CurrentDirectory);
            return new(query, ToolOutputParser.ParseSearch(output));
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(exception, "Could not search .NET tools for {Query}.", query);
            return new(query, [], "The tool search could not be completed. Check the package sources and try again.");
        }
    }

    public Task<ToolActionResult> InstallAsync(string packageId, string version, string scope, CancellationToken cancellationToken) =>
        RunToolActionAsync(packageId, version, scope, "install", cancellationToken);

    public Task<ToolActionResult> UpdateAsync(string packageId, string scope, CancellationToken cancellationToken) =>
        RunToolActionAsync(packageId, "", scope, "update", cancellationToken);

    public Task<ToolActionResult> UninstallAsync(string packageId, string scope, CancellationToken cancellationToken) =>
        RunToolActionAsync(packageId, "", scope, "uninstall", cancellationToken);

    public async Task<ToolActionResult> InitializeLocalAsync(CancellationToken cancellationToken)
    {
        if (FindManifest(Environment.CurrentDirectory) is not null) return new(true);
        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return new(false, "The dotnet command is not available on PATH.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await DotnetCommand.RunAsync(executable, ["new", "tool-manifest", "--output", ".config", "--no-update-check"], timeout.Token, Environment.CurrentDirectory);
            return new(true);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not initialize a local tool manifest in {Directory}.", Environment.CurrentDirectory);
            return new(false, "The local tool manifest could not be initialized.");
        }
    }

    public Task<ToolActionResult> RemoveLocalManifestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Environment.CurrentDirectory;
        var manifest = FindManifest(directory);
        if (manifest is null)
            return Task.FromResult(new ToolActionResult(false, "No local tool manifest exists in this workspace."));
        if (!IsManifestInWorkingDirectory(manifest, directory))
            return Task.FromResult(new ToolActionResult(false, "The local tool manifest belongs to a parent directory."));

        try
        {
            File.Delete(manifest);
            return Task.FromResult(new ToolActionResult(true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove the local tool manifest in {Directory}.", directory);
            return Task.FromResult(new ToolActionResult(false, "The local tool manifest could not be removed."));
        }
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

    private async Task<ToolActionResult> RunToolActionAsync(string packageId, string version, string scope, string action, CancellationToken cancellationToken)
    {
        if (!IsValidPackageId(packageId) || scope is not ("global" or "local"))
            return new(false, "The requested tool or installation scope is invalid.");
        if (scope == "local" && FindManifest(Environment.CurrentDirectory) is null)
            return new(false, "Initialize local tools before installing or removing a local tool.");

        var executable = DotnetLocator.Find(Environment.GetEnvironmentVariable("PATH"));
        if (executable is null) return new(false, "The dotnet command is not available on PATH.");
        var arguments = new List<string> { "tool", action, packageId, scope == "global" ? "--global" : "--local" };
        if (action == "install" && !string.IsNullOrWhiteSpace(version)) arguments.AddRange(["--version", version]);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await DotnetCommand.RunAsync(executable, [.. arguments], timeout.Token, Environment.CurrentDirectory);
            return new(true);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not {Action} .NET tool {PackageId} in {Scope} scope.", action, packageId, scope);
            return new(false, $"The tool could not be {action}ed.");
        }
    }

    private static bool IsValidPackageId(string value) =>
        value.Length is > 0 and <= 256 && char.IsLetterOrDigit(value[0]) &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');

    private static string? FindManifest(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var manifest = Path.Combine(current.FullName, ".config", "dotnet-tools.json");
            if (File.Exists(manifest)) return manifest;
        }
        return null;
    }

    private static bool IsManifestInWorkingDirectory(string manifest, string directory)
    {
        var expected = Path.GetFullPath(Path.Combine(directory, ".config", "dotnet-tools.json"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(manifest), expected, comparison);
    }
}

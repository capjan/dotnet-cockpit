using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using DotnetCockpit.Installation;

namespace DotnetCockpit.Setup;

internal sealed class DotnetUninstallToolService(ILogger<DotnetUninstallToolService> logger, HttpClient httpClient)
{
    private sealed record SetupReleaseCatalog(string LatestVersion, string LatestReleaseUrl, IReadOnlyList<SetupRelease> Releases);

    private const string ReleaseDownloadBase = "https://github.com/dotnet/cli-lab/releases/latest/download/";
    private const string ReleaseListUrl = "https://api.github.com/repos/dotnet/cli-lab/releases?per_page=100&page=1";

    public SetupSnapshot Read()
    {
        var operatingSystem = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : RuntimeInformation.OSDescription;
        var architecture = RuntimeInformation.OSArchitecture.ToString();
        var globalDirectory = GetGlobalDirectory();
        if (!IsSupported())
            return new("unsupported", operatingSystem, architecture, new(false, null, null), globalDirectory);

        var globalCandidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(globalDirectory, ExecutableName()), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ExecutableName()) }
            : new[] { Path.Combine(globalDirectory, ExecutableName()) };
        var candidates = globalCandidates.Concat(FindOnPath()).Distinct(PathComparer());
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var scope = globalCandidates.Any(global => IsSamePath(candidate, global)) ? "global" : "path";
            return new("ready", operatingSystem, architecture, new(true, candidate, scope), globalDirectory);
        }

        return new("ready", operatingSystem, architecture, new(false, null, null), globalDirectory);
    }

    public async Task<SetupSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var snapshot = Read();
        if (snapshot.Status != "ready")
            return snapshot;

        SetupReleaseCatalog? releaseCatalog = null;
        string? releaseListError = null;
        using (var releaseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            releaseTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                releaseCatalog = await ReadReleaseCatalogAsync(releaseTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Timed out while reading the dotnet-core-uninstall releases.");
                releaseListError = "The available releases could not be loaded right now.";
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException or UriFormatException)
            {
                logger.LogWarning(exception, "Could not read the dotnet-core-uninstall releases.");
                releaseListError = "The available releases could not be loaded right now.";
            }
        }

        string? installedVersion = null;
        if (snapshot.UninstallTool.Installed && snapshot.UninstallTool.Path is not null)
        {
            using var versionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            versionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                installedVersion = await ReadInstalledVersionAsync(snapshot.UninstallTool.Path, versionTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Timed out while reading the installed dotnet-core-uninstall version.");
            }
        }

        return snapshot with
        {
            UninstallTool = snapshot.UninstallTool with { Version = installedVersion },
            LatestVersion = releaseCatalog?.LatestVersion,
            LatestReleaseUrl = releaseCatalog?.LatestReleaseUrl,
            ReleaseListError = releaseListError,
            AvailableReleases = releaseCatalog?.Releases
        };
    }

    public async Task<SetupActionResult> InstallAsync(string? version, CancellationToken cancellationToken)
    {
        if (!IsSupported()) return new(false, "The .NET Uninstall Tool supports Windows and macOS only.");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"dotnet-cockpit-setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var (downloadBase, assetName) = await ResolveInstallAssetAsync(version, timeout.Token);
            if (OperatingSystem.IsMacOS())
                await InstallMacOsAsync(downloadBase, assetName, temporaryDirectory, timeout.Token);
            else
                await InstallWindowsAsync(downloadBase, assetName, temporaryDirectory, timeout.Token);

            if (!Read().UninstallTool.Installed)
            {
                logger.LogWarning("The dotnet-core-uninstall installer completed, but the executable is not in {Directory}.", GetGlobalDirectory());
                return new(false, "The installer completed, but the dotnet-core-uninstall executable was not found at the expected path.");
            }
            return new(true);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not install dotnet-core-uninstall globally.");
            return new(false, "The .NET Uninstall Tool could not be installed. Check the network connection and permissions, then try again.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out while installing dotnet-core-uninstall globally.");
            return new(false, "The .NET Uninstall Tool installation timed out. Check the network connection and try again.");
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public Task<SetupActionResult> RemoveInstallationAsync(string? version, string? kind, CancellationToken cancellationToken) =>
        RemoveInstallationsAsync([(version, kind)], cancellationToken);

    /// <summary>Removes everything in one elevated run, so the person approves once however many items are selected.</summary>
    public async Task<SetupActionResult> RemoveInstallationsAsync(IReadOnlyList<(string? Version, string? Kind)> items, CancellationToken cancellationToken)
    {
        if (!IsSupported()) return new(false, "The .NET Uninstall Tool supports Windows and macOS only.");

        var installation = Read().UninstallTool;
        if (!installation.Installed || installation.Path is null)
            return new(false, "The .NET Uninstall Tool is not installed.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var list = DotnetOutputParser.ParseUninstallList(await DotnetCommand.RunAsync(installation.Path, ["list"], timeout.Token));

            var removals = new List<(string Version, string Filter)>();
            var allResolved = true;
            foreach (var (version, kind) in items)
            {
                var targetVersion = version?.Trim() ?? "";
                var targetKind = kind?.Trim().ToLowerInvariant();
                var filters = targetVersion.Length == 0
                    ? []
                    : targetKind == "sdk" && list.SdkVersions.Contains(targetVersion, StringComparer.OrdinalIgnoreCase)
                        ? ["--sdk"]
                        : targetKind == "runtime"
                            ? list.Runtimes
                                .Where(runtime => string.Equals(runtime.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
                                .Select(runtime => RuntimeFilter(runtime.Name))
                                .Distinct(StringComparer.Ordinal)
                                .ToArray()
                            : Array.Empty<string>();
                if (filters.Length == 0) allResolved = false;
                removals.AddRange(filters.Select(filter => (targetVersion, filter)));
            }

            if (removals.Count == 0) return new(false, "The selected .NET installation is no longer available for removal.");
            await RemoveWithElevationAsync(installation.Path, removals, timeout.Token);
            return allResolved ? new(true) : new(false, "Some selected .NET installations were no longer available for removal.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            logger.LogWarning(exception, "Could not remove {Count} .NET installations.", items.Count);
            return new(false, "The selected .NET installation could not be removed. Check the permissions and try again.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out while removing {Count} .NET installations.", items.Count);
            return new(false, "The .NET installation removal timed out. Check the permissions and try again.");
        }
    }

    public async Task<SetupActionResult> UninstallAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported()) return new(false, "The .NET Uninstall Tool supports Windows and macOS only.");

        var installation = Read().UninstallTool;
        if (!installation.Installed) return new(true);
        if (installation.Scope != "global")
            return new(false, "The detected executable is outside a managed installation path and must be removed manually.");

        try
        {
            await RemoveGlobalInstallationAsync(installation.Path!, cancellationToken);

            if (File.Exists(installation.Path))
                return new(false, "The uninstall completed, but the dotnet-core-uninstall executable is still present.");
            return new(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not uninstall dotnet-core-uninstall from {Scope} scope.", installation.Scope);
            return new(false, "The .NET Uninstall Tool could not be uninstalled. Check the permissions and try again.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out while uninstalling dotnet-core-uninstall from {Scope} scope.", installation.Scope);
            return new(false, "The .NET Uninstall Tool uninstallation timed out. Check the permissions and try again.");
        }
    }

    private async Task InstallMacOsAsync(string downloadBase, string assetName, string temporaryDirectory, CancellationToken cancellationToken)
    {
        var archive = Path.Combine(temporaryDirectory, "dotnet-core-uninstall.tar.gz");
        await DownloadAsync($"{downloadBase}{assetName}", archive, cancellationToken);
        var staging = Path.Combine(temporaryDirectory, "staged");
        Directory.CreateDirectory(staging);
        await ExtractTarGzAsync(archive, staging, cancellationToken);
        var target = GetGlobalDirectory();
        var command = $"/bin/mkdir -p {ShellQuote(target)} && /bin/cp -f {ShellQuote(staging)}/* {ShellQuote(target)}/ && /bin/chmod 755 {ShellQuote(Path.Combine(target, ExecutableName()))}";
        var script = $"do shell script {AppleScriptLiteral(command)} with administrator privileges";
        await RunProcessAsync("/usr/bin/osascript", ["-e", script], cancellationToken);
    }

    private async Task InstallWindowsAsync(string downloadBase, string assetName, string temporaryDirectory, CancellationToken cancellationToken)
    {
        var installer = Path.Combine(temporaryDirectory, "dotnet-core-uninstall.msi");
        await DownloadAsync($"{downloadBase}{assetName}", installer, cancellationToken);
        await RunElevatedAsync("msiexec.exe", MsiInstallArguments(installer), cancellationToken);
    }

    private async Task RemoveGlobalInstallationAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
        {
            var macCommand = $"/bin/rm -f {ShellQuote(executablePath)}";
            var script = $"do shell script {AppleScriptLiteral(macCommand)} with administrator privileges";
            await RunProcessAsync("/usr/bin/osascript", ["-e", script], cancellationToken);
            return;
        }

        var globalDirectory = GetGlobalDirectory();
        var detectedDirectory = Path.GetDirectoryName(executablePath);
        var arguments = detectedDirectory is not null && IsSamePath(detectedDirectory, globalDirectory)
            ? WindowsRemovalArguments(globalDirectory, $"rmdir /s /q {QuoteArgument(globalDirectory)}")
            : WindowsRemovalArguments(executablePath, $"del /f /q {QuoteArgument(executablePath)}");
        await RunElevatedAsync("cmd.exe", arguments, cancellationToken);
    }

    /// <summary>Windows cannot delete a program while it runs, and Cockpit itself reads the tool's list in the background. Retrying inside the one elevated run avoids a second approval.</summary>
    internal static string WindowsRemovalArguments(string path, string removeCommand)
    {
        var target = QuoteArgument(path);
        return $"/d /c for /l %i in (1,1,15) do @if exist {target} ({removeCommand} & if exist {target} ping -n 2 127.0.0.1 > nul)";
    }

    private async Task RemoveWithElevationAsync(string executablePath, IReadOnlyList<(string Version, string Filter)> removals, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
        {
            var script = $"do shell script {AppleScriptLiteral(BuildBatchRemovalCommand(executablePath, removals))} with administrator privileges";
            await RunProcessAsync("/usr/bin/osascript", ["-e", script], cancellationToken);
            return;
        }

        foreach (var (version, filter) in removals)
            await RunElevatedAsync(executablePath, $"remove {QuoteArgument(version)} {QuoteArgument(filter)} --yes", cancellationToken);
    }

    /// <summary>msiexec does not accept quoted switches and answers them with a help dialog that waits forever, so only the path is quoted.</summary>
    internal static string MsiInstallArguments(string installer) => $"/i {QuoteArgument(installer)} /qn";

    /// <summary>One shell line for all removals. Every item is attempted, and the exit code reports whether any failed.</summary>
    internal static string BuildBatchRemovalCommand(string executablePath, IEnumerable<(string Version, string Filter)> removals)
    {
        var commands = removals.Select(removal =>
            $"{ShellQuote(executablePath)} 'remove' {ShellQuote(removal.Version)} {ShellQuote(removal.Filter)} '--yes' || status=1");
        return $"cd / ; status=0 ; {string.Join(" ; ", commands)} ; exit $status";
    }

    internal static string RuntimeFilter(string name) => name switch
    {
        "Microsoft.AspNetCore.App" => "--aspnet-runtime",
        "Microsoft.WindowsDesktop.App" => "--windows-desktop-runtime",
        _ => "--runtime"
    };

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static async Task<string?> ReadInstalledVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--version");
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The installed tool could not be started.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? NormalizeVersion(outputTask.Result) : null;
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<SetupReleaseCatalog> ReadReleaseCatalogAsync(CancellationToken cancellationToken)
    {
        var releases = new List<SetupRelease>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestUrl = ReleaseListUrl;
        while (requestUrl is not null)
        {
            if (!seenUrls.Add(requestUrl)) throw new InvalidDataException("The release pagination repeated a page.");
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The releases response was not an array.");

            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (ParseRelease(release) is { } parsed) releases.Add(parsed);
            }

            requestUrl = GetNextLink(response);
        }

        var sorted = releases
            .GroupBy(release => release.Version, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(release => release.Version, VersionComparer.Instance)
            .ToArray();
        if (sorted.Length == 0) throw new InvalidDataException("No compatible dotnet-core-uninstall releases were found.");
        return new(sorted[0].Version, sorted[0].Url, sorted);
    }

    private SetupRelease? ParseRelease(JsonElement release)
    {
        if (GetBoolean(release, "draft") || GetBoolean(release, "prerelease")) return null;
        var version = NormalizeVersion(GetString(release, "tag_name"));
        var url = GetString(release, "html_url");
        if (version is null || string.IsNullOrWhiteSpace(url)) return null;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var assetNames = assets.EnumerateArray().Select(asset => GetString(asset, "name")).OfType<string>().ToArray();
        var asset = FindAssetName(version, assetNames);
        return asset is null ? null : new(version, url, GetString(release, "published_at"), asset);
    }

    private string? FindAssetName(string version, IReadOnlyList<string> assetNames)
    {
        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return assetNames.FirstOrDefault(asset =>
                asset.StartsWith("dotnet-core-uninstall", StringComparison.OrdinalIgnoreCase) &&
                asset.EndsWith("-osx-arm64.tar.gz", StringComparison.OrdinalIgnoreCase));
        }

        var expected = OperatingSystem.IsWindows()
            ? new[] { "dotnet-core-uninstall.msi", $"dotnet-core-uninstall-{version}.msi" }
            : new[] { $"dotnet-core-uninstall-osx-{MacArchitecture()}.tar.gz", "dotnet-core-uninstall.tar.gz" };
        return expected.FirstOrDefault(candidate => assetNames.Contains(candidate, StringComparer.Ordinal));
    }

    private static string? GetNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        foreach (var link in values.SelectMany(value => value.Split(',')))
        {
            var parts = link.Split(';', StringSplitOptions.TrimEntries);
            if (parts.Length > 1 && parts.Skip(1).Any(part => string.Equals(part, "rel=\"next\"", StringComparison.OrdinalIgnoreCase)))
                return parts[0].Trim().Trim('<', '>');
        }
        return null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? NormalizeVersion(string? value)
    {
        var token = value?.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null) return null;
        token = token.TrimStart('v');
        var suffix = token.IndexOfAny(['+', '-']);
        if (suffix >= 0) token = token[..suffix];
        return token.Length > 0 && token.Split('.').All(part => int.TryParse(part, out _)) ? token : null;
    }

    private async Task<(string DownloadBase, string AssetName)> ResolveInstallAssetAsync(string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version) || string.Equals(version.Trim(), "latest", StringComparison.OrdinalIgnoreCase))
            return (ReleaseDownloadBase, CurrentAssetName());
        var normalized = NormalizeVersion(version);
        if (normalized is null) throw new InvalidOperationException("The requested release version is invalid.");
        var catalog = await ReadReleaseCatalogAsync(cancellationToken);
        var release = catalog.Releases.FirstOrDefault(candidate => string.Equals(candidate.Version, normalized, StringComparison.OrdinalIgnoreCase));
        return release is null
            ? throw new InvalidOperationException("The requested release version is not available for this platform.")
            : ($"https://github.com/dotnet/cli-lab/releases/download/{normalized}/", release.AssetName);
    }

    private string CurrentAssetName() => OperatingSystem.IsWindows()
        ? "dotnet-core-uninstall.msi"
        : $"dotnet-core-uninstall-osx-{MacArchitecture()}.tar.gz";

    private sealed class VersionComparer : IComparer<string>
    {
        public static VersionComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var leftParts = left.Split('.').Select(int.Parse).ToArray();
            var rightParts = right.Split('.').Select(int.Parse).ToArray();
            for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
            {
                var comparison = (index < leftParts.Length ? leftParts[index] : 0).CompareTo(index < rightParts.Length ? rightParts[index] : 0);
                if (comparison != 0) return comparison;
            }
            return 0;
        }
    }

    private static async Task ExtractTarGzAsync(string archive, string destination, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        await using var archiveStream = File.OpenRead(archive);
        await using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
        using var reader = new TarReader(gzipStream);
        while (await reader.GetNextEntryAsync(copyData: true, cancellationToken) is { } entry)
        {
            var relative = entry.Name.Replace('\\', '/');
            while (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
            if (relative.Length == 0) continue;
            if (relative.StartsWith("/", StringComparison.Ordinal) || relative.Split('/').Contains("..", StringComparer.Ordinal))
                throw new InvalidDataException("The downloaded archive contains an unsafe path.");
            var path = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, PathComparison())) throw new InvalidDataException("The downloaded archive contains an unsafe path.");
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(path);
                continue;
            }
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await entry.ExtractToFileAsync(path, overwrite: true, cancellationToken);
        }
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        await RunToCompletionAsync(startInfo, cancellationToken);
    }

    /// <summary>Windows hands the elevated process one command line, written exactly as that program expects it.</summary>
    private static Task RunElevatedAsync(string fileName, string arguments, CancellationToken cancellationToken) =>
        RunToCompletionAsync(new ProcessStartInfo { FileName = fileName, Arguments = arguments, UseShellExecute = true, Verb = "runas" }, cancellationToken);

    private static async Task RunToCompletionAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The installer process could not be started.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"The installer exited with code {process.ExitCode}.");
    }

    private IEnumerable<string> FindOnPath()
    {
        var executable = ExecutableName();
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory.Trim('"'), executable);
            if (Path.IsPathFullyQualified(path)) yield return path;
        }
    }

    /// <summary>The official Windows package is a 32-bit installer, so it installs under Program Files (x86) on every machine.</summary>
    internal static string GetGlobalDirectory() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet-core-uninstall")
        : "/usr/local/bin";

    private static string ExecutableName() => OperatingSystem.IsWindows() ? "dotnet-core-uninstall.exe" : "dotnet-core-uninstall";

    private static bool IsSupported() => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private static string MacArchitecture() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X64 => "x64",
        _ => throw new InvalidOperationException("The current macOS architecture is not supported by the .NET Uninstall Tool release.")
    };

    private static bool IsSamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison());

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

    private static string AppleScriptLiteral(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}

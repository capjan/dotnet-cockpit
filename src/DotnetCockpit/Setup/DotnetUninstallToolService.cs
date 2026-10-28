using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DotnetCockpit.Setup;

internal sealed class DotnetUninstallToolService(ILogger<DotnetUninstallToolService> logger, HttpClient httpClient)
{
    private const string ReleaseDownloadBase = "https://github.com/dotnet/cli-lab/releases/latest/download/";
    private const string LatestReleaseUrl = "https://api.github.com/repos/dotnet/cli-lab/releases/latest";

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
        if (snapshot.Status != "ready" || !snapshot.UninstallTool.Installed || snapshot.UninstallTool.Path is null)
            return snapshot;

        string? installedVersion;
        using (var versionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            versionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                installedVersion = await ReadInstalledVersionAsync(snapshot.UninstallTool.Path, versionTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Timed out while reading the installed dotnet-core-uninstall version.");
                installedVersion = null;
            }
        }
        string? latestVersion = null;
        string? updateCheckError = null;
        using var updateTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        updateTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            latestVersion = await ReadLatestVersionAsync(updateTimeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not check the latest dotnet-core-uninstall release.");
            updateCheckError = "The latest release could not be checked right now.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out while checking the latest dotnet-core-uninstall release.");
            updateCheckError = "The latest release could not be checked right now.";
        }

        return snapshot with
        {
            UninstallTool = snapshot.UninstallTool with { Version = installedVersion },
            LatestVersion = latestVersion,
            UpdateCheckError = updateCheckError
        };
    }

    public async Task<SetupActionResult> InstallAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported()) return new(false, "The .NET Uninstall Tool supports Windows and macOS only.");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"dotnet-cockpit-setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            if (OperatingSystem.IsMacOS())
                await InstallMacOsAsync(temporaryDirectory, timeout.Token);
            else
                await InstallWindowsAsync(temporaryDirectory, timeout.Token);

            if (!Read().UninstallTool.Installed)
                return new(false, "The installer completed, but the dotnet-core-uninstall executable was not found at the expected path.");
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

    private async Task InstallMacOsAsync(string temporaryDirectory, CancellationToken cancellationToken)
    {
        var archive = Path.Combine(temporaryDirectory, "dotnet-core-uninstall.tar.gz");
        await DownloadAsync($"{ReleaseDownloadBase}dotnet-core-uninstall-osx-{MacArchitecture()}.tar.gz", archive, cancellationToken);
        var staging = Path.Combine(temporaryDirectory, "staged");
        Directory.CreateDirectory(staging);
        await ExtractTarGzAsync(archive, staging, cancellationToken);
        var target = GetGlobalDirectory();
        var command = $"/bin/mkdir -p {ShellQuote(target)} && /bin/cp -f {ShellQuote(staging)}/* {ShellQuote(target)}/ && /bin/chmod 755 {ShellQuote(Path.Combine(target, ExecutableName()))}";
        var script = $"do shell script {AppleScriptLiteral(command)} with administrator privileges";
        await RunProcessAsync("/usr/bin/osascript", ["-e", script], cancellationToken, useShellExecute: false);
    }

    private async Task InstallWindowsAsync(string temporaryDirectory, CancellationToken cancellationToken)
    {
        var installer = Path.Combine(temporaryDirectory, "dotnet-core-uninstall.msi");
        await DownloadAsync($"{ReleaseDownloadBase}dotnet-core-uninstall.msi", installer, cancellationToken);
        var target = Path.Combine(GetGlobalDirectory(), "");
        await RunProcessAsync("msiexec.exe", ["/i", installer, "/qn", $"DOTNETCOREUNINSTALL={target}"], cancellationToken, useShellExecute: true, elevate: true);
    }

    private async Task RemoveGlobalInstallationAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
        {
            var macCommand = $"/bin/rm -f {ShellQuote(executablePath)}";
            var script = $"do shell script {AppleScriptLiteral(macCommand)} with administrator privileges";
            await RunProcessAsync("/usr/bin/osascript", ["-e", script], cancellationToken, useShellExecute: false);
            return;
        }

        var globalDirectory = GetGlobalDirectory();
        var detectedDirectory = Path.GetDirectoryName(executablePath);
        var windowsCommand = detectedDirectory is not null && IsSamePath(detectedDirectory, globalDirectory)
            ? $"if exist {QuoteArgument(globalDirectory)} rmdir /s /q {QuoteArgument(globalDirectory)}"
            : $"del /f /q {QuoteArgument(executablePath)}";
        await RunProcessAsync("cmd.exe", ["/d", "/c", windowsCommand], cancellationToken, useShellExecute: true, elevate: true);
    }

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

    private async Task<string> ReadLatestVersionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("tag_name", out var tag))
            throw new InvalidDataException("The latest release did not contain a version tag.");
        return NormalizeVersion(tag.GetString()) ?? throw new InvalidDataException("The latest release contained an invalid version tag.");
    }

    private static string? NormalizeVersion(string? value)
    {
        var token = value?.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null) return null;
        token = token.TrimStart('v');
        var suffix = token.IndexOfAny(['+', '-']);
        if (suffix >= 0) token = token[..suffix];
        return token.Length > 0 && token.Split('.').All(part => int.TryParse(part, out _)) ? token : null;
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

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool useShellExecute, bool elevate = false)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = useShellExecute };
        if (elevate) startInfo.Verb = "runas";
        if (useShellExecute)
            startInfo.Arguments = string.Join(' ', arguments.Select(QuoteArgument));
        else
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
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

    private static string GetGlobalDirectory() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet-core-uninstall")
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

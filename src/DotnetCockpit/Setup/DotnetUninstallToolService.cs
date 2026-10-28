using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace DotnetCockpit.Setup;

internal sealed class DotnetUninstallToolService(ILogger<DotnetUninstallToolService> logger, HttpClient httpClient)
{
    private const string ReleaseDownloadBase = "https://github.com/dotnet/cli-lab/releases/latest/download/";

    public SetupSnapshot Read()
    {
        var operatingSystem = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : RuntimeInformation.OSDescription;
        var architecture = RuntimeInformation.OSArchitecture.ToString();
        var userDirectory = GetUserDirectory();
        var globalDirectory = GetGlobalDirectory();
        if (!IsSupported())
            return new("unsupported", operatingSystem, architecture, new(false, null, null), userDirectory, globalDirectory);

        var userExecutable = Path.Combine(userDirectory, ExecutableName());
        var globalCandidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(globalDirectory, ExecutableName()), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ExecutableName()) }
            : new[] { Path.Combine(globalDirectory, ExecutableName()) };
        var candidates = new[] { userExecutable }.Concat(globalCandidates).Concat(FindOnPath()).Distinct(PathComparer());
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var scope = IsSamePath(candidate, userExecutable)
                ? "user"
                : globalCandidates.Any(global => IsSamePath(candidate, global)) ? "global" : "path";
            return new("ready", operatingSystem, architecture, new(true, candidate, scope), userDirectory, globalDirectory);
        }

        return new("ready", operatingSystem, architecture, new(false, null, null), userDirectory, globalDirectory);
    }

    public async Task<SetupActionResult> InstallAsync(string scope, CancellationToken cancellationToken)
    {
        if (scope is not ("user" or "global")) return new(false, "The requested installation scope is invalid.");
        if (!IsSupported()) return new(false, "The .NET Uninstall Tool supports Windows and macOS only.");
        if (Read().UninstallTool.Installed) return new(true);

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"dotnet-cockpit-setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            if (OperatingSystem.IsMacOS())
                await InstallMacOsAsync(scope, temporaryDirectory, timeout.Token);
            else
                await InstallWindowsAsync(scope, temporaryDirectory, timeout.Token);

            if (!Read().UninstallTool.Installed)
                return new(false, "The installer completed, but the dotnet-core-uninstall executable was not found at the expected path.");
            return new(true);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not install dotnet-core-uninstall for {Scope}.", scope);
            return new(false, "The .NET Uninstall Tool could not be installed. Check the network connection and permissions, then try again.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out while installing dotnet-core-uninstall for {Scope}.", scope);
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
        if (installation.Scope is not ("user" or "global"))
            return new(false, "The detected executable is outside a managed installation path and must be removed manually.");

        try
        {
            if (installation.Scope == "user")
                RemoveUserInstallation();
            else
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

    private async Task InstallMacOsAsync(string scope, string temporaryDirectory, CancellationToken cancellationToken)
    {
        var archive = Path.Combine(temporaryDirectory, "dotnet-core-uninstall.tar.gz");
        await DownloadAsync($"{ReleaseDownloadBase}dotnet-core-uninstall-osx-{MacArchitecture()}.tar.gz", archive, cancellationToken);
        var staging = Path.Combine(temporaryDirectory, "staged");
        Directory.CreateDirectory(staging);
        await ExtractTarGzAsync(archive, staging, cancellationToken);
        var target = scope == "user" ? GetUserDirectory() : GetGlobalDirectory();
        if (scope == "user")
        {
            CopyDirectory(staging, target);
            MakeExecutable(Path.Combine(target, ExecutableName()));
            return;
        }

        var command = $"/bin/mkdir -p {ShellQuote(target)} && /bin/cp -f {ShellQuote(staging)}/* {ShellQuote(target)}/ && /bin/chmod 755 {ShellQuote(Path.Combine(target, ExecutableName()))}";
        var script = $"do shell script {AppleScriptLiteral(command)} with administrator privileges";
        await RunProcessAsync("/usr/bin/osascript", ["-e", script], cancellationToken, useShellExecute: false);
    }

    private async Task InstallWindowsAsync(string scope, string temporaryDirectory, CancellationToken cancellationToken)
    {
        var installer = Path.Combine(temporaryDirectory, "dotnet-core-uninstall.msi");
        await DownloadAsync($"{ReleaseDownloadBase}dotnet-core-uninstall.msi", installer, cancellationToken);
        var target = Path.Combine(scope == "user" ? GetUserDirectory() : GetGlobalDirectory(), "");
        Directory.CreateDirectory(target);
        if (scope == "user")
        {
            await RunProcessAsync("msiexec.exe", ["/a", installer, "/qn", $"TARGETDIR={target}"], cancellationToken, useShellExecute: false);
            return;
        }

        await RunProcessAsync("msiexec.exe", ["/i", installer, "/qn", $"DOTNETCOREUNINSTALL={target}"], cancellationToken, useShellExecute: true, elevate: true);
    }

    private static void RemoveUserInstallation()
    {
        var directory = GetUserDirectory();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static string GetUserDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dotnet-core-uninstall");

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

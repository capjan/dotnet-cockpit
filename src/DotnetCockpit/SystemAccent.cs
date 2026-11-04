using System.ComponentModel;
using System.Diagnostics;

namespace DotnetCockpit;

/// <summary>
/// Reads the accent color from the macOS settings. The CSS system color AccentColor depends on the browser,
/// while Cockpit runs on the same machine and can simply ask the system.
/// </summary>
internal static class SystemAccent
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly Lock Gate = new();
    private static string? cached;
    private static DateTimeOffset cachedAt = DateTimeOffset.MinValue;

    /// <summary>The last known value. Pages never wait for the system; an older value refreshes in the background.</summary>
    public static string? Current
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return null;
            lock (Gate)
            {
                if (cached is null) return ReadFresh();
                if (DateTimeOffset.UtcNow - cachedAt > CacheDuration)
                {
                    cachedAt = DateTimeOffset.UtcNow;
                    _ = Task.Run(ReadFresh);
                }
                return cached;
            }
        }
    }

    public static string? ReadFresh()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var value = Parse(ReadSetting());
        lock (Gate)
        {
            cached = value;
            cachedAt = DateTimeOffset.UtcNow;
        }
        return value;
    }

    /// <summary>macOS stores a number. The key is missing for the multicolor choice, which appears blue.</summary>
    public static string Parse(string? value) => value?.Trim() switch
    {
        "-1" => "graphite",
        "0" => "red",
        "1" => "orange",
        "2" => "yellow",
        "3" => "green",
        "5" => "purple",
        "6" => "pink",
        _ => "blue"
    };

    private static string? ReadSetting()
    {
        try
        {
            var startInfo = new ProcessStartInfo("/usr/bin/defaults")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "read", "-g", "AppleAccentColor" }) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(1000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

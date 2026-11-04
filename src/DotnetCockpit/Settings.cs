using System.Text.Json;

namespace DotnetCockpit;

internal sealed record AppSettings(string Theme = "auto", bool PreflightOnLaunch = false, bool FirstLaunchCompleted = false);

/// <summary>
/// Stores preferences in the user profile. The browser's storage cannot hold them because Cockpit
/// chooses a free port on every start, which gives each launch a different origin.
/// </summary>
internal sealed class SettingsStore
{
    private readonly string path;
    private readonly Lock gate = new();
    private AppSettings current;

    public SettingsStore() : this(DefaultDirectory()) { }

    public SettingsStore(string directory)
    {
        path = Path.Combine(directory, "settings.json");
        current = Load(path);
    }

    public AppSettings Current { get { lock (gate) return current; } }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        lock (gate)
        {
            current = change(current);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(current, AppJsonContext.Default.AppSettings));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A read-only profile must not stop Cockpit; the choice still applies until it exits.
            }
        }
    }

    public static string NormalizeTheme(string? theme) => theme is "light" or "dark" ? theme : "auto";

    private static string DefaultDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_COCKPIT_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "dotnet-cockpit")
            : configured;
    }

    private static AppSettings Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return new();
            var settings = JsonSerializer.Deserialize(File.ReadAllText(file), AppJsonContext.Default.AppSettings) ?? new();
            return settings with { Theme = NormalizeTheme(settings.Theme) };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }
}

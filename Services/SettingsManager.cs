using System.IO;
using System.Text.Json;
using WinXStart.Models;

namespace WinXStart.Services;

public class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WinXStart");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    // Old location for one-time migration
    private static readonly string OldSettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinXStart", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Load settings from %USERPROFILE%\WinXStart\settings.json.
    /// Migrates from old location if needed. Creates default file if missing.
    /// </summary>
    public void Load()
    {
        try
        {
            MigrateOldSettings();

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                Save(); // re-save to add any new fields with defaults
            }
            else
            {
                Settings = new AppSettings();
                Save();
            }
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    /// <summary>Persist current settings to disk.</summary>
    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>Full path to settings file (for user reference).</summary>
    public static string FilePath => SettingsPath;

    private static void MigrateOldSettings()
    {
        if (!File.Exists(SettingsPath) && File.Exists(OldSettingsPath))
        {
            Directory.CreateDirectory(SettingsDir);
            File.Move(OldSettingsPath, SettingsPath);
        }
    }
}

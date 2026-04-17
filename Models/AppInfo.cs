using System.IO;

namespace WinXStart.Models;

public class AppInfo
{
    public string Name { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string IconPath { get; set; } = "";
    public string Category { get; set; } = "";
    public string ShortcutPath { get; set; } = "";

    /// <summary>Application User Model ID for Store/UWP apps (launched via shell:AppsFolder).</summary>
    public string AppUserModelId { get; set; } = "";

    /// <summary>Direct path to a PNG/JPG icon file (used by Store apps).</summary>
    public string IconImagePath { get; set; } = "";

    /// <summary>Medium tile background image (Square150x150Logo) — used to fill Medium/Large tiles.</summary>
    public string MediumLogoPath { get; set; } = "";

    /// <summary>Wide tile background image (Wide310x150Logo) — used to fill Wide tiles.</summary>
    public string WideLogoPath { get; set; } = "";

    public bool IsStoreApp => !string.IsNullOrEmpty(AppUserModelId);

    public string Id
    {
        get
        {
            if (!string.IsNullOrEmpty(AppUserModelId))
                return AppUserModelId.ToLowerInvariant();
            if (!string.IsNullOrEmpty(TargetPath))
                return Path.GetFileNameWithoutExtension(TargetPath).ToLowerInvariant();
            return Name.ToLowerInvariant().Replace(" ", "_");
        }
    }

    public char FirstLetter =>
        string.IsNullOrEmpty(Name) ? '#' : char.ToUpper(Name[0]) is >= 'A' and <= 'Z' ? char.ToUpper(Name[0]) : '#';
}

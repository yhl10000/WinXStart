using System.IO;
using System.Runtime.InteropServices;
using WinXStart.Models;

namespace WinXStart.Services;

public class AppScanner
{
    private static readonly string[] ExcludePatterns =
    {
        "uninstall", "help", "readme", "license", "release notes",
        "documentation", "manual", "guide", "what's new"
    };

    public List<AppInfo> ScanAll()
    {
        var apps = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);

        var commonStart = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        ScanFolder(Path.Combine(commonStart, "Programs"), apps);

        var userStart = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        ScanFolder(Path.Combine(userStart, "Programs"), apps);

        return apps.Values
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ScanFolder(string folder, Dictionary<string, AppInfo> apps)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var lnk in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
        {
            try
            {
                var app = ParseShortcut(lnk, folder);
                if (app != null && !IsExcluded(app))
                    apps.TryAdd(app.Name.ToLowerInvariant(), app);
            }
            catch
            {
                // Skip unparseable shortcuts
            }
        }
    }

    private AppInfo? ParseShortcut(string lnkPath, string baseFolder)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return null;

        dynamic? shell = null;
        dynamic? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            shortcut = shell.CreateShortcut(lnkPath);

            string targetPath = shortcut.TargetPath ?? "";
            string arguments = shortcut.Arguments ?? "";
            string iconLocation = shortcut.IconLocation ?? "";

            if (string.IsNullOrWhiteSpace(targetPath)) return null;

            var relativePath = Path.GetRelativePath(baseFolder, lnkPath);
            var category = Path.GetDirectoryName(relativePath) ?? "";

            return new AppInfo
            {
                Name = Path.GetFileNameWithoutExtension(lnkPath),
                TargetPath = targetPath,
                Arguments = arguments,
                IconPath = iconLocation,
                Category = category,
                ShortcutPath = lnkPath
            };
        }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    private static bool IsExcluded(AppInfo app)
    {
        var nameLower = app.Name.ToLowerInvariant();
        return ExcludePatterns.Any(p => nameLower.Contains(p));
    }
}

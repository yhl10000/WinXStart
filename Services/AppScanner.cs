using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;
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

        ScanStoreApps(apps);

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

    private static void ScanStoreApps(Dictionary<string, AppInfo> apps)
    {
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            var packages = pm.FindPackagesForUser("");

            foreach (var pkg in packages)
            {
                try
                {
                    if (pkg.IsFramework || pkg.IsResourcePackage)
                        continue;

                    // Skip packages without an installed location
                    string installPath;
                    try { installPath = pkg.InstalledLocation.Path; }
                    catch { continue; }

                    var manifestPath = Path.Combine(installPath, "AppxManifest.xml");
                    if (!File.Exists(manifestPath))
                        continue;

                    var familyName = pkg.Id.FamilyName;

                    // Use WinRT-resolved display name as fallback (already localized)
                    string pkgDisplayName;
                    try { pkgDisplayName = pkg.DisplayName; }
                    catch { pkgDisplayName = ""; }

                    foreach (var entry in ParseManifestApps(manifestPath, installPath, familyName, pkgDisplayName))
                    {
                        if (!string.IsNullOrEmpty(entry.Name) && !IsExcluded(entry))
                            apps.TryAdd(entry.Id, entry);
                    }
                }
                catch
                {
                    // Skip packages that throw (access denied, etc.)
                }
            }
        }
        catch
        {
            // PackageManager not available — older Windows or permission issue
        }
    }

    private static List<AppInfo> ParseManifestApps(string manifestPath, string installPath,
        string familyName, string pkgDisplayName)
    {
        var results = new List<AppInfo>();

        try
        {
            var doc = XDocument.Load(manifestPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var uap = doc.Root?.GetNamespaceOfPrefix("uap") ?? ns;

            var applications = doc.Descendants(ns + "Application");

            foreach (var appElem in applications)
            {
                var appId = appElem.Attribute("Id")?.Value;
                if (string.IsNullOrEmpty(appId)) continue;

                // Skip apps explicitly hidden from the app list
                var visual = appElem.Element(uap + "VisualElements");
                var appListEntry = visual?.Attribute("AppListEntry")?.Value;
                if ("none".Equals(appListEntry, StringComparison.OrdinalIgnoreCase))
                    continue;

                var aumid = $"{familyName}!{appId}";

                // Resolve display name: VisualElements → Properties → Package.DisplayName → family prefix
                var displayName = visual?.Attribute("DisplayName")?.Value ?? "";

                if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    displayName = doc.Descendants(ns + "DisplayName").FirstOrDefault()?.Value ?? "";
                }

                if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    displayName = pkgDisplayName;
                }

                if (string.IsNullOrEmpty(displayName) || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    displayName = familyName.Split('_')[0];
                }

                // Filter out non-user-friendly names
                if (!IsUserFriendlyName(displayName)) continue;

                // Find icon — prefer Square44x44Logo, then Square150x150Logo
                var iconPath = "";
                var logoAttr = visual?.Attribute("Square44x44Logo")?.Value
                    ?? visual?.Attribute("Square150x150Logo")?.Value
                    ?? "";

                if (!string.IsNullOrEmpty(logoAttr))
                    iconPath = FindScaledAsset(installPath, logoAttr);

                // Medium (150x150) and Wide (310x150) logos — used as tile background fill
                var mediumLogoAttr = visual?.Attribute("Square150x150Logo")?.Value ?? "";
                var wideLogoAttr = visual?.Attribute("Wide310x150Logo")?.Value ?? "";
                var mediumLogoPath = string.IsNullOrEmpty(mediumLogoAttr) ? "" : FindLargeAsset(installPath, mediumLogoAttr);
                var wideLogoPath = string.IsNullOrEmpty(wideLogoAttr) ? "" : FindLargeAsset(installPath, wideLogoAttr);

                results.Add(new AppInfo
                {
                    Name = displayName,
                    AppUserModelId = aumid,
                    IconImagePath = iconPath,
                    MediumLogoPath = mediumLogoPath,
                    WideLogoPath = wideLogoPath,
                    Category = "Store Apps"
                });
            }
        }
        catch
        {
            // Manifest parsing failed
        }

        return results;
    }

    /// <summary>
    /// Returns false for GUID-like names, reverse-domain (com.xxx), and other system identifiers.
    /// </summary>
    internal static bool IsUserFriendlyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // GUID pattern: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx or {guid}
        if (Guid.TryParse(name, out _)) return false;
        if (name.StartsWith('{') && name.EndsWith('}')) return false;

        // Reverse-domain notation: com.xxx, org.xxx, net.xxx
        if (name.StartsWith("com.", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("org.", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("net.", StringComparison.OrdinalIgnoreCase))
            return false;

        // Microsoft internal package IDs: Microsoft.xxx.yyy with no spaces
        // (real display names like "Microsoft Store" have spaces)
        if (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) && !name.Contains(' '))
            return false;

        // Names that are mostly non-alphanumeric or look like identifiers (e.g. "windows.immersivecontrolpanel")
        if (name.Contains('.') && !name.Contains(' '))
            return false;

        return true;
    }

    /// <summary>
    /// Finds the best available scaled asset for a given logo path.
    /// E.g. "Assets\Square44x44Logo.png" → tries scale-200, scale-100, targetsize-48, etc.
    /// </summary>
    private static string FindScaledAsset(string installPath, string relativeLogo)
    {
        var dir = Path.Combine(installPath, Path.GetDirectoryName(relativeLogo) ?? "");
        var baseName = Path.GetFileNameWithoutExtension(relativeLogo);
        var ext = Path.GetExtension(relativeLogo);

        if (!Directory.Exists(dir)) return "";

        // Preferred scale/targetsize suffixes in priority order
        var suffixes = new[]
        {
            ".targetsize-48", ".targetsize-64", ".targetsize-32",
            ".scale-200", ".scale-150", ".scale-100",
            ".targetsize-48_altform-unplated", ".targetsize-64_altform-unplated",
            ".targetsize-32_altform-unplated",
            ""  // exact name
        };

        foreach (var suffix in suffixes)
        {
            var candidate = Path.Combine(dir, baseName + suffix + ext);
            if (File.Exists(candidate)) return candidate;
        }

        // Fallback: find any file matching the base name
        try
        {
            var match = Directory.EnumerateFiles(dir, baseName + "*" + ext)
                .FirstOrDefault();
            if (match != null) return match;
        }
        catch { }

        return "";
    }

    /// <summary>
    /// Finds the best large-scale asset for tile backgrounds (prefer scale-400/200 for crisp rendering).
    /// </summary>
    private static string FindLargeAsset(string installPath, string relativeLogo)
    {
        var dir = Path.Combine(installPath, Path.GetDirectoryName(relativeLogo) ?? "");
        var baseName = Path.GetFileNameWithoutExtension(relativeLogo);
        var ext = Path.GetExtension(relativeLogo);

        if (!Directory.Exists(dir)) return "";

        // Prefer larger scales for tile backgrounds
        var suffixes = new[]
        {
            ".scale-200", ".scale-400", ".scale-150", ".scale-125", ".scale-100",
            ""  // exact name
        };

        foreach (var suffix in suffixes)
        {
            var candidate = Path.Combine(dir, baseName + suffix + ext);
            if (File.Exists(candidate)) return candidate;
        }

        // Fallback: any file matching base name, but skip "contrast" / "altform" variants
        try
        {
            var match = Directory.EnumerateFiles(dir, baseName + "*" + ext)
                .Where(f => !f.Contains("contrast", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains("altform", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (match != null) return match;
        }
        catch { }

        return "";
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

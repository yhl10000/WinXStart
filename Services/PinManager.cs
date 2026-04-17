using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinXStart.Models;

namespace WinXStart.Services;

public class PinManager
{
    private readonly string _configPath;
    private UserConfig _config;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public PinManager()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "WinXStart");

        Directory.CreateDirectory(configDir);

        _configPath = Path.Combine(configDir, "pins.json");

        // Migrate from old location
        var oldPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinXStart", "pins.json");
        if (!File.Exists(_configPath) && File.Exists(oldPath))
            File.Move(oldPath, _configPath);

        _config = Load();
    }

    public UserConfig Config => _config;
    public List<TileGroup> Groups => _config.Groups;
    public List<AppInfo> CustomApps => _config.CustomApps;

    /// <summary>
    /// Register a custom app (from disk) so it persists across restarts, then pin it.
    /// </summary>
    public void AddCustomApp(AppInfo app, string? groupName = null)
    {
        // Avoid duplicates
        if (!_config.CustomApps.Any(a =>
                a.TargetPath.Equals(app.TargetPath, StringComparison.OrdinalIgnoreCase)))
        {
            _config.CustomApps.Add(app);
        }

        Pin(app.Id, TileSize.Medium, groupName);
    }

    public bool IsPinned(string appId)
    {
        return _config.Groups
            .SelectMany(g => g.Tiles)
            .Any(t => t.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));
    }

    public void Pin(string appId, TileSize size = TileSize.Medium, string? groupName = null)
    {
        groupName ??= "Pinned";

        var group = _config.Groups.FirstOrDefault(g =>
            g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

        if (group == null)
        {
            group = new TileGroup { Name = groupName };
            _config.Groups.Add(group);
        }

        if (group.Tiles.Any(t => t.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase)))
            return;

        group.Tiles.Add(new PinnedTile
        {
            AppId = appId,
            Size = size,
            Order = group.Tiles.Count
        });

        Save();
    }

    public void Unpin(string appId)
    {
        foreach (var group in _config.Groups)
        {
            group.Tiles.RemoveAll(t =>
                t.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));
        }

        _config.Groups.RemoveAll(g => g.Tiles.Count == 0);

        // Ensure at least one group exists
        if (_config.Groups.Count == 0)
            _config.Groups.Add(new TileGroup { Name = "Pinned" });

        Save();
    }

    public void ResizeTile(string appId, TileSize newSize)
    {
        var tile = _config.Groups
            .SelectMany(g => g.Tiles)
            .FirstOrDefault(t => t.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));

        if (tile != null)
        {
            tile.Size = newSize;
            Save();
        }
    }

    public void ReorderInGroup(string groupName, int fromIndex, int toIndex)
    {
        var group = _config.Groups.FirstOrDefault(g =>
            g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        if (group == null) return;

        var tiles = group.Tiles.OrderBy(t => t.Order).ToList();
        if (fromIndex < 0 || fromIndex >= tiles.Count ||
            toIndex < 0 || toIndex >= tiles.Count ||
            fromIndex == toIndex)
            return;

        var tile = tiles[fromIndex];
        tiles.RemoveAt(fromIndex);
        tiles.Insert(toIndex, tile);

        for (int i = 0; i < tiles.Count; i++)
            tiles[i].Order = i;

        Save();
    }

    public void CreateGroup(string name)
    {
        if (_config.Groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;
        _config.Groups.Add(new TileGroup { Name = name });
        Save();
    }

    public void RenameGroup(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        var group = _config.Groups.FirstOrDefault(g =>
            g.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (group == null) return;
        group.Name = newName;
        Save();
    }

    public void DeleteGroup(string name)
    {
        _config.Groups.RemoveAll(g =>
            g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (_config.Groups.Count == 0)
            _config.Groups.Add(new TileGroup { Name = "Pinned" });

        Save();
    }

    public void MoveToGroup(string appId, string targetGroupName, int insertIndex = -1)
    {
        PinnedTile? tile = null;
        string? sourceGroupName = null;
        foreach (var g in _config.Groups)
        {
            var t = g.Tiles.FirstOrDefault(t =>
                t.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));
            if (t != null) { tile = t; sourceGroupName = g.Name; g.Tiles.Remove(t); break; }
        }
        if (tile == null) return;

        var target = _config.Groups.FirstOrDefault(g =>
            g.Name.Equals(targetGroupName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            target = new TileGroup { Name = targetGroupName };
            _config.Groups.Add(target);
        }
        if (insertIndex >= 0 && insertIndex <= target.Tiles.Count)
            target.Tiles.Insert(insertIndex, tile);
        else
            target.Tiles.Add(tile);

        // Rebuild Order to match list position
        for (int i = 0; i < target.Tiles.Count; i++)
            target.Tiles[i].Order = i;

        // Only remove the SOURCE group if it became empty (not all empty groups)
        if (sourceGroupName != null &&
            !sourceGroupName.Equals(targetGroupName, StringComparison.OrdinalIgnoreCase))
        {
            _config.Groups.RemoveAll(g =>
                g.Name.Equals(sourceGroupName, StringComparison.OrdinalIgnoreCase) &&
                g.Tiles.Count == 0);
        }

        if (_config.Groups.Count == 0)
            _config.Groups.Add(new TileGroup { Name = "Pinned" });

        Save();
    }

    private UserConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<UserConfig>(json, JsonOptions) ?? CreateDefault();
            }
        }
        catch
        {
            // Fallback to default
        }

        return CreateDefault();
    }

    private static UserConfig CreateDefault()
    {
        return new UserConfig
        {
            Groups = new List<TileGroup>
            {
                new() { Name = "Pinned", Tiles = new List<PinnedTile>() }
            }
        };
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch
        {
            // Silently fail on save errors
        }
    }
}

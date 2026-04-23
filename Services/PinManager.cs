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

    /// <summary>
    /// Default constructor: stores config under %UserProfile%\WinXStart\pins.json.
    /// </summary>
    public PinManager() : this(configDirOverride: null) { }

    /// <summary>
    /// Test/DI-friendly constructor. When <paramref name="configDirOverride"/> is non-null,
    /// the config file lives there and the legacy-location migration is skipped.
    /// </summary>
    public PinManager(string? configDirOverride)
    {
        string configDir;
        string? legacyPath = null;

        if (configDirOverride != null)
        {
            configDir = configDirOverride;
        }
        else
        {
            configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "WinXStart");
            legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinXStart", "pins.json");
        }

        Directory.CreateDirectory(configDir);

        _configPath = Path.Combine(configDir, "pins.json");

        // Migrate from old location (production path only)
        if (legacyPath != null && !File.Exists(_configPath) && File.Exists(legacyPath))
            File.Move(legacyPath, _configPath);

        _config = Load();
        NormalizeOrders();
    }

    /// <summary>
    /// Rewrites every group's tile Order to a dense 0..N-1 sequence based on
    /// current Order-sorted position. Fixes duplicates, gaps, and hand-edits.
    /// Saves if any group needed normalization.
    /// </summary>
    private void NormalizeOrders()
    {
        bool dirty = false;
        foreach (var group in _config.Groups)
        {
            var sorted = group.Tiles.OrderBy(t => t.Order).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Order != i)
                {
                    sorted[i].Order = i;
                    dirty = true;
                }
            }
        }
        if (dirty) Save();
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

        // Use max(Order)+1, not group.Tiles.Count. Count is the physical list
        // length, which can collide with an existing Order if earlier Unpin/Move
        // operations left gaps or if the file was hand-edited.
        int nextOrder = group.Tiles.Count == 0 ? 0 : group.Tiles.Max(t => t.Order) + 1;

        group.Tiles.Add(new PinnedTile
        {
            AppId = appId,
            Size = size,
            Order = nextOrder
        });

        Save();
    }

    public void Unpin(string appId)
    {
        foreach (var group in _config.Groups)
        {
            int before = group.Tiles.Count;
            group.Tiles.RemoveAll(t =>
                t.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));

            // Rewrite Order to close any gap left behind. Otherwise subsequent
            // Pin() (which uses max+1) keeps growing Order unbounded, and future
            // hand-edits or crash recovery see sparse Order values.
            if (group.Tiles.Count != before)
            {
                var sorted = group.Tiles.OrderBy(t => t.Order).ToList();
                for (int i = 0; i < sorted.Count; i++) sorted[i].Order = i;
            }
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
        // Locate tile & source group
        PinnedTile? tile = null;
        TileGroup? sourceGroup = null;
        foreach (var g in _config.Groups)
        {
            var t = g.Tiles.FirstOrDefault(t =>
                t.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));
            if (t != null) { tile = t; sourceGroup = g; break; }
        }
        if (tile == null || sourceGroup == null) return;

        var target = _config.Groups.FirstOrDefault(g =>
            g.Name.Equals(targetGroupName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            target = new TileGroup { Name = targetGroupName };
            _config.Groups.Add(target);
        }

        bool sameGroup = ReferenceEquals(sourceGroup, target);

        // CRITICAL: operate on Order-sorted view (not list-physical order)
        // because insertIndex is given in UI/Order coordinates, and list
        // physical order may diverge from Order after prior Pin/Move operations.
        if (sameGroup)
        {
            var sorted = target.Tiles.OrderBy(t => t.Order).ToList();
            sorted.Remove(tile);
            int idx = (insertIndex >= 0 && insertIndex <= sorted.Count) ? insertIndex : sorted.Count;
            sorted.Insert(idx, tile);
            for (int i = 0; i < sorted.Count; i++) sorted[i].Order = i;
        }
        else
        {
            // Remove from source, reindex source by Order to keep it consistent
            sourceGroup.Tiles.Remove(tile);
            var sourceSorted = sourceGroup.Tiles.OrderBy(t => t.Order).ToList();
            for (int i = 0; i < sourceSorted.Count; i++) sourceSorted[i].Order = i;

            // Insert into target at Order-coordinate insertIndex
            var targetSorted = target.Tiles.OrderBy(t => t.Order).ToList();
            int idx = (insertIndex >= 0 && insertIndex <= targetSorted.Count) ? insertIndex : targetSorted.Count;
            targetSorted.Insert(idx, tile);
            for (int i = 0; i < targetSorted.Count; i++) targetSorted[i].Order = i;

            // Ensure tile is physically present in target.Tiles (was only removed from source)
            if (!target.Tiles.Contains(tile))
                target.Tiles.Add(tile);

            // Remove source group if it became empty
            if (sourceGroup.Tiles.Count == 0)
                _config.Groups.Remove(sourceGroup);
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

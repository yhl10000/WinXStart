using System.Text;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinXStart.Models;
using WinXStart.Services;

namespace WinXStart.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly AppScanner _scanner;
    private readonly IconExtractor _iconExtractor;
    private readonly PinManager _pinManager;

    private string _searchText = "";
    private List<AppInfo> _allApps = new();

    public ObservableCollection<AppInfo> FilteredApps { get; } = new();
    public ObservableCollection<TileGroupViewModel> TileGroups { get; } = new();
    private static readonly string _displayName = GetDisplayName();
    public string UserName { get; } = _displayName;
    public string UserInitial { get; } = _displayName.Length > 0
        ? _displayName[..1].ToUpperInvariant() : "?";
    public ImageSource? UserAvatar { get; } = LoadUserAvatar();

    private static string GetDisplayName()
    {
        try
        {
            int size = 256;
            var buf = new StringBuilder(size);
            if (Interop.NativeMethods.GetUserNameExW(Interop.NativeMethods.NameDisplay, buf, ref size) && buf.Length > 0)
                return buf.ToString();
        }
        catch { }
        return Environment.UserName;
    }

    private static ImageSource? LoadUserAvatar()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (sid == null) return null;

            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
            if (key == null) return null;

            foreach (var name in new[] { "Image192", "Image96", "Image64", "Image48", "Image240" })
            {
                if (key.GetValue(name) is string path && File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 56;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
        }
        catch { }
        return null;
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                ApplyFilter();
        }
    }

    public ICommand LaunchCommand { get; }
    public ICommand LaunchTileCommand { get; }
    public ICommand PinCommand { get; }
    public ICommand UnpinCommand { get; }
    public ICommand CreateGroupCommand { get; }

    public event Action? RequestHide;

    public MainViewModel(AppScanner scanner, IconExtractor iconExtractor, PinManager pinManager)
    {
        _scanner = scanner;
        _iconExtractor = iconExtractor;
        _pinManager = pinManager;

        LaunchCommand = new RelayCommand(OnLaunch);
        LaunchTileCommand = new RelayCommand(OnLaunchTile);
        PinCommand = new RelayCommand(OnPin);
        UnpinCommand = new RelayCommand(OnUnpin);
        CreateGroupCommand = new RelayCommand(OnCreateGroup);

        LoadApps();
    }

    public void LoadApps()
    {
        _allApps = _scanner.ScanAll();

        // Merge persisted custom apps so tile resolution works
        foreach (var custom in _pinManager.CustomApps)
        {
            if (!_allApps.Any(a => a.Id.Equals(custom.Id, StringComparison.OrdinalIgnoreCase)))
                _allApps.Add(custom);
        }

        ApplyFilter();
        RefreshTileGroups();
    }

    public ImageSource GetIconForApp(AppInfo app) => _iconExtractor.GetIcon(app);
    public bool IsPinned(AppInfo app) => _pinManager.IsPinned(app.Id);
    public bool HasAnyTiles => TileGroups.Any(g => g.Tiles.Count > 0);

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        var query = _searchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allApps
            : _allApps.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var app in filtered)
            FilteredApps.Add(app);
    }

    public void RefreshTileGroups()
    {
        TileGroups.Clear();
        foreach (var group in _pinManager.Groups)
        {
            var groupVm = new TileGroupViewModel(group.Name);
            groupVm.NameChanged += OnGroupNameChanged;

            foreach (var tile in group.Tiles.OrderBy(t => t.Order))
            {
                var app = _allApps.FirstOrDefault(a =>
                    a.Id.Equals(tile.AppId, StringComparison.OrdinalIgnoreCase));
                if (app != null)
                {
                    var icon = _iconExtractor.GetIcon(app);
                    var tileVm = new TileViewModel(app, icon, tile.Size);
                    tileVm.ResizeRequested += (appId, size) => _pinManager.ResizeTile(appId, size);
                    groupVm.Tiles.Add(tileVm);
                }
            }
            TileGroups.Add(groupVm);
        }
        OnPropertyChanged(nameof(HasAnyTiles));
    }

    // Group operations
    public void MoveTileInGroup(TileGroupViewModel groupVm, int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= groupVm.Tiles.Count ||
            toIndex < 0 || toIndex >= groupVm.Tiles.Count ||
            fromIndex == toIndex) return;
        groupVm.Tiles.Move(fromIndex, toIndex);
        _pinManager.ReorderInGroup(groupVm.Name, fromIndex, toIndex);
        OnPropertyChanged(nameof(HasAnyTiles));
    }

    public void MoveTileToGroup(TileViewModel tile, TileGroupViewModel targetGroupVm)
    {
        _pinManager.MoveToGroup(tile.AppId, targetGroupVm.Name);
        RefreshTileGroups();
    }

    public void MoveTileToNewGroup(TileViewModel tile, string newGroupName)
    {
        _pinManager.CreateGroup(newGroupName);
        _pinManager.MoveToGroup(tile.AppId, newGroupName);
        RefreshTileGroups();
    }

    public void DeleteGroup(TileGroupViewModel groupVm)
    {
        _pinManager.DeleteGroup(groupVm.Name);
        RefreshTileGroups();
    }

    private void OnCreateGroup()
    {
        int n = 1;
        string name;
        do { name = $"New Group {n++}"; }
        while (_pinManager.Groups.Any(g =>
            g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        _pinManager.CreateGroup(name);
        RefreshTileGroups();

        var newVm = TileGroups.FirstOrDefault(g =>
            g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (newVm != null)
            newVm.IsEditingName = true;
    }

    private void OnGroupNameChanged(TileGroupViewModel groupVm, string oldName, string newName)
    {
        _pinManager.RenameGroup(oldName, newName);
    }

    // Launch
    private void LaunchApp(AppInfo app)
    {
        try { Process.Start(new ProcessStartInfo { FileName = app.TargetPath, Arguments = app.Arguments, UseShellExecute = true }); }
        catch
        {
            try { if (!string.IsNullOrEmpty(app.ShortcutPath)) Process.Start(new ProcessStartInfo { FileName = app.ShortcutPath, UseShellExecute = true }); }
            catch { }
        }
        RequestHide?.Invoke();
    }

    private void OnLaunch(object? param) { if (param is AppInfo app) LaunchApp(app); }
    private void OnLaunchTile(object? param) { if (param is TileViewModel tile) LaunchApp(tile.AppInfo); }

    private void OnPin(object? param)
    {
        if (param is AppInfo app) { _pinManager.Pin(app.Id); RefreshTileGroups(); }
    }

    private void OnUnpin(object? param)
    {
        if (param is TileViewModel tile) { _pinManager.Unpin(tile.AppId); RefreshTileGroups(); }
    }

    /// <summary>
    /// Pin an app from a file path (.exe or .lnk) selected by the user.
    /// </summary>
    public void PinFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        AppInfo app;

        if (ext == ".lnk")
        {
            app = ParseShortcutFile(filePath);
        }
        else
        {
            // Bare .exe (or .msc, .bat, etc.)
            app = new AppInfo
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                TargetPath = filePath
            };
        }

        if (string.IsNullOrWhiteSpace(app.TargetPath) && string.IsNullOrWhiteSpace(app.ShortcutPath))
            return;

        // Add to in-memory list so tile resolution works immediately
        if (!_allApps.Any(a => a.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase)))
            _allApps.Add(app);

        // Persist custom app + pin it
        _pinManager.AddCustomApp(app);
        ApplyFilter();
        RefreshTileGroups();
    }

    private static AppInfo ParseShortcutFile(string lnkPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            return new AppInfo { Name = Path.GetFileNameWithoutExtension(lnkPath), ShortcutPath = lnkPath };

        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null)
                return new AppInfo { Name = Path.GetFileNameWithoutExtension(lnkPath), ShortcutPath = lnkPath };

            shortcut = shell.CreateShortcut(lnkPath);
            return new AppInfo
            {
                Name = Path.GetFileNameWithoutExtension(lnkPath),
                TargetPath = shortcut.TargetPath ?? "",
                Arguments = shortcut.Arguments ?? "",
                IconPath = shortcut.IconLocation ?? "",
                ShortcutPath = lnkPath
            };
        }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }
}

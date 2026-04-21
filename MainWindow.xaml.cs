using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinXStart.Interop;
using WinXStart.Models;
using WinXStart.ViewModels;

namespace WinXStart;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;

    public MainWindow(MainViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        viewModel.RequestHide += HideMenu;
        viewModel.TileGroups.CollectionChanged += (_, _) => UpdateEmptyPlaceholder();

        // ── Custom drag ghost: Popup centered on the cursor ──
        // Gong's built-in ghost is disabled in XAML (UseDefaultDragAdorner=False,
        // no DragAdornerTemplate). We render our own ghost via the DragGhostPopup
        // defined in MainWindow.xaml and drive it from IDragSource callbacks.
        viewModel.TileDropHandler.DragStarted += OnTileDragStarted;
        viewModel.TileDropHandler.DragEnded   += OnTileDragEnded;
        PreviewDragOver += OnWindowPreviewDragOver;

        ApplySettings();
    }

    // ── Custom drag ghost ────────────────────────────────────

    private TileViewModel? _ghostTile;
    // Cached per-drag to avoid VisualTreeHelper.GetDpi + allocations on every mousemove.
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    private double _ghostHalfW;
    private double _ghostHalfH;
    private int _lastPopupUpdateTick;

    // Route E perf: images switched to LowQuality scaling during drag. Restore on end.
    // Under AllowsTransparency=True the window is software-rendered (layered window);
    // Fant/HighQuality scaling is CPU-expensive per frame. LowQuality = NearestNeighbor,
    // barely noticeable on 32x32 icons that are already near source size.
    private readonly List<System.Windows.Controls.Image> _scaledImagesDuringDrag = new();

    private void OnTileDragStarted(TileViewModel tile)
    {
        _ghostTile = tile;
        DragGhostContent.Content = BuildGhostVisual(tile);

        var dpi = VisualTreeHelper.GetDpi(this);
        _dpiScaleX = dpi.DpiScaleX;
        _dpiScaleY = dpi.DpiScaleY;
        _ghostHalfW = tile.TileWidth / 2.0;
        _ghostHalfH = tile.TileHeight / 2.0;
        _lastPopupUpdateTick = 0;

        // ── Route E perf optimizations ──
        // 1. BitmapCache the entire tile area so WPF rasterizes it once and blits on
        //    subsequent frames instead of re-rendering the visual tree. This matters
        //    under software rendering (AllowsTransparency=True).
        GroupsControl.CacheMode = new BitmapCache { EnableClearType = false, SnapsToDevicePixels = true };

        // 2. Downgrade all tile image scaling to LowQuality (NearestNeighbor) for the
        //    duration of the drag. Fant/HighQuality filter is a CPU killer in layered windows.
        _scaledImagesDuringDrag.Clear();
        foreach (var img in FindVisualChildren<System.Windows.Controls.Image>(GroupsControl))
        {
            if (RenderOptions.GetBitmapScalingMode(img) == BitmapScalingMode.HighQuality)
            {
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality);
                _scaledImagesDuringDrag.Add(img);
            }
        }

        DragGhostPopup.IsOpen = true;
    }

    private void OnTileDragEnded()
    {
        _ghostTile = null;
        DragGhostPopup.IsOpen = false;
        DragGhostContent.Content = null;

        // Restore Route E perf state.
        GroupsControl.CacheMode = null;
        foreach (var img in _scaledImagesDuringDrag)
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        _scaledImagesDuringDrag.Clear();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void OnWindowPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!DragGhostPopup.IsOpen || _ghostTile == null) return;

        // Throttle Popup repositioning to ~60fps. PreviewDragOver can fire several
        // hundred times per second and each Popup.HorizontalOffset assignment
        // forces a re-layout of the popup window — which is the main jank source.
        int now = Environment.TickCount;
        if (now - _lastPopupUpdateTick < 16) return;
        _lastPopupUpdateTick = now;

        var posInWindow = e.GetPosition(this);
        var screenPos = PointToScreen(posInWindow);

        // Center the ghost on the cursor. DPI scale cached at drag start.
        DragGhostPopup.HorizontalOffset = screenPos.X / _dpiScaleX - _ghostHalfW;
        DragGhostPopup.VerticalOffset   = screenPos.Y / _dpiScaleY - _ghostHalfH;
    }

    /// <summary>Builds a lightweight visual that mirrors the tile's appearance.</summary>
    private static FrameworkElement BuildGhostVisual(TileViewModel tile)
    {
        // Outer border = a subtle 1px bright halo that implies "floating" without
        // the cost of DropShadowEffect (which is a per-frame Gaussian blur and
        // wrecks drag FPS as the ghost moves with the cursor).
        var border = new Border
        {
            Width = tile.TileWidth,
            Height = tile.TileHeight,
            Background = tile.TileColor,
            CornerRadius = new CornerRadius(2),
            Opacity = 0.85,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
        };

        var grid = new Grid { ClipToBounds = true };

        if (tile.HasBackgroundImage && tile.BackgroundImage != null)
        {
            grid.Children.Add(new System.Windows.Controls.Image
            {
                Source = tile.BackgroundImage,
                Stretch = System.Windows.Media.Stretch.UniformToFill
            });
        }

        if (tile.Icon != null)
        {
            grid.Children.Add(new System.Windows.Controls.Image
            {
                Source = tile.Icon,
                Width = 32, Height = 32,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
        }

        border.Child = grid;
        return border;
    }

    public void ShowMenu()
    {
        if (IsVisible) { HideMenu(); return; }

        _viewModel.SearchText = "";

        // Phase 1: Move the hidden HWND onto the target monitor (in physical px)
        // BEFORE Show(). WPF's DIP→physical conversion for Left/Top/Width/Height
        // uses the HWND's "current monitor" DPI. If we leave it stuck on the
        // previous monitor's DPI, PositionWindow()'s correctly-computed DIPs get
        // multiplied by the wrong scale, producing a first frame at the wrong
        // size/position; WM_DPICHANGED then fires and corrects it — visible flicker.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetCursorPos(out var cursor))
        {
            var hMon = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                int cx = (mi.rcWork.Left + mi.rcWork.Right) / 2;
                int cy = (mi.rcWork.Top  + mi.rcWork.Bottom) / 2;
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, cx, cy, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
        }

        // Phase 2: Show at Opacity=0 so the first UpdateLayeredWindow frame (at
        // potentially-stale DPI) is fully transparent. WM_DPICHANGED is sent via
        // SendMessage (synchronous) during Show() — by the time Show() returns,
        // WPF's internal DPI matches the target monitor.
        Opacity = 0;
        Show();

        // Phase 3: Now that WPF's DPI is correct, position in DIPs and reveal.
        PositionWindow();
        Opacity = 1;
        Activate();
        SearchBox.Focus();
        UpdateEmptyPlaceholder();
    }

    public void HideMenu() => Hide();

    private void UpdateEmptyPlaceholder()
    {
        EmptyPlaceholder.Visibility = _viewModel.HasAnyTiles
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PositionWindow()
    {
        double ratio = Math.Clamp(_settings.WindowSizePercent, 10, 100) / 100.0;

        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var workArea = info.rcWork;

                // Use the TARGET monitor's DPI, not the window's current monitor DPI.
                // VisualTreeHelper.GetDpi(this) returns DPI of the monitor the window is
                // currently on — which is wrong on first show after a monitor switch.
                // Using wrong DPI computes wrong size/position, then WPF's subsequent
                // DPI-change relayout causes a visible flicker.
                double dpiScaleX = 1.0, dpiScaleY = 1.0;
                if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.Effective,
                        out uint dpiX, out uint dpiY) == 0)
                {
                    dpiScaleX = dpiX / 96.0;
                    dpiScaleY = dpiY / 96.0;
                }
                else
                {
                    var fallback = VisualTreeHelper.GetDpi(this);
                    dpiScaleX = fallback.DpiScaleX;
                    dpiScaleY = fallback.DpiScaleY;
                }

                double screenW = (workArea.Right - workArea.Left) / dpiScaleX;
                double screenH = (workArea.Bottom - workArea.Top) / dpiScaleY;
                double screenLeft = workArea.Left / dpiScaleX;
                double screenTop  = workArea.Top  / dpiScaleY;

                Width  = Math.Floor(screenW * ratio);
                Height = Math.Floor(screenH * ratio);

                Left = screenLeft + (screenW - Width)  / 2;
                Top  = screenTop  + (screenH - Height) / 2;
                return;
            }
        }

        // Fallback
        var area = SystemParameters.WorkArea;
        Width  = Math.Floor(area.Width  * ratio);
        Height = Math.Floor(area.Height * ratio);
        Left = (area.Width  - Width)  / 2;
        Top  = (area.Height - Height) / 2;
    }

    private void Window_Deactivated(object sender, EventArgs e) => HideMenu();

    private void ApplySettings()
    {
        byte alpha = (byte)(Math.Clamp(_settings.Opacity, 0, 100) * 255 / 100);

        // ── RootBorder gradient background ──
        var (start, end) = _settings.GradientDirection?.ToLowerInvariant() switch
        {
            "horizontal" => (new Point(0, 0.5), new Point(1, 0.5)),
            "vertical"   => (new Point(0.5, 0), new Point(0.5, 1)),
            _            => (new Point(0, 0),   new Point(1, 1))    // diagonal
        };

        var gradient = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        var colors = _settings.GradientColors;
        if (colors is { Length: > 0 })
        {
            for (int i = 0; i < colors.Length; i++)
            {
                var c = ParseColor(colors[i]);
                c = System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B);   // override alpha
                double offset = colors.Length == 1 ? 0 : (double)i / (colors.Length - 1);
                gradient.GradientStops.Add(new GradientStop(c, offset));
            }
        }
        RootBorder.Background = gradient;

        // ── Border ──
        RootBorder.BorderBrush = new SolidColorBrush(ParseColor(_settings.BorderColor));
        RootBorder.BorderThickness = new Thickness(_settings.BorderThickness);
        RootBorder.CornerRadius = new CornerRadius(_settings.CornerRadius);

        // ── Separator ──
        if (FindName("Separator") is System.Windows.Shapes.Rectangle sep)
            sep.Fill = new SolidColorBrush(ParseColor(_settings.SeparatorColor));

        // ── Bottom bar ──
        if (FindName("BottomBar") is Border bar)
            bar.Background = new SolidColorBrush(ParseColor(_settings.BottomBarBackground));

        // ── Search box (style overrides at runtime) ──
        SearchBox.Background = new SolidColorBrush(ParseColor(_settings.SearchBoxBackground));
        SearchBox.BorderBrush = new SolidColorBrush(ParseColor(_settings.SearchBoxBorder));

        // ── Font sizes (DynamicResource in XAML picks these up) ──
        Resources["TileFontSize"] = _settings.TileFontSize;
        Resources["GroupFontSize"] = _settings.GroupFontSize;
    }

    private static System.Windows.Media.Color ParseColor(string hex)
    {
        try
        {
            var obj = System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if (obj is System.Windows.Media.Color c) return c;
        }
        catch { }
        return Colors.White;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { HideMenu(); e.Handled = true; }
    }

    private void AppList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is AppInfo app)
            _viewModel.LaunchCommand.Execute(app);
    }

    private void PinApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is AppInfo app)
            _viewModel.PinCommand.Execute(app);
    }

    private void UnpinTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TileViewModel tile)
            _viewModel.UnpinCommand.Execute(tile);
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is AppInfo app &&
            !string.IsNullOrEmpty(app.ShortcutPath))
        {
            try { Process.Start("explorer.exe", $"/select,\"{app.ShortcutPath}\""); }
            catch { }
        }
    }

    private void Power_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Close WinX Start Menu app?", "WinX Start",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            Application.Current.Shutdown();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var ver = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

        var result = MessageBox.Show(
            $"WinXStart  v{ver}\n\n" +
            "A lightweight Windows 10-style Start Menu.\n\n" +
            "Author:  yhl10000\n" +
            "Email:   yhl10000@gmail.com\n" +
            "GitHub:  github.com/yhl10000/WinXStart\n\n" +
            "Click OK to open the GitHub page.",
            "About WinXStart",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.OK)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/yhl10000/WinXStart",
                UseShellExecute = true
            });
        }
    }

    private void PinFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an application to pin",
            Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            Multiselect = false
        };

        // Temporarily make window non-topmost so the dialog isn't hidden behind
        Topmost = false;
        var ok = dlg.ShowDialog(this);
        Topmost = true;

        if (ok == true && !string.IsNullOrEmpty(dlg.FileName))
        {
            _viewModel.PinFromFile(dlg.FileName);
            UpdateEmptyPlaceholder();
        }
    }

    // ── Group header ──────────────────────────────────────────

    private void GroupName_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is TextBlock tb &&
            tb.DataContext is TileGroupViewModel gvm)
        {
            gvm.IsEditingName = true;
            e.Handled = true;
        }
    }

    private void GroupNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
            Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); });
    }

    private void GroupNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TileGroupViewModel gvm)
        {
            if (e.Key == Key.Enter)  { CommitGroupRename(tb, gvm); e.Handled = true; }
            if (e.Key == Key.Escape) { gvm.IsEditingName = false;  e.Handled = true; }
        }
    }

    private void GroupNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TileGroupViewModel gvm)
            CommitGroupRename(tb, gvm);
    }

    private static void CommitGroupRename(TextBox tb, TileGroupViewModel gvm)
    {
        var newName = tb.Text.Trim();
        if (!string.IsNullOrEmpty(newName))
            gvm.Name = newName;   // triggers NameChanged → PinManager.RenameGroup
        gvm.IsEditingName = false;
    }

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TileGroupViewModel gvm)
            gvm.IsEditingName = true;
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TileGroupViewModel gvm)
        {
            var res = MessageBox.Show(
                $"Delete group \"{gvm.Name}\" and all its tiles?",
                "WinX Start", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
                _viewModel.DeleteGroup(gvm);
        }
    }

    // ── Tile context menu: "Move to group" ───────────────────

    private void TileContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;
        var tile = cm.Tag as TileViewModel
                   ?? (cm.PlacementTarget as FrameworkElement)?.DataContext as TileViewModel;
        if (tile == null) return;

        // Find the MoveToMenuItem inside this ContextMenu
        MenuItem? moveToItem = null;
        foreach (var item in cm.Items)
        {
            if (item is MenuItem mi && mi.Name == "MoveToMenuItem")
            { moveToItem = mi; break; }
        }
        if (moveToItem == null) return;

        // Find which group this tile belongs to
        var currentGroup = _viewModel.TileGroups
            .FirstOrDefault(g => g.Tiles.Contains(tile));

        moveToItem.Items.Clear();

        foreach (var group in _viewModel.TileGroups)
        {
            if (group == currentGroup) continue;
            var item = MakeDarkMenuItem(group.Name);
            var capturedGroup = group;
            item.Click += (_, _) => _viewModel.MoveTileToGroup(tile, capturedGroup);
            moveToItem.Items.Add(item);
        }

        // "New group..." option
        if (moveToItem.Items.Count > 0)
            moveToItem.Items.Add(new Separator());
        var newGroupItem = MakeDarkMenuItem("New group...");
        newGroupItem.Click += (_, _) =>
        {
            var name = PromptGroupName();
            if (!string.IsNullOrWhiteSpace(name))
                _viewModel.MoveTileToNewGroup(tile, name);
        };
        moveToItem.Items.Add(newGroupItem);

        moveToItem.IsEnabled = moveToItem.Items.Count > 0;
    }

    /// Creates a MenuItem with explicit dark theme colors so it looks correct
    /// inside dynamically-built submenus (which live in a separate popup visual tree
    /// and don't inherit the Window's implicit styles).
    private static MenuItem MakeDarkMenuItem(string header) => new()
    {
        Header = header,
        Foreground = System.Windows.Media.Brushes.White,
        Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
        Padding = new Thickness(8, 5, 8, 5)
    };

    private static string? PromptGroupName()
    {
        // Simple inline dialog via InputDialog window (re-use MessageBox pattern)
        var dlg = new Window
        {
            Title = "New Group Name",
            Width = 320, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x28, 0x28, 0x28))
        };
        var tb = new TextBox { Margin = new Thickness(16, 16, 16, 8),
                               Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33,0x33,0x33)),
                               Foreground = System.Windows.Media.Brushes.White,
                               BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00,0x78,0xD4)),
                               Padding = new Thickness(6,4,6,4), FontSize = 13 };
        var ok = new System.Windows.Controls.Button { Content = "OK", Width = 70, Height = 26,
                               HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                               Margin = new Thickness(0,0,16,12) };
        ok.Click += (_, _) => dlg.DialogResult = true;
        tb.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) dlg.DialogResult = true; };
        var sp = new StackPanel();
        sp.Children.Add(tb);
        sp.Children.Add(ok);
        dlg.Content = sp;
        dlg.Loaded += (_, _) => { tb.Focus(); };
        return dlg.ShowDialog() == true ? tb.Text.Trim() : null;
    }
}

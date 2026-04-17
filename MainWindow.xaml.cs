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

        ApplySettings();
    }

    public void ShowMenu()
    {
        if (IsVisible) { HideMenu(); return; }
        PositionWindow();
        _viewModel.SearchText = "";
        Show();
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
                var dpi = VisualTreeHelper.GetDpi(this);
                double screenW = (workArea.Right - workArea.Left) / dpi.DpiScaleX;
                double screenH = (workArea.Bottom - workArea.Top) / dpi.DpiScaleY;
                double screenLeft = workArea.Left / dpi.DpiScaleX;
                double screenTop  = workArea.Top  / dpi.DpiScaleY;

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

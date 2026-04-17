using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using WinXStart.Interop;
using WinXStart.Models;
using WinXStart.ViewModels;

namespace WinXStart;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;

    // Drag state
    private Point _dragStartPoint;
    private TileViewModel? _draggedTile;
    private TileGroupViewModel? _dragSourceGroup;
    private ItemsControl? _dragSourceItemsControl;
    private DragAdorner? _dragAdorner;
    private AdornerLayer? _adornerLayer;
    private FrameworkElement? _dragSourceElement;

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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        HideMenu();
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

    // ── Drag-and-drop tile reordering ─────────────────────────

    private void GroupTiles_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl ic) return;
        _dragSourceItemsControl = ic;
        _dragStartPoint = e.GetPosition(ic);
        _draggedTile = FindTileFromHit(e.OriginalSource as DependencyObject);
        _dragSourceGroup = ic.DataContext as TileGroupViewModel;
    }

    private void GroupTiles_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedTile == null ||
            _dragSourceItemsControl == null) return;

        var pos = e.GetPosition(_dragSourceItemsControl);
        if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            CreateDragAdorner(e);
            var data = new DataObject("WinXStartTileDrag", true);
            DragDrop.DoDragDrop(_dragSourceItemsControl, data, DragDropEffects.Move);
            RemoveDragAdorner();
            _draggedTile = null;
            _dragSourceGroup = null;
            _dragSourceItemsControl = null;
        }
    }

    private void GroupTiles_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("WinXStartTileDrag")
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void RootBorder_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (_dragAdorner != null)
        {
            var pos = e.GetPosition(RootBorder);
            _dragAdorner.SetPosition(pos.X, pos.Y);
        }
    }

    private void GroupTiles_Drop(object sender, DragEventArgs e)
    {
        if (_draggedTile == null || !e.Data.GetDataPresent("WinXStartTileDrag")) return;

        var targetIc = sender as ItemsControl;
        var targetGroup = targetIc?.DataContext as TileGroupViewModel;

        var target = FindTileFromHit(e.OriginalSource as DependencyObject);

        if (targetGroup != null && _dragSourceGroup != null && targetGroup != _dragSourceGroup)
        {
            // Cross-group move
            _viewModel.MoveTileToGroup(_draggedTile, targetGroup);
        }
        else if (target != null && target != _draggedTile && _dragSourceGroup != null)
        {
            // Same-group reorder
            var ic = _dragSourceItemsControl!;
            var from = _dragSourceGroup.Tiles.IndexOf(_draggedTile);
            var to   = _dragSourceGroup.Tiles.IndexOf(target);
            if (from >= 0 && to >= 0)
                _viewModel.MoveTileInGroup(_dragSourceGroup, from, to);
        }

        _draggedTile = null;
        e.Handled = true;
    }

    // ── Adorner helpers ───────────────────────────────────────

    private void CreateDragAdorner(MouseEventArgs e)
    {
        var tileButton = FindTileContainer(_draggedTile!, _dragSourceItemsControl!);
        if (tileButton == null) return;

        _adornerLayer = AdornerLayer.GetAdornerLayer(RootBorder);
        if (_adornerLayer == null) return;

        var mouseInTile = e.GetPosition(tileButton);
        var mouseInRoot = e.GetPosition(RootBorder);

        _dragAdorner = new DragAdorner(RootBorder, tileButton, mouseInTile.X, mouseInTile.Y);
        _adornerLayer.Add(_dragAdorner);
        _dragAdorner.SetPosition(mouseInRoot.X, mouseInRoot.Y);

        _dragSourceElement = tileButton;
        _dragSourceElement.Opacity = 0.3;
    }

    private void RemoveDragAdorner()
    {
        if (_dragAdorner != null && _adornerLayer != null)
        {
            _adornerLayer.Remove(_dragAdorner);
            _dragAdorner = null;
            _adornerLayer = null;
        }
        if (_dragSourceElement != null)
        {
            _dragSourceElement.Opacity = 1.0;
            _dragSourceElement = null;
        }
    }

    // ── Visual tree helpers ───────────────────────────────────

    private static FrameworkElement? FindTileContainer(TileViewModel tile, ItemsControl ic)
    {
        var container = ic.ItemContainerGenerator.ContainerFromItem(tile);
        if (container is DependencyObject depObj)
            return FindVisualChild<System.Windows.Controls.Button>(depObj);
        return null;
    }

    private static TileViewModel? FindTileFromHit(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement fe && fe.DataContext is TileViewModel tile)
                return tile;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}

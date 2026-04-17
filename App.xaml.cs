using System.Drawing;
using System.Windows;
using Microsoft.Win32;
using WinXStart.Converters;
using WinXStart.Services;
using WinXStart.ViewModels;
using WinForms = System.Windows.Forms;

namespace WinXStart;

public partial class App : Application
{
    private HotkeyManager? _hotkeyManager;
    private MainWindow? _mainWindow;
    private WinForms.NotifyIcon? _trayIcon;
    private SettingsManager? _settingsManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load visual settings
        _settingsManager = new SettingsManager();
        _settingsManager.Load();

        // Create services
        var scanner = new AppScanner();
        var iconExtractor = new IconExtractor();
        var pinManager = new PinManager();

        // Wire up converters
        AppIconConverter.IconExtractor = iconExtractor;

        // Create ViewModel
        var viewModel = new MainViewModel(scanner, iconExtractor, pinManager);
        PinStateConverter.ViewModel = viewModel;

        // Create main window (starts hidden)
        _mainWindow = new MainWindow(viewModel, _settingsManager.Settings);

        // Setup global hotkey (Win+Alt+Z) via dedicated message-only window
        // so it works even while the main window is hidden.
        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.Toggled += () =>
        {
            Dispatcher.Invoke(() => _mainWindow.ShowMenu());
        };
        _hotkeyManager.Register();
        _mainWindow.Loaded += (_, _) => { /* HWND no longer needed for hotkey */ };

        // Setup system tray icon
        SetupTrayIcon();

        // First-launch notification
        _trayIcon?.ShowBalloonTip(
            3000,
            "WinX Start",
            "Press Win+Alt+Z to open the Start menu.\nRight-click this icon for options.",
            WinForms.ToolTipIcon.Info);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "WinX Start - Win+Alt+Z to open",
            Visible = true
        };

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("Open Start Menu", null, (_, _) =>
            Dispatcher.Invoke(() => _mainWindow?.ShowMenu()));
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("Refresh Apps", null, (_, _) =>
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow?.DataContext is MainViewModel vm)
                    vm.LoadApps();
            }));
        contextMenu.Items.Add("Open Settings", null, (_, _) =>
        {
            var path = SettingsManager.FilePath;
            if (System.IO.File.Exists(path))
                System.Diagnostics.Process.Start("notepad.exe", $"\"{path}\"");
        });

        var autoStartItem = new WinForms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = IsAutoStartEnabled()
        };
        autoStartItem.CheckedChanged += (_, _) => SetAutoStart(autoStartItem.Checked);
        contextMenu.Items.Add(autoStartItem);

        contextMenu.Items.Add("About", null, (_, _) =>
            Dispatcher.Invoke(() => ShowAbout()));
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) =>
            Dispatcher.Invoke(Shutdown));

        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (_, _) =>
            Dispatcher.Invoke(() => _mainWindow?.ShowMenu());
    }

    private const string AutoStartRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "WinXStart";

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegKey);
        return key?.GetValue(AutoStartValueName) != null;
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(AutoStartValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }

    private static void ShowAbout()
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/yhl10000/WinXStart",
                UseShellExecute = true
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnExit(e);
    }
}

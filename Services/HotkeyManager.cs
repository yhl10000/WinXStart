using System.Windows;
using System.Windows.Interop;
using WinXStart.Interop;

namespace WinXStart.Services;

/// <summary>
/// Registers Win+Alt+Z as a global hotkey.
/// Uses a dedicated hidden message-only window so WM_HOTKEY is received
/// regardless of whether the main window is visible or hidden.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x57584B; // "WXK"

    private HwndSource? _msgWindow;
    private bool _registered;

    public event Action? Toggled;

    public void Register()
    {
        // Create an invisible message-only window (HWND_MESSAGE parent)
        var p = new HwndSourceParameters("WinXStartHotkey")
        {
            WindowStyle = 0,             // WS_OVERLAPPED – invisible
            ExtendedWindowStyle = 0,
            PositionX = 0, PositionY = 0,
            Width = 0, Height = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _msgWindow = new HwndSource(p);
        _msgWindow.AddHook(WndProc);

        const uint VK_Z = 0x5A;
        _registered = NativeMethods.RegisterHotKey(
            _msgWindow.Handle,
            HotkeyId,
            NativeMethods.MOD_WIN | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            VK_Z);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Toggled?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered && _msgWindow != null)
        {
            NativeMethods.UnregisterHotKey(_msgWindow.Handle, HotkeyId);
            _registered = false;
        }
        _msgWindow?.Dispose();
        _msgWindow = null;
    }
}

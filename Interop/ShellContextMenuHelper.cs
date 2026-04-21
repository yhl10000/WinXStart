using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using WinXStart.ViewModels;
using WinForms = System.Windows.Forms;

namespace WinXStart.Interop;

/// <summary>
/// Shows a native Win32 shell context menu for a tile, with custom items prepended
/// above Explorer's standard shell verbs (Pin to taskbar, Run as admin, etc.).
/// </summary>
public static class ShellContextMenuHelper
{
    // ── Custom command IDs (1..999) ──────────────────────────
    private const int CMD_UNPIN = 1;
    private const int CMD_RESIZE_SMALL = 10;
    private const int CMD_RESIZE_MEDIUM = 11;
    private const int CMD_RESIZE_LARGE = 12;
    private const int CMD_MOVE_BASE = 100;   // 100 + groupIndex
    private const int CMD_NEW_GROUP = 250;
    private const int SHELL_CMD_FIRST = 1000;
    private const int SHELL_CMD_LAST = 0x7FFF;

    /// <summary>True while the native context menu is displayed. Check in Window.Deactivated to suppress hide.</summary>
    public static bool IsOpen { get; private set; }

    // ─────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────

    public static void ShowForTile(
        IntPtr hwndOwner,
        TileViewModel tile,
        Point screenPoint,
        IReadOnlyList<TileGroupViewModel> groups,
        MainViewModel mainVm)
    {
        IContextMenu? ctxMenu = null;
        IContextMenu2? ctxMenu2 = null;
        IContextMenu3? ctxMenu3 = null;
        IntPtr hMenu = IntPtr.Zero;
        ContextMenuMsgWindow? msgWnd = null;

        try
        {
            IsOpen = true;

            // 1. Obtain shell IContextMenu for the tile's app
            ctxMenu = CreateShellContextMenu(hwndOwner, tile);

            // QI for IContextMenu2/3 (owner-draw support for icons, Send To, etc.)
            if (ctxMenu != null)
            {
                try { ctxMenu2 = (IContextMenu2)ctxMenu; } catch (InvalidCastException) { }
                try { ctxMenu3 = (IContextMenu3)ctxMenu; } catch (InvalidCastException) { }
            }

            // 2. Build the popup menu
            hMenu = Win32.CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
                throw new InvalidOperationException("CreatePopupMenu failed");

            // ── Custom items ─────────────────────────────────
            Win32.AppendMenu(hMenu, Win32.MF_STRING, (IntPtr)CMD_UNPIN, "Unpin from Start");
            Win32.AppendMenu(hMenu, Win32.MF_SEPARATOR, IntPtr.Zero, null);

            // Resize submenu
            IntPtr hResizeSub = Win32.CreatePopupMenu();
            Win32.AppendMenu(hResizeSub, Win32.MF_STRING, (IntPtr)CMD_RESIZE_SMALL, "Small");
            Win32.AppendMenu(hResizeSub, Win32.MF_STRING, (IntPtr)CMD_RESIZE_MEDIUM, "Medium");
            Win32.AppendMenu(hResizeSub, Win32.MF_STRING, (IntPtr)CMD_RESIZE_LARGE, "Large");
            Win32.AppendMenu(hMenu, Win32.MF_STRING | Win32.MF_POPUP, hResizeSub, "Resize");

            // Move to group submenu
            IntPtr hMoveSub = Win32.CreatePopupMenu();
            var currentGroup = groups.FirstOrDefault(g => g.Tiles.Contains(tile));
            int groupIdx = 0;
            foreach (var group in groups)
            {
                if (group != currentGroup)
                {
                    Win32.AppendMenu(hMoveSub, Win32.MF_STRING,
                        (IntPtr)(CMD_MOVE_BASE + groupIdx), group.Name);
                }
                groupIdx++;
            }
            if (Win32.GetMenuItemCount(hMoveSub) > 0)
                Win32.AppendMenu(hMoveSub, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(hMoveSub, Win32.MF_STRING, (IntPtr)CMD_NEW_GROUP, "New group...");
            Win32.AppendMenu(hMenu, Win32.MF_STRING | Win32.MF_POPUP, hMoveSub, "Move to group");

            // Separator before shell verbs
            Win32.AppendMenu(hMenu, Win32.MF_SEPARATOR, IntPtr.Zero, null);

            // 3. Append shell context menu items
            if (ctxMenu != null)
            {
                uint flags = CMF_NORMAL | CMF_EXPLORE;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    flags |= CMF_EXTENDEDVERBS;

                uint itemCount = (uint)Win32.GetMenuItemCount(hMenu);
                ctxMenu.QueryContextMenu(hMenu, itemCount,
                    SHELL_CMD_FIRST, SHELL_CMD_LAST, flags);
            }

            // 4. Create message-forwarding window for owner-draw items
            msgWnd = new ContextMenuMsgWindow(ctxMenu2, ctxMenu3);
            msgWnd.CreateHandle(new WinForms.CreateParams
            {
                Parent = Win32.HWND_MESSAGE
            });

            // 5. Show the menu (blocking)
            int cmd = Win32.TrackPopupMenuEx(
                hMenu,
                Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON,
                (int)screenPoint.X, (int)screenPoint.Y,
                msgWnd.Handle, IntPtr.Zero);

            if (cmd == 0) return;   // cancelled or error

            // 6. Dispatch
            if (cmd >= SHELL_CMD_FIRST)
                InvokeShellCommand(ctxMenu!, hwndOwner, cmd - SHELL_CMD_FIRST);
            else
                DispatchCustomCommand(cmd, tile, groups, mainVm);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShellContextMenuHelper: {ex}");
            App.NotifyError($"Context menu error: {ex.Message}");
        }
        finally
        {
            IsOpen = false;
            msgWnd?.DestroyHandle();
            if (hMenu != IntPtr.Zero) Win32.DestroyMenu(hMenu);
            // submenus destroyed with parent

            // Release COM — ctxMenu2/3 are QI aliases on the same RCW
            if (ctxMenu != null)
            {
                try { Marshal.ReleaseComObject(ctxMenu); } catch { }
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Shell IContextMenu resolution
    // ─────────────────────────────────────────────────────────

    private static IContextMenu? CreateShellContextMenu(IntPtr hwnd, TileViewModel tile)
    {
        // Determine parsing name / file path
        string? parsingName;
        if (tile.AppInfo.IsStoreApp)
            parsingName = $"shell:AppsFolder\\{tile.AppInfo.AppUserModelId}";
        else
            parsingName = !string.IsNullOrEmpty(tile.AppInfo.ShortcutPath)
                ? tile.AppInfo.ShortcutPath
                : tile.AppInfo.TargetPath;

        if (string.IsNullOrEmpty(parsingName))
            return null;

        // SHCreateItemFromParsingName → IShellItem
        Guid iidShellItem = IID_IShellItem;
        int hr = Win32.SHCreateItemFromParsingName(parsingName, IntPtr.Zero,
            ref iidShellItem, out var shellItemObj);
        if (hr != 0 || shellItemObj == null)
        {
            Debug.WriteLine($"SHCreateItemFromParsingName failed: 0x{hr:X8} for '{parsingName}'");
            return null;
        }

        try
        {
            // Approach 1: PIDL-based (most reliable)
            hr = Win32.SHGetIDListFromObject(shellItemObj, out var pidl);
            if (hr != 0 || pidl == IntPtr.Zero)
                return null;

            try
            {
                Guid iidShellFolder = IID_IShellFolder;
                hr = Win32.SHBindToParent(pidl, ref iidShellFolder,
                    out var folderObj, out var childPidl);
                if (hr != 0 || folderObj == null)
                    return null;

                var folder = (IShellFolder)folderObj;
                Guid iidCtxMenu = IID_IContextMenu;
                uint reserved = 0;
                hr = folder.GetUIObjectOf(hwnd, 1, [childPidl],
                    ref iidCtxMenu, ref reserved, out var ctxObj);
                // Note: childPidl points INTO pidl — do not free separately.

                if (hr == 0 && ctxObj != null)
                    return (IContextMenu)ctxObj;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shellItemObj);
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    //  Command dispatch
    // ─────────────────────────────────────────────────────────

    private static void InvokeShellCommand(IContextMenu ctxMenu, IntPtr hwnd, int cmdOffset)
    {
        var ci = new CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = CMIC_MASK_UNICODE,
            hwnd = hwnd,
            lpVerb = (IntPtr)cmdOffset,
            lpVerbW = (IntPtr)cmdOffset,
            nShow = SW_SHOWNORMAL,
        };

        IntPtr ptr = Marshal.AllocHGlobal(ci.cbSize);
        try
        {
            Marshal.StructureToPtr(ci, ptr, false);
            ctxMenu.InvokeCommand(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void DispatchCustomCommand(
        int cmd, TileViewModel tile,
        IReadOnlyList<TileGroupViewModel> groups,
        MainViewModel mainVm)
    {
        switch (cmd)
        {
            case CMD_UNPIN:
                mainVm.UnpinCommand.Execute(tile);
                break;

            case CMD_RESIZE_SMALL:
                tile.ResizeSmallCommand.Execute(null);
                break;
            case CMD_RESIZE_MEDIUM:
                tile.ResizeMediumCommand.Execute(null);
                break;
            case CMD_RESIZE_LARGE:
                tile.ResizeLargeCommand.Execute(null);
                break;

            case CMD_NEW_GROUP:
                var name = PromptGroupName();
                if (!string.IsNullOrWhiteSpace(name))
                    mainVm.MoveTileToNewGroup(tile, name);
                break;

            default:
                // Move to existing group: CMD_MOVE_BASE + groupIndex
                if (cmd >= CMD_MOVE_BASE && cmd < CMD_NEW_GROUP)
                {
                    int idx = cmd - CMD_MOVE_BASE;
                    if (idx >= 0 && idx < groups.Count)
                        mainVm.MoveTileToGroup(tile, groups[idx]);
                }
                break;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  "New group…" dialog (ported from MainWindow.xaml.cs)
    // ─────────────────────────────────────────────────────────

    internal static string? PromptGroupName()
    {
        var dlg = new Window
        {
            Title = "New Group Name",
            Width = 320, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x28, 0x28, 0x28))
        };
        var tb = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(16, 16, 16, 8),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 13
        };
        var ok = new System.Windows.Controls.Button
        {
            Content = "OK", Width = 70, Height = 26,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 16, 12)
        };
        ok.Click += (_, _) => dlg.DialogResult = true;
        tb.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) dlg.DialogResult = true;
        };
        var sp = new System.Windows.Controls.StackPanel();
        sp.Children.Add(tb);
        sp.Children.Add(ok);
        dlg.Content = sp;
        dlg.Loaded += (_, _) => { tb.Focus(); };
        return dlg.ShowDialog() == true ? tb.Text.Trim() : null;
    }

    // ─────────────────────────────────────────────────────────
    //  Message-forwarding window (owner-draw items, Send To…)
    // ─────────────────────────────────────────────────────────

    private sealed class ContextMenuMsgWindow : WinForms.NativeWindow
    {
        private readonly IContextMenu2? _cm2;
        private readonly IContextMenu3? _cm3;

        public ContextMenuMsgWindow(IContextMenu2? cm2, IContextMenu3? cm3)
        {
            _cm2 = cm2;
            _cm3 = cm3;
        }

        protected override void WndProc(ref WinForms.Message m)
        {
            // WM_MENUCHAR → IContextMenu3
            if (_cm3 != null && m.Msg == WM_MENUCHAR)
            {
                if (_cm3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam,
                        out var result) == 0)
                {
                    m.Result = result;
                    return;
                }
            }

            // WM_INITMENUPOPUP / WM_DRAWITEM / WM_MEASUREITEM → IContextMenu2
            if (_cm2 != null)
            {
                switch (m.Msg)
                {
                    case WM_INITMENUPOPUP:
                    case WM_DRAWITEM:
                    case WM_MEASUREITEM:
                        if (_cm2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0)
                        {
                            m.Result = IntPtr.Zero;
                            return;
                        }
                        break;
                }
            }

            base.WndProc(ref m);
        }

        private const int WM_INITMENUPOPUP = 0x0117;
        private const int WM_DRAWITEM      = 0x002B;
        private const int WM_MEASUREITEM   = 0x002C;
        private const int WM_MENUCHAR      = 0x0120;
    }

    // ─────────────────────────────────────────────────────────
    //  COM interface definitions (raw, no Vanara type deps)
    // ─────────────────────────────────────────────────────────

    private static readonly Guid IID_IShellItem =
        new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IShellFolder =
        new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu =
        new("000214e4-0000-0000-c000-000000000046");

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int GetAttributesOf(uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref Guid riid, ref uint rgfReserved,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu,
            int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(IntPtr pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType,
            IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2 : IContextMenu
    {
        [PreserveSig] new int QueryContextMenu(IntPtr hmenu, uint indexMenu,
            int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] new int InvokeCommand(IntPtr pici);
        [PreserveSig] new int GetCommandString(UIntPtr idCmd, uint uType,
            IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig] new int QueryContextMenu(IntPtr hmenu, uint indexMenu,
            int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] new int InvokeCommand(IntPtr pici);
        [PreserveSig] new int GetCommandString(UIntPtr idCmd, uint uType,
            IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] new int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam,
            out IntPtr plResult);
    }

    // ── CMINVOKECOMMANDINFOEX ────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public int ptInvokeX;
        public int ptInvokeY;
    }

    private const int CMIC_MASK_UNICODE = 0x00004000;
    private const int SW_SHOWNORMAL = 1;

    // ── CMF flags ────────────────────────────────────────────
    private const uint CMF_NORMAL        = 0x00000000;
    private const uint CMF_EXPLORE       = 0x00000004;
    private const uint CMF_EXTENDEDVERBS = 0x00000100;

    // ── P/Invoke ─────────────────────────────────────────────

    private static class Win32
    {
        public static readonly IntPtr HWND_MESSAGE = new(-3);

        // Shell32
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(
            string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("shell32.dll", PreserveSig = true)]
        public static extern int SHGetIDListFromObject(
            [MarshalAs(UnmanagedType.Interface)] object punk, out IntPtr ppidl);

        [DllImport("shell32.dll", PreserveSig = true)]
        public static extern int SHBindToParent(
            IntPtr pidl, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv,
            out IntPtr ppidlLast);

        // User32
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AppendMenu(IntPtr hMenu, uint uFlags,
            IntPtr uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        public static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags,
            int x, int y, IntPtr hWnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyMenu(IntPtr hMenu);

        // Menu constants
        public const uint MF_STRING    = 0x00000000;
        public const uint MF_SEPARATOR = 0x00000800;
        public const uint MF_POPUP     = 0x00000010;
        public const uint MF_GRAYED    = 0x00000001;

        public const uint TPM_RETURNCMD  = 0x0100;
        public const uint TPM_RIGHTBUTTON = 0x0002;
    }
}

using System;
using System.Runtime.InteropServices;

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// Raw Win32 interop declarations for the tray icon (§8), the non-activating
/// popup style (§6.3), monitor work areas (§9) and the tray context menu
/// (§10.2). Free of WPF types so it stays a thin, auditable boundary.
/// UNITS (§9.1/§14, build #12): every RECT/POINT/MONITORINFO value here is
/// PHYSICAL px in Win32 screen space — never WPF DIP. The one px→DIP conversion
/// in the application is PxToDip, below; its call sites (complete list):
/// MainWindow.PositionAboveIcon. Every other coordinate path in the app stays
/// physical px end-to-end (cursor→menu, hook-pt↔popup-rect) and must NOT be
/// converted — see the §9.1 audit table.
/// </summary>
internal static class TrayInterop
{
    // ------------------------------------------------------------------
    // shellapi.h — Shell_NotifyIcon / icon rectangle
    // ------------------------------------------------------------------

    public const int NIM_ADD = 0x00000000;
    public const int NIM_MODIFY = 0x00000001;
    public const int NIM_DELETE = 0x00000002;
    public const int NIM_SETFOCUS = 0x00000003;
    public const int NIM_SETVERSION = 0x00000004;

    /// <summary>
    /// NOTIFYICON_VERSION_3 semantics: the callback message carries the icon id
    /// in wParam and the notification event code (WM_LBUTTONUP, ...) in lParam.
    /// Deliberately chosen over VERSION_4 to keep message handling classic.
    /// </summary>
    public const uint NOTIFYICON_VERSION_3 = 3;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;

    /// <summary>Custom callback message the shell posts to the helper window (WM_APP + 1).</summary>
    public const int WM_TRAYICON = 0x8001;

    // Notification events delivered in lParam (version 3 semantics).
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CONTEXTMENU = 0x007B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion; // union with uTimeout
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        // guidItem / hBalloonIcon (Vista+) intentionally omitted; cbSize
        // reflects this smaller, backward-compatible layout.
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    /// <summary>Physical px in Win32 screen space — never DIP (§9.1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public bool Contains(int x, int y) =>
            x >= Left && x < Right && y >= Top && y < Bottom;
    }

    /// <summary>Physical px in Win32 screen space — never DIP (§9.1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // ------------------------------------------------------------------
    // Units policy (§9.1/§14) — THE px→DIP conversion, build #12
    // ------------------------------------------------------------------
    // Above this boundary the app is DIP-canonical; inside Win32 it is physical
    // px. WPF performs the DIP→px render transform itself (per window), so only
    // the px→DIP direction exists in app code, and it exists exactly here.

    /// <summary>DIPs per inch — the WPF reference DPI (1 DIP = 1 physical px at 100%).</summary>
    public const double DipsPerInch = 96.0;

    /// <summary>
    /// Effective DPI of the monitor the given window currently occupies
    /// (user32, Windows 10 1607+). Returns 0 on failure/invalid handle.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// Acquires the DPI PxToDip must use for Window.Left/Top consumption: the
    /// popup window's OWN effective DPI — the exact factor WPF inverts when it
    /// renders Left/Top back to physical px (§9 write-factor invariant). Never
    /// guesses 96 on failure: returns false so the caller degrades to the §12
    /// pure-DIP fallback instead of silently misplacing the popup.
    /// </summary>
    public static bool TryGetWindowDpi(IntPtr hwnd, out uint dpi)
    {
        dpi = GetDpiForWindow(hwnd);
        return dpi != 0;
    }

    /// <summary>
    /// THE px→DIP conversion (§9.1/§14) — the only one in the application.
    /// <paramref name="dpi"/> must be the effective DPI of the coordinate space
    /// in which the result is consumed: for Window.Left/Top that is the popup's
    /// own window DPI (TryGetWindowDpi), NOT the icon monitor's —
    /// GetDpiForMonitor-style monitor DPI is the factor for px-native consumers
    /// (SetWindowPos/MoveWindow) and would land the popup off by the monitor
    /// ratio when the popup's current monitor differs from the icon's (§9).
    /// Throws on dpi == 0: a programming error (acquire via TryGetWindowDpi
    /// first), never a runtime condition.
    /// </summary>
    public static double PxToDip(int px, uint dpi)
    {
        if (dpi == 0)
            throw new ArgumentOutOfRangeException(nameof(dpi),
                "DPI not acquired; refusing to guess (a guessed 96 silently misplaces the popup).");
        return px * DipsPerInch / dpi;
    }

    // Charset note: NOTIFYICONDATA's inline strings marshal as UTF-16 per the
    // struct's charset, so this import must bind Shell_NotifyIconW (below);
    // with the default (Ansi) CharSet the runtime binds Shell_NotifyIconA,
    // which reads the UTF-16 bytes as "W\0" — a tooltip truncated to its first
    // letter — and misaligns every field after szTip (App. B B1).

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    /// <summary>Returns the icon's screen rectangle in physical px. Returns S_OK (0) on success.</summary>
    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    // ------------------------------------------------------------------
    // user32.dll — icon, window styles, monitor, cursor
    // ------------------------------------------------------------------

    /// <summary>Shared icon handle for IDI_APPLICATION (same icon as SystemIcons.Application).</summary>
    public static readonly IntPtr IDI_APPLICATION = new(32512);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_NOACTIVATE = 0x08000000L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const uint WS_POPUP = 0x80000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    public static long GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex).ToInt64()
            : GetWindowLong32(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());

    /// <summary>Adds WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW (§6.3) to the extended style.</summary>
    public static void ApplyNoActivateToolWindowStyle(IntPtr hWnd)
    {
        long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, (IntPtr)(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>rcMonitor/rcWork: physical px in Win32 screen space — never DIP (§9.1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>rect: physical px — selects the monitor containing it (§9).</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromRect(ref RECT rect, uint dwFlags);

    /// <summary>pt: physical px (this app passes (0,0) only to reach the primary).</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>lpRect: physical px — pairs 1:1 with hook points (§9.1: px↔px, no conversion).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>lpPoint: physical px — feeds TrackPopupMenu directly (§9.1: no conversion).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>X/Y/cx/cy: physical px (this app passes none: SWP_NOMOVE | SWP_NOSIZE).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // ------------------------------------------------------------------
    // user32.dll — tray context menu (§10.2 right-click → Exit)
    // ------------------------------------------------------------------

    public const uint MF_STRING = 0x00000000;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RIGHTALIGN = 0x0008;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;
    /// <summary>Win7+: clamps the menu into the monitor work area even when the
    /// anchor point itself lies outside it (e.g. cursor on a taskbar strip).
    /// Supported by both TrackPopupMenu and TrackPopupMenuEx (MSDN).</summary>
    public const uint TPM_WORKAREA = 0x00010000;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const int IDM_EXIT = 1;
    public const int WM_NULL = 0x0000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    /// <summary>Shows the menu; x/y are physical px (pair with GetCursorPos — §9.1:
    /// native↔native pass-through, never converted). Returns the chosen command id, or 0 if dismissed.</summary>
    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(
        IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ------------------------------------------------------------------
    // user32.dll — low-level input hooks for popup dismissal (§10.2).
    // The popup is WS_EX_NOACTIVATE, so it can never rely on focus-loss
    // events; global low-level hooks observe (but never consume) mouse and
    // keyboard input while the popup is open.
    // ------------------------------------------------------------------

    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_KEYDOWN = 0x0100;
    public const uint VK_ESCAPE = 0x1B;

    public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>pt: physical px in Win32 screen space ("per-monitor-aware screen
    /// coordinates") — pairs 1:1 with GetWindowRect px in the containment
    /// compare (§9.1: px↔px, never converted; valid only under PMv2, §14).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
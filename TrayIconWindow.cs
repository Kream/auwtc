using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static ActuallyUsableWorldTimeClock2.TrayInterop; // fully qualified: a top-level using resolves in the global namespace

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// Hidden helper window that owns the tray icon (design doc §6.3, §8).
/// Raises LeftClick/RightClick and exposes TryGetIconRect() for popup
/// anchoring (§9). The window is never shown; only its HWND matters.
/// </summary>
internal sealed class TrayIconWindow : IDisposable
{
    private const uint IconId = 1;

    private readonly Window _window;
    private HwndSource? _source;
    private NOTIFYICONDATA _nid;
    private bool _iconAdded;

    /// <summary>Our tray HICON (GDI+-created PNG rung, or the shared IDI_APPLICATION fallback).</summary>
    private IntPtr _hIcon = IntPtr.Zero;

    /// <summary>True only when _hIcon was created by us (Bitmap.GetHicon) and must be DestroyIcon'd in Dispose; LoadIcon handles are shared and must NEVER be destroyed.</summary>
    private bool _ownsIcon;

    /// <summary>Raised on left-click release on the icon (toggle trigger, §10.1/§10.2).</summary>
    public event Action? LeftClick;

    /// <summary>Raised on right-click release on the icon (context menu trigger, §10.2).</summary>
    public event Action? RightClick;

    public TrayIconWindow()
    {
        _window = new Window
        {
            Width = 0,
            Height = 0,
            ShowInTaskbar = false,
        };

        WindowInteropHelper helper = new(_window);
        helper.EnsureHandle(); // create the HWND without showing the window
        _source = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("HwndSource missing after EnsureHandle.");
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Adds the tray icon (§7.1 build #15: DPI-rung PNG from the embedded asset
    /// set; IDI_APPLICATION stays as the §12 fallback). Returns false on
    /// failure (§12: caller shows a message box and exits — the app is
    /// useless without the icon).
    /// </summary>
    public bool CreateIcon()
    {
        // §7.1 (v4.5): pick the PNG rung for THIS helper window's DPI — a
        // physical-px decision (§9.1: tray metrics, never DIP). Unknown DPI
        // (TryGetWindowDpi false ⇒ 0) resolves to the base 16-px rung inside
        // TrayIconAssets.
        _ = TryGetWindowDpi(_source!.Handle, out uint dpi); // 0 on failure → handled inside
        if (TrayIconAssets.TryCreateIconForDpi(dpi, out _hIcon))
        {
            _ownsIcon = true; // GDI+-created: ours to DestroyIcon in Dispose
        }
        else
        {
            // §12 fallback: shared system icon (must NOT be destroyed).
            _hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
            _ownsIcon = false;
        }

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = IconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = (uint)WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "World Time Zone Clock",
        };

        _iconAdded = Shell_NotifyIcon(NIM_ADD, ref _nid);
        if (!_iconAdded)
            return false;

        // Version 3: lParam = event code (WM_LBUTTONUP...), wParam = icon id.
        NOTIFYICONDATA versionData = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _nid.hWnd,
            uID = IconId,
            uVersion = NOTIFYICON_VERSION_3,
        };
        Shell_NotifyIcon(NIM_SETVERSION, ref versionData);
        return true;
    }

    /// <summary>
    /// Icon screen rectangle for popup anchoring (§8/§9).
    /// Returns false on failure → caller uses the §12 fallback position.
    /// </summary>
    public bool TryGetIconRect(out RECT rect)
    {
        rect = default;
        if (!_iconAdded || _source is null)
            return false;

        NOTIFYICONIDENTIFIER id = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _source.Handle,
            uID = IconId,
        };
        return Shell_NotifyIconGetRect(ref id, out rect) == 0; // S_OK
    }

    /// <summary>
    /// Right-click → "Exit" menu at the cursor (§10.2).
    /// Returns true if the user chose Exit.
    /// </summary>
    public bool ShowContextMenu()
    {
        if (!_iconAdded || _source is null)
            return false;

        // Two conditions must BOTH hold when TrackPopupMenu creates the menu
        // window (Raymond Chen, "Why do my context menus render underneath
        // the taskbar?"):
        //   1. The helper must be in the TOPMOST band — the menu inherits the
        //      z-band of the foreground window, and only a topmost owner puts
        //      the menu above the always-topmost taskbar. SetForegroundWindow
        //      alone is NOT enough: our helper lives in the normal band, so
        //      the menu materialized UNDER the taskbar (build #6 smoke).
        //   2. The helper must actually be foreground — required for the menu
        //      to dismiss on an outside click, and because the menu inherits
        //      the band of the FOREGROUND window: a band lift alone is NOT
        //      enough — with SWP_NOACTIVATE the helper was never activated, so
        //      the foreground window stayed whatever app the user was in and
        //      the menu still materialized in the lower band (round-3 smoke).
        // Changing bands is not an activation, so the order is: lift first,
        // activate second — the final state is a topmost foreground helper.
        //
        // Band+foreground alone were falsified on Win10 22H2 / vertical
        // right-edge taskbar (builds #6/#7 smokes): the menu still opened
        // UNDER the taskbar strip. Structural cause: with no horizontal
        // alignment flag the menu pins its LEFT edge to cursor.x and grows
        // rightward, and the default "restrict to work area" rule only
        // engages when the anchor point is INSIDE the work area — the cursor
        // sits on the taskbar strip, which is OUTSIDE it. TPM_WORKAREA forces
        // the clamp unconditionally, so the menu is pulled fully inside the
        // work area regardless of where the anchor landed.
        SetWindowPos(_source.Handle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetForegroundWindow(_source.Handle);
        // §9.1 (v4.3): GetCursorPos and TrackPopupMenu both speak physical px —
        // native↔native pass-through, NO DIP conversion on this path.
        GetCursorPos(out POINT p);

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return false;
        AppendMenu(menu, MF_STRING, (IntPtr)IDM_EXIT, "Exit");

        int cmd = TrackPopupMenu(
            menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_RETURNCMD | TPM_NONOTIFY | TPM_WORKAREA,
            p.X, p.Y, 0, _source.Handle, IntPtr.Zero);

        PostMessage(_source.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero); // dismissal fix (KB135788)
        DestroyMenu(menu);

        if (cmd == 0)
        {
            // Menu dismissed (e.g. ESC): return focus to the notification area,
            // per the Shell_NotifyIcon docs (NIM_SETFOCUS).
            NOTIFYICONDATA focusData = new()
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _nid.hWnd,
                uID = IconId,
            };
            Shell_NotifyIcon(NIM_SETFOCUS, ref focusData);
        }

        return cmd == IDM_EXIT;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_TRAYICON)
            return IntPtr.Zero;

        switch (lParam.ToInt32())
        {
            case WM_LBUTTONUP:
                LeftClick?.Invoke();
                break;
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU: // some shell versions deliver this instead
                RightClick?.Invoke();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_iconAdded)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid); // §8: destroy on exit
            _iconAdded = false;
        }
        if (_ownsIcon && _hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon); // build #15: our GDI+ HICON; never the LoadIcon shared fallback
            _hIcon = IntPtr.Zero;
            _ownsIcon = false;
        }
        _source?.RemoveHook(WndProc);
        _source = null; // window/HWND dies with the process
    }
}
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static ActuallyUsableWorldTimeClock2.TrayInterop; // fully qualified: a top-level using resolves in the global namespace

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// Dismisses the popup on Esc and outside clicks (§10.2). The popup is
/// WS_EX_NOACTIVATE and never takes focus, so WPF focus/activation events
/// cannot supply these signals; WH_MOUSE_LL and WH_KEYBOARD_LL observe all
/// input without consuming any of it. Dismissal is marshaled to the UI
/// dispatcher, and the hooks are unhooked outside the hook callback.
/// </summary>
internal sealed class PopupDismissalWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LowLevelHookProc _mouseProc;
    private readonly LowLevelHookProc _keyProc;

    private IntPtr _mouseHook;
    private IntPtr _keyHook;
    private RECT _popupRect;
    private Action? _onDismiss;

    public PopupDismissalWatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _mouseProc = MouseHook;
        _keyProc = KeyboardHook;
    }

    /// <summary>Starts watching while the popup is open. UI thread, after Show().</summary>
    public void Start(Window popup, Action onDismiss)
    {
        IntPtr hwnd = new WindowInteropHelper(popup).Handle;
        GetWindowRect(hwnd, out _popupRect); // physical pixels — same space as the hook's points
        _onDismiss = onDismiss;

        IntPtr hMod = Marshal.GetHINSTANCE(typeof(PopupDismissalWatcher).Module);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, hMod, 0);
        // A failed hook degrades gracefully: the popup still closes via the tray
        // toggle, just without Esc/outside-click dismissal for that open.
    }

    public void Stop()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keyHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyHook);
            _keyHook = IntPtr.Zero;
        }
        _onDismiss = null;
    }

    /// <summary>Schedules dismissal on the UI thread; never unhooks from inside a hook callback.</summary>
    private void ScheduleDismiss()
    {
        Action? dismiss = _onDismiss;
        if (dismiss != null)
            _dispatcher.InvokeAsync(dismiss);
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                MSLLHOOKSTRUCT info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (!_popupRect.Contains(info.pt.X, info.pt.Y))
                    ScheduleDismiss(); // any click outside the popup, incl. taskbar/desktop/other apps
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_KEYDOWN)
        {
            KBDLLHOOKSTRUCT info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == VK_ESCAPE)
                ScheduleDismiss(); // §10.2: Esc closes; the key is passed through untouched
        }
        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static ActuallyUsableWorldTimeClock2.TrayInterop; // fully qualified: a top-level using resolves in the global namespace

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// The borderless popup (§6.3, §7.2): 180×100 DIP, topmost, never activating
/// (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW applied in OnSourceInitialized) so it
/// never steals keyboard focus from the active application. Placement follows
/// §9 (v4.3): DIP is canonical; the ONE px→DIP conversion is
/// TrayInterop.PxToDip at the interop boundary, fed with the popup's OWN
/// window DPI (§9.1). Dismissal (Esc / outside click) is PopupDismissalWatcher's
/// (px↔px end-to-end, no DIPs).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>§9 (v4.3): popup bottom edge sits this far above the icon's top edge. Unit: DIP (constant visual gap at any scale).</summary>
    private const double AnchorGap = 8; // DIP

    public MainWindow()
    {
        InitializeComponent();
        // Create the HWND up front (without showing) so the no-activate style is
        // applied (OnSourceInitialized) before the first Show(), and so the
        // window DPI is acquirable (TryGetWindowDpi) before Show().
        new WindowInteropHelper(this).EnsureHandle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // §6.3: Topmost alone does not provide WS_EX_NOACTIVATE.
        ApplyNoActivateToolWindowStyle(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// §9 (v4.3): anchored above the tray icon. The shell reports the icon rect
    /// (§8) and the work area in PHYSICAL px; both are converted ONCE, here, via
    /// PxToDip with the POPUP WINDOW's own effective DPI — the same factor WPF
    /// inverts when it renders Window.Left/Top back to physical px (the
    /// write-factor invariant, §9.1). Every value below the conversion is DIP.
    /// </summary>
    internal void PositionAboveIcon(RECT iconRect) // physical px (§8)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;

        // §9.1: the conversion factor. Never a guessed 96 (that silently
        // misplaces the popup) — degrade to the §12 pure-DIP fallback instead.
        if (!TryGetWindowDpi(handle, out uint dpi))
        {
            PositionAtBottomRight();
            return;
        }

        // Work area (physical px) of the monitor containing the ICON — the
        // §9 multi-monitor rule; SystemParameters.WorkArea is primary-only.
        // Monitor selection must see the rect in its native unit (px), before
        // any conversion (§9.1 ordering invariant).
        IntPtr monitor = MonitorFromRect(ref iconRect, MONITOR_DEFAULTTONEAREST);
        MONITORINFO info = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            PositionAtBottomRight();
            return;
        }

        // === the entire px→DIP boundary (§9.1); everything below is DIP ===
        double iconLeft = PxToDip(iconRect.Left, dpi);
        double iconTop = PxToDip(iconRect.Top, dpi);
        double iconRight = PxToDip(iconRect.Right, dpi);
        double workLeft = PxToDip(info.rcWork.Left, dpi);
        double workTop = PxToDip(info.rcWork.Top, dpi);
        double workRight = PxToDip(info.rcWork.Right, dpi);
        double workBottom = PxToDip(info.rcWork.Bottom, dpi);
        // ===================================================================

        // Width/Height are the XAML DIP values (180×100, §7.2) — valid because
        // ResizeMode=NoResize and no SizeToContent (§9 invariant).
        //
        // §9 formulas (v4.3 — every term DIP):
        //   iconCenterX = icon.Left + icon.Width / 2                                  [DIP]
        //   Left = clamp(iconCenterX − Width / 2,      work.Left,  work.Right − Width)     [DIP]
        //   Top  = clamp(icon.Top − Height − AnchorGap, work.Top,  work.Bottom − Height)  [DIP]
        double iconCenterX = (iconLeft + iconRight) / 2;
        Left = Clamp(iconCenterX - Width / 2, workLeft, workRight - Width);
        Top = Clamp(iconTop - Height - AnchorGap, workTop, workBottom - Height);
    }

    /// <summary>
    /// §12 fallback (v4.3): bottom-right of the PRIMARY monitor's working area,
    /// AnchorGap (8 DIP) margins. SystemParameters.WorkArea is already WPF DIP
    /// and primary-monitor-only by construction — exactly §12's semantics — so
    /// this path performs NO px→DIP conversion. Do not add one (§9.1 audit
    /// table). Invariant: the popup's effective DPI equals the primary
    /// monitor's on this path.
    /// </summary>
    public void PositionAtBottomRight()
    {
        Rect work = SystemParameters.WorkArea; // DIP (WPF-native; re-read per call)
        Left = work.Right - Width - AnchorGap; // DIP − DIP − DIP
        Top = work.Bottom - Height - AnchorGap;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
}
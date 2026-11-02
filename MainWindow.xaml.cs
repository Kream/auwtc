using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static ActuallyUsableWorldTimeClock2.TrayInterop; // fully qualified: a top-level using resolves in the global namespace

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// The borderless popup (§6.3, §7.2): 180×100, topmost, never activating
/// (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW applied in OnSourceInitialized) so it
/// never steals keyboard focus from the active application. Placement follows
/// §9 (anchored above the tray icon) with the §12 bottom-right fallback.
/// Dismissal (Esc / outside click) is provided by PopupDismissalWatcher, since
/// a no-activate window receives no focus or activation events itself.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>§9: the popup's bottom edge sits this far above the icon's top edge.</summary>
    private const double AnchorGap = 8;

    public MainWindow()
    {
        InitializeComponent();
        // Create the HWND up front (without showing) so the DPI transform used
        // for positioning exists before the first Show().
        new WindowInteropHelper(this).EnsureHandle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // §6.3: Topmost alone does not provide WS_EX_NOACTIVATE.
        ApplyNoActivateToolWindowStyle(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// §9: horizontally centered on the icon, bottom edge 8 px above the icon's
    /// top edge, clamped to the working area of the icon's monitor.
    /// The shell reports physical pixels while WPF uses device-independent
    /// pixels, so the icon rect and work area are converted via the DPI
    /// transform before the formulas are applied.
    /// </summary>
    internal void PositionAboveIcon(RECT iconRect)
    {
        // A never-yet-shown window (OpenPopup positions BEFORE Show()) has no
        // PresentationSource — PresentationSource.FromVisual(this) is null and
        // was the crash at line 47. The HWND was created eagerly in the ctor
        // (EnsureHandle), so HwndSource.FromHwnd(handle) always resolves;
        // its CompositionTarget carries the same DPI transform.
        Matrix fromDevice = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)
                                ?.CompositionTarget?.TransformFromDevice
                            ?? Matrix.Identity;

        Point iconTopLeft = fromDevice.Transform(new Point(iconRect.Left, iconRect.Top));
        Point iconBottomRight = fromDevice.Transform(new Point(iconRect.Right, iconRect.Bottom));

        IntPtr monitor = MonitorFromRect(ref iconRect, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        Point workTopLeft = fromDevice.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
        Point workBottomRight = fromDevice.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));

        // §9 clamp formulas:
        //   iconCenterX = iconRect.Left + iconRect.Width / 2
        //   popup.Left  = clamp(iconCenterX - popup.Width / 2, work.Left, work.Right - popup.Width)
        //   popup.Top   = clamp(iconRect.Top - popup.Height - 8, work.Top, work.Bottom - popup.Height)
        double iconCenterX = (iconTopLeft.X + iconBottomRight.X) / 2;
        Left = Clamp(iconCenterX - Width / 2, workTopLeft.X, workBottomRight.X - Width);
        Top = Clamp(iconTopLeft.Y - Height - AnchorGap, workTopLeft.Y, workBottomRight.Y - Height);
    }

    /// <summary>§12 fallback: bottom-right of the primary monitor's work area, 8 px margin.</summary>
    public void PositionAtBottomRight()
    {
        Rect work = SystemParameters.WorkArea;
        Left = work.Right - Width - AnchorGap;
        Top = work.Bottom - Height - AnchorGap;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
}

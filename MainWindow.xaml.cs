using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static ActuallyUsableWorldTimeClock2.TrayInterop; // fully qualified: a top-level using resolves in the global namespace

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// The borderless popup (§6.3, §7.2): 180-DIP manual width, content-sized
/// height (SizeToContent="Height", build #13), topmost, never activating
/// (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW applied in OnSourceInitialized) so it
/// never steals keyboard focus from the active application. Placement follows
/// §9 (v4.4): DIP is canonical; the ONE px→DIP conversion is
/// TrayInterop.PxToDip at the interop boundary, fed with the popup's OWN
/// window DPI (§9.1); the clamp terms use the MEASURED placement size
/// (GetPlacementSizeDip — never the nominal constants). Dismissal (Esc /
/// outside click) is PopupDismissalWatcher's (px↔px end-to-end, no DIPs).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>§9 (v4.3): popup bottom edge sits this far above the icon's top edge. Unit: DIP (constant visual gap at any scale).</summary>
    private const double AnchorGap = 8; // DIP

    /// <summary>
    /// §7.2 (v4.4): defensive floor for a degenerate (empty-Rows) measure —
    /// ≈ one text row. Rows is never empty in this build (Refresh always fills
    /// the city list), so this only guards a hypothetical future data path.
    /// Kept in code (not XAML MinHeight) so it cannot interact with the
    /// SizeToContent sizing machinery.
    /// </summary>
    private const double MinPlacementHeightDip = 24;

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
    /// §9 (v4.4): the size the popup will render at, in DIP — measured FRESH
    /// on every call (called post-Refresh, pre-Show/position), never the
    /// nominal constants. Width is manual (SizeToContent="Height" never sizes
    /// width), so Width is the authoritative X term (the pre-layout
    /// ActualWidth is 0 and is never used). Height: force one layout pass
    /// (UpdateLayout — Rows was rebuilt by Refresh() since the last layout, so
    /// the cached Actual* would be stale) and read the content-sized
    /// ActualHeight; if that yields nothing usable (the never-laid-out
    /// anomaly), fall back to a manual content Measure with the SAME
    /// constraint Window.MeasureCore applies under SizeToContent="Height" —
    /// no non-client chrome exists (WindowStyle=None + AllowsTransparency),
    /// so the fallback provably equals the height Show() applies. Both paths
    /// are DIP and DPI-independent (§14): measurement never crosses the
    /// px↔DIP boundary, which stays exactly PxToDip (build #12 policy intact).
    /// </summary>
    internal Size GetPlacementSizeDip()
    {
        UpdateLayout(); // measure+arrange NOW, hidden — Rows is settled post-Refresh

        double width = Width; // manual → authoritative; never NaN (set in XAML)
        double height = ActualHeight;

        if (height <= 0 || double.IsNaN(height) || double.IsInfinity(height))
        {
            // Defensive: manual content measure == the Window's own SizeToContent
            // constraint (infinite height at the manual width, no chrome).
            if (Content is UIElement content)
            {
                content.Measure(new Size(width, double.PositiveInfinity));
                height = content.DesiredSize.Height;
            }
        }

        return new Size(width, Math.Max(height, MinPlacementHeightDip));
    }

    /// <summary>
    /// §9 (v4.4): anchored above the tray icon. The shell reports the icon rect
    /// (§8) and the work area in PHYSICAL px; both are converted ONCE, here, via
    /// PxToDip with the POPUP WINDOW's own effective DPI — the same factor WPF
    /// inverts when it renders Window.Left/Top back to physical px (the
    /// write-factor invariant, §9.1). Every value below the conversion is DIP;
    /// the clamp terms use the measured placement size (GetPlacementSizeDip).
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

        // §9 measured-size invariant (v4.4): the clamp size and the size
        // Show() renders agree by construction — GetPlacementSizeDip measures
        // the same settled content under the same constraint SizeToContent
        // applies (no chrome), so popup.Width/Height below are the real
        // rendered size, not nominal constants.
        Size placement = GetPlacementSizeDip();

        // §9 formulas (v4.4 — every term DIP; popup size = measured):
        //   iconCenterX = icon.Left + icon.Width / 2                                        [DIP]
        //   Left = clamp(iconCenterX − popup.Width / 2,      work.Left,  work.Right − popup.Width)   [DIP]
        //   Top  = clamp(icon.Top − popup.Height − AnchorGap, work.Top,  work.Bottom − popup.Height) [DIP]
        double iconCenterX = (iconLeft + iconRight) / 2;
        Left = Clamp(iconCenterX - placement.Width / 2, workLeft, workRight - placement.Width);
        Top = Clamp(iconTop - placement.Height - AnchorGap, workTop, workBottom - placement.Height);
    }

    /// <summary>
    /// §12 fallback (v4.4): bottom-right of the PRIMARY monitor's working area,
    /// AnchorGap (8 DIP) margins. SystemParameters.WorkArea is already WPF DIP
    /// and primary-monitor-only by construction — exactly §12's semantics — so
    /// this path performs NO px→DIP conversion. Do not add one (§9.1 audit
    /// table). Uses the same measured placement size as the anchored path
    /// (never the nominal Height, which is unset since build #13 — reading it
    /// would compute NaN Top). Invariant: the popup's effective DPI equals the
    /// primary monitor's on this path.
    /// </summary>
    public void PositionAtBottomRight()
    {
        Size placement = GetPlacementSizeDip();
        Rect work = SystemParameters.WorkArea; // DIP (WPF-native; re-read per call)
        Left = work.Right - placement.Width - AnchorGap; // DIP − DIP − DIP
        Top = work.Bottom - placement.Height - AnchorGap;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
}
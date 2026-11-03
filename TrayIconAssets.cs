using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// Tray-icon asset loading (design doc §7.1, build #15): the DPI-rung PNG set
/// (assets/tray_NN.png) embedded as assembly resources → HICON for
/// NOTIFYICONDATA.hIcon. Embedded (not loose Content) so the single-file
/// self-contained publish carries them inside the bundle.
/// UNITS (§7.1/§9.1): rung selection is a PHYSICAL-px decision — tray metrics
/// scale with the monitor DPI (16 px @ 96 DPI → 64 px @ 384 DPI); it is never a
/// DIP quantity, and this class performs no px↔DIP conversion (PxToDip stays
/// the app's only conversion).
/// </summary>
internal static class TrayIconAssets
{
  /// <summary>Base tray metric (physical px @ 96 DPI = 100%): the shell's small-icon size.</summary>
  private const uint BaseDpi = 96;

  /// <summary>Available rungs (physical px) — the 100–400% tray ladder: 16/20/24/32/48/64.</summary>
  private static readonly int[] Rungs = { 16, 20, 24, 32, 48, 64 };

  /// <summary>
  /// Smallest rung ≥ the tray metric at this DPI (16 × dpi/96 px), or the
  /// largest rung (64) if the ideal exceeds it. dpi==0 (unknown) → base rung 16.
  /// </summary>
  public static int RungForDpi(uint dpi)
  {
    uint effective = dpi == 0 ? BaseDpi : dpi;
    double idealPx = 16.0 * effective / BaseDpi; // physical px @ this DPI
    foreach (int rung in Rungs)
      if (rung >= idealPx)
        return rung;
    return Rungs[^1];
  }

  /// <summary>
  /// Loads the DPI-appropriate PNG from the embedded assets and converts it
  /// to an owned HICON (GDI+; destroy with DestroyIcon when done). Returns
  /// false on any loading failure → caller falls back to IDI_APPLICATION (§12).
  /// </summary>
  public static bool TryCreateIconForDpi(uint dpi, out IntPtr hIcon)
  {
    hIcon = IntPtr.Zero;
    int rung = RungForDpi(dpi);
    string suffix = $"assets.tray_{rung}.png";

    Assembly assembly = typeof(TrayIconAssets).Assembly;
    string? name = Array.Find(assembly.GetManifestResourceNames(),
        n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    if (name is null)
      return false;

    using Stream? stream = assembly.GetManifestResourceStream(name);
    if (stream is null)
      return false;

    // GDI+ decodes PNG natively; GetHicon returns an HICON owned by us.
    using Bitmap bitmap = new(stream);
    hIcon = bitmap.GetHicon();
    return hIcon != IntPtr.Zero;
  }
}
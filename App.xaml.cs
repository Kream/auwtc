using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using static ActuallyUsableWorldTimeClock2.TrayInterop; // fully qualified: a top-level using resolves in the global namespace

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// Wires the presentation layer together (§6.3/§10): single-instance check,
/// tray icon creation, popup toggle with a snapshot refresh per open, context
/// menu with Exit, and deterministic teardown on exit.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// A left-click on the tray icon while the popup is open is also an
    /// "outside click" (§10.2). If the hook dismissal landed within this grace
    /// period, the LeftClick event is that same physical click — ignore it so
    /// the popup closes and stays closed.
    /// </summary>
    private const int ToggleGraceMs = 300;

    private InstanceManager? _instance;
    private TrayIconWindow? _tray;
    private ClockViewModel? _viewModel;
    private MainWindow? _popup;
    private PopupDismissalWatcher? _watcher;

    private bool _dismissedByHook;
    private long _dismissedAtMs;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WireCrashLog();

        _instance = new InstanceManager();
        if (!_instance.IsPrimaryInstance)
        {
            Shutdown(); // §12: second launch exits silently with code 0.
            return;
        }

        _tray = new TrayIconWindow();
        if (!_tray.CreateIcon())
        {
            // §12: the app is useless without the tray icon.
            MessageBox.Show(
                "World Time Zone Clock could not create its tray icon and will now exit.",
                "World Time Zone Clock",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _viewModel = new ClockViewModel();
        _popup = new MainWindow { DataContext = _viewModel };
        _watcher = new PopupDismissalWatcher();

        _tray.LeftClick += TogglePopup;
        _tray.RightClick += ShowMenuAndMaybeExit;
    }

    private void TogglePopup()
    {
        if (_popup!.IsVisible)
        {
            ClosePopup();
            return;
        }

        if (_dismissedByHook && Environment.TickCount64 - _dismissedAtMs < ToggleGraceMs)
            return; // this click just dismissed the popup; don't reopen it

        OpenPopup();
    }

    private void OpenPopup()
    {
        _viewModel!.Refresh(); // §10.1: snapshot at the moment of the click.

        if (_tray!.TryGetIconRect(out RECT iconRect))
            _popup!.PositionAboveIcon(iconRect);
        else
            _popup!.PositionAtBottomRight(); // §12 fallback

        _popup.Show();
        _watcher!.Start(_popup, OnHookDismiss);
    }

    private void ClosePopup()
    {
        if (_popup is null || !_popup.IsVisible)
            return;
        _watcher!.Stop();
        _popup.Hide();
    }

    /// <summary>Watcher callback (Esc or outside click); runs on the UI thread.</summary>
    private void OnHookDismiss()
    {
        if (_popup!.IsVisible)
        {
            _dismissedByHook = true;
            _dismissedAtMs = Environment.TickCount64;
            ClosePopup();
        }
    }

    private void ShowMenuAndMaybeExit()
    {
        ClosePopup();
        if (_tray!.ShowContextMenu())
            Shutdown(); // §10.2: the Exit item was chosen.
    }

    // ---- crash evidence capture (diagnostic; does not change crash behavior) ----

    private static string CrashLogPath => Path.Combine(AppContext.BaseDirectory, "crash.log");

    private static void WireCrashLog()
    {
        // Both handlers ONLY write evidence; neither sets Handled, so the
        // process still dies exactly as before — we just get the stack.
        Current.DispatcherUnhandledException += (s, e) =>
            LogCrash("DispatcherUnhandledException\n" + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LogCrash("AppDomain.UnhandledException\n" + e.ExceptionObject);
    }

    private static void LogCrash(string content)
    {
        try
        {
            File.AppendAllText(CrashLogPath,
                $"\r\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} PID {Environment.ProcessId} =====\r\n{content}\r\n");
        }
        catch
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "WorldTimeZoneClock.crash.log"), content);
            }
            catch { /* out of options; die as before */ }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _popup?.Close();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

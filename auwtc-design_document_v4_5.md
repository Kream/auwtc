# Design Document: World Time Zone Clock (System Tray)

> **Version:** 4.5
> **Supersedes:** `auwtc-design_document_v4_4.md`
> **What changed in this revision:** see Appendix A (Change Log) at the end of this document. The full revision history (v2 → v3 → v3.1 → v4 → v4.1 → v4.2 → v4.3 → v4.4) is preserved in Appendix A and in the git history of this repository.

## 1. Overview

A small Windows 10 system tray application that displays the current 24-hour local time for four cities — Los Angeles (LAX), New York (JFK), London (LHR), and Christchurch (CHC) — in a minimalist, borderless popup. The popup is anchored above the tray icon and is toggled by clicking the icon. The app is a standalone executable (no installer) built with C# and WPF on .NET 8 LTS, published as a single self-contained file.

## 2. Purpose

Provide a lightweight, always-visible world clock in the system tray that shows the current time in four cities, with no UI clutter and no runtime dependencies beyond the .NET runtime.

## 3. Scope

- **In scope:** System tray icon, borderless popup, 24-hour time display for LAX/JFK/LHR/CHC, single-instance enforcement, right-click exit, outside-click dismissal, static time snapshot at click time, snapshot-consistency unit tests, DPI-correct measured-size positioning, standalone .exe.
- **Out of scope:** Startup logic (managed externally), taskbar entry, console window, complex UI, real-time time updates, custom icons (built-in icon is sufficient), a cap/scroll for very large city counts (backlog).

## 4. Non-Functional Requirements

- **Platform:** Windows 10 (and later).
- **Runtime:** .NET 8 LTS (self-contained).
- **Language:** C#.
- **Framework:** WPF (for future popup extensibility).
- **Tray icon:** DPI-rung PNG set from `assets/` (build #15) with the built-in `IDI_APPLICATION` fallback (§7.1/§12).
- **Popup style:** Borderless, no title bar, no close button, topmost, no focus steal.
- **Popup size:** **Width 180 DIP manual; height content-sized** (`SizeToContent="Height"`, build #13) — one natural row per city, any count (§13); the popup grows with the city list. At 150% scaling the width is 270 physical px.
- **DPI awareness:** Per-Monitor V2, declared in `app.manifest` (embedded via the `.csproj` `ApplicationManifest` property) — the §9/§9.1 conversion policy is correct only under per-monitor awareness (§14).
- **Time format:** 24-hour `HH:mm` (e.g., `17:13`).
- **City order:** LAX, JFK, LHR, CHC (one line each).
- **Single instance:** No duplicate processes; second launch exits silently.
- **Dismissal:** Tray icon click (toggle), outside click (taskbar, other windows, desktop, active window), right-click context menu (Exit), Esc key.
- **Time accuracy:** Snapshot at the moment of the tray click; no live refresh while the popup is open.
- **Future extensibility:** Easy to add/remove cities and to enrich the popup display.

## 5. Technology Stack

- **IDE:** Any IDE with .NET 8 WPF support (e.g., Visual Studio 2022 or later, JetBrains Rider, VS Code with the C# Dev Kit). The design does not depend on a specific IDE.
- **Language:** C#.
- **UI framework:** WPF (Windows Desktop project, target `net8.0-windows`).
- **Runtime:** .NET 8 LTS (self-contained single-file publish).
- **Build command (publish):**

  ```
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
  ```

  Output: a single `WorldTimeZoneClock.exe` in `bin\Release\net8.0-windows\publish\`. Copy this file to the target program directory.

  **Packaging (MSIX, build #16 — §15.3):** the distribution artifact is a **self-signed, sideloaded `.msix`** produced by the Windows Application Packaging Project — `Package\Package.wapproj`, **relocated 2026-11-03 into the repo root** (sibling of the main csproj; one solution, `ActuallyUsableWorldTimeClock2.slnx`, now carries all three projects: app + tests + `Package`). The wapproj references the app via a **relative** path (`..\ActuallyUsableWorldTimeZoneClock2.csproj`) and Content-links both the manifest (`..\packaging\Package.appxmanifest`, Link `Package.appxmanifest`) and the logo (`..\assets\installer_256.png`, Link `Assets\installer_256.png`). `AppxAutoIncrementPackageRevision=False` in the wapproj enforces §15.3's Revision-pinned-at-0 rule mechanically. Package output goes to the VS default `Package\bin\…\AppPackages\` (gitignored, like the `.pfx` signing secret, the `.cer`, and `.wapproj.user`). Under MSIX the install location is read-only, so `crash.log`'s primary path fails and the §12/B2 `%TEMP%` fallback carries crash evidence.

## 6. Architecture

The app is split into three loosely coupled layers:

1. **Data layer** — city definitions and timezone resolution.
2. **Business logic layer** — time snapshot computation and single-instance enforcement.
3. **Presentation layer** — WPF UI (tray icon, popup window, context menu).

### 6.1 Data Model

- **`City`** (data class)
  - `Name` (string): e.g., `"LAX"`.
  - `TimeZoneId` (string): e.g., `"America/Los_Angeles"` (IANA ID).
  - `Order` (int): display order (1 = LAX, 2 = JFK, 3 = LHR).
- **`CityTimeRow`** (display model)
  - `City` (City)
  - `Time` (string): formatted as `HH:mm` (e.g., `"17:13"`).
- **`CityTimeProvider`** (service)
  - Constructor: optional `Func<DateTimeOffset>? clock` — omitted/null = the real clock (`DateTimeOffset.Now`), so the production wiring in `App` is unchanged. The clock is **only read through `Now`** — once per snapshot (§10.1/§10.3); a read per city could straddle a minute boundary and display mutually inconsistent rows.
  - `Now` (DateTimeOffset): the one clock read; called once per popup open (build #11, App. B B11).
  - `GetLocalTime(City city, DateTimeOffset instant)` → `string`: the city's wall-clock time **at the given instant**, formatted as 24-hour `HH:mm`.
  - `GetLocalTimes(IEnumerable<City> cities, DateTimeOffset instant)` → `IReadOnlyList<CityTimeRow>`: batch conversion of every city from **one shared instant**, preserving city order (§10.1). A bad zone degrades only its own row (§12).
  - Conversion: `TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId))` — the `DateTimeOffset` overload converts the instant (via its embedded offset) to the zone's wall clock; DST handling is automatic.
  - There is **no** clock-reading parameterless overload: the per-city `GetLocalTime(City)` of build #9 re-read the clock per call and was the §10.3 race vector; it was removed (App. B B10).

### 6.2 Business Logic

- **`ClockViewModel`**
  - Constructor: optional `Func<DateTimeOffset>? clock`, passed through to `CityTimeProvider` (omitted/null = the real clock; the `App` wiring is unchanged).
  - `ObservableCollection<City>` — the list of cities in order (LAX, JFK, LHR, CHC).
  - `ObservableCollection<CityTimeRow>` — computed rows from `CityTimeProvider`.
  - `Refresh()`: **captures one instant** (`CityTimeProvider.Now` — the single clock read at the moment of the tray click), computes the **entire** snapshot via `GetLocalTimes(Cities, instant)`, and only then swaps `Rows` (clear + add) — the rows are fully computed before `Rows` is touched, so no transient empty/partial state is observable (App. B B10).
- **`InstanceManager`**
  - Creates a named `Mutex` with the name `Local\WorldTimeZoneClock`.
  - If the mutex is already held, the second launch exits immediately (no popup, no UI).
  - If the mutex is newly created, the first launch proceeds.

### 6.3 Presentation

- **Tray icon helper** — a hidden WPF window that owns the `Shell_NotifyIcon` handle (P/Invoke) and exposes `GetRect()` for the icon's screen rectangle.
- **Popup window** — a WPF `Window` with:
  - `WindowStyle = None` (borderless: no title bar, no close button)
  - `AllowsTransparency = true` (needed only for the future rounded-corner/shadow styling in §7.3; harmless for a plain rectangle)
  - `ShowInTaskbar = false`
  - `Topmost = true`
  - `ShowActivated = false` and `Focusable = false`
  - `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` applied via P/Invoke (`SetWindowLongPtr`) so the popup never takes keyboard focus from the active application — `Topmost` alone does **not** provide `WS_EX_NOACTIVATE`.
  - `SizeToContent = "Height"`, manual `Width = 180` DIP — **never** `Show`-then-position (the topmost popup would flash at a default location); placement uses the **measured** size (§9.2) before `Show()` (App. B B13).
- **Context menu** — right-click on the tray icon shows an "Exit" item.
- **Keyboard shortcut** — `Esc` closes the popup.

### 6.4 Test Suite (builds #11 and #14)

- **Project:** `tests/WorldTimeZoneClock.Tests` — xUnit (`xunit` 2.9.2), `Microsoft.NET.Test.Sdk` 17.11.1, `xunit.runner.visualstudio` 2.8.2 (exact pins — the repo's first NuGet use), `net8.0-windows` + `UseWPF` (absorbs the main project's WPF framework references), `RuntimeIdentifier` `win-x64` + `SelfContained` `false` (matches the main project's RID-specific setup and avoids the RID-mismatch restore warning class), `IsPackable` `false`; references the `WinExe` main project directly (the data layer has no executable-specific surface); added to the `.slnx`. The main `.csproj` excludes `tests/**` from its default globs (`DefaultItemExcludes`): the main project's directory is the repository root, so the SDK's recursive default globs would otherwise sweep the nested test sources into the `WinExe` app, which has no test-framework references (CS0246 on the first compile of build #10; App. B B10/B11).
- **`ClockSnapshotTests`** (§10.3 conformance; view-model scope only — window/UI behavior is manual-smoke territory per repo practice). **Roster-agnostic (build #14, App. B B14):** the documented cities are asserted **by name** (the LAX/JFK/LHR boundary pins by name; every roster member via the whole-roster same-instant check) — §4 requires those cities to exist, not that the list is closed; no case assumes the roster's count, order, or exclusivity, so adding a `City` to `ClockViewModel.Cities` (the §13 growth path — proven for real in build #14 with CHC) keeps the suite green:
  1. **Probe clock** (advancing + read counter): `Refresh()` performs exactly **one** clock read and every row derives from that **first** reading — with the old per-city reads, later rows jump a minute. The constant-clock tests below cannot detect this regression (a constant clock makes the race structurally unreproducible); the probe is the direct detector.
  2. **Constant boundary clocks**: `…:59.999` → LAX `12:59` / JFK `15:59` / LHR `20:59`, and `…:00.001` → `13:00` / `16:00` / `21:00` (one open = one mutually consistent set — the trio pinned by name, any added city covered by the whole-roster check).
  3. **Fresh snapshot per open**: a second `Refresh()` takes its own instant (read count 2, rows change).
  4. **DST edges** (instant semantics): spring-forward gap `02:00` does not exist → `03:00`; fall-back first `01:59` (DST) / second `01:00` (standard) — `ConvertTime(DateTimeOffset, tz)` never throws on gap/ambiguity because the input is an absolute instant.
  5. **§12 degradation**: an invalid zone blanks **only** its own row; the row count and the other rows are unaffected.

## 7. UI Design

### 7.1 Tray Icon

- **Tray icon (build #15):** the DPI-rung PNG set under `assets/` — `tray_16`/`tray_20`/`tray_24`/`tray_32`/`tray_48`/`tray_64` (square, physical px, one per standard tray rung: 16 px @ 96 DPI up to 64 px @ 384 DPI) — embedded as assembly resources (single-file-publish safe) and selected at startup by the helper window's DPI (`GetDpiForWindow`; rung = smallest PNG ≥ `16 × dpi/96`, clamped to 64). Rung selection is a **physical-px** decision (§9.1: tray metrics scale with monitor DPI, never DIP).
- **Fallback (§12):** if the asset set fails to load, `IDI_APPLICATION` (`SystemIcons.Application` — the pre-#15 built-in icon) keeps the app usable.
- **Exe/installer icon:** `assets/installer.ico` via the `.csproj` `ApplicationIcon` (the exe's Win32 icon resource — Explorer and the future MSIX packaging reuse it). `assets/installer_256.png` (256×256) is the source reserved for the MSIX visual assets (§15.3, packaging change-set).
- **Handle lifetime:** the GDI+-created tray HICON is destroyed (`DestroyIcon`) on teardown alongside `NIM_DELETE`; the shared `LoadIcon` fallback handle is never destroyed.

### 7.2 Popup Window

- **Size (v4.4):** **Width 180 DIP manual; height content-sized** (`SizeToContent="Height"`) — the popup renders one natural-height row per city and grows vertically with the city list (§13). The manual width is the authoritative horizontal size: `SizeToContent="Height"` never sizes the width.
- **Layout:** An `ItemsControl` bound to `CityTimeRow` whose items panel is a **vertical `StackPanel`** — one natural-height row per city, **any count** (the old `UniformGrid Rows="3"` hardcoded three rows and equalized cell heights; it was the literal §13 blocker, App. B B13).
- **Text:** City name left-aligned (`TextTrimming="CharacterEllipsis"` — over-wide names clip, not overflow), time right-aligned, in a 12 pt font (WPF `FontSize` is in DIP, so 12 pt = `FontSize 16` DIP). Consolas 12 pt (monospaced) is recommended for clean column alignment.
- **Row rhythm:** per-row `Margin="12,4"` DIP — the vertical padding the old `UniformGrid` (3 equal cells over a fixed 100-DIP height) supplied by equal distribution; without it the rows would render tight-packed (App. B B13).
- **No border, no title bar, no close button.**
- **Positioning:** Anchored above the tray icon — the popup's bottom edge is 8 DIP above the icon's top edge, horizontally centered on the icon, clamped to the working area of the icon's monitor using the **measured** placement size (exact formulas in §9).

### 7.3 Visual Style

- Minimalist: plain background, no decorations.
- If desired, a subtle drop shadow or rounded corners can be added later (future-proofed by the data-driven design).

## 8. Tray Icon & Notification

- Use `Shell_NotifyIcon` (P/Invoke) from a hidden helper window.
- **Icon rectangle:** `Shell_NotifyIconGetRect` returns the icon's screen rectangle **in physical px** (Win32 screen space — §9.1).
- **Anchor:** The popup is placed relative to that rectangle (see §7.2).
- **Icon lifecycle:** The icon is created on startup and destroyed when the app exits.

## 9. Positioning & Layout

**Canonical unit: the WPF DIP** (device-independent pixel, 1/96 inch) — since build #12. Above the interop boundary everything is DIP; inside Win32 everything is physical px; the **one** conversion between the spaces is `TrayInterop.PxToDip` (§9.1). WPF performs the DIP→px render transform itself (per window), so no conversion in that direction exists in app code. The policy is correct only under per-monitor DPI awareness (§4, `app.manifest`).

**Popup size (v4.4):** the placement size is the **measured** size (§9.2, `MainWindow.GetPlacementSizeDip()`), never the nominal constants:

- **Width:** the manual `Width = 180` DIP (XAML) — `SizeToContent="Height"` never sizes width, so it is the authoritative X term.
- **Height:** the content-sized height, measured fresh per placement (§9.2) after `Refresh()` has settled `Rows`.

- **Anchor edge:** The popup's bottom edge is placed **8 DIP** above the top edge of the tray icon rectangle.
- **Horizontal alignment:** The popup's horizontal center is aligned to the tray icon's horizontal center.
- **Inputs (physical px):** the icon rect (§8, `Shell_NotifyIconGetRect`) and the work area of the monitor containing the icon (`MonitorFromRect(iconRect)` + `GetMonitorInfo` — **not** `SystemParameters.WorkArea`, which is primary-monitor-only and DIP-native).
- **Conversion (once, at the boundary):** every icon-rect and work-area field via `PxToDip(px, dpi)`, where `dpi` = the **popup window's own effective DPI** (`TryGetWindowDpi` → `GetDpiForWindow`) — the exact factor WPF inverts when it renders `Window.Left`/`Top` back to physical px (the *write-factor invariant*). The icon monitor's DPI (`GetDpiForMonitor`) is **not** valid here: monitor DPI is the factor for px-native consumers (`SetWindowPos`/`MoveWindow`), and using it for `Left`/`Top` lands the popup off by the monitor ratio whenever the popup's current monitor differs from the icon's.
- **Clamping (every value DIP; `popup.Width`/`popup.Height` = the measured placement size, §9.2):**
  - `iconCenterX = icon.Left + icon.Width / 2` **[DIP]**
  - `popup.Left = clamp(iconCenterX − popup.Width / 2, work.Left, work.Right − popup.Width)` **[DIP]**
  - `popup.Top = clamp(icon.Top − popup.Height − 8, work.Top, work.Bottom − popup.Height)` **[DIP]**
- **Measured-size invariant (v4.4):** the clamp size and the size `Show()` renders agree **by construction** — `GetPlacementSizeDip()` forces one layout pass on the hidden window after `Refresh()` and reads the same settled content under the same constraint `SizeToContent="Height"` applies at `Show()` (no non-client chrome: `WindowStyle=None` + `AllowsTransparency`), so the terms are the real rendered size, not nominal constants. (Build #12's "valid because `ResizeMode = NoResize` and no `SizeToContent`" sentence is retired — the precondition changed, the guarantee moved to the measured-size invariant.)
- **Multi-monitor:** the popup is always placed on the monitor that contains the tray icon (the work area comes from `MonitorFromRect(iconRect)`), and the conversion factor is the popup window's own DPI at the moment of the write.
- **Ordering invariant:** monitor selection (`MonitorFromRect`) must see the rect in its **native** unit (physical px), before any conversion.

### 9.1 Units audit table (build #12; popup-size rows updated v4.4)

| Coordinate value | Unit | Conversion |
|---|---|---|
| `Shell_NotifyIconGetRect` icon rect | physical px | `PxToDip` (popup window DPI) in `MainWindow.PositionAboveIcon` — the only conversion call sites in the app |
| `MONITORINFO.rcMonitor` / `rcWork` | physical px | `PxToDip` (same popup window DPI) in `PositionAboveIcon` |
| `GetCursorPos` → `TrackPopupMenu` x/y | physical px | none — native↔native pass-through (px↔px) |
| `GetWindowRect` popup rect ↔ `MSLLHOOKSTRUCT.pt` | physical px | none — px↔px containment compare (valid only under per-monitor awareness, §14) |
| `SetWindowPos` (menu band lift) | — | none — `SWP_NOMOVE \| SWP_NOSIZE`, no coordinates |
| `Window.Left`/`Top` | DIP | none — canonical; WPF renders DIP→px per window |
| Manual `Window.Width = 180` (XAML) | DIP | none — authoritative X term; `SizeToContent="Height"` never sizes width |
| Measured placement size (`GetPlacementSizeDip`: `UpdateLayout` → `ActualHeight`, manual-measure fallback) | DIP | none — layout/measure values are DIP and DPI-independent; never crosses the px↔DIP boundary |
| `FontSize`, `Margin` | DIP | none — layout values, DIP-native |
| `SystemParameters.WorkArea` | DIP (WPF) | none — §12 fallback only; already DIP and primary-monitor-only by construction |
| `AnchorGap = 8` | DIP | none — constant visual gap at any scale |

Every other value in the app is non-coordinate (time strings, flags, handles, IDs).

### 9.2 Measured placement size (build #13)

`MainWindow.GetPlacementSizeDip()` → `Size`, measured **fresh on every placement** (called post-`Refresh()`, pre-`Show()`):

- `UpdateLayout()` — forces measure+arrange on the hidden window **now**; `Rows` was rebuilt by `Refresh()` since the last layout, so any cached `Actual*` would be stale. Visibility gates rendering, not layout: a never-shown window with an HWND (ctor `EnsureHandle()`) still measures/arranges.
- Width = the manual `Width` (never the pre-layout `ActualWidth`, which is 0).
- Height = `ActualHeight`; if that yields nothing usable (the never-laid-out anomaly), fall back to a manual `Content.Measure(new Size(width, ∞))` + `DesiredSize.Height` — the same constraint `Window.MeasureCore` applies under `SizeToContent="Height"` (no chrome), so it provably equals the `Show()` height.
- Floor: `MinPlacementHeightDip = 24` DIP (≈ one text row) guards a degenerate empty-`Rows` measure — in **code**, not XAML `MinHeight`, so it cannot interact with the `SizeToContent` sizing machinery.

Both paths are DIP and DPI-independent (§14): measurement never crosses the px↔DIP boundary, which stays exactly `PxToDip`.

## 10. Interaction Flow

### 10.1 Open

1. **Trigger:** Left-click on the tray icon.
2. **Single instance:** If another instance is already running (mutex `Local\WorldTimeZoneClock` is held), the new process exits immediately.
3. **Snapshot:** `ClockViewModel.Refresh()` captures **one instant** (the injected clock's reading — the real clock on the default wiring) at the moment of the click and converts each city in order (LAX → JFK → LHR → CHC) **from that single instant**, filling the `CityTimeRow` collection.
4. **Position:** Popup is anchored above the tray icon per §9, using the measured placement size (§9.2) for the clamp terms.
5. **Show:** Popup is shown topmost, without activating (no focus steal, no taskbar entry). Positioning completes **before** `Show()` — never Show-then-position (the topmost popup would flash at a default location).

### 10.2 Toggle & Close

- Left-click on the tray icon while the popup is open **closes** it (toggle behavior).
- **Outside click** (on the taskbar, another application's window, the desktop, or anywhere outside the popup) closes the popup.
- **Esc** closes the popup.
- **Right-click on the tray icon** → context menu with a single **Exit** item → application exits.
- Closing the popup does **not** exit the app; the tray icon remains.

### 10.3 Refresh Behavior

- Time values are a **snapshot taken at the moment of the tray click**; the displayed time does not update while the popup is open.
- **All rows of one open derive from one identical instant** (§10.1): they can never straddle a minute boundary or otherwise disagree with each other.
- Each subsequent open of the popup takes a fresh snapshot (and a fresh measured placement size, §9.2 — a changed row count re-sizes the popup).

## 11. Time Handling

- **Source:** the injected clock — a `Func<DateTimeOffset>`, default `DateTimeOffset.Now` (§6.1) — read **exactly once per snapshot** (§10.1): `ClockViewModel.Refresh()` captures the reading via `CityTimeProvider.Now` and passes the single instant to every city conversion. The default real clock is the local machine time; any offset-carrying reading (`Now` or `UtcNow`-shaped) is equivalent because the conversion operates on the instant via the offset (§14).
- **Conversion:** `TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId))` — the `DateTimeOffset` overload converts the instant to the destination zone's wall clock.
- **IANA IDs on Windows:** On .NET 6+ (and therefore .NET 8), `TimeZoneInfo.FindSystemTimeZoneById` natively accepts IANA IDs (e.g., `America/Los_Angeles`) — Windows has an ICU-based IANA↔Windows mapping built into the runtime. No manual mapping table is needed.
- **DST:** Handled automatically by `TimeZoneInfo` for the snapshot date/time; the spring-forward gap and fall-back ambiguity resolve deterministically from the absolute instant (test case §6.4.4).
- **Formatting:** `HH:mm` (24-hour, zero-padded, invariant culture so it does not depend on the user's locale).
- **City order:** Always LAX, JFK, LHR, CHC (hard-coded in the data layer; §13).

## 12. Error Handling

- **Unknown/invalid `TimeZoneId`:** `CityTimeProvider` wraps `FindSystemTimeZoneById`/`ConvertTime` per city in a try/catch and returns the string `"--:--"` for that row; the popup still shows all rows.
- **Mutex acquisition failure:** The second process exits silently with code 0 (by design, not an error path).
- **`Shell_NotifyIconGetRect` failure — or DPI acquisition (`TryGetWindowDpi`) or monitor-info (`GetMonitorInfo`) failure on the anchored path:** fall back to the bottom-right corner of the primary monitor's working area minus an **8 DIP** margin — `SystemParameters.WorkArea`, which is already WPF DIP and primary-monitor-only by construction (exactly §12's semantics), so the fallback path performs **no** px→DIP conversion (§9.1). The fallback uses the **same measured placement size** (§9.2) as the anchored path — never the nominal `Height`, which is unset since build #13 (reading it would compute a NaN `Top`).
- **Icon creation failure:** Show a message box and exit (the app has no purpose without the tray icon).
- **Logging:** None (out of scope).

## 13. Future Extensibility

- **More cities:** Add a `City` entry (name, IANA ID, order); rows render automatically **and the popup grows vertically** — one natural-height row per city (vertical `StackPanel`), any count, `SizeToContent="Height"` sizing the window to them (build #13; the fixed 100-DIP height and `UniformGrid Rows="3"` are gone). **Proven in practice (build #14):** a fourth city (CHC, `Pacific/Auckland` — NZDT, UTC+13, the southern-hemisphere DST case) was added to the hard-coded list, rendered correctly, and the roster-agnostic suite (§6.4) stayed green. A cap/scroll for very large counts is deliberately out of scope (§3).
- **Richer rows:** Extend `CityTimeRow` (e.g., date, day/night indicator, seconds) without changing the rendering pipeline — the popup re-measures per open (§9.2).
- **Live updates:** A `DispatcherTimer` could re-call `Refresh()` while the popup is open (out of scope).
- **Custom icon/skin:** Done for the tray + exe in build #15 (§7.1: DPI-rung PNG set + `installer.ico`); a richer skinned icon set remains future work.
- **Persistence:** None needed; city list is currently hard-coded in the data layer.

## 14. Glossary

- **DIP:** device-independent pixel, 1/96 inch — WPF's canonical coordinate unit (`Window.Left`/`Top`, `Width`/`Height`, `FontSize`, margins). At 100% scaling 1 DIP = 1 physical px; WPF converts DIP→px per window at render. Since build #12 the app's units policy is DIP above the Win32 interop boundary (§9.1).
- **DST:** Daylight saving time.
- **DateTimeOffset:** .NET structure representing a date/time instant plus its UTC offset; `TimeZoneInfo.ConvertTime(DateTimeOffset, TimeZoneInfo)` converts the instant to a destination zone's wall clock. The injectable clock (§6.1) is a `Func<DateTimeOffset>`.
- **IANA time zone ID:** A portable time zone identifier from the Olson/tz database (e.g., `America/Los_Angeles`), accepted directly by `TimeZoneInfo.FindSystemTimeZoneById` on .NET 6+ (including .NET 8).
- **MSIX:** Windows app-package format; packages a full-trust desktop app (WPF included) for Store, web, or sideload distribution. The package manifest (`Package.appxmanifest`) carries a **four-part** version mapped to this repo's build ID by §15.3.
- **Per-Monitor V2 (PMv2):** Windows DPI-awareness mode in which each top-level window is scaled by the DPI of its current monitor; declared in the app's `app.manifest` (§4). The §9/§9.1 conversion policy and the hook-vs-rect comparisons in `PopupDismissalWatcher` are correct only under per-monitor awareness.
- **Physical pixel (px):** Device pixel in Win32 screen space — the unit of every `TrayInterop` `RECT`/`POINT` and of all shell-reported geometry.
- **SizeToContent ("Height"):** WPF window-sizing mode that sizes the window to its content; with `"Height"` only the height is content-sized and a manual `Width` is the authoritative width (§7.2/§9.2, build #13).
- **TimeZoneInfo:** .NET class for timezone-aware time conversion.
- **P/Invoke:** Platform Invocation Services for calling native Windows APIs (e.g., `Shell_NotifyIcon`, `Shell_NotifyIconGetRect`).
- **WS_EX_NOACTIVATE:** Extended window style that prevents a window from receiving keyboard focus when clicked/shown.

## 15. Build ID Incrementation

Every new build increments the build ID by exactly one. The build ID is the patch digit of `VersionPrefix`/`FileVersion` in `ActuallyUsableWorldTimeZoneClock2.csproj` (e.g., `0.0.8` = build #8), echoed in the `InformationalVersion` build tag (e.g., `0.0.8-build8-tpm-workarea`). This section operationalizes the per-build versioning protocol recorded in Appendix B6.

### 15.1 When the build ID increments

- **Source change → bump.** Any change to compiled sources (`.cs`, `.xaml`, or build-affecting `.csproj` properties) produces a new build and must increment the build ID.
- **Docs-only → no bump.** Changes that do not touch the binary (this design document, README, LICENSE) do not increment the build ID — creating or revising this document is itself a docs-only change.
- **One bump per build.** Multiple source edits folded into a single build session share one build ID.

### 15.2 How an LLM increments the build ID

An LLM asked to create a new build must treat the increment as a deliberate, verified procedure — never from memory:

1. **Ground the current number.** Read `ActuallyUsableWorldTimeZoneClock2.csproj` from disk and note the existing `VersionPrefix`, `FileVersion`, and `InformationalVersion`. Cross-check the build number against the last generated `build-info.txt` (next to the exe) and the highest `B<n>` row in Appendix B. If the sources disagree, stop and reconcile before editing anything.
2. **Compute the next ID.** New build number = current patch digit + 1. The major and minor components never move unless explicitly instructed.
3. **Edit the `.csproj`.** Set `VersionPrefix` and `FileVersion` to `0.0.<N+1>`, and set `InformationalVersion` to `0.0.<N+1>-build<N+1>-<slug>`, where `<slug>` is a new, short, lowercase kebab-case label for this build's change (e.g., build 9 of a menu-ladder change → `0.0.9-build9-menu-ladder`). Never reuse a slug from an earlier build.
4. **Re-read before building.** Open the edited `.csproj` again and confirm all three properties agree on the new number (no stale digit, no typo'd slug).
5. **Build and check the receipt.** Run the publish command (§5); the `StampBuildInfo` target writes `build-info.txt` next to the exe. Its `version=` line must read `0.0.<N+1>-build<N+1>-<slug>`. Any mismatch is a failed increment: fix and rebuild. Never hand-edit `build-info.txt` — it is generated output, not configuration.
6. **Record the build.** Add an Appendix B row (`B<n+1>`) for the build; if documented behavior changed, update the affected design sections in the same revision.
7. **Never decrement, reuse, or skip.** Build IDs are monotonic. An aborted or failed build does not give its number back — record it in Appendix B and let the next real build take the following number.

### 15.3 MSIX package version mapping (decided 2026-11-03, ahead of the packaging change-set)

The MSIX package manifest (`Package.appxmanifest`) requires a **four-part** `Major.Minor.Build.Revision` version; the repo protocol (§15.1) is three-part `0.0.<build>`. The mapping between the two is fixed **for now**:

- **Rule:** `Package.appxmanifest` `Identity/Version` = `0.0.<build>.0` — the repo build number becomes the **Build** digit, and the **Revision** digit is pinned at `0`. The revision segment is reserved: it moves only if a packaging requirement ever forces it (e.g., a Store resubmission of the same build must bump some digit — that digit is the revision).
- **Current application:** build #16 (the MSIX packaging change-set: Windows Application Packaging Project + `Package.appxmanifest`) → `Package.appxmanifest` Version = `0.0.16.0` ↔ `.csproj` `VersionPrefix`/`FileVersion` = `0.0.16`, `InformationalVersion` = `0.0.16-build16-<slug>` (fresh kebab slug per §15.2 step 3). *(Amended 2026-11-03: the DPI-rung tray-icon change-set consumed build #15 — App. B B15 — so the packaging change-set takes the next number; the mapping rule itself is unchanged.)*
- **Status:** decided "for now" — provisional pending the packaging wizard's actual requirements. **Applied 2026-11-03 (build #16):** the manifest is authored at `packaging/Package.appxmanifest` with `Version="0.0.16.0"`; the distribution decision is **self-signed sideload** (user, 2026-11-03) — the `Publisher` attribute (`CN=Kream`) must equal the self-signed certificate's Subject exactly (Visual Studio generates the test certificate to match the manifest); the wapproj pins the Revision digit at 0 mechanically (`AppxAutoIncrementPackageRevision=False`). If a packaging constraint forces a change, the mapping is renegotiated and recorded here in a new document revision; the `.csproj` protocol (§15.1/§15.2) itself never changes.
- **Scope:** the mapping governs only the package manifest. The assembly versions inside the packaged exe stay governed by §15.1/§15.2 unchanged; `crash.log`/`build-info.txt` are artifacts of the main project's build and are unaffected by the mapping (App. B B2/B6).

## Appendix A — Change Log (v2 → v4.5)

| # | Section | Change | Reason |
|---|---------|--------|--------|
| 1 | §5 | IDE line generalized (any .NET 8–capable IDE; examples given) | The design does not depend on a specific IDE version |
| 2 | §6.3 / §7.2 | Replaced WinForms `TableLayoutPanel` with WPF `Grid` (or `ItemsControl`) | `TableLayoutPanel` is a WinForms control, not available in WPF; consistency with §13 |
| 3 | §7.2 | Font choice made explicit: Consolas 12 pt (monospaced column alignment); Segoe UI 12 pt acceptable with fixed-width columns | Unambiguous UI specification |
| 4 | §10.1 / §10.3 | Snapshot semantics (time frozen at click; fresh snapshot on each open) now documented in a dedicated Interaction Flow section | Was implicit in v2 §3 |
| 5 | §11 | IANA ID support (native on .NET 6+ via the runtime's ICU-based mapping) is now documented in the Time Handling section, not only the glossary | Keeps the key runtime fact next to the implementation-facing content |
| 6 | §6.3 | Added `WS_EX_NOACTIVATE`/`WS_EX_TOOLWINDOW` requirement; justified `AllowsTransparency` (future styling only) | `Topmost` alone does not prevent focus steal; transparency is not needed for a plain rectangle |
| 7 | §6.3 | Removed duplicated bullets (`WindowStyle = None`, `ShowInTaskbar = false` appeared twice) | Editorial cleanup |
| 8 | §12 | Error handling consolidated: invalid time zone IDs → `--:--`, silent exit on second launch, icon-rect fallback position, icon-creation failure | Robustness requirements were implicit |
| 9 | §14 | Glossary updated for .NET 6+ IANA support; added `WS_EX_NOACTIVATE` entry | Consistency with §6.3/§11 |
| 10 | §9 | Clamping rules made concrete with exact formulas; multi-monitor rule spelled out | Removes ambiguity for implementation |
| 11 | §8 | Documented `Shell_NotifyIconGetRect` as the source of the icon rectangle and icon lifecycle (create on startup, destroy on exit) | Implementation-relevant detail |
| 12 | New §15 | Added "Build ID Incrementation": when a build ID increments (source change → bump, docs-only → no bump, one bump per build) and a step-by-step procedure an LLM must follow to increment it | The rule previously lived only as a one-line record in Appendix B6; it is now explicit, followable specification |
| 13 | Header / App. A | Version 3.0 → 3.1; Supersedes → `auwtc-design_document_v3.md`; this change log extended | Document revision discipline; docs-only revision, so the build ID stays at #8 (per §15.1) |
| 14 | Header / §7.2 / §8 / §13 / App. B | Version 3.1 → 4.0; Supersedes → `auwtc-design_document_v3_1.md`; cleaned edition: stripped obsolete/self-referential text — removed the `<!-- APPEND-POINT -->` marker and reworded the Appendix B append rule (future entries append at the bottom of the table), dropped the "for v3" qualifier in §13, added the WPF DIP rendering of "12 pt" (`FontSize 16`, matching `MainWindow.xaml`), and added the missing `§` sigils in the §7.2/§8 cross-references; the header now points to git history for superseded revisions | Extraneous, inaccurate, and obsolete content removed; normative specification unchanged; docs-only revision, so the build ID stays at #8 (per §15.1) |
| 15 | Header / §6.1 / §6.2 / §11 / §14 / App. B | Version 4.0 → 4.1; Supersedes → `auwtc-design_document_v4.md`; documented build #9's injectable clock — optional `Func<DateTimeOffset>? clock` on `CityTimeProvider` and `ClockViewModel` (default `DateTimeOffset.Now`), `GetLocalTime` converts the injected instant via the `DateTimeOffset` overload of `ConvertTime`; the per-city read structure (one clock read per row, §10.3 snapshot per open) is deliberately preserved so the minute-boundary race becomes controllable in tests rather than removed; `DateTimeOffset` glossary entry added; App. B row B8 added and its heading extended to build #9 | Keeps the spec current with the source (§15.2 step 6); docs-only revision — the build-ID bump to #9 belongs to the source change, not this document |
| 16 | Header / §3 / §6.1 / §6.2 / new §6.4 / §10.1 / §10.3 / §11 / §12 / App. B | Version 4.1 → 4.2; Supersedes → `auwtc-design_document_v4_1.md`; **the single-instant snapshot change-set (landed as build #11)**: `Refresh()` captures ONE instant (`CityTimeProvider.Now` — the injectable clock's single read per open) and converts every city from that shared instant via `GetLocalTimes(Cities, instant)`; §10.3 gains the explicit invariant that all rows of one open derive from one identical instant (they can never straddle a minute boundary); `CityTimeProvider` reshaped to instant-parameterized form — `GetLocalTime(city, instant)` + batch `GetLocalTimes(cities, instant)`; the clock-reading parameterless `GetLocalTime(City)` overload **removed** (it was the §10.3 race vector); §11 reworded to "read exactly once per snapshot"; new §6.4 documents the test project (xUnit: probe-clock + boundary + fresh-snapshot + DST + degradation cases) and the main-csproj `tests/**` glob exclusion; App. B gains rows B9 (build #9 aborted, never compiled), B10 (build #10 failed compile, no binary) and B11, heading extended to build #11 | v4.1's per-city clock reads violated §10.3's "snapshot at the moment of the tray click" — three reads can straddle a minute boundary and display mutually inconsistent rows; one captured instant per open fixes the violation by construction; the test suite and its glob exclusion land as build #11 after build #10's first compile failed (App. B B10/B11); docs ride the same build bump (§15.2 step 6) |
| 17 | Header / §4 / §6.3-note / §7.2 / §8 / §9 (+ new §9.1) / §12 / §14 / App. B | Version 4.2 → 4.3; Supersedes → `auwtc-design_document_v4_2.md`; **build #12's DIP units policy**: DIP declared canonical (§9); popup size restated 180 × 100 DIP and margins 8 DIP (§4/§7.2/§12); the single px→DIP conversion `TrayInterop.PxToDip(px, dpi)` + `TryGetWindowDpi` (`GetDpiForWindow`) documented with the **write-factor invariant** (convert with the popup window's own DPI, the factor WPF inverts on render — not the icon monitor's; monitor DPI is reserved for px-native consumers); DPI awareness (Per-Monitor V2, `app.manifest`) added to §4 and §14; §9 formulas restated with every unit labeled plus the ordering invariant (monitor selection sees raw px); §9.1 adds the coordinate audit table (cursor↔menu and hook-pt↔popup-rect stay physical px end-to-end; `SystemParameters.WorkArea` stays DIP-native on the §12 fallback); §12 fallback reworded to cover DPI-acquisition/monitor-info failure; §8 notes the icon rect's unit; DIP / PMv2 / physical-px glossary entries added | v4.2 mixed physical px (shell rects, "180 × 100 pixels", "8 px") with WPF DIPs (Left/Top/FontSize) with no conversion stated and no DPI awareness declared — at any scale ≠ 100% the positioning math was unit-ambiguous; the panel adjudicated the mechanism (popup's own window DPI via `GetDpiForWindow`; `GetDpiForMonitor` reserved for px-native consumers) and the §12 sourcing (`SystemParameters.WorkArea`, DIP-native, alone); docs ride the build-#12 bump (§15.2 step 6) |
| 18 | Header / §1 / §2 / §3 / §4 / §6.2 / §6.3 / §7.2 / §8 / §9 (+ §9.1 rows + new §9.2) / §10.1 / §11 / §12 / §13 / §14 / App. B | Version 4.3 → 4.4; Supersedes → `auwtc-design_document_v4_3.md`; **build #13's content-sized popup**: `SizeToContent="Height"` with manual `Width=180` DIP (§4/§7.2) — the fixed `Height=100` removed; the items panel switched `UniformGrid Rows="3"` → vertical `StackPanel` (one natural-height row per city, ANY count); per-row `Margin="12,4"` DIP restores the row rhythm the old equal-distribution supplied; `TextTrimming="CharacterEllipsis"` on the name column (over-wide names clip, never overflow); §9 clamp terms restated to use the **measured placement size** (`GetPlacementSizeDip`, new §9.2: `UpdateLayout` → `ActualHeight`, manual-measure fallback, 24-DIP code-side floor) — never the nominal constants; the build-#12 "valid because ResizeMode=NoResize and no SizeToContent" sentence retired in favor of the measured-size invariant (the same settled content is measured under the same constraint `Show()` applies); §12 fallback uses the same measured size (nominal `Height` is unset since this build — reading it would NaN the `Top`); §3 records the large-N cap/scroll as out of scope (backlog); `SizeToContent` glossary entry added; **build #14's roster-agnostic test suite (§6.4)**: the documented cities asserted BY NAME, no case assuming the roster's count/order/exclusivity; **build #14's roster addition**: CHC (Christchurch, `Pacific/Auckland`) joined the hard-coded city list — §1/§2/§3/§4/§6.2/§10.1/§11 updated to the four-city roster, §13 records the growth path as proven | v4.3's fixed 180×100 contradicted §13's "added cities render automatically" — a 4th row would overflow the fixed 100-DIP height (and `UniformGrid Rows="3"` hardcoded three rows), while §9's clamp used nominal constants; the panel adjudicated (a)-primary `UpdateLayout`+`ActualHeight` with (b) manual `Content.Measure` as the defensive fallback, StackPanel over `UniformGrid Columns="1"` (equalized cell heights), code-side floor over XAML `MinHeight` (SizeToContent precedence interplay), and confirmed measurement is DIP/DPI-independent so the build-#12 px↔DIP policy is untouched; build #14: a user-added 4th city (the §13 growth path) legitimately broke 3 hardcoded-roster test cases while the app behaved correctly — the tests were the defect (App. B B14); docs ride the build-#13/#14 bumps (§15.2 step 6) |
| 19 | New §15.3 / §14 | Version 4.4 → 4.5; Supersedes → `auwtc-design_document_v4_4.md`; **MSIX package version mapping decided ahead of the packaging change-set**: `Package.appxmanifest` (four-part version, mandatory) = `0.0.<build>.0`, Revision digit pinned at 0; provisional ("for now") pending the signing/distribution decision; MSIX glossary entry added; scope note: the mapping governs only the package manifest — assembly versions inside the packaged exe stay governed by §15.1/§15.2 unchanged | The user resolved the first of the three packaging decisions (version mapping) before any packaging files land, so the §15.2 procedure is deterministic when the packaging change-set is authored; docs-only revision — the build-ID bump belongs to the packaging change-set, not this document (§15.1) |
| 20 | §4 / §7.1 / §13 / §15.3 / App. B | (v4.5, amended in place alongside row 19 before either revision was committed): **build #15's DPI-rung tray icons** — §7.1 rewritten (embedded `assets/tray_NN.png` rung set selected by `GetDpiForWindow`, `IDI_APPLICATION` fallback, `installer.ico` → `.csproj` `ApplicationIcon`, `installer_256.png` reserved for the MSIX change-set, `DestroyIcon` handle-lifetime note); §4 tray-icon NFR updated; §13 custom-icon bullet retired to "done"; §15.3 "current application" shifted build #15 → build #16 (`0.0.16.0`); App. B gains B15 | The icons exist on disk (2026-11-03) and tray-icon selection is DPI-dependent, contradicting the old §7.1 ("built-in system icon"); the user ruled icons-first, so the icon change-set consumed build #15 and MSIX shifts to #16; docs ride the build-#15 bump (§15.2 step 6) |
| 21 | §5 / §15.3 / App. B | (v4.5, amended in place before any v4.5 commit): **build #16's MSIX packaging scaffold** — §5 gains the packaging block (self-signed sideload `.msix` via the Windows Application Packaging Project, manifest at `packaging/Package.appxmanifest`, Content-linked logo assets, gitignored `.pfx`/package outputs, `bin/`-nothing-needed note); §15.3 status updated to APPLIED (manifest `0.0.16.0` ↔ `.csproj` `0.0.16`; distribution = self-signed sideload, user decision 2026-11-03; `Publisher="CN=Kream"` must match the certificate Subject); App. B gains B16 | The user confirmed the tray icons work (build #15 validated user-side) and resolved distribution = self-signed sideload, asking for the proper `Package.appxmanifest` ahead of the VS-side wapproj; the wapproj itself is not hand-authored (its MSBuild targets ship inside the VS install); docs ride the build-#16 bump (§15.2 step 6) |

> Note: §5 names example IDEs only; the design is IDE-agnostic. Use whichever toolchain the team standardizes on.

## Appendix B — Implementation Record (post-v3.0, through build #16)

> Deliberate deltas and fixes made during construction against v3.0. The design
> sections above remain the specification; where reality forced a change, it is
> recorded here. Future entries are appended at the bottom of this table.

| # | Area | Record |
|---|------|--------|
| B1 | Interop hygiene | All P/Invoke centralized in `TrayInterop.cs` (consumed via `using static`); `HWND_TOPMOST`/`HWND_NOTOPMOST` are `static readonly IntPtr` (C# forbids `const IntPtr`); `Shell_NotifyIcon` uses the W variant with `CharSet.Unicode` after A-variant tooltip garbling was observed and fixed. |
| B2 | Crash log | §12 kept application logging out of scope, but a diagnostic crash log was added as an implementation aid: `DispatcherUnhandledException` + `AppDomain.UnhandledException` append full stacks to `crash.log` next to the exe (fallback `%TEMP%\WorldTimeZoneClock.crash.log`); handlers never set `Handled=true`. Validated end-to-end on the left-click NRE. |
| B3 | Left-click toggle | Popup toggles on left-click with a 300 ms re-open grace (`ToggleGraceMs`) so the dismissing click cannot immediately re-open the popup. Esc dismissal routes through `NIM_SETFOCUS` → `WM_TRAYICON` (0x8001). |
| B4 | Tooltip | `NOTIFYICON_VERSION_3` + W variant; tooltip verified on build #5. |
| B5 | Context menu under taskbar | With a right-edge vertical taskbar the Exit menu rendered UNDER the taskbar. Z-band fixes (owner lifted to `HWND_TOPMOST`, foreground juggling) all failed — structural cause: `TrackPopupMenu`'s default work-area restriction applies only when the cursor is inside the work area, and a taskbar cursor is outside it; no horizontal alignment flag existed either. Fix (build #8): `TPM_WORKAREA` added to uFlags → menu clamped inside the work area. **Validated user-side (T1 pass).** `TPM_RIGHTALIGN` (0x0008) pre-staged in `TrayInterop` for a right-anchored variant if ever needed. |
| B6 | Versioning | Per-build protocol: patch digit of `VersionPrefix` = build number, bumped only on source change; `InformationalVersion` carries a build tag (e.g. `0.0.8-build8-tpm-workarea`); `StampBuildInfo` target writes `build-info.txt` (version / built-UTC / config / rid) next to the exe after every build — machine-readable proof of what is compiled. **(v4.5/§15.3: MSIX `Package.appxmanifest` maps 1:1 onto this protocol as `0.0.<build>.0`, Revision pinned at 0 — decided 2026-11-03, provisional, ahead of build #15.)** |
| B7 | Build #8 validation | Green compile (0W/0E, 4.59 s), dll 24,576 B; smoke round 3: T1 (Exit menu above right-edge taskbar + outside-click dismiss) PASS, T2 (Esc close → immediate re-open not eaten by the 300 ms grace) PASS — first Esc exercise. Menu ladder steps 2–3 not needed. |
| B8 | Injectable clock (build #9, superseded by B10) | `CityTimeProvider`/`ClockViewModel` accepted an optional `Func<DateTimeOffset>? clock` (default the real clock) and `GetLocalTime` read the clock once per call — per-city reads preserved so the minute-boundary straddle was deterministically testable. The injection point survives into build #11 (`CityTimeProvider.Now`); the per-city read structure did not (see B10). |
| B9 | Aborted build #9 (never compiled) | The clock-injection session staged `0.0.9-build9-injectable-clock` sources + csproj but no compile/publish ever ran; the next session superseded the per-city-read design before any build #9 binary existed. Number consumed per §15.2 rule 7; recorded, never reused or skipped. |
| B10 | Single-instant snapshot — FAILED compile (build #10, never built) | The snapshot change-set — `Refresh()` captures ONE instant via `CityTimeProvider.Now` and converts all rows via `GetLocalTimes(Cities, instant)`; parameterless clock-reading `GetLocalTime(City)` removed (sole caller `ClockViewModel`, verified against the full tree); first test project `tests/WorldTimeZoneClock.Tests` (xUnit 2.9.2, Test.Sdk 17.11.1, runner 2.8.2 — exact pins, repo's first NuGet use; net8.0-windows + UseWPF, RID win-x64 + SelfContained=false, IsPackable=false; added to `.slnx`) — was staged as `0.0.10-build10-single-instant-snapshot`, but its FIRST compile (`dotnet publish -c Release -r win-x64 --self-contained false <main csproj>`) FAILED: 27× CS0246 (`Xunit`/`Fact`/`Theory`/`InlineData`), every error in `tests\WorldTimeZoneClock.Tests\ClockSnapshotTests.cs` and tagged `[<main csproj>]`. Root cause: the main project's directory IS the repository root, so the SDK's recursive default globs swept the nested test sources into the `WinExe` app, which has no test-framework references — not a restore/NuGet problem. No binary, no `build-info.txt` receipt. Number consumed per §15.2 rule 7 (failed build); recorded, never reused. |
| B11 | Test glob exclusion (build #11) | Fix: `<DefaultItemExcludes>$(DefaultItemExcludes);tests/**</DefaultItemExcludes>` in the main `.csproj` — `DefaultItemExcludes` is consulted by every default glob (`Compile`, `None`, `Page`, `Resource`, `EmbeddedResource`), so one line keeps the whole `tests` subtree (including any future `tests/**/obj` generated sources) out of the app compilation; property evaluation precedes item evaluation, so the in-body setting is honored by the SDK globs. Test project unchanged. The B10 snapshot change-set (feature sources identical to the failed staging) lands as part of this build; version → `0.0.11-build11-test-glob-exclusion` — fresh slug per §15.2 step 3 describing this build's delta (the #10 slug `single-instant-snapshot` is retired). csproj re-read after edit: all three properties agree. **User-side validation: compile green, app ran in tray ("seems to work fine").** The `dotnet test` suite receipt (9 cases) remains outstanding: `dotnet test` must target `tests\WorldTimeZoneClock.Tests\WorldTimeZoneClock.Tests.csproj` (the main csproj has no test packages — testing it runs zero tests). |
| B12 | DIP units policy (build #12) | `app.manifest` added (PMv2: `dpiAwareness PerMonitorV2, PerMonitor` + `dpiAware true/PM`) + `<ApplicationManifest>` in the csproj — load-bearing: WPF on .NET 8 defaults to system-DPI-aware (dotnet/wpf#859), and under system-awareness the OS virtualizes window geometry while shell rects stay true px (silently breaking the hook-pt↔`GetWindowRect` compare too). The ONE conversion: `TrayInterop.PxToDip(px, dpi)` (pure; throws on dpi==0) + `TryGetWindowDpi` (`GetDpiForWindow` — the POPUP WINDOW's OWN DPI, the write-factor invariant; icon-monitor `GetDpiForMonitor` rejected for `Left`/`Top` — px-native consumers only). `MainWindow.PositionAboveIcon` rewritten: raw-px monitor selection (ordering invariant), 7 `PxToDip` conversions, `?? Matrix.Identity` hazard removed (`System.Windows.Media` import dropped); DPI/monitor-info failure ⇒ §12 fallback. `PositionAtBottomRight` behavior unchanged (`SystemParameters.WorkArea` — already DIP + primary-only = §12 semantics; NO conversion on this path). Cursor→menu and hook-pt↔rect paths annotated px↔px native. Version → `0.0.12-build12-dip-units-policy` (re-read after edit: all three agree). Doc v4.3; README synced. Compile/test/publish receipts pending — authored in a no-shell session; §15.2 step 5 handed to the maintainer (incl. PMv2 verification of the shipped exe). |
| B13 | Content-sized popup (build #13) | `MainWindow.xaml`: `Height="100"` removed, `SizeToContent="Height"` added (manual `Width=180` DIP stays — `SizeToContent="Height"` never sizes width, so it is the authoritative X term); items panel `UniformGrid Rows="3"` → vertical `StackPanel` (the §13 blocker: Rows="3" hardcoded the count AND equalized cell heights); per-row `Margin="12,4"` (restores the vertical rhythm the old equal-distribution supplied); `TextTrimming="CharacterEllipsis"` on the name column. `MainWindow.xaml.cs`: `GetPlacementSizeDip()` added — measured fresh per placement, post-`Refresh()` pre-`Show()`: `UpdateLayout()` (visibility gates rendering, not layout — the hidden window with its ctor-`EnsureHandle` HWND still measures/arranges; `Rows` was rebuilt since the last layout, so cached `Actual*` would be stale) → `ActualHeight`; defensive fallback `Content.Measure(new Size(width, ∞))` + `DesiredSize.Height` (the same constraint `Window.MeasureCore` applies under `SizeToContent="Height"`, no chrome — provably equals the `Show()` height); 24-DIP code-side floor (in code, NOT XAML `MinHeight`, so it cannot interact with the `SizeToContent` machinery). `PositionAboveIcon` and `PositionAtBottomRight` clamp with the measured size — nominal `Height` is unset since this build (reading it in the §12 fallback would compute NaN `Top`; panel both-models catch). Never Show-then-position (topmost flash — panel both-models reject). Version → `0.0.13-build13-sizetocontent-popup` (fresh slug; re-read after edit: all three agree). Doc v4.4 (§9.2, measured-size invariant, §13 growth promise); README synced. Compile/test/publish receipts pending — no-shell session; §15.2 step 5 handed to the maintainer. Panel adjudications recorded in checkthis.txt: (a)-primary+(b)-fallback size read; StackPanel over `UniformGrid Columns="1"`; code-side floor over XAML MinHeight; measured size in BOTH position paths. |
| B14 | Roster-agnostic tests (build #14) | The user exercised the §13 growth path for real: added `new City("CHC", "Pacific/Auckland", 4)` to `ClockViewModel.Cities` and ran the suite — 3 cases failed (9 total: 6 pass / 3 fail) while the app behaved CORRECTLY (a 4th, mutually-consistent Auckland row appeared). Root cause: the TESTS, not the app — case 2 asserted an exact 3-element array, case 4 assumed `Rows[0]` is LAX, case 5 assumed `Rows.Count == 4` and `"--:--"` at index 3; the tests encoded the default roster's count/order/exclusivity, which §4 never promised (§4 requires the trio to exist; §13 promises added cities render). Auckland values verified correct: 2024-01-15 = NZDT (UTC+13) ⇒ 20:59:59.999Z → 09:59 next day, matching the observed rows. Fix: `ClockSnapshotTests` rewritten roster-agnostic — a `TimeOf(vm, name)` BY-NAME helper everywhere (no index/count assumptions); case 2 keeps the boundary pins but asserts the trio BY NAME + a count invariant (`Rows.Count == Cities.Count`) + a whole-roster same-instant check (added cities neither break nor shift the pins); case 4 asserts LAX BY NAME (was `Rows[0]`); case 5 switched to baseline-compare (healthy pre-addition snapshot vs post-addition snapshot — healthy rows byte-identical, ONLY the BAD row degrades; zero roster knowledge; appended `Order=int.MaxValue`); cases 1/3 were already roster-agnostic and stay (case 1 gains the count invariant). Still 9 cases. NO Fusion call this turn (user directive: resolve solo). The user's CHC line is left in the working tree (their §13 exercise; their call whether it ships). Version → `0.0.14-build14-roster-agnostic-tests` (fresh slug; re-read after edit: all three agree). Doc v4.4 amended in place (§6.4 heading + roster-agnostic preamble; App. A row 18 extended; App. B heading → #14). **User-side validation (2026-11-03): suite 9/9 green WITH the CHC line in; publish compile green (framework-dependent probe).** The CHC roster addition ships in this build (same session, one build ID per §15.1); docs synced in place (four-city roster updates ride this docs-only pass). The §5 self-contained publish + `build-info.txt` verification + PMv2 binary check remain the standing gate before push. |
| B15 | DPI-rung tray icons (build #15) | `TrayIconAssets` (new file): the `assets/tray_{16,20,24,32,48,64}.png` rung set embedded as assembly resources (single-file-safe: embedded resources travel INSIDE the bundle — loose `Content` files would be left outside the single-file exe) and loaded via GDI+ (`System.Drawing.Common` 8.0.0 — the MAIN project's first NuGet dependency) as `Bitmap.GetHicon()`; rung = smallest PNG ≥ `16 × dpi/96` from `GetDpiForWindow(helper)` (dpi==0 → 96 → 16-px rung), clamped to 64 — a PHYSICAL-px decision (§7.1/§9.1: tray metrics, never DIP). `TrayIconWindow.CreateIcon` selects the rung per the helper window's DPI and passes the owned HICON to `NOTIFYICONDATA.hIcon`; `Dispose` destroys it with `DestroyIcon` ONLY when owned (the `LoadIcon`/`IDI_APPLICATION` fallback handle is shared and must never be destroyed — classic Win32 lifetime rule). `assets/installer.ico` wired as `.csproj` `<ApplicationIcon>` (exe Win32 icon resource — reused later by the MSIX change-set §15.3); `assets/installer_256.png` reserved for the MSIX visual assets. Version → `0.0.15-build15-tray-icons` (fresh slug; re-read after edit: all three agree). Doc v4.5 amended in place (§7.1 rewrite, §4, §13, §15.3 shift #15→#16, App A rows 19–20, App B heading → #15); README synced. Compile/test/publish receipts pending — authored in a no-shell session; §15.2 step 5 handed to the maintainer (expect 9/9 tests + §5 publish + build-info.txt = version=0.0.15-build15-tray-icons + PMv2 check + smoke: tray shows the new icon at the monitor's DPI, crisp at 100/125/150%). NO Fusion call this turn (user directive); no image decoding performed (file names only, per user instruction). **User-side validation (2026-11-03): "icons work now" — the DPI-rung tray icon displays correctly; the formal `build-info.txt` receipt is subsumed into build #16's next publish.** |
| B16 | MSIX packaging scaffold (build #16) | `packaging/Package.appxmanifest` authored in-repo as the source of truth (the VS wizard's generated manifest is replaced by it): `Identity` `Name="WorldTimeZoneClock"`, `Version="0.0.16.0"` (§15.3: `0.0.<build>.0`, Revision pinned at 0), `Publisher="CN=Kream"` (must equal the self-signed certificate's Subject — VS generates the test cert to match the manifest), `ProcessorArchitecture="x64"`; full-trust desktop-bridge shape: `EntryPoint="Windows.FullTrustApplication"`, `TargetDeviceFamily Name="Windows.Desktop" MinVersion `10.0.14393.0` (the desktop-bridge floor) MaxVersionTested `10.0.22621.0`, `rescap:Capability Name="runFullTrust"`; `VisualElements`/`Properties` logos reference `Assets\installer_256.png` (the wapproj Content-**links** `..\assets\installer_256.png`; scale variants not generated — image processing unavailable this session — sideload tolerates single-source logos). Main `.csproj` bumped `0.0.15` → `0.0.16` / `0.0.16-build16-msix-packaging` (fresh slug) so the packaged exe's version matches the manifest per §15.3. `.gitignore` += `*.pfx` (signing cert = secret, never commit) + `*.msix`/`*.appx`/`*.msixbundle`/`*.appxbundle`/`*.msixupload`/`*.appxupload` (VS emits packages to `Package/AppPackages/`, not under `bin/`). The `.wapproj` is NOT hand-authored (its MSBuild targets ship inside the VS install): maintainer adds it via Add → New Project → "Windows Application Packaging Project", references `ActuallyUsableWorldTimeZoneClock2.csproj`, replaces the generated manifest with `packaging/Package.appxmanifest`, adds the `..\assets\installer_256.png` Content link (`Link="Assets\installer_256.png"`), creates the self-signed certificate (subject `CN=Kream`), then Create App Packages → sideload. `crash.log` under MSIX: install dir is read-only → the B2 `%TEMP%` fallback carries crash evidence. Nothing needed in `bin/` (gitignored, consumed automatically at package time). Panel not consulted (recent user directives); grounded via MS Learn desktop-bridge packaging docs. **RELOCATED 2026-11-03 (same build #16):** the user moved the whole `auwtc` working directory from `C:\Users\quirk\Documents\` into the workspace (`/work/auwtc` = `Y:\personal\librechat_files\auwtc`); the wapproj was moved into the repo (`Package\` sibling of the main csproj), its absolute `Y:\...` paths converted to relative `..\...` paths, `AppxAutoIncrementPackageRevision` set to `False` (§15.3 Revision-pinned-at-0 enforcement), the repo `.slnx` extended to all three projects (app + tests + Package), `Package/**` added to `DefaultItemExcludes` (the wizard's boilerplate PNGs would otherwise be swept into the app by the `**/*.png` glob — same class of hazard as `tests/**`, App. B B11), `.gitignore` extended (`*.cer`, `*.wapproj.user`, `AppPackages/`), and all moved junk quarantined under `temp_folder_for_LLMs/auwtc_obsolete/`: two throwaway VS projects (one .NET-4.7.2-era `ActuallyUsableWorldTimeClock2` with old-style csproj/App.config/MainWindow.xaml.txt, one `net10.0-windows` scratch `ActuallyUsableWorldTimeZoneClock`), a v2-era design doc, VS state dirs, generated `bin/`/`obj/`. The relocated wapproj had drifted ahead of the last staged state (it carried a wizard-run and a `0.0.18.0`-manifest reference) — normalized back to the §15.3-consistent state. **Version reconciled (user ruling "A", 2026-11-03):** the manifest `Identity/Version` was reverted `0.0.18.0` → `0.0.16.0` to match `.csproj` `0.0.16` per §15.3; no build numbers were skipped (#17 was never consumed by any source change). |
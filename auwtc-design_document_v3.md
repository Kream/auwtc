# Design Document: World Time Zone Clock (System Tray)

> **Version:** 3.0
> **Supersedes:** `auwtc-design_document_v2.md`
> **What changed in this revision:** see Appendix A (Change Log) at the end of this document.

## 1. Overview

A small Windows 10 system tray application that displays the current 24-hour local time for three cities — Los Angeles (LAX), New York (JFK), and London (LHR) — in a minimalist, borderless popup. The popup is anchored above the tray icon and is toggled by clicking the icon. The app is a standalone executable (no installer) built with C# and WPF on .NET 8 LTS, published as a single self-contained file.

## 2. Purpose

Provide a lightweight, always-visible world clock in the system tray that shows the current time in three major cities, with no UI clutter and no runtime dependencies beyond the .NET runtime.

## 3. Scope

- **In scope:** System tray icon, borderless popup, 24-hour time display for LAX/JFK/LHR, single-instance enforcement, right-click exit, outside-click dismissal, static time snapshot at click time, standalone .exe.
- **Out of scope:** Startup logic (managed externally), taskbar entry, console window, complex UI, real-time time updates, custom icons (built-in icon is sufficient).

## 4. Non-Functional Requirements

- **Platform:** Windows 10 (and later).
- **Runtime:** .NET 8 LTS (self-contained).
- **Language:** C#.
- **Framework:** WPF (for future popup extensibility).
- **Tray icon:** Built-in system icon (no custom design required).
- **Popup style:** Borderless, no title bar, no close button, topmost, no focus steal.
- **Popup size:** 180 × 100 pixels (reasonable default).
- **Time format:** 24-hour `HH:mm` (e.g., `17:13`).
- **City order:** LAX, JFK, LHR (one line each).
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
  - `GetLocalTime(City city)` → `string` (24-hour time for that city at the moment of the call).
  - Uses `TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId))`.
  - Returns `HH:mm` in 24-hour format.

### 6.2 Business Logic

- **`ClockViewModel`**
  - `ObservableCollection<City>` — the list of cities in order (LAX, JFK, LHR).
  - `ObservableCollection<CityTimeRow>` — computed rows from `CityTimeProvider`.
  - Exposes a `Refresh()` method that recomputes all rows (called when the popup opens).
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
- **Context menu** — right-click on the tray icon shows an "Exit" item.
- **Keyboard shortcut** — `Esc` closes the popup.

## 7. UI Design

### 7.1 Tray Icon

- Use the built-in `SystemIcons.Application` as the tray icon (and as the window icon). No custom icon file is required.
- The icon comes from the system at runtime; nothing needs to be embedded or shipped alongside the exe.

### 7.2 Popup Window

- **Size:** 180 × 100 pixels (reasonable default; enough for three rows of text).
- **Layout:** A three-row WPF `Grid` (or an `ItemsControl` bound to `CityTimeRow`, see §13) with three rows:
  - Row 1: `LAX` + time
  - Row 2: `JFK` + time
  - Row 3: `LHR` + time
- **Text:** City name left-aligned, time right-aligned, in a 12 pt font. Consolas 12 pt (monospaced) is recommended for clean column alignment; Segoe UI 12 pt is acceptable if the `Grid` columns use fixed widths.
- **No border, no title bar, no close button.**
- **Positioning:** Anchored above the tray icon — the popup's bottom edge is 8 pixels above the icon's top edge, horizontally centered on the icon, and clamped to the working area of the icon's monitor so it never overflows a screen edge (exact formulas in §9).

### 7.3 Visual Style

- Minimalist: plain background, no decorations.
- If desired, a subtle drop shadow or rounded corners can be added later (future-proofed by the data-driven design).

## 8. Tray Icon & Notification

- Use `Shell_NotifyIcon` (P/Invoke) from a hidden helper window.
- **Icon rectangle:** `Shell_NotifyIconGetRect` returns the icon's screen rectangle.
- **Anchor:** The popup is placed relative to that rectangle (see 7.2).
- **Icon lifecycle:** The icon is created on startup and destroyed when the app exits.

## 9. Positioning & Layout

- **Anchor edge:** The popup's bottom edge is placed 8 pixels above the top edge of the tray icon rectangle.
- **Horizontal alignment:** The popup's horizontal center is aligned to the tray icon's horizontal center.
- **Clamping** (both coordinates, always within the working area of the monitor that contains the tray icon):
  - `iconCenterX = iconRect.Left + iconRect.Width / 2`
  - `popup.Left = clamp(iconCenterX - popup.Width / 2, WorkingArea.Left, WorkingArea.Right - popup.Width)`
  - `popup.Top = clamp(iconRect.Top - popup.Height - 8, WorkingArea.Top, WorkingArea.Bottom - popup.Height)`
- **Multi-monitor:** The popup is always placed on the same monitor as the tray icon (the icon's screen coordinates determine the popup's monitor).

## 10. Interaction Flow

### 10.1 Open

1. **Trigger:** Left-click on the tray icon.
2. **Single instance:** If another instance is already running (mutex `Local\WorldTimeZoneClock` is held), the new process exits immediately.
3. **Snapshot:** `ClockViewModel.Refresh()` calls `CityTimeProvider` for each city in order (LAX → JFK → LHR) and fills the `CityTimeRow` collection.
4. **Position:** Popup is anchored above the tray icon per §9.
5. **Show:** Popup is shown topmost, without activating (no focus steal, no taskbar entry).

### 10.2 Toggle & Close

- Left-click on the tray icon while the popup is open **closes** it (toggle behavior).
- **Outside click** (on the taskbar, another application's window, the desktop, or anywhere outside the popup) closes the popup.
- **Esc** closes the popup.
- **Right-click on the tray icon** → context menu with a single **Exit** item → application exits.
- Closing the popup does **not** exit the app; the tray icon remains.

### 10.3 Refresh Behavior

- Time values are a **snapshot taken at the moment of the tray click**; the displayed time does not update while the popup is open.
- Each subsequent open of the popup takes a fresh snapshot.

## 11. Time Handling

- **Source:** `DateTime.Now` (local machine time) converted per city.
- **Conversion:** `TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId))`.
- **IANA IDs on Windows:** On .NET 6+ (and therefore .NET 8), `TimeZoneInfo.FindSystemTimeZoneById` natively accepts IANA IDs (e.g., `America/Los_Angeles`) — Windows has an ICU-based IANA↔Windows mapping built into the runtime. No manual mapping table is needed.
- **DST:** Handled automatically by `TimeZoneInfo` for the snapshot date/time.
- **Formatting:** `HH:mm` (24-hour, zero-padded, invariant culture so it does not depend on the user's locale).
- **City order:** Always LAX, JFK, LHR.

## 12. Error Handling

- **Unknown/invalid `TimeZoneId`:** `CityTimeProvider` wraps `FindSystemTimeZoneById`/`ConvertTime` in a try/catch and returns the string `"--:--"` for that row; the popup still shows all three rows.
- **Mutex acquisition failure:** The second process exits silently with code 0 (by design, not an error path).
- **`Shell_NotifyIconGetRect` failure:** Fall back to the bottom-right corner of the primary monitor's working area (minus an 8 px margin) so the popup is still visible near the tray.
- **Icon creation failure:** Show a message box and exit (the app has no purpose without the tray icon).
- **Logging:** None (out of scope).

## 13. Future Extensibility

- **More cities:** Add a `City` entry (name, IANA ID, order); rows render automatically if the popup uses an `ItemsControl` bound to the `CityTimeRow` collection.
- **Richer rows:** Extend `CityTimeRow` (e.g., date, day/night indicator, seconds) without changing the rendering pipeline.
- **Live updates:** A `DispatcherTimer` could re-call `Refresh()` while the popup is open (out of scope for v3).
- **Custom icon/skin:** Replace the built-in `SystemIcons.Application` bitmap with an embedded resource.
- **Persistence:** None needed; city list is currently hard-coded in the data layer.

## 14. Glossary

- **DST:** Daylight saving time.
- **IANA time zone ID:** A portable time zone identifier from the Olson/tz database (e.g., `America/Los_Angeles`), accepted directly by `TimeZoneInfo.FindSystemTimeZoneById` on .NET 6+ (including .NET 8).
- **TimeZoneInfo:** .NET class for timezone-aware time conversion.
- **P/Invoke:** Platform Invocation Services for calling native Windows APIs (e.g., `Shell_NotifyIcon`, `Shell_NotifyIconGetRect`).
- **WS_EX_NOACTIVATE:** Extended window style that prevents a window from receiving keyboard focus when clicked/shown.

## Appendix A — Change Log (v2 → v3)

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

> Note: §5 names example IDEs only; the design is IDE-agnostic. Use whichever toolchain the team standardizes on.

## Appendix B — Implementation Record (post-v3.0, through build #8)

> Deliberate deltas and fixes made during construction against v3.0. The design
> sections above remain the specification; where reality forced a change, it is
> recorded here. Future entries go above the `<!-- APPEND-POINT -->` marker.

| # | Area | Record |
|---|------|--------|
| B1 | Interop hygiene | All P/Invoke centralized in `TrayInterop.cs` (consumed via `using static`); `HWND_TOPMOST`/`HWND_NOTOPMOST` are `static readonly IntPtr` (C# forbids `const IntPtr`); `Shell_NotifyIcon` uses the W variant with `CharSet.Unicode` after A-variant tooltip garbling was observed and fixed. |
| B2 | Crash log | §12 kept application logging out of scope, but a diagnostic crash log was added as an implementation aid: `DispatcherUnhandledException` + `AppDomain.UnhandledException` append full stacks to `crash.log` next to the exe (fallback `%TEMP%\WorldTimeZoneClock.crash.log`); handlers never set `Handled=true`. Validated end-to-end on the left-click NRE. |
| B3 | Left-click toggle | Popup toggles on left-click with a 300 ms re-open grace (`ToggleGraceMs`) so the dismissing click cannot immediately re-open the popup. Esc dismissal routes through `NIM_SETFOCUS` → `WM_TRAYICON` (0x8001). |
| B4 | Tooltip | `NOTIFYICON_VERSION_3` + W variant; tooltip verified on build #5. |
| B5 | Context menu under taskbar | With a right-edge vertical taskbar the Exit menu rendered UNDER the taskbar. Z-band fixes (owner lifted to `HWND_TOPMOST`, foreground juggling) all failed — structural cause: `TrackPopupMenu`'s default work-area restriction applies only when the cursor is inside the work area, and a taskbar cursor is outside it; no horizontal alignment flag existed either. Fix (build #8): `TPM_WORKAREA` added to uFlags → menu clamped inside the work area. **Validated user-side (T1 pass).** `TPM_RIGHTALIGN` (0x0008) pre-staged in `TrayInterop` for a right-anchored variant if ever needed. |
| B6 | Versioning | Per-build protocol: patch digit of `VersionPrefix` = build number, bumped only on source change; `InformationalVersion` carries a build tag (e.g. `0.0.8-build8-tpm-workarea`); `StampBuildInfo` target writes `build-info.txt` (version / built-UTC / config / rid) next to the exe after every build — machine-readable proof of what is compiled. |
| B7 | Build #8 validation | Green compile (0W/0E, 4.59 s), dll 24,576 B; smoke round 3: T1 (Exit menu above right-edge taskbar + outside-click dismiss) PASS, T2 (Esc close → immediate re-open not eaten by the 300 ms grace) PASS — first Esc exercise. Menu ladder steps 2–3 not needed. |

<!-- APPEND-POINT -->

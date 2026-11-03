# Design Document: World Time Zone Clock (System Tray)

> **Version:** 4.2
> **Supersedes:** `auwtc-design_document_v4_1.md`
> **What changed in this revision:** see Appendix A (Change Log) at the end of this document. The full revision history (v2 → v3 → v3.1 → v4 → v4.1) is preserved in Appendix A and in the git history of this repository.

## 1. Overview

A small Windows 10 system tray application that displays the current 24-hour local time for three cities — Los Angeles (LAX), New York (JFK), and London (LHR) — in a minimalist, borderless popup. The popup is anchored above the tray icon and is toggled by clicking the icon. The app is a standalone executable (no installer) built with C# and WPF on .NET 8 LTS, published as a single self-contained file.

## 2. Purpose

Provide a lightweight, always-visible world clock in the system tray that shows the current time in three major cities, with no UI clutter and no runtime dependencies beyond the .NET runtime.

## 3. Scope

- **In scope:** System tray icon, borderless popup, 24-hour time display for LAX/JFK/LHR, single-instance enforcement, right-click exit, outside-click dismissal, static time snapshot at click time, snapshot-consistency unit tests, standalone .exe.
- **Out of scope:** Startup logic (managed externally), taskbar entry, console window, complex UI, real-time time updates, custom icons (built-in icon is sufficient).

## 4. Non-Functional Requirements

- **Platform:** Windows 10 (and and later).
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
  - Constructor: optional `Func<DateTimeOffset>? clock` — omitted/null = the real clock (`DateTimeOffset.Now`), so the production wiring in `App` is unchanged. The clock is **only read through `Now`** — once per snapshot (§10.1/§10.3); a read per city could straddle a minute boundary and display mutually inconsistent rows.
  - `Now` (DateTimeOffset): the one clock read; called once per popup open (build #11, App. B B11).
  - `GetLocalTime(City city, DateTimeOffset instant)` → `string`: the city's wall-clock time **at the given instant**, formatted as 24-hour `HH:mm`.
  - `GetLocalTimes(IEnumerable<City> cities, DateTimeOffset instant)` → `IReadOnlyList<CityTimeRow>`: batch conversion of every city from **one shared instant**, preserving city order (§10.1). A bad zone degrades only its own row (§12).
  - Conversion: `TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId))` — the `DateTimeOffset` overload converts the instant (via its embedded offset) to the zone's wall clock; DST handling is automatic.
  - There is **no** clock-reading parameterless overload: the per-city `GetLocalTime(City)` of build #9 re-read the clock per call and was the §10.3 race vector; it was removed (App. B B10).

### 6.2 Business Logic

- **`ClockViewModel`**
  - Constructor: optional `Func<DateTimeOffset>? clock`, passed through to `CityTimeProvider` (omitted/null = the real clock; the `App` wiring is unchanged).
  - `ObservableCollection<City>` — the list of cities in order (LAX, JFK, LHR).
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
- **Context menu** — right-click on the tray icon shows an "Exit" item.
- **Keyboard shortcut** — `Esc` closes the popup.

### 6.4 Test Suite (build #11)

- **Project:** `tests/WorldTimeZoneClock.Tests` — xUnit (`xunit` 2.9.2), `Microsoft.NET.Test.Sdk` 17.11.1, `xunit.runner.visualstudio` 2.8.2 (exact pins — the repo's first NuGet use), `net8.0-windows` + `UseWPF` (absorbs the main project's WPF framework references), `RuntimeIdentifier` `win-x64` + `SelfContained` `false` (matches the main project's RID-specific setup and avoids the RID-mismatch restore warning class), `IsPackable` `false`; references the `WinExe` main project directly (the data layer has no executable-specific surface); added to the `.slnx`. The main `.csproj` excludes `tests/**` from its default globs (`DefaultItemExcludes`): the main project's directory is the repository root, so the SDK's recursive default globs would otherwise sweep the nested test sources into the `WinExe` app, which has no test-framework references (CS0246 on the first compile of build #10; App. B B10/B11).
- **`ClockSnapshotTests`** (§10.3 conformance):
  1. **Probe clock** (advancing + read counter): `Refresh()` performs exactly **one** clock read and every row derives from that **first** reading — with the old per-city reads, later rows jump a minute. The constant-clock tests below cannot detect this regression (a constant clock makes the race structurally unreproducible); the probe is the direct detector.
  2. **Constant boundary clocks**: `…:59.999` → LAX `12:59` / JFK `15:59` / LHR `20:59`, and `…:00.001` → `13:00` / `16:00` / `21:00` (one open = one mutually consistent triple).
  3. **Fresh snapshot per open**: a second `Refresh()` takes its own instant (read count 2, rows change).
  4. **DST edges** (instant semantics): spring-forward gap `02:00` does not exist → `03:00`; fall-back first `01:59` (DST) / second `01:00` (standard) — `ConvertTime(DateTimeOffset, tz)` never throws on gap/ambiguity because the input is an absolute instant.
  5. **§12 degradation**: an invalid zone blanks **only** its own row; the row count and the other rows are unaffected.

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
- **Text:** City name left-aligned, time right-aligned, in a 12 pt font (WPF `FontSize` is in device-independent pixels, so 12 pt = `FontSize 16`). Consolas 12 pt (monospaced) is recommended for clean column alignment; Segoe UI 12 pt is acceptable if the `Grid` columns use fixed widths.
- **No border, no title bar, no close button.**
- **Positioning:** Anchored above the tray icon — the popup's bottom edge is 8 pixels above the icon's top edge, horizontally centered on the icon, and clamped to the working area of the icon's monitor so it never overflows a screen edge (exact formulas in §9).

### 7.3 Visual Style

- Minimalist: plain background, no decorations.
- If desired, a subtle drop shadow or rounded corners can be added later (future-proofed by the data-driven design).

## 8. Tray Icon & Notification

- Use `Shell_NotifyIcon` (P/Invoke) from a hidden helper window.
- **Icon rectangle:** `Shell_NotifyIconGetRect` returns the icon's screen rectangle.
- **Anchor:** The popup is placed relative to that rectangle (see §7.2).
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
3. **Snapshot:** `ClockViewModel.Refresh()` captures **one instant** (the injected clock's reading — the real clock on the default wiring) at the moment of the click and converts each city in order (LAX → JFK → LHR) **from that single instant**, filling the `CityTimeRow` collection.
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
- **All rows of one open derive from one identical instant** (§10.1): they can never straddle a minute boundary or otherwise disagree with each other.
- Each subsequent open of the popup takes a fresh snapshot.

## 11. Time Handling

- **Source:** the injected clock — a `Func<DateTimeOffset>`, default `DateTimeOffset.Now` (§6.1) — read **exactly once per snapshot** (§10.1): `ClockViewModel.Refresh()` captures the reading via `CityTimeProvider.Now` and passes the single instant to every city conversion. The default real clock is the local machine time; any offset-carrying reading (`Now` or `UtcNow`-shaped) is equivalent because the conversion operates on the instant via the offset (§14).
- **Conversion:** `TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId))` — the `DateTimeOffset` overload converts the instant to the destination zone's wall clock.
- **IANA IDs on Windows:** On .NET 6+ (and therefore .NET 8), `TimeZoneInfo.FindSystemTimeZoneById` natively accepts IANA IDs (e.g., `America/Los_Angeles`) — Windows has an ICU-based IANA↔Windows mapping built into the runtime. No manual mapping table is needed.
- **DST:** Handled automatically by `TimeZoneInfo` for the snapshot date/time; the spring-forward gap and fall-back ambiguity resolve deterministically from the absolute instant (test case §6.4.4).
- **Formatting:** `HH:mm` (24-hour, zero-padded, invariant culture so it does not depend on the user's locale).
- **City order:** Always LAX, JFK, LHR.

## 12. Error Handling

- **Unknown/invalid `TimeZoneId`:** `CityTimeProvider` wraps `FindSystemTimeZoneById`/`ConvertTime` per city in a try/catch and returns the string `"--:--"` for that row; the popup still shows all three rows.
- **Mutex acquisition failure:** The second process exits silently with code 0 (by design, not an error path).
- **`Shell_NotifyIconGetRect` failure:** Fall back to the bottom-right corner of the primary monitor's working area (minus an 8 px margin) so the popup is still visible near the tray.
- **Icon creation failure:** Show a message box and exit (the app has no purpose without the tray icon).
- **Logging:** None (out of scope).

## 13. Future Extensibility

- **More cities:** Add a `City` entry (name, IANA ID, order); rows render automatically if the popup uses an `ItemsControl` bound to the `CityTimeRow` collection.
- **Richer rows:** Extend `CityTimeRow` (e.g., date, day/night indicator, seconds) without changing the rendering pipeline.
- **Live updates:** A `DispatcherTimer` could re-call `Refresh()` while the popup is open (out of scope).
- **Custom icon/skin:** Replace the built-in `SystemIcons.Application` bitmap with an embedded resource.
- **Persistence:** None needed; city list is currently hard-coded in the data layer.

## 14. Glossary

- **DST:** Daylight saving time.
- **DateTimeOffset:** .NET structure representing a date/time instant plus its UTC offset; `TimeZoneInfo.ConvertTime(DateTimeOffset, TimeZoneInfo)` converts the instant to a destination zone's wall clock. The injectable clock (§6.1) is a `Func<DateTimeOffset>`.
- **IANA time zone ID:** A portable time zone identifier from the Olson/tz database (e.g., `America/Los_Angeles`), accepted directly by `TimeZoneInfo.FindSystemTimeZoneById` on .NET 6+ (including .NET 8).
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

## Appendix A — Change Log (v2 → v4.2)

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

> Note: §5 names example IDEs only; the design is IDE-agnostic. Use whichever toolchain the team standardizes on.

## Appendix B — Implementation Record (post-v3.0, through build #11)

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
| B6 | Versioning | Per-build protocol: patch digit of `VersionPrefix` = build number, bumped only on source change; `InformationalVersion` carries a build tag (e.g. `0.0.8-build8-tpm-workarea`); `StampBuildInfo` target writes `build-info.txt` (version / built-UTC / config / rid) next to the exe after every build — machine-readable proof of what is compiled. |
| B7 | Build #8 validation | Green compile (0W/0E, 4.59 s), dll 24,576 B; smoke round 3: T1 (Exit menu above right-edge taskbar + outside-click dismiss) PASS, T2 (Esc close → immediate re-open not eaten by the 300 ms grace) PASS — first Esc exercise. Menu ladder steps 2–3 not needed. |
| B8 | Injectable clock (build #9, superseded by B10) | `CityTimeProvider`/`ClockViewModel` accepted an optional `Func<DateTimeOffset>? clock` (default the real clock) and `GetLocalTime` read the clock once per call — per-city reads preserved so the minute-boundary straddle was deterministically testable. The injection point survives into build #10 (`CityTimeProvider.Now`); the per-city read structure did not (see B10). |
| B9 | Aborted build #9 (never compiled) | The clock-injection session staged `0.0.9-build9-injectable-clock` sources + csproj but no compile/publish ever ran; the next session superseded the per-city-read design before any build #9 binary existed. Number consumed per §15.2 rule 7; recorded, never reused or skipped. |
| B10 | Single-instant snapshot — FAILED compile (build #10, never built) | The snapshot change-set — `Refresh()` captures ONE instant via `CityTimeProvider.Now` and converts all rows via `GetLocalTimes(Cities, instant)`; parameterless clock-reading `GetLocalTime(City)` removed (sole caller `ClockViewModel`, verified against the full tree); first test project `tests/WorldTimeZoneClock.Tests` (xUnit 2.9.2, Test.Sdk 17.11.1, runner 2.8.2 — exact pins, repo's first NuGet use; net8.0-windows + UseWPF, RID win-x64 + SelfContained=false, IsPackable=false; added to `.slnx`) — was staged as `0.0.10-build10-single-instant-snapshot`, but its FIRST compile (`dotnet publish -c Release -r win-x64 --self-contained false <main csproj>`) FAILED: 27× CS0246 (`Xunit`/`Fact`/`Theory`/`InlineData`), every error in `tests\WorldTimeZoneClock.Tests\ClockSnapshotTests.cs` and tagged `[<main csproj>]`. Root cause: the main project's directory IS the repository root, so the SDK's recursive default globs swept the nested test sources into the `WinExe` app, which has no test-framework references — not a restore/NuGet problem. No binary, no `build-info.txt` receipt. Number consumed per §15.2 rule 7 (failed build); recorded, never reused. |
| B11 | Test glob exclusion (build #11) | Fix: `<DefaultItemExcludes>$(DefaultItemExcludes);tests/**</DefaultItemExcludes>` in the main `.csproj` — `DefaultItemExcludes` is consulted by every default glob (`Compile`, `None`, `Page`, `Resource`, `EmbeddedResource`), so one line keeps the whole `tests` subtree (including any future `tests/**/obj` generated sources) out of the app compilation; property evaluation precedes item evaluation, so the in-body setting is honored by the SDK globs. Test project unchanged. The B10 snapshot change-set (feature sources identical to the failed staging) lands as part of this build; version → `0.0.11-build11-test-glob-exclusion` — fresh slug per §15.2 step 3 describing this build's delta (the #10 slug `single-instant-snapshot` is retired). csproj re-read after edit: all three properties agree. Compile/test/publish receipts pending — authored in a no-shell session; §15.2 step 5 handed to the maintainer (§5 protocol publish command; `dotnet test` scoped to the test csproj — the `.slnx` format needs a .NET 9 SDK). |
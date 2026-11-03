# ActuallyUsableWorldTimeClock2 (WorldTimeZoneClock)

A minimal, actually-usable Windows tray world clock. Four cities (LAX / JFK / LHR / CHC),
one always-visible tray icon, a compact borderless popup, and a working Exit menu.

- **Runtime:** .NET 8, WPF, `net8.0-windows` (win-x64)
- **Assembly:** `WorldTimeZoneClock`
- **Version:** 0.0.14-build14-roster-agnostic-tests (patch digit = build number; bump only on source change)

## Features
- Tray icon (shell `NOTIFYICON_VERSION_3`) with left-click toggle popup showing
  local times for Los Angeles, New York, London, Christchurch (tz database names
  `America/Los_Angeles`, `America/New_York`, `Europe/London`, `Pacific/Auckland`).
- Popup: 180 DIP manual width × content-sized height (`SizeToContent="Height"`, build #13 —
  added cities render AND grow the popup; `UniformGrid Rows="3"` → vertical `StackPanel`),
  borderless, topmost, no-activate (`WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`),
  icon-anchored with work-area clamping using the MEASURED placement size
  (never the nominal constants); auto-dismisses on outside click / Esc.
- Right-click → Exit context menu (TrackPopupMenu with `TPM_WORKAREA` so it stays
  inside the work area even when the cursor is on a taskbar strip, e.g. a
  right-edge vertical taskbar).
- Single instance (named mutex `Local\WorldTimeZoneClock`; second launch exits silently).
- DPI (build #12): Per-Monitor V2 awareness declared in `app.manifest`; DIP is the canonical
  unit, and the ONE px→DIP conversion (`TrayInterop.PxToDip` + `GetDpiForWindow`, the popup's
  own window DPI) sits at the interop boundary — see the §9.1 units audit table in the design
  doc. Cursor→menu and hook-point↔popup-rect paths stay physical px end-to-end.
- Single-instant snapshot (build #10): `Refresh()` captures ONE instant (the injectable clock's
  single read per open, `CityTimeProvider.Now`) and converts every city from it
  (`GetLocalTimes(cities, instant)` → `ConvertTime(DateTimeOffset, tz)`) — the rows can never
  straddle a minute boundary. The clock is injectable (`Func<DateTimeOffset>`, default
  `DateTimeOffset.Now`), so tests drive the snapshot deterministically.
- Tests: `tests/WorldTimeZoneClock.Tests` (xUnit, 9 cases — **roster-agnostic**: add cities freely; the documented set is asserted by name) — probe-clock (exactly-one-read per open),
  `:59.999`/`:00.001` boundary pins, fresh-snapshot-per-open, DST edges, `--:--` degradation.
  Run `dotnet test tests\WorldTimeZoneClock.Tests\WorldTimeZoneClock.Tests.csproj` (scope the
  test project directly — the `.slnx` format needs a .NET 9 SDK). The main `.csproj` excludes
  `tests/**` from its default globs (`DefaultItemExcludes`) so the app never compiles the
  test sources (App. B B11).
- Crash logging: `DispatcherUnhandledException` + `AppDomain.UnhandledException` append
  full stacks to `crash.log` next to the exe (fallback `%TEMP%\WorldTimeZoneClock.crash.log`).
- Per-build stamp: `build-info.txt` written next to the exe on every build
  (`version=…;built=<UTC ISO-8601>;config=…;rid=…`).

## Build
```
dotnet build -c Debug -r win-x64
```

## Versioning protocol
- `VersionPrefix` / `FileVersion` / `InformationalVersion` live in the `.csproj`.
- Patch digit = internal build number; only bumped when source changes.
- `build-info.txt` in the output folder is the machine-readable build stamp.
- Check the exe via Explorer → Details, or:
  ```
  (Get-Item .\bin\Debug\net8.0-windows\win-x64\WorldTimeZoneClock.exe).VersionInfo
  ```

## Fallbacks (design doc §12)
- Unknown/unloaded time zone → `--:--` for that row.
- Second launch → silent exit 0.
- Icon rect lookup failure → popup placed bottom-right of primary work area (−8 px).
- Tray icon registration failure → MessageBox, then exit.

## License / status
GPL-3.0-only. Full license text is in `LICENSE`, verbatim from
<https://www.gnu.org/licenses/gpl-3.0.txt> (SPDX identifier: `GPL-3.0-only`).

Personal project. See `auwtc-design_document*.md` for the full design record
(§9 positioning, §12 fallbacks, §13 city-list notes).

# ActuallyUsableWorldTimeClock2 (WorldTimeZoneClock)

A minimal, actually-usable Windows tray world clock. Three cities (LAX / JFK / LHR),
one always-visible tray icon, a compact borderless popup, and a working Exit menu.

- **Runtime:** .NET 8, WPF, `net8.0-windows` (win-x64)
- **Assembly:** `WorldTimeZoneClock`
- **Version:** 0.0.8-build8-tpm-workarea (patch digit = build number; bump only on source change)

## Features
- Tray icon (shell `NOTIFYICON_VERSION_3`) with left-click toggle popup showing
  local times for Los Angeles, New York, London (tz database names
  `America/Los_Angeles`, `America/New_York`, `Europe/London`).
- Popup: 180×100, borderless, topmost, no-activate (`WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW`),
  icon-anchored with work-area clamping; auto-dismisses on outside click / focus loss / Esc.
- Right-click → Exit context menu (TrackPopupMenu with `TPM_WORKAREA` so it stays
  inside the work area even when the cursor is on a taskbar strip, e.g. a
  right-edge vertical taskbar).
- Single instance (named mutex `Local\WorldTimeZoneClock`; second launch exits silently).
- DPI-aware positioning (`HwndSource...TransformFromDevice` with `?? Matrix.Identity` guard).
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

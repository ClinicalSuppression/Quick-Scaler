# Windows Quick Scaler

Windows Quick Scaler is a small Windows desktop utility for changing display scaling and quickly applying saved scaling presets from its dashboard, the notification-area (system tray) icon, hotkeys, or the command line — built for switching between a desk and a couch ~10 feet away.

**Current version: 1.1**

## Features

- Shows each connected display's friendly name, native resolution, and **current scale**, with a relative layout map you can click (or use the keyboard on) to select targets.
- One-click **Desk** and **Couch** mode buttons (pin any preset to either), plus per-display and all-display scaling from 100% to 500%.
- **Revert safety net:** after applying, you get 15 seconds to revert to the previous scales — in the dashboard or via a notification popup when the window is hidden.
- Named presets you create yourself (e.g. "Couch Night"), with rename, delete (with confirmation), and duplicate-name protection. Presets persist in `%LOCALAPPDATA%\WindowsQuickScaler\presets.json` (versioned format, with `presets.json.bak` recovery if the file is corrupted; older plain-array files still load).
- Same presets in the **tray icon** right-click menu; double-click the tray icon to reopen the dashboard.
- **Hotkeys** (toggleable): `Ctrl+Alt+D` Desk, `Ctrl+Alt+C` Couch, `Ctrl+Alt+↑/↓` step the scale.
- **Command line:** `--preset`, `--scale`, `--desk`, `--couch`, `--tray`, `--export`/`--import` (see `--help`).
- **Auto-apply:** optionally apply a preset when displays are added/removed, and per-app profiles (e.g. `game.exe` → "Couch" preset) based on the foreground program.
- Detects display changes (plug in a TV, change resolution) and refreshes automatically; runs from Windows startup minimized to the tray if you enable it.
- First-run wizard that offers Desk (125%) / Couch (200%) presets, hotkeys, and startup registration.
- Single-instance guard, per-monitor DPI awareness (PerMonitorV2), and an error log at `%LOCALAPPDATA%\WindowsQuickScaler\app.log`.
- First-run default **100% — All monitors** preset when no presets exist yet.

## Requirements

- Windows 10 or later with active displays.
- For the framework-dependent build: the **.NET 8 Desktop Runtime** (x64). The self-contained publish profile (below) needs no runtime.
- Inno Setup 6 to compile the installer.
- The app does not require administrator privileges. Windows or a graphics driver may impose its own limitations on available scaling settings.

## Use

1. Launch Windows Quick Scaler (or press `Ctrl+Alt+D` / `Ctrl+Alt+C`).
2. The status bar shows the current scale of every display. Click monitor cards to choose targets (they default to selected).
3. Pick a **Target Scale** (pre-selected from the current scale), then **Apply to Selected** — or double-click a card to apply to that display only.
4. To save a preset, optionally type a name (e.g. "Couch Night") and click **Save Preset**; double-clicking a preset in the list applies it, as does right-clicking the tray icon.
5. Pin a preset with **Pin as Desk** / **Pin as Couch** so the big buttons (and hotkeys) use it.
6. After applying, use **Revert** within 15 seconds to undo, or **Keep** to confirm.

Scaling in Windows normally requires a sign-out to take visual effect; the app says so in the status bar. If a preset targets a disconnected display, it applies to the connected matches and reports the missing ones.

### Command line

```text
WindowsQuickScaler.exe                  open the dashboard
WindowsQuickScaler.exe --tray           start minimized to the tray
WindowsQuickScaler.exe --desk           apply the Desk preset (or 125% on all)
WindowsQuickScaler.exe --couch          apply the Couch preset (or 200% on all)
WindowsQuickScaler.exe --preset "Couch Night"
WindowsQuickScaler.exe --scale 200 --all
WindowsQuickScaler.exe --scale 200 --monitor 1,2     (1-based monitor numbers)
WindowsQuickScaler.exe --scale 150 --device "\\.\DISPLAY1"
WindowsQuickScaler.exe --preset "Desk" --exit         apply, then exit
WindowsQuickScaler.exe --export presets.json
WindowsQuickScaler.exe --import presets.json
WindowsQuickScaler.exe --help
```

### Settings and data files (all in `%LOCALAPPDATA%\WindowsQuickScaler\`)

| File | Contents |
| --- | --- |
| `presets.json` | Your presets (versioned; `presets.json.bak` is the previous good copy) |
| `settings.json` | Tray/startup/hotkey preferences, Desk/Couch pins, auto-apply and per-app profiles |
| `app.log` | Unhandled-error log (safe to delete) |

Back up `presets.json` to preserve or transfer presets, or use `--export`/`--import`.

## Build from source

Open a PowerShell terminal in the project directory:

```powershell
dotnet restore .\WindowsQuickScaler.slnx
dotnet build .\WindowsQuickScaler.slnx -c Release -warnaserror
dotnet run --project .\WindowsQuickScaler.Tests\WindowsQuickScaler.Tests.csproj -c Release
```

Publish a Windows x64 build (framework-dependent; needs the .NET 8 Desktop Runtime):

```powershell
dotnet publish .\WindowsQuickScaler.csproj -c Release -r win-x64 --self-contained false -warnaserror
```

Or publish a **self-contained single-file** build that needs no runtime, using the included publish profile:

```powershell
dotnet publish .\WindowsQuickScaler.csproj -p:PublishProfile=win-x64-selfcontained -warnaserror
```

## Build the installer

After publishing, compile `installer.iss` with Inno Setup 6:

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" .\installer.iss
```

The installer packages the x64 publish output, uses the app icon from `Assets\WindowsQuickScaler.ico`, warns if the .NET 8 Desktop Runtime is missing, and writes `WindowsQuickScaler_Setup_1.1.exe` to `Output\` in the project directory. Upgrades replace the existing installation in place (single Add/Remove entry).

## Project layout

| File | Purpose |
| --- | --- |
| `App.xaml`, `App.xaml.cs` | WPF entry point, single-instance guard, global error handling, `--help`. |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Dashboard, display selection, Desk/Couch modes, revert flow, tray menu, hotkeys, startup registration, CLI handling, auto-apply, per-app profiles, first-run wizard trigger. |
| `ScaleManager.cs` | Windows Display Configuration interop: current scale, range, friendly names, native size, and setting scale. |
| `PresetStore.cs` | Preset model, validation, versioned atomic persistence with `.bak` recovery, export/import. |
| `AppSettings.cs` | Preferences model and atomic `settings.json` persistence. |
| `CliArgs.cs` | Testable command-line parser. |
| `TrayMenuBuilder.cs` | Pure tray-menu ordering logic. |
| `NotificationPopup.cs` | Focus-stealing-free notification with an action button (Revert). |
| `PromptDialog.cs` | Rename dialog and first-run wizard window. |
| `app.manifest` | asInvoker trust level and Windows 10/11 compatibility (DPI mode is set via `ApplicationHighDpiMode`). |
| `WindowsQuickScaler.Tests\` | Dependency-free test harness (45 checks) covering validation, persistence, CLI parsing, and tray ordering. |
| `.github\workflows\build.yml` | CI: restore, build with `-warnaserror`, run tests, publish. |
| `Assets\WindowsQuickScaler.ico` | Multi-resolution application, tray, shortcut, and installer icon. |
| `installer.iss` | Inno Setup installer definition. |

## Data and troubleshooting

- If a preset file is invalid, the app tries the `.bak` copy and tells you; the corrupt file is left unchanged until you save a preset.
- If a preset targets a monitor that is disconnected, the app applies it to matching connected displays and reports the missing target.
- If Windows rejects a scale change, the app reports the display-specific error. The supported range depends on each display; scales of 350%+ ask for confirmation first.
- If the tray icon is hidden, open Windows **Settings → Personalization → Taskbar → Other system tray icons** and enable Windows Quick Scaler.
- If a hotkey doesn't work, another app may already use that combination; the app registers Desk/Couch/step hotkeys independently and reports conflicts in the status bar.
- If an extreme scale leaves the screen hard to read, use the 15-second **Revert** (or sign out/in as a last resort).

## License

Windows Quick Scaler is distributed under the [PolyForm Noncommercial License 1.0.0](./LICENSE), copyright (c) 2026 ClinicalSuppression.

The license permits use, modification, and distribution for permitted noncommercial purposes. Commercial use—including incorporating the software into a paid app—is not permitted under these terms. Contact the copyright holder to request a separate commercial license.

PolyForm Noncommercial is source-available and is not an OSI-approved open-source license. See the full terms in `LICENSE`. For commercial licensing inquiries, contact [ClinicalSuppression](https://github.com/ClinicalSuppression).

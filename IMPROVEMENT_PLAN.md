# WindowsQuickScaler — Audit & Improvement Plan

**Goal:** easily change Windows display scaling to stay usable whether sitting at a desk or on a couch ~10 feet away.
**Audit date:** 2026-11-06 · **Version audited:** 0.9 · **Status: all phases implemented and released as v1.1** (build clean, 45/45 tests pass, installer + self-contained publish verified — see §5).

---

## 1. What already works well (keep it)

- `ScaleManager.cs` — real Win32 `QueryDisplayConfig` / `DisplayConfigGet/SetDeviceInfo` interop, validates per-display scale range, retries enumeration (`ErrorInsufficientBuffer`), clear per-display error messages.
- `PresetStore.cs` — atomic save (temp file + `File.Move` overwrite), validates before saving, does not silently overwrite a corrupt file on load.
- `MainWindow.xaml.cs` — applies per-monitor, reports partial failures, reports disconnected targets, handles tray minimize.
- `README.md`, `installer.iss` — accurate build/install docs, per-user install (no admin).

## 2. Findings — errors & issues

### P0 — Critical / correctness

| # | Issue | Where | Evidence / impact |
|---|---|---|---|
| C1 | **No current-scale readout anywhere.** The combo defaults to `100%` (`SelectedIndex="0"`), so one click on *Apply Selected* can silently reset a 150% user to 100%. | `MainWindow.xaml:49-55`, `MainWindow.xaml.cs:231-243` | No query of `CurrentScaleRelative` in `ScaleManager` |
| C2 | **No revert / confirmation.** Windows itself gives a 15-second "Keep changes?" revert. At 300-500% (allowed by `ScaleManager`/`PresetStore`) the user may be unable to see the dialog and must sign out. | `MainWindow.xaml.cs:245-331` | This is the #1 risk for the couch use-case |
| C3 | **Tray menu rebuild logic is fragile.** Starts `[Open, Sep, Exit]` (count 3); `while (Count > 2) RemoveAt(1)` deletes the original separator, then presets are inserted at `Count-1`. Works by accident, breaks if menu items/order change. | `MainWindow.xaml.cs:53-60, 142-165` | Use a named placeholder item / submenu instead |
| C4 | **Startup checkbox is never initialised from the registry.** It always starts unchecked even when the `Run` key exists, so users cannot see/accurately toggle state. Startup also opens the full window (no `--tray` flag). | `MainWindow.xaml:29`, `MainWindow.xaml.cs:425-450` | Read registry on load; add start-minimised arg |
| C5 | **No display-change refresh.** Plugging in / turning on a TV, or changing resolution, is never detected (`WM_DISPLAYCHANGE` / `Screen` refresh). Monitor cards go stale until restart — exactly the couch/TV scenario. | `MainWindow.xaml.cs:72-120` (only loaded once in ctor) | Subscribe to `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` |
| C6 | **"Minimize to tray" preference is not persisted** despite label "(Default: On)". Every launch resets it. | `MainWindow.xaml:28`, `Window_Closing:207-215` | Persist to `settings.json` alongside `presets.json` |
| C7 | **Constructor can crash on icon load.** `ExtractAssociatedIcon` throws in `MainWindow()` with no fallback; no `DispatcherUnhandledException` handler in `App.xaml.cs`. | `MainWindow.xaml.cs:39-44`, `App.xaml.cs` (empty) | Fall back to `SystemIcons.Application`, add global handler + logging |

### P1 — Important

| # | Issue | Where | Impact |
|---|---|---|---|
| I1 | **No custom preset names.** Names are auto-generated (`150% — Monitor 1, Monitor 2`), duplicates allowed, no rename/edit. "Desk 125%" / "Couch 200%" — the core value prop — is impossible. | `BtnAddPreset_Click:342-379`, `CreatePresetName:396-405` | UI never sets the `Name` field `PresetStore` already supports |
| I2 | **Monitor identity is unreliable.** Friendly name is `DISPLAY1 → "Monitor 1"`; size shown is `Screen.Bounds` (logical px, now scaled), not native resolution — at 200% a 4K screen reads "1920x1080". Presets keyed only on `\\.\DISPLAYn`, which can re-map on reconnect. | `MainWindow.xaml.cs:77-85` | Query EDID target name + native mode via `DisplayConfigGetDeviceInfo` |
| I3 | **Ambiguous "Apply Selected".** If a preset is highlighted it applies the preset; otherwise it applies combo+selected cards. Nothing in the UI says which mode is armed. | `BtnApply_Click:217-229` | Separate *Apply preset* / *Apply scale* buttons, or one button with explicit target label |
| I4 | **Scaling range mismatch.** Presets/`ScaleManager` allow 100-500%, the dropdown only offers 100-200%, the error text can promise up to 500% that the UI cannot produce. | `ScaleManager.cs:18-19`, `PresetStore.cs:104`, `MainWindow.xaml:49-55` | Generate dropdown from `SupportedScalePercentages` |
| I5 | **No single-instance guard.** Two instances → two tray icons, race on `presets.json` (last write wins). | whole app | Named `Mutex` + "already running" activation |
| I6 | **Tray clicks can show modal dialogs behind a hidden window** (`MessageBox.Show` with no owner after hide-to-tray); success feedback is only a 2.5s balloon tip — far too short from 10 ft, no in-window status. | `MainWindow.xaml.cs:281-331, 207-215` | Owner-less alerts or Toast notifications with actions (see §4) |
| I7 | **Installer has no `AppId`.** Upgrades stack duplicate Add/Remove entries. No .NET 8 Desktop Runtime check for the framework-dependent publish; version hard-coded in 3 places. | `installer.iss`, `WindowsQuickScaler.csproj:5-8` | Add `AppId`, `MinVersion`, runtime bootstrapper check, single source of version |
| I8 | **Accessibility / input.** Monitor cards respond only to `MouseLeftButtonUp` — no keyboard focus, Tab, Space/Enter, touch, or screen-reader support; state shown by colour alone; label font 9pt (ironic for a readability tool); fixed 900×560 window, left panel locked at 330px, `PresetsListBox` fixed 190px with no scroll — crops at high DPI / many presets. | `MainWindow.xaml`, `MainWindow.xaml.cs:88-117, 196-205` | Real keyboard-operable controls, focus visuals, resizable/scrollable layout |

### P2 — Polish / maintainability

| # | Issue | Where |
|---|---|---|
| P1 | `Validate()` crashes with `NullReferenceException` on `null` preset elements in hand-edited JSON (`[null]`); no duplicate names/device names, no version field for future schema migration. | `PresetStore.cs:99-111` |
| P2 | Duplicate `DeviceNames` in a preset → target applied twice. `ToDictionary` throws if duplicate device names ever appear. | `MainWindow.xaml.cs:266-275` |
| P3 | New `SolidColorBrush` allocated on every toggle; `ExtractAssociatedIcon` handle / tray `Icon` disposal order in `Closed` only — a crash leaves a ghost tray icon until hover. | `MainWindow.xaml.cs:64-69, 196-205` |
| P4 | Zero tests, zero CI, no `--help`/CLI, no logging. Manual testing only. | repo layout |
| P5 | No DPI-awareness manifest (`PerMonitorV2`); resolution/canvas layout clamps cards to ≥70×50px, breaking relative proportions for 3+ monitors; `ClipToBounds` hides overflow. | `LayoutMonitorCards:167-194` |
| P6 | Delete preset has no confirm/undo; silent no-op when nothing selected; `My.Computer`… (no-op feedback generally). | `BtnDeletePreset_Click:381-394` |
| P7 | `WindowsQuickScaler_Setup_0.9.exe` publish artifact sits in the project root (git-ignored, but confusing); LICENSE copy is bundled but README license section is fine. | root / `installer.iss:19` |

## 3. UX/UI redesign — Desk ⇄ Couch

Design principle: **the couch path must need ≤2 inputs and work from 10 ft with a remote/keyboard; the desk path stays precise.**

### 3.1 Layout (top → bottom)

1. **Header status bar** — "Current: Main 150% · TV 100%" + *Last applied … [Revert]* + persistent banner "You may need to sign out for changes to take effect."
2. **Two big mode buttons (the hero control, ≥48px targets):** `🖥 Desk 125%` and `🛋 Couch 200%` — one click applies, with the revert countdown (C2). Customisable labels bound to two pinned presets.
3. **Monitor cards** — real `ToggleButton`s: name (EDID) + `3840×2160 native · now 150%`, check-badge *and* colour for selected state, keyboard/arrow navigation, double-click = apply to that monitor alone.
4. **Presets list** — custom `ListBoxItem` showing `Name (200% · All)` + *Apply / Rename / Delete* on hover/focus; double-click or Enter applies; text box for a custom name in *Save Preset* (defaults to the auto-name); duplicate detection warning.
5. **Scale picker** — generated from supported percentages, grouped as `Desk (100-150) / Couch (175-300) / Extreme (350-500⚠)`, pre-selected from the *current* scale; slider alternative for quick scrubbing.
6. **Settings row** — tray/startup/minimise checkboxes persisted (C4, C6), each with a help tooltip that explains *why*.

### 3.2 Couch interactions

- **Hotkeys** (configurable, on by default): `Ctrl+Alt+D` = Desk, `Ctrl+Alt+C` = Couch, `Ctrl+Alt+↑/↓` step scale. Registered via `RegisterHotKey`; conflict → warn, never steal silently.
- **CLI:** `--tray`, `--preset "Couch 200%"`, `--scale 200 --all|--monitor 1` — enables shortcuts, scripts, game launchers, remote apps.
- **Toast notifications** (`Windows.UI.Notifications` / CommunityToolkit) with **Apply / Revert** action buttons instead of 2.5s balloon tips.
- **Auto-apply on display change (opt-in per preset):** "When TV connects → Couch 200%"; "When TV disconnects → Desk 125%". Built on C5's `DisplaySettingsChanged`.
- **First-run wizard:** detects monitors, suggests Desk/Couch presets from current scale, offers to pin hotkeys and add to startup (minimised).

### 3.3 Accessibility

Focus-visible outlines, 12pt minimum UI text, high-contrast + colour-blind-safe selected state (icon + border + text), UI Automation names on every control, full keyboard flow from tray to apply, responsive layout that survives 300% app scaling.

## 4. Technical plan (phased)

### Phase 0 — Quick wins (½–1 day)
1. Generate scale dropdown from `ScaleManager.SupportedScalePercentages`; pre-select **current** scale once C1 is done (I4).
2. Initialise `ChkStartup` from registry; persist `ChkMinimizeToTray` in `settings.json` (C4, C6).
3. Split Apply into two explicit buttons; disable controls while applying; add in-window status text (I3, I6).
4. Add `AppId` + .NET runtime check + version from csproj to Inno; stop writing the setup exe into the repo root (I7).
5. `Validate()` null-element guard + duplicate checks (P1, P2).

### Phase 1 — Correctness & robustness (2–3 days)
6. Query & show **current scale + native resolution + EDID name** per monitor; unify detection on `QueryDisplayConfig` instead of `Screen.AllScreens` (C1, I2).
7. **Revert/confirm flow:** apply → 15s countdown toast/in-window banner → "Keep / Revert" (safety net for extreme scales) (C2).
8. `DisplaySettingsChanged` refresh + tray-menu placeholder refactor (C3, C5).
9. Single-instance mutex; global exception handler + simple file log; icon fallback (C7, I5, P3).
10. DPI-awareness manifest; adaptive, scrollable, keyboard-operable layout (I8, P5). Delete-confirm + empty-selection feedback (P6).

### Phase 2 — Couch UX (3–4 days)
11. Hero Desk/Couch buttons + custom preset naming/rename + duplicate detection (I1, §3.1).
12. Hotkeys + CLI args + Toast actions (§3.2).
13. First-run wizard (§3.2).

### Phase 3 — Power features (1–2 weeks, optional)
14. Auto-apply rules on display connect/disconnect and per-app foreground profiles.
15. Preset import/export (share `presets.json`), schema versioning + `.bak` recovery on corrupt file.
16. Self-contained single-file publish option (no .NET runtime prerequisite) + code-signing note.

### Phase 4 — Release hygiene
17. First unit tests (`PresetStore.Validate`, name generation, tray-menu ordering) + GitHub Actions CI (build + `warnaserror`).
18. Bump to 1.0, refresh README screenshots, changelog.

## 5. Acceptance criteria (definition of done) — all met in v1.1

- [x] From the couch, `Ctrl+Alt+C` or one click applies Couch 200%, shows a big on-screen confirmation, and **Revert works without signing out** (15s revert window in-dashboard and via notification popup when hidden).
- [x] Current scale is correct for every monitor immediately after launch and after plugging/unplugging a display (live `GetDpiScale` query + `DisplaySettingsChanged` refresh).
- [x] Corrupt `presets.json` never loses the user's presets (`.bak` recovery + untouched corrupt file); invalid entries produce a specific, fixable message (per-field validation).
- [x] Full flow (select monitors → apply → custom-name a preset → apply from tray) is completable keyboard-only (ToggleButton cards, Enter/Delete in preset list, hotkeys).
- [x] `dotnet build -c Release -warnaserror` stays clean (0 warnings, 0 errors); installer has an `AppId` and upgrades in place (single Add/Remove entry), verified by compiling `installer.iss` with Inno Setup 6.
- [x] Bonus verification: framework-dependent and self-contained single-file publishes succeed; `WindowsQuickScaler.Tests` passes 45/45 checks; CI workflow added.

## 6. Suggested execution order

Phase 0 → Phase 1 (safety/revert + refresh first — these fix the "stuck at 500%" and "stale TV" scenarios) → Phase 2.

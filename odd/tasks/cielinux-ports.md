# CieLinux ports

Locator: `odd/tasks/cielinux-ports.md` · Engram mirror: `odd/cielinux-ports/tasks` · Branch: `feat/cielinux-ports` (from `main` @ `573d8c5`)

## Objective

Port the recent CieLinux tray, alert-title and mini-dodge changes to CielWin, then refresh CieLinux's
`docs/cielwin-portability.md` so it reflects what CielWin now has.

## Problem / why

CieLinux gained features after the last portability assessment (`0c19e24`). An analysis of 13 commits found 6
already in CielWin, 3 Linux-only (layer-shell bottom layer, systemd restart, dodge IPC wedge guard) and these to port:

- `6f7f3b6` tray: "Wallpaper mode" -> "Scene Mode", "Scene wallpaper" -> "Scene Wallpaper", "Mini window" -> "Scene Mini".
- `e19b3c8` alerts: the failure title reaches each scene's reference ring (`sceneAlertTitleLimits` per scene hook).
- `5a92a38` raphael: the alert see-through stamps the gold ring sprites (`sprites.outlineGlyphsGold`).
- `b0f16a4` + `c549808` mini: the mini glides aside when the cursor approaches; on Windows by polling
  `GetCursorPos` (the mini is click-through, `WS_EX_TRANSPARENT`), so only the cancel/epoch rule of `c549808` applies.

## Scope

- T1 tray labels, tests, README.
- T2 alert title reach + raphael gold see-through (one slice: the sprite mismatch only shows with the deeper reach).
- T3 mini cursor dodge (Windows cursor polling, work-area fit via monitor work area, reuse the existing glide).
- T4 CieLinux `docs/cielwin-portability.md` §4 refresh (separate repository, passive doc).

## Constraints

- Test-first where a deterministic runner exists (xUnit; scene Node harnesses).
- Scene changes keep marked begin/end blocks un-nested (S1/S4b markers vs the ported R1 blocks) so the strip regexes
  and hash pins keep working; re-pin hashes deliberately.
- New/grown Node harness timeouts stay at the 15-min CI bound.
- Visual changes (T2, T3) need the user's live approval in the running app.
- Delivery strategy: local `feature-branch-chain` (forecast ~900 authored lines > 400): slices are commit ranges on
  this branch, merged locally into `main` with `--no-ff`. No push.

## Tasks

- [x] T1 Tray labels "Scene Mode" / "Scene Wallpaper" / "Scene Mini". Route: inline (mechanical, 3 files, no
      research). `TrayIconHost.ModeMenuLabel` const now pins the submenu header. RED: test build failed
      (`ModeMenuLabel` missing); GREEN: `TrayIconHostTests` 24/24. README tables updated. Commit `1ab6c37`.
      RDD: medium, under budget (81 lines; pending in the slice). The first assess was `unassessable` because of
      unrelated untracked user files (`.claude/`, `docs/`, `AGENTS.md`, ...); reassessed with `--untracked-scope=exclude`.
- [x] T2 Alert title reaches the reference ring; raphael see-through stamps the gold sprites. Route: delegated
      (writer trigger: shared overlay + 4 hooks + harnesses). R1 blocks (`// Linux port (R1)` ...
      `// Linux port end (R1).`) never nest in S1/S4b blocks; `stripS4b` strips R1 first so `PRE_S4B` stays pinned;
      new `PRE_R1` pins (573d8c5, LF-normalized) in `alert-title-reach.checks.js` (added to the test csproj).
      Raphael case 4 now asserts gold sprite stamping (the old `drawGlyphRing` assertion is what 5a92a38 replaces).
      RED: processing 60/66, raphael 34/37, explorer 42/44, idle 28/30 (only the new R1 checks). GREEN:
      `dotnet test CielWin.App.Tests -c Release --filter Wallpaper` 153/153; build 0 warnings.
      CI margin: explorer 194s -> 204s, idle 162s -> 172s locally; ~5x on CI is ~17 min, so all four scene
      `HarnessTimeout`s go 15 -> 30 min. Pending: user's live visual check (WARNING/FAILED, wallpaper and mini,
      4 scenes; raphael gold ring through the letters). Commit `c714680`.
      RDD: slice T1+T2 (573d8c5..c714680, 663 lines) medium, `slice_budget_reached`. The preflight STATUS stopped at
      `collect intended_untracked_selection`; gentle-ai 4.0.0 rejected the selection JSON built from its own
      arguments ("must be exact ... v1 JSON") and publishes no schema for it (same as CosmicWin, 2026-09-29).
      User chose to continue without reporting; no consent envelope was reached, so no decline ran. Slice delivered
      unreviewed under ordinary policy with writer self-verification (medium tier) as the check of record.
- [x] T3 Mini cursor dodge. Route: delegated (writer trigger: controller + interop + pure logic + tests).
      Pure `MiniDodge`/`MiniDodger` (zone = window rect + 24 px; shift side + 32 px along the dominant axis away from
      the cursor; perpendicular fallbacks; must fit the nearest monitor's `rcWork`; return 400 ms after the cursor
      leaves both spots, injected clock). `IMiniDodgeDesktop` + `Win32MiniDodgeDesktop` (`GetCursorPos`,
      `MonitorFromRect`/`GetMonitorInfoW`, `LibraryImport`; null on failure). Controller polls every 100 ms
      (Background `DispatcherTimer`) from `Show` to `Dispose`; `Show`/`MoveTo`/`GlideTo` cancel the dodge and set home;
      dodge moves never persist `mini-position`; a failing tick cancels, traces once and raises `PlacementLost`.
      Of `c549808` only the cancel/dispose rule applies (synchronous cursor read). Margins are physical pixels.
      RED: build failed (`MiniDodgeDirection` missing, then `dodgeDesktop` parameter missing). GREEN: build 0
      warnings; App.Tests 747 passed (non-desktop, scene harnesses excluded); Interop.Tests 269 passed / 5 skipped;
      `Win32MiniDodgeDesktopTests` (RequiresDesktop) 2/2 locally; parent spot check 73/73. Pending: user's live check
      (dodge at each position, return delay, Alt+M while aside, no focus steal, `trace.log` dodge lines).
- [ ] T4 CieLinux `docs/cielwin-portability.md` refresh.

## Acceptance

- Tray shows the new labels; tests pin them.
- The alert title reveal reaches each scene's reference ring; raphael's see-through matches its gold ring.
- When the cursor approaches the mini it glides aside within the monitor work area and returns; hotkey moves and
  cancel still behave as before.
- Full `dotnet test CielWin.sln -c Release --filter "Category!=RequiresDesktop"` green.

## Progress

- Branch created from `main` @ `573d8c5`.

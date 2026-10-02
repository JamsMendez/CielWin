# CielWin extraction

## Objective
Standalone Windows app that ships only CosmicWin's HTML scene wallpaper, the mini scene window,
and the local HTTP server for scene switching and alerts. No tiling, layout, launcher, video,
named pipe, or CosmicWinAlert client.

## Problem / why
Anyone wanting only the HTML scenes or the mini window must install the full CosmicWin tiling
window manager, which also requires administrator rights.

## Source
`../CosmicWin` at commit `9f49973`. Key origins (per mapping):
- Wallpaper attach: `CosmicWin.Interop/Win32/Win32VideoWallpaperHost.cs` (strip video player).
- Scene/alert layer: `CosmicWin.App/Alerts/WebViewAlertLayerController.cs` + helpers, `Alerts/Web/**`.
- Scenes: `CosmicWin.App/Wallpaper/Web/{processing,explorer,idle,raphael,shared}`.
- Mini: `CosmicWin.App/Wallpaper/{MiniSceneWindowController,WebView2MiniSceneBrowser,MiniWindowPlacement,MiniProcessFailurePolicy}.cs`,
  `CosmicWin.Interop/Win32/Win32MiniSceneWindow.cs`.
- HTTP: `CosmicWin.Interop/{LocalHttpCommandServer,AlertHttpProtocol,WallpaperSceneHttpProtocol}.cs`,
  `CosmicWin.App/Alerts/AlertHttpTokenFile.cs`.
- Composition: `CosmicWin.App/AppComposition.cs` is NOT copied; a new minimal root is written.

## Scope and constraints
- Runs unelevated (`asInvoker`); administrator rights are not required for these features.
- One HTTP server hosts both `POST /v1/alerts` and `POST /v1/wallpaper/scene`.
- Settings (`%LOCALAPPDATA%\CielWin\settings.conf`):
  - `http-server-port` (default 47811)
  - `wallpaper-mode` = `scene` | `scene-mini` (renamed from `html` | `html-mini`)
  - `http-server` = `on` | `off` — turns scene switching and alerts on/off together
  - App-persisted state: `scene` (current scene), `mini-position`
  - FPS fixed at 60 (no setting).
- Hotkeys via `RegisterHotKey`: Alt+M cycles mini position clockwise, Alt+Shift+M counter-clockwise
  (new `MiniWindowPlacement.Previous`).
- Tray icon: switch mode, switch scene, exit.
- Artifacts in English. MIT licence carried over.

## TDD
Strict TDD: enabled (source: user global CLAUDE.md). Runner: `dotnet test CielWin.sln`
(xunit). Scene JS tests need `node` (v24 available).

## Delivery
Forecast: well over 400 authored changed lines (mostly ported code). Strategy: `ask-on-risk`,
chain strategy `feature-branch-chain` (user choice): slices chain on `feat/cielwin-extraction`,
which merges to `main` once only when the app is functional. No remote configured yet.
Slice boundaries are recorded per task below once PRs exist.

## Tasks
- [x] T1 Scaffold: `CielWin.sln`, `CielWin.Interop`, `CielWin.App` (WinExe, asInvoker manifest),
      test projects, empty build green. Route: inline (mechanical).
- [x] T2 Interop HTTP: server with alert + scene routes only, protocols, token file, tests.
      Route: delegated writer.
- [ ] T3 Interop Win32: attach-only wallpaper host, composition overlay surface, mini window,
      fullscreen detector, tests. Route: delegated writer.
- [ ] T4 Settings: trimmed settings model + file + store, new mode names, tests. Route: delegated writer.
- [ ] T5 Scene + alert layer: alert parser/queue/tile layout, WebView2 layer controller,
      scene/alert web assets, node scene tests. Route: delegated writer.
- [ ] T6 Mini: controller, browser, placement with `Previous`, failure policy, tests.
      Route: delegated writer.
- [ ] T7 Composition root, Alt+M / Alt+Shift+M hotkeys, tray, fullscreen pause, wiring tests.
      Route: delegated writer.
- [ ] T8 README: install, settings, HTTP API examples, hotkeys. Route: inline.

## Acceptance criteria
- `dotnet build` and `dotnet test` green.
- App starts unelevated, shows scene wallpaper or mini, switches scene via HTTP, shows alerts,
  `http-server = off` disables both routes, Alt+M / Alt+Shift+M cycle mini position and persist it.

## Progress
- Repo initialized on `main` (`03d0bd0`), branch `feat/cielwin-extraction`.
- T2 done: RED = compile failures (ported tests, no production types); GREEN = 144 tests (Interop.Tests 122, App.Tests 22), 0 failed, 0 skipped; build 0 warnings.

## Next step
T1.

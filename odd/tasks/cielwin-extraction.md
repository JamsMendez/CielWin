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
- [x] T3 Interop Win32: attach-only wallpaper host, composition overlay surface, mini window,
      fullscreen detector, tests. Route: delegated writer.
- [x] T4 Settings: trimmed settings model + file + store, new mode names, tests. Route: delegated writer.
- [x] T5 Scene + alert layer: alert parser/queue/tile layout, WebView2 layer controller,
      scene/alert web assets, node scene tests. Route: delegated writer.
- [ ] T5b Review follow-ups (user request): fix the actionable review warnings listed under
      Follow-ups, with tests. Route: delegated writer.
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

- T3 done: RED = compile failures (38 errors: ported tests, no Win32 production types); GREEN = 191 passed + 13 skipped
  real-desktop (Interop.Tests 169 + 13 skipped, App.Tests 22), 0 failed; build 0 warnings. The 13 real-desktop facts also
  passed (13/13) with `CIELWIN_RUN_DESKTOP_TESTS=1`. Cut vs reference: Media Foundation, D3D11/DXGI device + swapchain, D2D tint,
  video transform/shake, `IVideoWallpaperHost`, Win32NativeWindowSource assertions; the DirectComposition device is created with a
  null DXGI device (visual-only tree: root visual, no swapchain visual). Added `ISceneWallpaperHost` and a public
  `MtaActionThread` (moved from `AppComposition`) so T7 can drive the host. Desktop test gate simplified (no RealDesktopLock).

- Commits: T1 `84dc3ac`, T2 `a5d7d6b`, T3 `40825ce`, T4 `770508d`.
- Review T1+T2 (base `03d0bd0`..`a5d7d6b`): assessed medium, `slice_budget_reached`; user granted;
  lens review-reliability approved and acknowledged (lineage review-181f97a56a24a410). Reviewed
  boundary is now `a5d7d6b`.
- Review T3 (`a5d7d6b`..`40825ce`): medium, `slice_budget_reached`; user granted; review-reliability
  approved and acknowledged (lineage review-afbc04a19e59ecb6). Reviewed boundary is now `40825ce`.
- Review T4 (`40825ce`..`770508d`): medium, `slice_budget_reached`; user granted; review-reliability
  approved and acknowledged (lineage review-477834cb87a81a8c). Reviewed boundary is now `770508d`.

- T4 done: RED = compile failures (68 errors: ported tests, no Settings types); GREEN = App.Tests 108 (was 22), Interop.Tests 169 + 13 skipped, 0 failed; build 0 warnings.
  Settings API in `CielWin.App`: `Settings(HttpServerEnabled, HttpServerPort, WallpaperMode, WallpaperScene, MiniPosition)`, `Settings.Parse/Serialize/Default`,
  `SettingsFile.{ResolvePath,Load,LoadOrCreate,Save}`, `SynchronizedSettingsStore(initial, save).Update/Current`. Enums `WallpaperMode {Scene, SceneMini}`,
  `MiniPosition` (clockwise), `WallpaperScene` live in CielWin.App (Interop only validates the scene name as a string). Alias decisions: read-only legacy
  `wallpaper-mode = html|html-mini|mini`, `wallpaper-scene`, `mini-corner`, `alert-http`, `alert-http-port` (new keys win in any order); `video` and all
  tiling/border/gap/video-path/fps/alerts keys are ignored. Only new names are written.

- T5 done: RED = compile failures (ported tests, no Alerts/Wallpaper production types); GREEN = App.Tests 257 (was 108), Interop.Tests 169 + 13 skipped, 0 failed;
  build 0 warnings. App.Tests now takes ~3.5 min (node vm-sandbox scene harnesses). Controller: `WebViewAlertLayerController(ICompositionOverlaySurface host, trace, clock, WallpaperScene scene)`,
  scene-only (no html/video flag, fps fixed 60, `SwitchScene` is void), user data `%LOCALAPPDATA%\CielWin\WebView2Scene`, virtual host `cielwin-scene.example`.
  Fixed mosaic gap decision: `AlertTileLayout.GapPixels = 8` (no gap setting in CielWin). Cut vs reference: video tint (`AlertTint*`, `IAlertTintSink`,
  `VideoPlayerAlertTintSink`, `AlertMaskDecoder`, tint messages), the video-mode `Alerts/Web/alert-layer.*` page + its fonts + `alert-layer-layout`/web-page tests (scene pages embed
  `shared/js/alert-overlay.js`), `WebViewAlertLayerVisibility` (its only input was the html/video flag), composition-wiring tests (T7). Scene web assets copied wholesale (comments still
  mention `alert-layer.js` as the original contract). Mini files are T6.
- T5 split for review (lens_context_budget_exceeded on the 20k-line commit, then on a 3.5k slice):
  `25795b6` alerts+controller, `79e30c9` shared runtime, `5050641` processing, then explorer/idle/raphael
  as 10 commits `9f1682d`..`c1ccbd2`. Each built in a clean worktree; final tree identical to the
  original (backups `backup/t5-unsplit`, `backup/scenes-unsplit`).
- Reviews: `770508d..25795b6` (4 lenses) approved+ack; `..79e30c9` approved+ack; `..5050641` (4 lenses)
  approved+ack; `5050641..86320dd` approved+ack. `86320dd..4dc7414` (4 lenses): correction_required,
  then terminal `corrupted_or_unverifiable_authority` (gentle-ai 3.7.0 defect, existing issue #4571;
  occurrence comment posted with user consent). Candidate declined (declined_this_candidate); that
  range stays unreviewed and the triggering findings are unknown. Pending: `4dc7414..12dc958`,
  `12dc958..cee39af`, `cee39af..c1ccbd2`.

## Follow-ups (non-blocking review advice)
- `AlertHttpTokenFile` abandoned-mutex path (`AlertHttpTokenFile.cs:137-144`) has no test.
- R3-001 `Win32MiniSceneWindow.cs:84-109`: a failed DirectComposition setup returns false but leaves
  the created HWND alive (no cleanup in the catch). Fix when T6/T7 wire mini recovery.
- R3-002 `MtaActionThread.cs:62-69`: `Invoke` silently returns after a 5 s wait timeout; caller
  cannot tell the work never ran. Surface it (diagnostic or bool) in T7.
- R3-003 `Win32SceneWallpaperHostRealAttachTests.cs:406-438`: real-desktop test weakness (warning).
- R3-004 `ISceneWallpaperHost.cs:14-19`: doc suggestion.
- T4 R3-001 `SettingsFile.cs:38-41`: a transient read failure silently yields defaults; a later
  save (scene switch, Alt+M) would then overwrite the user's file. Fix in T7 (report + avoid save-over).
- T4 R3-002 `SettingsFile.cs:98`: `File.WriteAllText` is not atomic; use temp file + replace. Fix in T7.
- T4 R3-003 `SynchronizedSettingsStoreTests.cs:15-52`: test suggestion.
- T5 `WebViewAlertLayerController.cs:374-375` (R3/R4): after a WebView2 process failure the layer is torn
  down and nothing recreates it. `:307`: navigation has no timeout. Fix in T7 (recovery like the mini).
- T5 `processing/js/main.js:71-108`: a throwing scene layer leaves canvas save/restore unbalanced.
- T5 `explorer/js/animate.js:175-177` cache key committed before mask build; `earth.js:318-319` mini basis.

## Next step
T6 Mini.

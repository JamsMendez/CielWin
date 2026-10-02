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
- Tray icon: switch mode, switch scene, exit. Icon = Raphael scene, mini variant, transparent background
  (user request 2026-10-02), generated reproducibly by a script under `tools/tray-icon/`.
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
- [x] T5b Review follow-ups (user request): fix the actionable review warnings listed under
      Follow-ups, with tests. Route: delegated writer.
- [x] T6 Mini: controller, browser, placement with `Previous`, failure policy, tests.
      Route: delegated writer.
- [x] T7 Composition root, Alt+M / Alt+Shift+M hotkeys, tray, fullscreen pause, wiring tests.
      Route: delegated writer.
- [x] T8 README: install, settings, HTTP API examples, hotkeys. Route: inline.

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
  occurrence comment posted, then deleted at user request; re-report only if it reproduces on v4.x). Candidate declined (declined_this_candidate); that
  range stays unreviewed and the triggering findings are unknown.
- Reviews on gentle-ai 4.0.0 (2026-10-01): `4dc7414..12dc958` (4 lenses) approved+ack;
  `12dc958..cee39af` (1 lens) correction_required -> terminal `corrupted_or_unverifiable_authority`
  again (#4571 reproduces on v4.0.0; occurrence comment posted with user consent, issuecomment-5946179272),
  declined_this_candidate, stays unreviewed with unknown findings; `cee39af..c1ccbd2` (4 lenses)
  approved+ack; `c1ccbd2..3ce61fd` (1 lens) approved+ack.

- T5b done (commits in `git log`: `fix(interop)`, `fix(settings)`, `fix(alerts)`, `fix(scene)`): each fix test-first with
  observed RED. Fixed: mini window releases HWND/class on failed composition setup (and on CreateWindow failure);
  `MtaActionThread.Invoke` returns bool (+ optional timeout); `SettingsFile.TryLoad` -> `SettingsLoadResult{Settings,Status,CanSave}`
  (T7 must skip saves when `!CanSave`) and atomic temp+`File.Replace` save; abandoned-mutex test; `AlertLayerPreloadState`
  bounded recovery (`RuntimeFailed`, 2 per 10 min, `GaveUp`, host change refills), 30 s navigation timeout checked by the poll,
  `Failed()` no longer drops a showing alert, `MiniProcessFailurePolicy` ported (T6 reuses it); explorer/idle lighting-mask
  cache key committed after the mask build; explorer/idle earth flare uses `activeSceneBasis()`. Not defects: scene
  save/restore (every scene catch already calls `resetCanvasStateForFrame`), "nothing recreates the layer" (the 250 ms poll
  already recreated it, only the cap was missing).

- T6 done (route: delegated writer; 2+ non-trivial files): RED = CS0246 (`MiniSceneWindowController`, `IMiniSceneBrowser`
  missing); GREEN = 71 focused tests; full suite App 357 (was 286), Interop 172 + 14 skipped, build 0 warnings.
  Geometry uses `CielWin.Interop.Rectangle` (no layout `Rect`); `Show(scene, bounds)` has no fps (URL via
  `SceneUrl(scene, "mini")`, fps=60); user data `%LOCALAPPDATA%\CielWin\WebView2Mini`. `MiniWindowPlacement.Previous`
  is the exact inverse of `Next`. Dropped 3 duplicate tests already covered by `AlertLayerMessagesTests` /
  `WebViewAlertLayerControllerTests`. `MiniModeWiringTests` deferred to T7.

## Follow-ups (non-blocking review advice)
- FIXED (T5b) `AlertHttpTokenFile` abandoned-mutex path (`AlertHttpTokenFile.cs:137-144`) has no test.
- FIXED (T5b) R3-001 `Win32MiniSceneWindow.cs:84-109`: a failed DirectComposition setup returns false but leaves
  the created HWND alive (no cleanup in the catch). Fix when T6/T7 wire mini recovery.
- FIXED (T5b) R3-002 `MtaActionThread.cs:62-69`: `Invoke` silently returns after a 5 s wait timeout; caller
  cannot tell the work never ran. Surface it (diagnostic or bool) in T7.
- R3-003 `Win32SceneWallpaperHostRealAttachTests.cs:406-438`: real-desktop test weakness (warning).
- R3-004 `ISceneWallpaperHost.cs:14-19`: doc suggestion.
- FIXED (T5b; T7 consumes `TryLoad`/`CanSave`) T4 R3-001 `SettingsFile.cs:38-41`: a transient read failure silently yields defaults; a later
  save (scene switch, Alt+M) would then overwrite the user's file. Fix in T7 (report + avoid save-over).
- FIXED (T5b) T4 R3-002 `SettingsFile.cs:98`: `File.WriteAllText` is not atomic; use temp file + replace. Fix in T7.
- T4 R3-003 `SynchronizedSettingsStoreTests.cs:15-52`: test suggestion.
- FIXED (T5b) T5 `WebViewAlertLayerController.cs:374-375` (R3/R4): after a WebView2 process failure the layer is torn
  down and nothing recreates it. `:307`: navigation has no timeout. Fix in T7 (recovery like the mini).
- NOT A DEFECT (T5b) T5 `processing/js/main.js:71-108`: a throwing scene layer leaves canvas save/restore unbalanced.
- FIXED (T5b) T5 `explorer/js/animate.js:175-177` cache key committed before mask build (and idle); `earth.js:318-319` mini basis (and idle). Unflagged same-pattern: `rings.js` uses `sceneBasis(W,H)` in mini for ruler/paragraph/outer ring sizes (left as is).
- Idle slice (`4dc7414..12dc958`, informational): misleading "verbatim" headers in `idle/js/animate.js:1-6`
  and `rings.js:1-3`; `IdleSceneNodeTests.cs:34-44` timeout budget/rationale understated (R2/R3/R4);
  unused processId tuple element; mini-fit magic numbers and duplicated layer config in `idle-scene.tests.js`;
  mini ruler basis mismatch untested (`rings.js:356-361`).
- Raphael slice (`cee39af..c1ccbd2`, informational): `raphael/js/sprites.js:353-370` retries a failed bake
  every frame (R3/R4 warning); `nebula.js:282-284` disables itself permanently and silently (R3 warning);
  stale comments/literals in `raphael-scene.tests.js:344-357,485-490`; triplicate cycle-rate helpers
  `feathers.js:32-49`; `RaphaelSceneNodeTests.cs:70,93-97` suggestions.
- T5b slice (`c1ccbd2..3ce61fd`, informational): R3-001 `AlertLayerPreloadState.cs:100-103`,
  R3-002 `SettingsFile.cs:81-82` (warnings).
- T7 done (route: delegated writer; many non-trivial files): RED = 44 compile errors (missing `AppComposition`,
  `HotkeyModifiers`, `TrayMenuController`, `CompositionHost`, ...); GREEN = App 472 (was 357), Interop 182 + 16 skipped,
  build 0 warnings; mutation checks: removing the `CanSave` guard fails 4 `SettingsPersistenceWiringTests`, removing the
  mini-not-ready guard fails 2 `MiniModeWiringTests`. Hotkeys via `RegisterHotKey` on a message-only window (no hook);
  `!CanSave` swaps every save for a trace; trace at `%LOCALAPPDATA%\CielWin	race.log` (1 MB + 1 backup). Tray icon
  `CielWin.App/Assets/raphael-mini.ico` generated by `tools/tray-icon/render-raphael-mini.mjs` (headless Edge via CDP,
  frozen clock 4000 ms, nebula + mini base hidden by injected script, deterministic output). Committed as 4 work units.
  Product decisions: live tray mode switch KEPT (user, 2026-10-02). Open (conservative choice taken, user to confirm): mini
  never pauses for fullscreen; hotkeys registered in both modes, conflict only traced;
  HTTP scene switch while mini failed replies 202 (CosmicWin 503); 16-24 px icon pale on light taskbars; old
  `CielWin.App/cielwin.ico` now unused.

- Single-instance guard (user request 2026-10-02, route: inline, 3 files): `SingleInstanceGuard` named mutex
  `Local\CielWin.SingleInstance` decided by creation (never waits); `App.OnStartup` exits before wiring when taken.
  RED = CS0246; GREEN = 6 guard tests; full suite App 478, Interop 182 + 16 skipped, build 0 warnings.
- Review `1248a4a..7560839` (1 lens) approved+ack. R3-001 (guard crashed on a name it cannot open) FIXED test-first
  (RED: `WaitHandleCannotBeOpenedException`; GREEN 7/7). Open: WARNING R3-002 `App.xaml.cs:27` a throwing
  `ProductionComposition.Wire` crashes startup with no trace line; suggestions `FileTrace.cs:49`,
  `StartupSettingsTests.cs:39-49`.
- T8 done (route: delegated writer, reading prepared the write): `README.md`, passive docs, structural check against
  source passed. It found the settings template naming `alert-http.token` instead of `http.token`: FIXED test-first
  (RED 1 failed, GREEN; full suite App 480, Interop 182 + 16 skipped). Noted, not changed: 503 "alerts are disabled"
  is unreachable (port closed when off); scene route answers 202 for the active scene; busy alerts get 202 and are
  dropped (intended, `AlertQueue`); settings are read only at startup (no watcher).
- T7 reviews: `a728b2e..d9f2496` (hotkeys+tray, 1 lens) approved+ack; `d9f2496..1248a4a` (composition, 1 lens)
  approved+ack; `1248a4a..3f2850f` (production entry, 387 lines) assessed medium `under_budget`, pending in the
  next slice. Informational: WARNING `Win32HotkeyRegistrar.cs:113-119` swallows errors silently, WARNING
  `TrayIconHost.cs:26-42` tray click unguarded, WARNING `MiniSceneSurface.cs:97-98` hotkey move unguarded;
  suggestions: generator has no timeouts (`render-raphael-mini.mjs:100-103`), `TrayGlyphs.cs:42-60` bitmap leak,
  hotkey test leak on failure, old icon resource removed (`CielWin.App.csproj:18`), partial surface leak
  (`AppComposition.cs:166-168`).
- T6 review (`3ce61fd..2ba405d`, 1 lens) approved+ack (informational): R3-001 `MiniSceneWindowController.cs:176-179`
  and R3-002 `:227-231` (warnings), R3-003 `WebView2MiniSceneBrowser.cs:95-101`, R3-004 `MiniSceneWindowController.cs:175`
  (suggestions).

## Next step (resume here — updated 2026-10-01)
State: branch `feat/cielwin-extraction`, gentle-ai 4.0.0 synced, RDD on. All slice reviews done
(see Progress). Unreviewed ranges: `86320dd..4dc7414`, `12dc958..cee39af` (#4571).
1. All tasks done. Resolve remaining T7 open product questions with the user; follow-up warnings above;
   decide the merge to `main` (user call, app must be functional — run it first).
2. Backup branches `backup/t5-unsplit`, `backup/scenes-unsplit` can be deleted (user decision).
3. Engram mirror: re-save this document to topic `odd/cielwin-extraction/tasks` when engram is available.

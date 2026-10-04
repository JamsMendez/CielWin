# Scene optimizations

Locator: `odd/tasks/scene-optimizations.md` · Engram mirror: `odd/scene-optimizations/tasks` · Branch: `feat/scene-optimizations` (from `main` @ `7ecdc79`)

## Objective

Port CieLinux's scene optimizations to CielWin without sacrificing appearance.
Sources (CieLinux, read-only): `bc66a34` NEB-1, `4574b4a` W1/W4 explorer/idle CPU, `bf213ae` 30/60 FPS tray,
`dcc933b` PERF-5 baked glows; evidence in CieLinux `odd/tasks/wallpaper-*.md`, `global-tray-fps.md`,
`docs/cielwin-portability.md`.

## Problem / why

CielWin still runs the pre-optimization scene code (`mediump` nebula, per-frame `shadowBlur`, per-channel earth loop,
per-frame gradients and alert letter blur) and always requests `?fps=60`. CielWin has no scene draw-rate/CPU
measurement, so GPU-bound wins are unproven on WebView2.

## Scope

- S1 pixel-identical: NEB-1 `highp`; earth grayscale fast path (explorer + idle); cached vignette / blue-layer gradients.
- S2 Windows measurement: draws/s and CPU for Processing, Raphael, explorer, explorer + alert at 60 FPS (before/after).
- S3 host-side 30/60 FPS setting + tray item (page side already exists in `shared/js/render-loop.js`).
- S4 near-identical (only with measured gain and user visual approval): PERF-5 baked glows; alert-overlay caches + W4;
  spark atlas (adapted, CielWin lacks the mini O1–O3 base).

## Constraints

- Appearance first: S1 must be byte/pixel-identical; S4 needs a live visual OK from the user before merge.
- Test-first with the scene tests (`CielWin.App.Tests/Wallpaper/Web/*.tests.js`) and the xUnit suites.
- Delivery: local feature-branch chain, slices = commit ranges, local `--no-ff` merge into `main`, user pushes.
  Forecast ~1200 authored lines.

## Tasks

- [x] S1 NEB-1 + earth fast path + cached gradients. Route: delegated (writer: 4+ scene files + tests).
  Commit: `27dbc31`. Surface widened (user-approved) to `idle/js/rings.js` and
  `explorer/js/rising-sparks.js` (CieLinux keeps the idle vignette and blue-layer glow there, not in `animate.js`).
- [x] S1b Review advisories on S1. Route: delegated (writer: 2 rings.js + 2 scene test files + render-loop comment).
  Commit: `6901667`.
- [x] S2 Windows baseline measurement (draws/s, CPU). Route: delegated (measurement worker).
- [x] S3 30/60 FPS setting + tray. Route: delegated (writer: settings + seams + tray + composition + tests).
  Commit: `6e7e772`. Surface widened (user-approved) to `Wallpaper/MiniSceneWindowController.cs` (the mini builds
  its own page URL).
- [ ] S4 PERF-5, alert caches + W4, spark atlas — gated on S2 numbers and user visual approval. Route: delegated.

## Acceptance criteria

- S1: identical output proven by tests; full suites green.
- S3: setting persisted, tray toggles, scenes reload at the chosen cap.
- S4: measured improvement on Windows and user visual approval.

## Progress / next step

- S2 baseline (7ecdc79, Release, 3440x1440@164Hz, RTX 4060 Ti, 60 cap, probe on a copied build in the session scratchpad `scenemeasure/measure.mjs`): processing 34–37 draws/s (GPU process ~110%); raphael ~60 (GPU ~110%, little headroom); explorer 60 (128–147% CPU); idle 60 (74–84%); explorer+warning 57–59 (~218% CPU). Implication: PERF-5 justified for processing/raphael; alert caches + W4 justified for explorer+alert.
- S1 done: NEB-1 `highp` (processing + raphael nebula.js); earth grayscale fast path (explorer + idle earth.js);
  cached vignette (explorer + idle rings.js) and blue-layer glow (explorer rising-sparks.js). Changes live in
  `// Scene optimization begin/end (S1)` blocks that only add lines (CieLinux W1 blocks, marker renamed).
  - Byte-identity proof: stripping the S1 blocks restores the pre-S1 SHA-256 of every touched earth/rings/rising-sparks
    file; the earth fast path is compared byte-for-byte (`Buffer.compare`) against the pre-S1 `renderEarthFrame`
    evaluated in the same sandbox, sizes 64/109/33/241/64 x 7 longitudes, and makes 0 Math.round/min/max calls per
    frame; gradients are created once per geometry with the reference args/stops, filled every frame (blue layer:
    same 'color' tint + 'screen' glow fillRects), rebuilt once on resize.
  - RED: processing 44/45, raphael 26/27 (highp); explorer 24/27 then 26/28 (S1 pins, `EARTH_EQUIRECT_GRAY is not
    defined`, vignette/glow "steady frames reuse"); idle 23/25 then 24/26 (S1 pins, earth, vignette reuse).
  - GREEN: processing 45/45, raphael 27/27, explorer 28/28, idle 26/26. `dotnet build CielWin.sln` 0 warnings / 0
    errors; `dotnet test CielWin.sln`: Interop 269 passed / 22 skipped, App 671 passed / 0 failed.
  - Deviation: the earth reference runs in the same vm realm (one load; the earth.js bake costs ~7 s per realm)
    instead of CieLinux's second stripped-copy harness. CielWin had no source hash pins, so nothing was re-pinned.

- S1b done (review advisories on S1):
  - Vignette (explorer + idle `rings.js`): the cached path now builds the gradient before `context.save()`, so a
    throwing `createRadialGradient` no longer leaves a dangling save that grows the state stack every failing frame.
    Same calls and fills on success; the edit stays inside the S1 block (pre-S1 pins unchanged). CieLinux `4574b4a`
    `scenes/explorer/js/rings.js:779-780` has the same save-before-gradient ordering (not fixed there; read-only).
  - Pins: the pre-S1 SHA-256 pins now hash LF-normalized text (`\r\n` -> `\n` before stripping) and were re-pinned;
    they equal `git show 7ecdc79:<file> | tr -d '\r' | sha256sum`, and an LF and a CRLF copy hash the same.
  - Earth fallback: new test re-evaluates the S1 `renderEarthFrame` with `EARTH_EQUIRECT_GRAY = null` or
    `EARTH_LITTLE_ENDIAN = false` shadowed, compares bytes to the pre-S1 reference (sizes 64/109/33 x 3 longitudes)
    and checks the fast path is never entered; it shares one page with the earth/vignette tests (no extra bake).
  - `shared/js/render-loop.js` comment now points at `SceneUrl(scene, variant, fps)` and the `frame-rate` setting.
  - RED: explorer 28/30 (pins `js/earth.js` with normalized pins on raw text; vignette "1 saves, 0 restores");
    idle vignette "1 saves, 0 restores" (run against a scratch copy with the HEAD rings.js). The fallback test is a
    characterization test (passes on S1 code).
  - GREEN: explorer 30/30, idle 28/28. `dotnet build CielWin.sln` 0 warnings / 0 errors;
    `dotnet test CielWin.sln`: Interop 269 passed / 22 skipped, App 716 passed / 0 failed.

- S3 done: `frame-rate` setting (`30`|`60`, default `60`, anything else keeps the default / an earlier valid
  value), serialized with a comment after `scene`; `Settings.FrameRates`/`IsFrameRate`. `SceneUrl(scene, variant,
  fps)` carries it (only 30/60 reach the URL, else 60); the wallpaper layer (`WebViewAlertLayerController`) and the
  mini (`MiniSceneWindowController`) get it through the `CreateSceneLayer`/`CreateMiniWindow` seams. Tray: **Frame
  rate** submenu (30 FPS / 60 FPS, checked = current) after Scene. On change `AppComposition.SelectFrameRate`
  persists, then `ReplaceSurface()` (the mode-switch path: dispose, `AlertDriver.SurfaceReplaced()`, activate);
  same rate or any other value is a no-op. `SurfaceReplaced` clears only the displayed id and the H4 repeat timer,
  so a showing alert / held warning comes back for its remaining time without re-sounding.
  - RED: test project failed to compile, 40 errors (missing `Settings.FrameRate`/`IsFrameRate`/`FrameRates`,
    `SceneUrl(..., fps)`, `TrayMenuController.FrameRate`/`SelectFrameRate`/`FrameRates`, `TrayMenuEntry.FrameRate`,
    `TrayIconHost.FrameRateLabel`, the fps-taking seams and `MiniSceneWindowController(fps:)`).
  - GREEN: focused 282/282; `dotnet build CielWin.sln` 0 warnings / 0 errors; `dotnet test CielWin.sln`: Interop
    269 passed / 22 skipped, App 716 passed / 0 failed (+45 tests incl. `FrameRateWiringTests`).
  - Deviations from CieLinux `bf213ae`: default 60 (CieLinux 30) to keep CielWin's cadence and appearance (user
    decision); persisted immediately, following CielWin's mode-switch path, not after the new page reports ready
    (user decision); no submenu glyph (Mode/Scene have one). Not touched (outside surface): the stale
    `Settings.cs TryReadWallpaperFps` reference lives in `Wallpaper/Web/shared/js/render-loop.js:66`, not the host (fixed in S1b).

Next: S4 stays gated on user visual approval.

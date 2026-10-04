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
  Commit: HASH_PLACEHOLDER. Surface widened (user-approved) to `idle/js/rings.js` and
  `explorer/js/rising-sparks.js` (CieLinux keeps the idle vignette and blue-layer glow there, not in `animate.js`).
- [x] S2 Windows baseline measurement (draws/s, CPU). Route: delegated (measurement worker).
- [ ] S3 30/60 FPS setting + tray. Route: delegated (writer).
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

Next: S3 (30/60 FPS setting + tray); S4 stays gated on user visual approval.

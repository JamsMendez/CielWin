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
- S4 split into three slices, each gated on measurement and user visual approval. Route: delegated.
  - [x] S4a PERF-5 baked glows (processing + raphael) — implemented, pending Windows measurement and user visual
    approval. Route: delegated (writer: 2 layers.js + 2 sprites.js + new check module + 2 harnesses).
    Commit: `c22fe8c`.
  - [x] S4b alert-overlay caches + W4 (explorer + alert) — implemented, pending Windows measurement and user
    visual approval. Route: delegated (writer: alert-overlay.js + explorer animate.js + new check module + 2
    harnesses). Commit: `e055fd2`.
  - [x] S4a2 Bound glow-cache keys (S4a review WARNING). Route: delegated (writer: test-only, no source change).
    Commit: `e0e4d36`.
  - [x] S4c spark atlas (adapted, CielWin lacks the mini O1–O3 base) — implemented, pending Windows measurement
    and user visual approval. Route: delegated (writer: rising-sparks.js + new check module + explorer harness).
    Commit: `ae8b482`.

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

- S4a done (implementation; CieLinux `dcc933b` PERF-5, no later CieLinux commit touched these scene files —
  `bc66a34` is the nebula shader, already S1; `4574b4a`/`bf213ae`/alerts commits touch only tests or other scenes):
  - `sprites.js` (both scenes): CieLinux's PERF-5 block verbatim (`wallpaperGlowCache` keyed by `WxH@scaleX,scaleY`,
    `bakeShadowLayer` = the reference canvas shadow drawn off-bitmap and brought back with `shadowOffsetX`,
    `wallpaperLineGlow`/`stampWallpaperLineGlows` end caps + stretched middle, 8+1 pulse levels blended,
    disc glows in 3% blur-to-radius steps).
  - Baked (full wallpaper only; mini keeps every `shadowBlur`): processing rays/spokes (`drawGlowSegments`),
    folding band outlines (butt-capped segments, closing bar dropped when it coincides), central octagon (pulse
    levels; wobble moves only the stroke), core disc; raphael rays/spokes, golden hexadecagon (pulse levels), hot
    core disc (`paintRadialGlowLayer` with a shadow), counter panels (static bake), glyph-ring delimiters.
  - Deviations: adapted onto CielWin's reference functions (no O1–O3/B5 mini base): band outline segments come
    from the `points` objects, the octagon bake uses `CENTRAL_OCTAGON_STROKE_PX`. Raphael's five delimiter
    shadows (cached in CieLinux by O3/B5 as a pixel-identical device-aligned layer) are baked here with
    `bakeShadowLayer`, screened onto each other in the bake ('screen' is associative/commutative) and stamped
    at (cx, cy): near-identical, not pixel-identical (sub-pixel resampling of the blur). The alert-overlay letter
    blur (`shared/js/alert-overlay.js`) is left for S4b.
  - Could look different: thin ray edges (CieLinux measured mean 0.22–0.27 levels, max ~60 on thin ray edges),
    band outline corners at the bars, pulse frames between baked levels, delimiter rings (resampled blur).
  - Tests: new `CielWin.App.Tests/Wallpaper/Web/blur-free-glow.checks.js` (port of CieLinux
    `blur-free-glow.contract.test.mjs`, per-canvas recording contexts), registered by both scene harnesses: S4a
    blocks only add lines (stripping them restores the 2b6a795 LF SHA-256 of all four files); no frame paints a
    shadow at 1920x1080/3440x1440/1280x720@2 and mini still does (and bakes nothing); bakes are shadow-only,
    warm after one 600 s period, rebuilt on resize; every layer keeps the reference paints minus shadows; ray
    glows are laid along each segment; pulse glows blend levels around the reference blur. The processing
    "mini scales fixed px sizes" test now reads blurs on the cold (baking) call and widths on the warm one, and
    checks the core disc's requested blur (20) via a `stampWallpaperDiscGlow` spy.
  - RED: processing 45/51 (6 S4a checks), raphael 27/33 (6 S4a checks). GREEN: processing 51/51, raphael 33/33.
    `dotnet build CielWin.sln` 0 warnings / 0 errors; `dotnet test CielWin.sln`: Interop 269 passed / 22 skipped,
    App 716 passed / 0 failed.

Next: S4a needs a Windows draws/s + GPU measurement (S2 probe) and the user's live visual approval; then S4b.
- S4a measured (c22fe8c, Release, same machine/method as S2, 1 pass): processing 56.4 draws/s (was 34–37), p99 30.4 ms; raphael 60, CPU ~101% (was ~144%); explorer 60, ~92% (was 128–147%); idle 60, ~47% (was 74–84%); explorer+warning 59.4, ~184% (was ~218%). Includes S1 gains. User approved S4a appearance live (2026-10-04).

- S4b done (implementation; CieLinux `4574b4a` W1 alert pieces + W4, in `// Scene optimization begin/end (S4b)`
  blocks that only add lines; stripping them restores the 5b8a22b LF SHA-256 of `shared/js/alert-overlay.js` and
  `explorer/js/animate.js`):
  - W1 (`alert-overlay.js`): wash (evenodd path), title letters and their pre-blurred drop shadow baked once per
    title/wash/letters color, tile size, scale and measured title width (re-baked when the title face loads),
    stamped with drawImage; rails, see-through, frame and modules stay per frame in reference order. Released
    when the overlay stops.
  - W4 (full wallpaper only): explorer draws its rising sparks once into a canvas-sized layer (composited
    'lighter') and the see-through letters blit it (`sceneSeeThroughFrameLayer`) instead of a second
    `drawRisingSparks` pass; recolor/clip/stamp of the intersections over the two title bands only (all scenes);
    side modules drawn into a layer once per 100 ms counter value and stamped; a shown FAILED tile downscales
    the scene canvas directly (no whole-canvas copy; a revealing tile keeps the copy).
  - Adaptations: CielWin has no luma-keyed mini (`lumaKeyedAlertLetters`/`keyedLetters`), so the full/mini split
    uses `isMiniVariant` (`alertFullWallpaper()`), and the letters always use `theme.letters`. Mini keeps the
    reference see-through, per-frame modules and backdrop copy but gets the W1 static bake (as CieLinux).
    Skipped: the spark atlas (S4c); nothing in W4 depended on the atlas or the O1–O3/B5 mini code.
  - Could look different: the letter shadow (baked with its letters onto a transparent layer, then stamped),
    explorer see-through sparks (additive via a premultiplied layer; CieLinux measured max 3 levels, mean
    ≤0.0093), module boxes (stamped from a layer at the same sub-pixel origin).
  - Tests: new `CielWin.App.Tests/Wallpaper/Web/alert-overlay-cache.checks.js` (per-canvas recording mock):
    processing harness — bake once with the theme's title/wash/letters/intersections colors for warning and
    failed, shadow bake = reference stamp, no per-frame shadow/evenodd; rebuilt on resize, title face load and
    alert change, released on hide; module labels only on counter ticks; band-only recolor/clip/stamp; direct
    backdrop (1 and 2 FAILED tiles) and copy kept while revealing; mini keeps the reference paths. Explorer
    harness — S4b pins; one spark pass per frame with the layer stamped 'lighter' and blitted per band
    (warning, warning+failed tiles, after resize), released after hide. The existing explorer hook test now
    nulls `sceneSeeThroughFrameLayer` to exercise the hook fallback.
  - RED (final tests vs the 5b8a22b alert-overlay.js/animate.js in a scratch copy): processing 51/57 (6 S4b
    checks), explorer S4b checks 0/2. GREEN: processing 57/57, explorer 32/32, idle 28/28, raphael 33/33.
    `dotnet build CielWin.sln` 0 warnings / 0 errors; `dotnet test CielWin.sln`: Interop 269 passed / 22
    skipped, App 716 passed / 0 failed.

Next: S4b needs the Windows explorer+warning draws/s + CPU measurement (S2 probe) and the user's live visual
approval; then S4c.

- S4a2 done (review WARNING on `c22fe8c`: line-glow keys hold the full strokeStyle, pulse keys hold `r`, and the
  cache only clears on resize, so a per-frame-varying alpha/radius would re-bake and grow without bound). Proven
  bounded, no source change: every key input is constant or quantized per geometry — ray alphas/widths/blurs are
  module constants (`CENTRAL_RAY_*`, `0.70/0.22/5`), band outline width is `minD * const`, octagon/hexadecagon `r`
  is `min(W,H) * const` / `coreRadius(minD)`, pulse levels are the fixed 9 bakes, and disc glows key on an integer
  3% step of blur/radius over a bounded range. CieLinux `dcc933b` uses the identical keys and callers (no
  quantization, no bound test). Appearance unchanged (tests only).
  - Test: `blur-free-glow.checks.js` "the glow cache stays bounded over a long run of distinct frames" (both
    harnesses): 1000 irregular timestamps over one 600 s period, then 2 x 300 at other offsets in later periods;
    cache <= 64 entries, no later bake, no new key. Observed: processing 14 entries, raphael 24, later bakes 0.
    Sample count is limited by the .NET 30 s per-harness timeout (~6 ms per mocked frame; 2400 + 2 x 1500 timed
    out raphael).
  - RED capability (scratch copy with a per-call random ray alpha): processing 3009, raphael 3019 entries — FAIL.
    GREEN: processing 58/58, raphael 34/34. `dotnet build CielWin.sln` 0 warnings / 0 errors; `dotnet test
    CielWin.sln`: Interop 269 passed / 22 skipped, App 716 passed / 0 failed.
- S4b measured (e055fd2 via worktree at 9e58537, Release, 1 pass): explorer+warning 60 draws/s, ~103% CPU (was ~184% after S4a, ~218% baseline); processing 55.8, raphael 60 ~101%, explorer 60 ~94%, idle 60 ~54%. User approved S4b appearance live (explorer + held warning, 2026-10-04).

- S4c done (implementation; CieLinux `4574b4a` W1 spark atlas, in `// Scene optimization begin/end (S4c)` blocks
  that only add lines; stripping them restores the 24f3941 LF SHA-256 of `explorer/js/rising-sparks.js`; the S1
  pin test now strips S4c first). Idle has no rising sparks, so only explorer changed; `animate.js` untouched.
  - Full wallpaper only: a mature spark (age >= `RISING_SPARK_TRAIL_SECONDS`) is two trail halves (samples 0-3,
    3-6) stamped from one atlas, each rotated onto its chord in the context's own transform (`getTransform`, so
    the S4b spark layer and the see-through hook get the same streaks); young sparks keep the reference strokes.
    Half 0 baked at unit alpha, half 1 (segments 4-6 + head) at half alpha, stamped once at 2a or twice at a, so
    the additive brightness is kept without clamping in the bake. Atlas rebuilt only when W, H or DPR change.
  - Adapted vs copied: the atlas bake, cell layout (rows <= 4096 device px), base-transform fallback and stamp
    math are CieLinux's. Not ported: the mini atlas, O1 slot caching/trail buffer/culling, O1b hoisted terms,
    O1d mini head. The bake uses the reference expressions inline (`t = s / 6`, `mix(0.4, 1, t)`, template
    color string); the three chord samples (0, 3, 6) come from `risingSparkPosition` with the reference
    sample-age expression. Additions: the sprites carry the context's `globalAlpha` (always 1 today) and the
    young-spark reset restores it instead of a hard-coded 1; zero-length chords are guarded.
  - Fidelity choices: length buckets every 0.25 device px (CieLinux 0.5; endpoint error halves), size buckets
    every 0.05 (as CieLinux). Atlas 4032x552 at 3440x1440 (5100 cells, ~8.9 MB; CieLinux ~4032x282), baked once.
    Kept CieLinux's two halves per spark (thirds would cut chord error but add a third stamp and a new design).
  - Measured in the harness (no rasterizer, geometric bound): 7 timestamps at 1720x720, 1974 mature + 178 young
    sparks, 6506 draw ops vs 15064 reference paths (same as CieLinux); trail-point error median 0.030 device px,
    p99 0.407, worst 0.726 at 3440x1440 and 1720x720@2 (CieLinux median 0.05, p99 0.5, worst ~0.8); 0.028 /
    0.382 / 0.680 at 1920x1080@1.25; size error <= 0.025.
  - Could look different: mature spark streaks slightly softer (bilinear resampling of a rotated sprite; CieLinux
    measured mean 0.17 levels, max <= 153 on streaks), inner trail samples up to ~0.7 device px off the bend.
  - Tests: new `CielWin.App.Tests/Wallpaper/Web/rising-spark-atlas.checks.js` (registered by the explorer
    harness; 2 realms): S4c pins; mature = two atlas halves and young = reference ops (vs the stripped
    `drawRisingSparksReference` in the same realm); halves on the analytic trail with exact additive alpha at 3
    geometries; bake ops = reference segments (half 1 at half gain), once per geometry, rebuilt on resize and DPR
    change; composes with a translated context and a context alpha; full frames stamp from one atlas; bounded
    over 1500 irregular timestamps + 3 x 100 in later hours (counting context, ~1.0M stamps, no rebuild, no new
    canvas); mini stream identical to the reference and no atlas.
  - RED: S4c checks 0/8 (`risingSparkFullAtlas is not defined`, no sprite transforms, pin unmarked). GREEN:
    explorer 40/40 (~3 min, timeout 15 min). `dotnet build CielWin.sln` 0 warnings / 0 errors; `dotnet test
    CielWin.sln`: Interop 269 passed / 22 skipped, App 716 passed / 0 failed.

Next: S4c needs the Windows explorer draws/s + CPU measurement (S2 probe) and the user's live visual approval.

# Alert sounds

Locator: `odd/tasks/alert-sounds.md` · Engram mirror: `odd/alert-sounds/tasks` · Branch: `feat/alert-sounds` (from `main` @ `f114c15`)

## Objective

Play a sound when a failed or warning alert appears. Stage 1 (this document): built-in sounds shipped in the repo.
Stage 2 (later, separate tasks): import custom sounds.

## Problem / why

Alerts are visual only; an alert that appears while the user looks elsewhere goes unnoticed.

## Scope

Stage 1:
- Built-in `failed.wav` / `warning.wav`: the user's own sounds (`tools/alert-sounds/source/*.m4a`, AAC), converted
  to 16-bit PCM WAV by a repo script because `SoundPlayer` only reads WAV.
- Played host-side (C#) from `AlertDriver`, so it works the same for the full wallpaper and the mini window and does
  not depend on the WebView autoplay policy.
- One sound per alert: only when a NEW alert is displayed, never on a re-show of the same alert (surface recreation).
  An alert with both failed and warning groups plays only the failed sound.
- A mute setting (persisted in `Settings`) with a tray toggle.

Stage 2 (decided, not started): per-kind import of `.wav` or `.mp3` via the tray, copied to
`%LOCALAPPDATA%\CielWin\sounds\`; imported files play through WPF `MediaPlayer`, built-ins keep `SoundPlayer`;
a failing imported sound (`MediaFailed`, missing file) falls back to the built-in and is traced. Reset to default.

## Constraints

- Test-first where a deterministic test exists (xUnit, `dotnet test`).
- Playback never blocks the UI thread and never throws out of `AlertDriver` (failures traced, type name only, per the
  `FileTrace` rule).
- Delivery strategy: `ask-on-risk` (forecast ~300 authored lines for stage 1, under the 400 budget).

## Tasks

- [x] S1 Built-in sounds (commits `831b1f5`, then reopened for the user's sounds: see S1b).
      Route: inline (one small script, no design open).
- [x] S1b Reopened: user supplied `failed.m4a` / `warning.m4a` (in untracked `docs/media`). Sources copied to
      `tools/alert-sounds/source/`; `tools/alert-sounds/convert-alert-sounds.py` (ffmpeg, bit-exact) replaces the
      synthesizer and writes the WAVs. Route: inline.
- [x] S2 Player seam + playback: `IAlertSoundPlayer` (production: `SoundPlayer` over embedded WAV resources);
      `AlertDriver` plays once per newly displayed alert, failed wins over warning, failures traced.
      Route: delegated writer (AlertDriver, new player, csproj, composition, tests: 2+ non-trivial files).
- [x] S3 Mute: `Settings` gains a sound on/off flag (default on, parsed/serialized, old settings files still load);
      tray toggle; muted means no playback. Route: same delegated writer.

## Acceptance criteria

- S1: script re-run produces byte-identical files.
- S2/S3: new tests RED before, GREEN after; full suites green; `dotnet build` 0 warnings.
- Manual: user hears the failed and warning sounds once per alert, and silence when muted.

## Progress

- 2026-10-02: branch and document created. S1 implemented: script ran twice, sha256 identical
  (failed.wav 62666 B: E5->A4 descending; warning.wav 39734 B: A5 ping; 44.1 kHz 16-bit mono, peak 0.45).
  No RED: generated assets have no meaningful runnable test before they exist; determinism check instead.

- S1b: user sounds converted (failed.wav 319532 B, warning.wav 241708 B; 48 kHz stereo 16-bit; RIFF); re-run
  sha256 identical. No RED (asset swap); determinism check instead.
- S2 done (route: delegated writer). `IAlertSoundPlayer` + `EmbeddedAlertSoundPlayer` (one `SoundPlayer` per kind,
  loaded once, errors traced). `AlertDriver` keeps `_sounded` (last `ActiveAlert` instance played or tried) and plays
  only after a successful `ShowAlert` of a different instance; failed wins; a throwing player traces
  `alert sound-failed error=<Type>`. `AppComposition.Wire` takes the player as 4th parameter. RED: CS0246
  `IAlertSoundPlayer` missing. GREEN: App 505 passed. Commit `c1808b7`.
- S3 done (route: same writer). `Settings.AlertSoundsEnabled` (key `alert-sounds = on|off`, default on, missing or
  unreadable keeps on); tray entry "Alert sounds" between Scene and Exit, tick refreshed on open, toggle persisted and
  traced; alerts shown while muted count as sounded (unmuting never replays them). RED: CS1729/CS1061/CS0117 on the
  new members. GREEN: App 521 passed (baseline 489), Interop 184 passed + 16 skipped; `dotnet build --no-incremental`
  0 warnings. Commit `4d3e8b8`.
- Parent spot check after S1b: `dotnet build` 0 warnings; 140 sound/tray/settings tests pass on the new WAVs.

- Review `f114c15..3155617` (729 lines, high: process boundary in the convert script): consent granted, 4 lenses,
  approved and acknowledged (lineage `review-b2982d6a7a5231da`, authority burned). The collect step for the
  intended-untracked selection rejected the known-good JSON shape as invalid; preflight re-run with
  `--untracked-scope=exclude` instead (unrelated untracked `docs/`). Non-blocking follow-ups, all in
  `EmbeddedAlertSoundPlayer.cs`: R2-duplicated-sound-failure-trace (26-42, warning), R2-silent-default-kind-mapping
  (20-24), R3-embedded-player-failure-paths-untested (26-67).

- `fix/mini-topmost-reassert` merged into this branch (`ec2b051`, user-approved): Debug build 0 warnings, App 521
  passed, Interop 189 passed + 16 skipped.
- Manual check (2026-10-02): Release build from this branch, `POST /v1/alerts` failed then warning (both 202);
  user confirmed both sounds were heard. No `alert sound-failed` in `trace.log`. Mute toggle not yet checked by hand.

## Stage 2: import only (user decision 2026-10-02)

No built-in sounds ship (copyright): the app is silent until the user imports a sound. Stage 1's embedded sounds are
removed. Stage 1 is NOT merged separately; stage 2 continues on this branch (single PR).

Decisions:
- Formats: `.wav`, `.mp3`, `.m4a` (the user's own sounds are m4a; WPF `MediaPlayer` plays all three).
- Each kind plays only its own imported sound; a kind with no sound stays silent (no cross-kind fallback).
- Imported files are copied to `%LOCALAPPDATA%\CielWin\sounds\` so moving/deleting the original does not break them.
- The "Alert sounds" mute toggle is hidden until at least one sound is imported.
- A missing or unplayable imported file is traced (type name / reason code only) and stays silent; never throws.

Resolved: every sound file was purged from this branch's history (see Progress). Commit ids above this point in
the document are pre-rewrite and no longer exist.

- [x] I1 Remove built-in sounds: `CielWin.App/Assets/Sounds/*`, the csproj embedding, `EmbeddedAlertSoundPlayer`,
      `tools/alert-sounds/**`, and their tests. Route: delegated writer.
- [x] I2 Sound library: settings keys for the imported failed/warning sound file names; importer that validates the
      extension and copies into the sounds folder (replacing the previous one of that kind); remove per kind.
      Route: same writer.
- [x] I3 Player: `MediaPlayer`-based `IAlertSoundPlayer` that plays the imported sound of the kind; missing file or
      `MediaFailed` traced and silent. Route: same writer.
- [x] I4 Tray: "Import failed sound…", "Import warning sound…" (file dialog), "Remove failed/warning sound" only when
      that kind is imported, "Alert sounds" toggle only when at least one is imported. Route: same writer.

- I1-I4 done (route: delegated writer). Commits (pre-rewrite ids) `a775c2d` remove built-in sounds, `4720c1d` import
  (I2+I3), `4532fa7` tray entries. Settings keys `failed-sound` / `warning-sound` (bare file name; paths, `..` or
  other extensions read as no sound). `AlertSoundLibrary` (folder `%LOCALAPPDATA%\CielWin\sounds`, copy to temp then
  move over `<kind>.<ext>`, other extensions of that kind deleted). `MediaAlertSoundPlayer` behind `IAlertSoundOutput`
  (one `MediaPlayer` per kind, volume 1.0, closed after play/fail); traces `alert sound-skipped kind=X
  reason=missing-file`, `alert sound-failed kind=X error=<Type>`. Tray: imports, removes (only when that kind has a
  sound), mute toggle (only when any sound); `item.Available` set on open. README updated.
  RED: CS0246 `MediaAlertSoundPlayer`/`IAlertSoundOutput`; CS0117 new `TrayMenuEntry` members. I1 deletion: no RED.
  GREEN: App 586 passed, Interop 189 + 16 skipped, `dotnet build --no-incremental` 0 warnings.
  Untested by design: real `MediaPlayer` and `OpenFileDialog` (manual check). Known risk: re-importing a kind while
  its sound plays may hit a file lock (traced `import-failed`, previous sound kept).
- History rewrite approved by the user (2026-10-02): `git filter-branch --index-filter` over `main..feat/alert-sounds`
  removed `CielWin.App/Assets/Sounds` and `tools/alert-sounds/source` from every commit (two subjects reworded to
  match their content); `refs/original` deleted, reflog expired, `git gc --prune=now`. Checks: no `.wav/.m4a/.mp3`
  in `git rev-list --all --reflog --objects`; old commits gone; 0 unreachable objects; branch never pushed (no remote
  branch contained them). `main` and `fix/mini-topmost-reassert` untouched. Post-rewrite: `dotnet build
  --no-incremental` 0 warnings; App 586 passed, Interop 189 + 16 skipped. Note: commits from the generator script
  up to the I1 removal reference WAV resources that no longer exist in history, so those intermediate commits do not
  build on their own; the branch tip does.

- Review `f683bb2..64d538b` (1429 lines, medium, `slice_budget_reached`): consent granted, 1 lens (reliability),
  approved and acknowledged (lineage `review-ccee89bd33709b51`, authority burned). Findings (non-blocking):
  R3-import-partial-commit-on-stale-delete (`AlertSoundLibrary.cs:83-90`, warning) FIXED below;
  R3-failed-copy-test-does-not-prove-kept-setting (`AlertSoundImportWiringTests.cs:139-148`) and
  R3-foreground-hook-failure-silent-and-callback-untested (`Win32MiniSceneWindow.cs:180-182`) left as follow-ups.
- Fix: a stale other-extension file that cannot be deleted no longer fails an import that already moved the new
  sound in (best-effort delete). Route: inline (one file + test). RED: new
  `Import_WhenThePreviousSoundOfAnotherExtensionIsLocked_StillCommitsTheNewSound` failed with `IOException`.
  GREEN: App 587 passed; build 0 warnings.

- Follow-ups closed (route: delegated writer, user asked to cover them before the manual check):
  - R3-failed-copy-test-does-not-prove-kept-setting: test now imports `previous.wav` first, then a missing
    `gone.mp3`; asserts no new save, file intact, no `failed.mp3`, remove entry visible, next alert plays the
    previous file. Passed on first run (guards existing behavior, no defect). Commit `6284ab3`.
  - R3-foreground-hook-failure-silent-and-callback-untested: `IMiniSceneSurface.ReassertsTopmost`; callback moved to
    `Win32MiniSceneWindow.OnForegroundChanged` (behavior unchanged); controller traces
    `mini-window: foreground hook unavailable` once after create+place when false. RED: compile errors (Interop) and
    `ShowTracesOnce...` failing (App). 4 real-desktop tests (`RequiresDesktop`, skipped while CielWin runs; run once
    with the gate lifted temporarily: 4/4 passed). Commit `d6609f4`.
  - Verification: `dotnet build --no-incremental` 0 warnings; App 589 passed; Interop 189 passed + 20 skipped; CI
    filter `Category!=RequiresDesktop`: App 589, Interop 189 + 3 skipped. Parent spot check: 38 import/controller
    tests pass.
- Assess `64d538b..d6609f4`: medium, 193 lines, `review_due` false (`under_budget`); pending in the next slice.

- Side request (user, 2026-10-02): default HTTP port 47811 -> 43811 (`AlertHttpProtocol.DefaultPort`, README, two
  default-port tests). RED: `AlertHttpProtocolTests` expected 43811, actual 47811. GREEN: App 589, Interop 189 + 20
  skipped; build 0 warnings. Commit `4df47a3`. Settings files with an explicit port keep it.
- Manual check started: user imported both m4a sounds via the tray (`alert-sound imported kind=failed|warning`,
  files `failed.m4a`/`warning.m4a` in the sounds folder); test alerts failed then warning on 43811 answered 202,
  no `alert sound-skipped|failed` traced.

- User confirmed both imported sounds play (2026-10-02).

- Remove checked by hand (2026-10-02): `alert-sound removed kind=failed|warning` traced, both settings keys empty,
  sounds folder empty.

## Next step

Merge decision: assess the unreviewed slice since `64d538b`; optionally squash stage-1 commits that no longer build.

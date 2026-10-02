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

Open before merge: the user's m4a/wav are in this branch's history (`3155617`, `ec2b051`'s ancestors). They must not
reach `main`/remote history: squash or rewrite the branch before merging (needs user approval).

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
- History rewrite approved by the user (2026-10-02): purge every sound file from `main..feat/alert-sounds`.

## Next step

Rewrite history, verify, then the user's manual check of import/play/remove/menu visibility.

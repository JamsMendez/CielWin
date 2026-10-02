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

## Next step

Stage 2 (import .wav/.mp3), or merge stage 1 to `main` first (user decides).

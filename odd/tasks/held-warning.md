# Held warning

Locator: `odd/tasks/held-warning.md` · Engram mirror: `odd/held-warning/tasks` · Branch: `feat/held-warning` (from `main` @ `14ff645`)

## Objective

Port CieLinux's held-warning alert API (H1) and its repeating warning sound (H4) to CielWin for API parity.
Source spec: CieLinux commit `2f0e3e6` (README *Held warning* / *Writing a client*, `docs/cielwin-portability.md` §4,
`src/alerts.cpp`, `src/http-server.cpp`, `tests/alerts.contract.test.mjs`).

## Problem / why

Callers (the cielinux-scenes Claude Code plugin) keep a warning up while a question waits. CielWin answers `400` to
`duration: 0`, `404` to `/v1/alerts/clear`, and replies exactly `ok`, so callers fall back to a timed warning.

## Scope

- `POST /v1/alerts`: `"duration": 0` allowed with `warning` only; with any `failed` →
  `400 error: 'duration:0' requires warning only`.
- Accepted alert → `202 ok id=<n>` (`n` grows per run); ignored (busy) → plain `202 ok`.
- `POST /v1/alerts/clear`: same checks, body ≤ 64 bytes, `{}` (clears the held alert only) or `{"id": n}` (n ≥ 1,
  clears that alert held or timed, showing/suspended/waiting); always `202 ok`.
- Failed request preempts a showing/waiting held warning; held is suspended and resumes after for the rest of its hold,
  same id, no second sound; not resumed if cleared or past hold max meanwhile.
- Held request during a timed alert waits (5-minute start limit still applies).
- Safety max `alert-hold-max-seconds` (default 600, range 10–3600), counted from the request.
- H4: while a held warning shows (not suspended, waiting or covered), its warning sound repeats every 5 s, restarting
  from each show/resume; timed alerts play once.
- Diagnostics: dropped past hold max, suspended by failed alert, cleared.

## Constraints

- Test-first with the existing xUnit suites (`dotnet test CielWin.sln`).
- Delivery strategy: `ask-on-risk` → user chose `feature-branch-chain`, all local: slices are commit ranges on
  `feat/held-warning`, merged locally into `main` with `--no-ff` at the end; no PRs, no push (the user pushes `main`).
- Slices: S1 = H1 (`14ff645..6cf8733`); S2 = H1b; S3 = H4.

## Tasks

- [x] H1 Held warning API: parser, queue (ids, clear, hold max, preempt/resume), HTTP protocol + clear route,
      setting, diagnostics, tests. Route: delegated (writer trigger: 5+ non-trivial files).
- [x] H1b Review advisories (R3, reliability): (a) a held warning preempted twice replays its sound on the second
      resume (`AlertDriver` remembers only the last two sounded ids); (b) a held request accepted while another held
      warning is suspended lets two held warnings coexist and a later failed request overwrites `_suspended` without a
      diagnostic. Route: delegated (writer).
- [x] H4 Repeating held-warning sound in `AlertDriver` with driver tests. Route: delegated (same writer, separate commit).

## Acceptance criteria

- All scope bullets covered by tests; full `dotnet test CielWin.sln` green.

## Progress / next step

H1 done (route: delegated writer).
- RED: 57 compile errors (missing H1 API) after writing the tests first.
- GREEN: `dotnet build CielWin.sln` 0 errors / 0 warnings; `dotnet test CielWin.sln`: Interop.Tests 269 passed /
  22 skipped / 0 failed; App.Tests 657 passed / 0 failed.
- Files: `AlertCommand.cs`, `AlertCommandParser.cs`, `AlertQueue.cs`, `AlertDriver.cs`, `CompositionHost.cs`,
  `ProductionComposition.cs`, `AppComposition.cs` (surface added with user approval), `Settings.cs`,
  `AlertHttpProtocol.cs`, `LocalHttpCommandServer.cs`, `README.md`, tests (new `AlertQueueHeldWarningTests`,
  `AlertDriverHeldWarningTests`; updated parser, settings, protocol, server, composition wiring tests).
- Deviation from CieLinux: clear id is `null` (not `0`) for "the held warning".
- H4 hook: `ActiveAlert.Id` is stable across re-show/resume, `StartedAt` resets on each show/resume, and
  `Command.IsHeld` marks a held warning; the driver sees a show/resume as the tick the displayed id changes.
- Commit: `2e247ae` feat(alerts): add held warnings with clear route
- RDD: medium, granted; 1-lens (reliability) review approved and acknowledged (lineage
  `review-c6d462844bb67776`, authority burned); two non-blocking WARNING findings became H1b.
- Running count: ~1300 changed lines after H1 (over the 400 budget); chain strategy chosen (local feature-branch chain).
- Next: H1b, then H4.

H1b done (route: delegated writer; R3 advisories from the H1 review).
- (a) `AlertDriver` remembers the last sounded id plus the last sounded held id (`_soundedHeld`), replacing the
  last-two-ids pair, so a held warning preempted any number of times never replays on resume, while one preempted
  before it ever showed still sounds on its first show.
- (b) `AlertQueue.Enqueue` ignores a held request while a held warning is suspended (`alert ignored: a held warning
  is already suspended`, plain `202 ok`); with the existing showing/waiting rules at most one held warning is alive,
  so `_suspended` is never overwritten. README *Held warning* documents both rules.
- CieLinux `2f0e3e6` has the same two bugs (`announced`/`announcedBefore` pair; `enqueue` never checks `suspended`);
  CielWin follows the documented rules here instead of mirroring them.
- RED: 3 new tests failed (queue: held request accepted as id 3 instead of 0; resume returned id 3 instead of 1;
  driver: sounds `[Warning, Failed, Failed, Warning]` instead of `[Warning, Failed, Failed]`).
- GREEN: `dotnet build CielWin.sln` 0 errors / 0 warnings; `dotnet test CielWin.sln`: Interop.Tests 269 passed /
  22 skipped / 0 failed; App.Tests 660 passed / 0 failed.
- Commit: `a97697d` fix(alerts): keep one held warning and never replay its sound on resume
- Next: H4.
- H1b RDD assess (base `6cf8733`): medium, 137 lines, `review_due=false` (`under_budget`); stays pending in the slice with H4.

H4 done (route: delegated writer, separate commit).
- `AlertDriver` keeps `_repeatAt` on the existing UI tick (no new timer): set to show/resume time + 5 s
  (`HeldWarningRepeat`) when a held warning starts; on each tick with the same displayed held id it is reset while the
  surface is not visible (covered), re-armed a full period later when visible again, and on reaching it plays
  `Warning` if `soundsEnabled()` at that moment (mute read per repeat, no trace when muted) and re-arms. Cleared by any
  displayed-id change, a null surface and `SurfaceReplaced` (a re-show restarts the cadence, plays nothing).
  Throwing player traced via the shared `TryPlay`. Timed alerts never repeat.
- Tests: 9 `H4_*` facts in `AlertDriverHeldWarningTests` (repeat until clear, hold max, timed once, suspended +
  resume restart, covered + uncover restart, waiting while covered, surface replace restart, mute at repeat time,
  throwing player) and 1 theory (2 cases) in `AlertDriverSoundTests` mirroring CieLinux's "no extra clock read"
  stepping-clock case (598000 ms show; no repeat after 5 updates, one after 6). README *Held warning* and the sound
  section state the 5 s exception.
- Deviations from CieLinux: none in behavior; a null surface also stops the cadence; tests added for surface replace,
  mute, and throwing player (CieLinux covers mute in the sounds unit test).
- RED: 8 of the new held-warning tests failed (e.g. sounds `[Warning]` instead of `[Warning, Warning, Warning]`);
  the timed-once test passed as expected.
- GREEN: `dotnet build CielWin.sln` 0 errors / 0 warnings; `dotnet test CielWin.sln`: Interop.Tests 269 passed /
  22 skipped / 0 failed; App.Tests 671 passed / 0 failed.
- Commit: `a8b8364` feat(alerts): repeat the held warning sound every 5 seconds
- Next: RDD assess of the S2+S3 slice (base `6cf8733`), then local `--no-ff` merge into `main`.
- S2+S3 RDD (base `6cf8733`, H1b+H4): medium, 447 lines, `slice_budget_reached`; granted; 1-lens (reliability)
  review approved and acknowledged (lineage `review-22d30f5a9b3dfbef`, authority burned). One non-blocking
  SUGGESTION (follow-up): test that the suspended-held guard lifts once the suspended warning is cleared or expires.
- Parent spot check: `dotnet test CielWin.App.Tests --filter AlertDriver` 30/30.
- CieLinux has the same H1b bugs; fixed there on `fix/held-warning-one-held-and-sound` at the user's request.
- Delivery: merged locally into `main` with `--no-ff`; the user pushes.

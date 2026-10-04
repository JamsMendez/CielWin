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
- Delivery strategy: `ask-on-risk`. Forecast ~600 authored lines (over the 400 budget): ask for a chain strategy
  before slicing if the running count exceeds it.

## Tasks

- [x] H1 Held warning API: parser, queue (ids, clear, hold max, preempt/resume), HTTP protocol + clear route,
      setting, diagnostics, tests. Route: delegated (writer trigger: 5+ non-trivial files).
- [ ] H4 Repeating held-warning sound in `AlertDriver` with driver tests. Route: delegated (same writer, separate commit).

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
- Commit: COMMIT_PENDING
- Next: H4.

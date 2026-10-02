# CielWin follow-ups

Locator: `odd/tasks/cielwin-follow-ups.md` · Engram mirror: `odd/cielwin-follow-ups/tasks` · Branch: `fix/cielwin-follow-ups` (from `main` @ `868364d`)

## Objective

Close the non-blocking follow-ups left after the extraction merge (see `odd/tasks/cielwin-extraction.md`).

## Scope

- Guard the unguarded event handlers flagged by review.
- Leave a trace line when startup composition throws.
- Resolve three small product decisions with the user, one at a time.

## Constraints

- Test-first where a deterministic test exists (xUnit, `dotnet test`).
- No behavior change beyond the listed items. CosmicWin stays read-only reference.
- Delivery strategy: `ask-on-risk` (forecast well under 400 authored lines).

## Tasks

- [x] F1 Guard tray click (`TrayIconHost.cs:26-42`), mini hotkey move (`MiniSceneSurface.cs:97-98`) and hotkey
      registrar error swallowing (`Win32HotkeyRegistrar.cs:113-119`): exceptions are traced, never crash or vanish silently.
      Route: delegated writer (3 non-trivial files + tests).
- [ ] F2 Startup failure trace: a throwing `ProductionComposition.Wire` (`App.xaml.cs`) writes a trace line before the
      process exits. Route: delegated writer (same writer as F1).
- [ ] F3 Product decisions (ask one at a time):
  - [ ] F3a HTTP scene switch while mini window failed: keep 202 or reply 503 like CosmicWin.
  - [ ] F3b Tray icon pale at 16-24 px on light taskbars.
  - [ ] F3c Delete unused `CielWin.App/cielwin.ico`.

## Acceptance criteria

- F1/F2: new tests RED before, GREEN after; full suites green; build 0 warnings.
- F3: each decision recorded here with the user's answer and any resulting change.

## Progress

- 2026-10-02: branch created, document created.
- F1 done (route: delegated writer). Tray clicks go through `TrayIconHost.Guarded` (traces
  `tray click-failed item=<mode|scene|exit> error=<Type>`); `MiniSceneSurface.TryMoveTo` catches a failing display read
  or move (traces `mini-position move-failed error=<Type>`, returns false, nothing persisted);
  `Win32HotkeyRegistrar(onHandlerFailed)` reports a throwing `Pressed` handler by type name (production traces
  `hotkey handler-failed error=<Type>`), a throwing callback is swallowed too. Type names only, per the `FileTrace` rule.
  RED: compile errors CS0117 `TrayIconHost.Guarded` missing (x3), CS1729 `Win32HotkeyRegistrar` 1-arg ctor missing (x2);
  then (tray compiled) 2 `MiniModeWiringTests.AltM_When...` failed with the escaping `InvalidOperationException`
  ("no monitor", "move failed"). GREEN: filtered tests pass; full suites App 485 passed (was 480),
  Interop 184 passed + 16 skipped (was 182 + 16); `dotnet build --no-incremental` 0 warnings, 0 errors.

## Next step

F1+F2 writer; ask F3a.

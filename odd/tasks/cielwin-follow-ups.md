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
- [x] F2 Startup failure trace: a throwing `ProductionComposition.Wire` (`App.xaml.cs`) writes a trace line before the
      process exits. Route: delegated writer (same writer as F1).
- [ ] F3 Product decisions (ask one at a time):
  - [x] F3a HTTP scene switch while mini window failed: keep 202 or reply 503 like CosmicWin.
        Decision (user, 2026-10-02): keep 202. The scene is persisted and applied on recovery, which is what
        "Accepted" means. No code change.
  - [ ] F3b Tray icon pale at 16-24 px on light taskbars.
        Decision (user, 2026-10-02): fix it. Thin dark outline and higher contrast at 16/20/24 px only, in
        `tools/tray-icon/render-raphael-mini.mjs`, then regenerate `CielWin.App/Assets/raphael-mini.ico`.
        Needs a manual visual check by the user. Route: delegated writer.
  - [x] F3c Delete unused `CielWin.App/cielwin.ico`.
        Decision (user, 2026-10-02): delete. Verified unreferenced by any `.csproj` or source. Route: inline.

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
  Commit `ffd4a48`.
- F2 done (route: delegated writer). `ProductionComposition.Wire` creates the trace first, then runs the wiring
  through `TraceStartupFailure`, which traces `startup wire-failed error=<Type>` and rethrows the same exception
  (crash semantics unchanged; `App.xaml.cs` untouched). Exception type only, not the message: `FileTrace` and
  `MtaActionThread` forbid messages (they can hold absolute paths). RED: CS0117 `TraceStartupFailure` missing (x3).
  GREEN: 3 `StartupFailureTraceTests` pass; full suites App 488 passed, Interop 184 passed + 16 skipped;
  `dotnet build --no-incremental` 0 warnings, 0 errors. Commit `047ea3b`. Parent spot check: 16 focused tests pass.
- Review assess `868364d..047ea3b`: risk medium, `review_due` false (`under_budget`); stays pending in the slice.
- F3a decided (keep 202), F3c done: `cielwin.ico` deleted.

## Next step

F3b icon contrast fix, then the user's visual check.

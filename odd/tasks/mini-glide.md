# Mini glide

Locator: `odd/tasks/mini-glide.md` · Engram mirror: `odd/mini-glide/tasks` · Branch: `feat/mini-glide` (from `main` @ `8b3a282`)

## Objective

Alt+M / Alt+Shift+M glide the mini window to its next position with a short eased animation instead of jumping.

## Problem / why

The jump is abrupt; a short glide shows where the window went. It must stay fast so it never feels sluggish.

## Scope

- Hotkey moves (`MiniSceneSurface.TryMoveTo`) glide: ~220 ms, ease-out cubic, frame-driven on the UI thread.
- A new hotkey press mid-glide retargets from the current animated position (no queue, no jump back).
- Non-hotkey placements (show, watch-tick re-place after a taskbar change) stay instant and cancel a running glide.
- The browser viewport is resized once, at the end of the glide, never per frame.
- Alt+M leak: reproduced only in the user's Alacritty running WSL (`cat -v` shows bare `m`/`M` per auto-repeat,
  no ESC prefix); not reproduced with injected input into WT/Alacritty running PowerShell. Fixed with a
  low-level keyboard hook (G2).

## Constraints

- Test-first: pure interpolation/easing and controller glide behavior through the existing injected clock.
- Never activates the window (same placement flags as today); a failing frame never throws out of the dispatcher.
- Delivery strategy: `ask-on-risk` (forecast ~250 authored lines, under the 400 budget).

## Tasks

- [x] G1 Glide animation for hotkey moves. Route: delegated (writer trigger: surface, controller, interface,
      tests = 2+ non-trivial files). `MiniGlide` (pure math) + `IFrameSource` seam over
      `CompositionTarget.Rendering`; `IMiniSceneWindow.GlideTo`. RED observed (11 failing), GREEN: writer full run
      606/606; parent spot check 69/69 (glide/controller/wiring filter). Pending: manual check in the running app.
      Commit `0a04654`. RDD: medium, granted, 1-lens review approved and acknowledged (lineage
      `review-dd30578e4cd4461f`, authority burned).
- [x] G1b Review advisories. Route: delegated (writer). (a) `IMiniSceneWindow.PlacementLost` raised when a glide
      fails to land; `MiniSceneSurface` clears `_placed` so the next tick re-places at the confirmed `Position`
      (persist semantics unchanged). (b) `Show` stops a running glide first (the controller has no `Hide`; `MoveTo`
      and `Dispose` already stopped it). RED: 3 failing (land-failure reports placement lost; wiring tick re-places;
      Show mid-glide with a throwing scene switch). Show-mid-glide plain case was already green (Show->MoveTo
      stopped it). GREEN: App.Tests 611/611, Interop.Tests 189 passed / 20 skipped. Commit `a314ee6`.
- [x] G2 Alt+M leak. Route: delegated (writer). `Win32KeyboardHookRegistrar` (WH_KEYBOARD_LL on its own background
      thread with a GetMessage loop; swallows fresh down, repeats and up; injects VK 0xE8 after firing) behind
      `IHotkeyRegistrar`; pure `KeyboardChordMatcher` (exact modifiers: Ctrl+Alt/Win never match; key held before
      Alt passes). Production falls back to `Win32HotkeyRegistrar` with `hotkey hook-unavailable error=...`.
      `Pressed` now raised on the hook thread; `AppComposition` already posts via `OnUiThread`. RED: matcher 10
      failing, fallback choice 3 failing. GREEN: App.Tests 614/614, Interop.Tests 210 passed / 22 skipped;
      opt-in real-hook lifecycle facts (`CIELWIN_RUN_DESKTOP_TESTS=1`) passed. Commit `c4be138`.
      Pending: the user's manual re-check with the real app (Alacritty->WSL `cat -v`, Alt tap menu, AltGr).
- [x] G3 Hook review advisories. Route: delegated (writer). (a) Stale down-state: `KeyboardChordMatcher.OnKey` takes
      the event time (`KBDLLHOOKSTRUCT.time`); a down of a key believed down is a repeat only within
      `StaleThresholdMs` = 1500 ms of that key's last event (unsigned subtraction, wrap-safe), otherwise the stale
      down/swallowing state is dropped and the down is fresh. (b) HookProc mapping: pure
      `HandleHookEvent(message, vkCode, time, ModifierSnapshot)` + `ModifierSnapshot.ToHotkeyModifiers`; HookProc
      keeps only the native reads + `CallNextHookEx`. RED: 15 failing (3 stale/wrap matcher, 12 hook mapping).
      GREEN: `dotnet build` 0 warnings; Interop.Tests 233 passed / 22 skipped; App.Tests 614/614. Commit `49e1d58`.

## Acceptance

- Holding/pressing the chords moves the window smoothly to the same final bounds as before; final position persisted
  as before.
- `dotnet test` green.

## Progress

- RDD for a314ee6..e1618a1: medium, granted, 1-lens approved and acknowledged (lineage `review-d4b6fdb2313d203b`).
  Advisories (non-blocking follow-ups): stale key-down state if a key-up is missed (secure desktop / hook
  removed) costs one keystroke; HookProc native mapping has no automated test. Both addressed in G3.
- G1, G1b, G2 done (commits recorded above). Next: user's manual check of the glide and of Alt+M in the real
  app; then RDD assessment for `a314ee6..c4be138` and delivery under repository policy.
- 2026-10-02 manual check by the user on the fresh Release build: glide looks good; Alacritty->WSL `cat -v` stays empty
  on Alt+M / Alt+Shift+M (tap and hold); plain m, AltGr and Alt menu fine. Feature complete; push/PR are the user's call.
- 2026-10-02 merged into `main` (`69e0573`, --no-ff), branch deleted, released as `v1.2.0`.

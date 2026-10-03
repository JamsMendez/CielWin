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
  no ESC prefix); not reproduced with injected input into WT/Alacritty running PowerShell. Fix decision pending (G2).

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
- [ ] G1b Review advisories (in scope): (a) a glide land failure leaves `MiniSceneSurface._placed` at the target, so
      ticks never re-place a window stuck mid-glide; (b) `Show`/`Hide` do not stop a running glide.
- [ ] G2 Alt+M leak (pending user decision on a WH_KEYBOARD_LL hook replacing RegisterHotKey).

## Acceptance

- Holding/pressing the chords moves the window smoothly to the same final bounds as before; final position persisted
  as before.
- `dotnet test` green.

## Progress

- G1 done (commit recorded below). Next: G2 after the user's decision.

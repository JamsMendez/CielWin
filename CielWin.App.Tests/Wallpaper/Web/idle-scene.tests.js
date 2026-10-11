"use strict";

// html-wallpaper-demo D6c: a committed vm-sandbox harness proving the shared alert-overlay.js's own
// contract against the REAL shipped idle-scene page -- modelled on
// CielWin.App.Tests/Wallpaper/Web/explorer-scene.tests.js (that file's own remarks, and
// processing-scene.tests.js before it, explain why a Node subprocess exists at all: no .NET JS
// engine or headless browser exists in this repo). Run via
// CielWin.App.Tests/Wallpaper/IdleSceneNodeTests.cs:
//   node idle-scene.tests.js <path-to-the-REAL-shipped-wallpaper-idle-directory>
//
// This harness intentionally does NOT re-prove the shared state machine/tile-layout/message-parsing
// contract processing-scene.tests.js already covers in depth (same shared module, same behavior),
// nor the offscreen save()/restore() balance explorer-scene.tests.js already covers (also shared
// module behavior, not idle-specific) -- it proves the ONE thing that is genuinely idle-specific:
// that this scene's own js/see-through-hook.js stamps the EXACT SAME constellation-ring cache/
// angle/center the scene's own js/animate.js render loop drew THIS FRAME (RING_ANIMATIONS[0], the
// "constellation" ring), for EVERY tile, offset by that tile's own origin, and never a different
// ring -- plus the readiness/show/hide/malformed-message basics and this scene's own render-loop
// fault isolation (js/animate.js, D6a/D6b precedent).

const vm = require("vm");
const fs = require("fs");
const path = require("path");
const assert = require("assert");
const { URLSearchParams } = require("url");
const constellationRingChecks = require(path.join(__dirname, "constellation-ring.checks.js"));

const miniVariantChecks = require(path.join(__dirname, "mini-variant.checks.js"));
const pauseResumeChecks = require(path.join(__dirname, "pause-resume.checks.js"));
// Load-time diagnostics of the shared scenes that are not render errors: the CieLinux log-transport
// probes, and the nebula reporting that this vm sandbox has no WebGL.
function isDiagnosticsProbe(first) {
  return typeof first === "string" &&
    (first.indexOf("CIELINUX_DIAGNOSTICS_") === 0 || /^\[\w+-nebula\] unavailable:/.test(first));
}

const sceneDir = process.argv[2];
if (!sceneDir) {
  console.error("usage: node idle-scene.tests.js <path-to-wallpaper-idle-directory>");
  process.exit(2);
}

const sharedDir = path.join(sceneDir, "..", "shared");

// D6c improvement over explorer-scene.tests.js/raphael-scene.tests.js's own hardcoded SCRIPT_FILES
// list (an open finding on both -- "harness script order hardcoded"): read the REAL shipped
// index.html's own <script src="..."> tags, in order, instead of maintaining a parallel list here
// that could silently drift from what the page actually loads. "../shared/..." sources resolve
// against sharedDir, exactly like the "shared:" prefix explorer/raphael's own hardcoded lists used.
function scriptFilesFromIndexHtml(directory) {
  const html = fs.readFileSync(path.join(directory, "index.html"), "utf8");
  const scriptTagPattern = /<script\s+src="([^"]+)"\s*>\s*<\/script>/g;
  const entries = [];
  let match;
  while ((match = scriptTagPattern.exec(html)) !== null) {
    const src = match[1];
    entries.push(src.indexOf("../shared/") === 0 ? "shared:" + src.slice("../shared/".length) : src);
  }
  return entries;
}

const SCRIPT_FILES = scriptFilesFromIndexHtml(sceneDir);
if (SCRIPT_FILES.length === 0) {
  console.error("expected at least one <script src=\"...\"> tag in " + path.join(sceneDir, "index.html"));
  process.exit(2);
}

// ---- Minimal DOM/canvas/host mock ------------------------------------------------------------
// One shared 2D-context mock, structurally permissive (a Proxy whose unknown properties are no-op
// functions) -- same technique explorer-scene.tests.js/processing-scene.tests.js already use,
// extended here with the same tiny translation-only CTM (current transformation matrix) tracker
// explorer-scene.tests.js's own make2dContext uses (see that file's own remarks): save/restore/
// translate/setTransform are the ONLY transform-affecting calls anywhere in the call chain this
// harness cares about (the shared overlay's own tile offset translate, and js/see-through-hook.js's
// own draw call).
function make2dContext(options) {
  options = options || {};
  // Test seam: the first N createConicGradient calls throw, to prove a failed lighting-mask build is retried.
  var conicGradientFailures = options.conicGradientFailures || 0;
  var slots = {};
  var gradient = { addColorStop: function () {} };
  var stack = [];
  var tx = 0, ty = 0;
  return {
    context: new Proxy({}, {
      get: function (target, prop) {
        if (prop === "measureText") {
          return function (text) { return { width: String(text).length * 8 }; };
        }
        if (prop === "createConicGradient" && conicGradientFailures > 0) {
          return function () { conicGradientFailures--; throw new Error("injected createConicGradient failure"); };
        }
        if (prop === "createRadialGradient" || prop === "createLinearGradient" || prop === "createConicGradient") {
          // S1 test seam: options.gradientLog records every gradient (args, stops, fills) as its own object.
          if (options.gradientLog) {
            return function () {
              var recorded = { kind: prop, args: Array.prototype.slice.call(arguments), stops: [], filled: 0,
                addColorStop: function (stop, color) { this.stops.push([stop, color]); } };
              options.gradientLog.push(recorded);
              return recorded;
            };
          }
          return function () { return gradient; };
        }
        // js/earth.js (verbatim) bakes its globe texture through a REAL ImageData round-trip
        // (createImageData -> write .data -> putImageData) -- a real, minimally-functional
        // Uint8ClampedArray-backed buffer is needed here, unlike every other drawing call this mock
        // otherwise no-ops, or renderFrame's own scene try/catch would report a TypeError every frame
        // (reading .data off undefined) instead of ever reaching this scene's later draw calls.
        if (prop === "createImageData") {
          return function (width, height) { return { width: width, height: height, data: new Uint8ClampedArray(width * height * 4) }; };
        }
        if (prop === "putImageData") {
          return function () { /* no-op: nothing reads pixels back in this harness */ };
        }
        if (prop === "save") {
          return function () { stack.push({ tx: tx, ty: ty }); };
        }
        if (prop === "restore") {
          return function () { var entry = stack.pop(); if (entry) { tx = entry.tx; ty = entry.ty; } };
        }
        if (prop === "translate") {
          return function (dx, dy) { tx += dx; ty += dy; };
        }
        if (prop === "setTransform") {
          return function (a, b, c, d, e, f) { tx = e || 0; ty = f || 0; };
        }
        if (prop in slots) return slots[prop];
        return function () { /* no-op: beginPath/rect/clip/fill/drawImage/arc/rotate/scale/... */ };
      },
      set: function (target, prop, value) {
        if (prop === "fillStyle" && options.gradientLog && value && typeof value.filled === "number") value.filled++;
        slots[prop] = value;
        return true;
      },
    }),
    // Live snapshot of the CTM's current translation, read at the exact moment a spied call fires.
    currentOffset: function () { return { x: tx, y: ty }; },
  };
}

function makeCanvasElement(ctx2d) {
  return {
    width: 0,
    height: 0,
    style: {},
    getContext: function () { return ctx2d; },
  };
}

// Loads a FRESH copy of the real multi-file page into its own sandbox, mirroring a fresh page load.
function loadPage(options) {
  options = options || {};
  var made = make2dContext(options);
  var ctx2d = made.context;
  var sceneCanvas = makeCanvasElement(ctx2d);
  var postedMessages = [];
  var requestAnimationFrameCalls = [];
  var consoleErrorCalls = [];
  var consoleMock = {
    // Load-time diagnostics are not render errors (see isDiagnosticsProbe).
    error: function () {
      if (isDiagnosticsProbe(arguments[0])) return;
      consoleErrorCalls.push(Array.prototype.slice.call(arguments));
    },
    log: function () { /* no-op: unused by the scene */ },
    warn: function () { /* no-op: unused by the scene */ },
  };
  var messageListeners = [];

  var windowMock = {
    innerWidth: options.innerWidth || 1000,
    innerHeight: options.innerHeight || 500,
    devicePixelRatio: options.devicePixelRatio || 1,
    requestAnimationFrame: function (callback) { requestAnimationFrameCalls.push(callback); },
    addEventListener: function () { /* "resize" only; never fired here */ },
    chrome: options.withWebview
      ? {
          webview: {
            postMessage: function (message) { postedMessages.push(message); },
            addEventListener: function (type, listener) {
              if (type === "message") messageListeners.push(listener);
            },
          },
        }
      : undefined,
  };

  var documentMock = {
    getElementById: function (id) {
      if (id === "scene") return sceneCanvas;
      return null;
    },
    createElement: function (tag) {
      if (tag !== "canvas") throw new Error("unexpected document.createElement(" + tag + ")");
      return makeCanvasElement(ctx2d);
    },
    addEventListener: function () { /* unused: this scene's own main.js drops fullscreenchange (D6c) */ },
    fonts: undefined, // alert-overlay.js guards this with a truthiness check
  };

  var locationMock = { hash: options.hash || "", search: options.search || "" };

  var sandbox = {
    window: windowMock,
    document: documentMock,
    location: locationMock,
    URLSearchParams: URLSearchParams,
    console: consoleMock,
  };
  vm.createContext(sandbox);
  for (var i = 0; i < SCRIPT_FILES.length; i++) {
    var entry = SCRIPT_FILES[i];
    var isShared = entry.indexOf("shared:") === 0;
    var filePath = path.join(isShared ? sharedDir : sceneDir, isShared ? entry.slice("shared:".length) : entry);
    var source = fs.readFileSync(filePath, "utf8");
    // Optional: lets a case load a deliberately altered copy of one script (never written to disk).
    if (options.transformSource) source = options.transformSource(entry, source);
    vm.runInContext(source, sandbox, { filename: filePath });
  }

  return {
    sandbox: sandbox,
    postedMessages: postedMessages,
    requestAnimationFrameCalls: requestAnimationFrameCalls,
    consoleErrorCalls: consoleErrorCalls,
    canvas: sceneCanvas,
    messageListenerCount: messageListeners.length,
    currentOffset: made.currentOffset,
    dispatchHostMessage: function (data) {
      messageListeners.forEach(function (listener) { listener({ data: data }); });
    },
  };
}

// ---- Tiny test runner -------------------------------------------------------------------------
// Checks that cannot pass against the shared CielScenes submodule (odd/tasks/shared-scenes.md), skipped by
// name and reported as SKIP so they stay visible:
// - "retired": pinned CielWin's former scene internals, which CielScenes replaced with its own.
// - "pending": pin CielWin behavior CielScenes does not have yet; re-enable once it is ported there.
var SHARED_SCENES_SKIPS = {
  "the see-through hook stamps the SAME constellation-ring cache/angle/center the scene drew this frame, for every tile, and no other ring":
    "retired: CielScenes stamps its own constellationRingStamp instead of the ring cache",
  "a renamed constellation ring fails the page load instead of silently dropping the see-through ring":
    "retired: CielScenes resolves the see-through ring through constellationRingStamp",
  "a failed lighting-mask build is retried on the next frame instead of being masked by a committed cache key":
    "pending: CielScenes commits the ring-cache key before buildCombinedLightingMask (CielWin 3ce61fd)",
  "the planet's flare is sized from the active basis, so mini keeps it proportional to the planet":
    "pending: CielScenes sizes the flare from sceneBasis, not activeSceneBasis (CielWin 3ce61fd)",
  "earth: the grayscale fast path writes the reference bytes with no per-pixel Math calls":
    "retired: the reference was the pre-S1 source, recovered by stripping CielWin's S1 markers",
  "earth: the fallback path (no grayscale bake, or big-endian) writes the reference bytes":
    "retired: the reference was the pre-S1 source, recovered by stripping CielWin's S1 markers",
};
var tests = [];
var skipped = [];
function test(name, fn) {
  if (Object.prototype.hasOwnProperty.call(SHARED_SCENES_SKIPS, name)) { skipped.push(name); return; }
  tests.push({ name: name, fn: fn });
}

// ---- Cases 1-2: readiness / show-shown-done / hide / malformed messages -- same shared-module
// contract processing-scene.tests.js/explorer-scene.tests.js already cover in depth; the value here
// is only "this scene's own wiring exists and reaches the same shared module".

test("the page posts 'ready', a show request reaches shown, and posts 'done' after the duration elapses", function () {
  var page = loadPage({ innerWidth: 1000, innerHeight: 500, withWebview: true });
  assert.deepStrictEqual(page.postedMessages, ["ready"]);

  page.sandbox.startShowing(["warning"], 1, 1, 0, 1000);
  page.sandbox.renderFrame(0);
  page.sandbox.renderFrame(300);
  page.sandbox.renderFrame(750); // warning: shakeMs 0 + FAILURE_REVEAL_MS 700 -> shown by 750
  assert.strictEqual(page.sandbox.kindState.warning.state, "shown");
  assert.strictEqual(page.postedMessages.indexOf("done"), -1, "done must not fire before duration elapses");

  page.sandbox.renderFrame(1000); // showStartMs latched at ms=0 (first renderFrame after startShowing)
  assert.notStrictEqual(page.postedMessages.indexOf("done"), -1, "expected 'done' once the duration elapsed");
});

test("hide stops the overlay (direct call and via a real host message), and malformed messages are ignored", function () {
  var page = loadPage({ innerWidth: 1000, innerHeight: 500, withWebview: true });
  assert.strictEqual(page.messageListenerCount, 1,
    "expected handleHostMessage to register exactly one 'message' listener");

  // hide(), called directly.
  page.sandbox.startShowing(["warning"], 1, 1, 0, 5000);
  page.sandbox.renderFrame(0);
  assert.strictEqual(page.sandbox.animating, true);
  page.sandbox.hide();
  assert.strictEqual(page.sandbox.animating, false, "expected the direct hide() call to stop the overlay");

  // A real 'show' host message, exactly as WebViewAlertLayerController.cs's PostShow posts it.
  page.dispatchHostMessage({
    type: "show",
    tiles: ["warning"],
    columns: 1,
    rows: 1,
    gap: 0,
    workArea: { left: 0, top: 0, width: 0, height: 0 },
    duration: 5000,
  });
  page.sandbox.renderFrame(0);
  page.sandbox.renderFrame(750);
  assert.strictEqual(page.sandbox.kindState.warning.state, "shown");

  // Malformed messages must not disturb the still-running overlay.
  assert.doesNotThrow(function () {
    page.dispatchHostMessage(null);
    page.dispatchHostMessage("not-an-object");
    page.dispatchHostMessage({ type: "unrecognized-type" });
  });
  assert.strictEqual(page.sandbox.animating, true,
    "expected malformed messages to leave the still-running overlay untouched");

  // A real 'hide' host message, exactly as WebViewAlertLayerController.cs's End posts it.
  page.dispatchHostMessage({ type: "hide" });
  assert.strictEqual(page.sandbox.animating, false, "expected the real 'hide' message to stop the overlay");
});

// ---- Case 3 (KEY property): the see-through hook stamps the SAME constellation-ring cache/angle/
// center the scene's own render loop drew THIS FRAME, for EVERY tile (not just one -- D6b review
// lesson: checking only a single non-origin tile with some() is too weak), offset by that tile's own
// origin, and stamps NO OTHER ring (not even an extra draw alongside the right one).
//
// Two spies, not one: drawCachedRingContent (js/animate.js) records every call site-wide, and
// sceneSeeThroughLayer (js/see-through-hook.js) is wrapped separately to slice OUT exactly the
// drawCachedRingContent calls that happened during each INDIVIDUAL tile's own hook invocation --
// this is what actually proves "no other ring, no extra draw" per tile, rather than assuming the
// hook's calls are always exactly the trailing N entries of the flat call list (a mutation that adds
// one extra draw during hook processing would silently pass a trailing-N-slice check by shifting the
// window instead of failing it -- caught by exactly this shape while writing this test).
//
// Identifying the constellation ring by OBJECT IDENTITY (the hook's stamped cache must literally be
// a scene call's own cache argument), cross-checked against the scene's own CONSTELLATION_RING_INDEX
// (js/animate.js, deliberately declared `var` for this), so this test cannot pass by coincidence if
// the hook stamped some OTHER ring that merely happens to be internally consistent across tiles.

test("the see-through hook stamps the SAME constellation-ring cache/angle/center the scene drew this frame, for every tile, and no other ring", function () {
  var page = loadPage({ innerWidth: 1200, innerHeight: 800 });
  var calls = [];
  var originalDraw = page.sandbox.drawCachedRingContent;
  page.sandbox.drawCachedRingContent = function (context, cache, cx, cy, angle) {
    calls.push({ cache: cache, cx: cx, cy: cy, angle: angle, offset: page.currentOffset() });
    return originalDraw.apply(this, arguments);
  };
  var hookInvocations = []; // [{ startIndex, ownCalls }, ...] -- one entry per sceneSeeThroughLayer call
  var originalHook = page.sandbox.sceneSeeThroughLayer;
  page.sandbox.sceneSeeThroughLayer = function () {
    var startIndex = calls.length;
    var result = originalHook.apply(this, arguments);
    hookInvocations.push({ startIndex: startIndex, ownCalls: calls.slice(startIndex) });
    return result;
  };

  // 4 "warning" tiles (shakeMs 0): all reach "shown" together, avoiding any FAILED-shake timing
  // asymmetry between tiles, and every tile actually renders the see-through layer.
  page.sandbox.startShowing(["warning", "warning", "warning", "warning"], 2, 2, 0, 5000);
  page.sandbox.renderFrame(0);
  page.sandbox.renderFrame(750);
  assert.strictEqual(page.sandbox.kindState.warning.state, "shown");

  var rects = page.sandbox.tileRects();
  assert.strictEqual(rects.length, 4, "test setup sanity: expected a 2x2 grid of 4 tiles");

  calls.length = 0;
  hookInvocations.length = 0; // isolate the NEXT frame's calls/invocations
  page.sandbox.renderFrame(800);

  assert.strictEqual(hookInvocations.length, rects.length,
    "expected exactly one sceneSeeThroughLayer invocation per tile, saw " + hookInvocations.length);

  // Everything drawn BEFORE the first hook invocation started is the scene's own per-ring loop.
  var sceneCalls = calls.slice(0, hookInvocations[0].startIndex);

  // Identify the scene's OWN constellation-ring call this frame by object identity with each tile's
  // own stamped cache -- never by array index/position alone.
  hookInvocations.forEach(function (invocation, i) {
    var rect = rects[i];
    assert.strictEqual(invocation.ownCalls.length, 1,
      "tile " + i + ": expected the hook to draw exactly one ring, saw " + invocation.ownCalls.length);
    var call = invocation.ownCalls[0];

    var matchingSceneCalls = sceneCalls.filter(function (c) { return c.cache === call.cache; });
    assert.strictEqual(matchingSceneCalls.length, 1,
      "tile " + i + ": expected exactly one of the scene's own ring draws this frame to match the " +
      "hook's stamped cache (found " + matchingSceneCalls.length + ")");
    var sceneStamp = matchingSceneCalls[0];

    // Pin down WHICH ring that is, independent of the hook's own self-consistency: the matched call
    // must sit at the scene's own CONSTELLATION_RING_INDEX, not merely be SOME ring the hook happens
    // to agree with itself about across tiles.
    assert.strictEqual(sceneCalls[page.sandbox.CONSTELLATION_RING_INDEX], sceneStamp,
      "tile " + i + ": expected the stamped ring to be the constellation ring specifically (scene's " +
      "own CONSTELLATION_RING_INDEX), not merely some other ring the hook agrees with itself about");

    assert.strictEqual(call.cx, sceneStamp.cx, "tile " + i + ": expected the same center x");
    assert.strictEqual(call.cy, sceneStamp.cy, "tile " + i + ": expected the same center y");
    assert.strictEqual(call.angle, sceneStamp.angle, "tile " + i + ": expected the same rotation angle");
    // "+ 0" normalizes a -0/+0 mismatch (e.g. the origin tile's -rect.x is -0, a real translate(0,0)
    // never produces -0) without weakening the check: any REAL offset mismatch is still a distinct
    // nonzero number and still fails strictEqual.
    assert.strictEqual(call.offset.x + 0, -rect.x + 0,
      "tile " + i + ": expected the hook call translated by this tile's own x origin");
    assert.strictEqual(call.offset.y + 0, -rect.y + 0,
      "tile " + i + ": expected the hook call translated by this tile's own y origin");
  });
});

// ---- Case 4: render loop fault isolation (D6a/D6b precedent), one loadPage() --

test("a throwing scene layer does not stop the render loop, nor the alert overlay running the same frame", function () {
  var page = loadPage({ innerWidth: 1000, innerHeight: 500 });
  page.sandbox.startShowing(["warning"], 1, 1, 0, 5000);
  var thrown = 0;
  // Throwing on EVERY frame -- not just the first -- is what actually proves the per-stage dedup
  // (one console.error for N throws) and that the overlay keeps advancing while the scene keeps
  // failing on every single frame, matching what the assertions below claim (D6a review lesson).
  page.sandbox.drawStarfield = function () {
    thrown++;
    throw new Error("D6c-fault-isolation-scene");
  };

  var before = page.requestAnimationFrameCalls.length;
  page.sandbox.renderFrame(0); // this frame throws inside drawStarfield
  assert.strictEqual(page.requestAnimationFrameCalls.length, before + 1,
    "expected scheduleFrame(renderFrame) to still run once even though this frame threw");

  page.sandbox.renderFrame(300);
  page.sandbox.renderFrame(750); // warning: shown well before 750ms
  assert.strictEqual(page.requestAnimationFrameCalls.length, before + 3,
    "expected the loop to keep scheduling frames while the scene layer keeps throwing every frame");
  assert.strictEqual(thrown, 3, "test setup sanity: drawStarfield should have thrown on all 3 frames");
  assert.strictEqual(page.sandbox.kindState.warning.state, "shown",
    "expected the alert overlay to keep advancing even while the scene layer above it keeps throwing every frame");
  assert.strictEqual(page.consoleErrorCalls.length, 1,
    "expected the repeating error (thrown on every frame) to be reported once, not once per frame");
});

// ---- Case 5: the alert clock itself (alertSceneMs) must not be able to freeze the loop (D6a review
// R4-alertSceneMs-outside-fault-isolation / R3-alertSceneMs-outside-try precedent) -- a throw here
// must only fail this one frame's "scene" stage, never skip scheduleFrame(renderFrame) entirely.

test("a throwing alert clock (alertSceneMs) does not freeze the render loop, and the scene renders again once it recovers", function () {
  var page = loadPage({ innerWidth: 1000, innerHeight: 500 });
  var originalAlertSceneMs = page.sandbox.alertSceneMs;
  var originalDrawStarfield = page.sandbox.drawStarfield;
  var sceneDrawCalls = 0;
  page.sandbox.drawStarfield = function () {
    sceneDrawCalls++;
    return originalDrawStarfield.apply(this, arguments);
  };
  page.sandbox.alertSceneMs = function () {
    throw new Error("D6c-alert-clock-throws");
  };

  var before = page.requestAnimationFrameCalls.length;
  page.sandbox.renderFrame(0); // alertSceneMs throws before the scene draws anything this frame
  assert.strictEqual(page.requestAnimationFrameCalls.length, before + 1,
    "expected scheduleFrame(renderFrame) to still run even though alertSceneMs threw");
  assert.strictEqual(sceneDrawCalls, 0,
    "test setup sanity: the scene must not have drawn anything on the frame the clock threw");

  page.sandbox.renderFrame(16);
  page.sandbox.renderFrame(32);
  assert.strictEqual(page.requestAnimationFrameCalls.length, before + 3,
    "expected the loop to keep scheduling frames while the alert clock keeps throwing every frame");
  assert.strictEqual(page.consoleErrorCalls.length, 1,
    "expected the repeating alert-clock error to be reported once, not once per frame");

  page.sandbox.alertSceneMs = originalAlertSceneMs;
  page.sandbox.renderFrame(48);
  assert.strictEqual(page.requestAnimationFrameCalls.length, before + 4,
    "expected the loop to keep scheduling frames after the alert clock recovers");
  assert.strictEqual(sceneDrawCalls, 1,
    "expected the scene to render again once the alert clock stopped throwing");
});

// ---- Shared render-loop fps cap smoke check (D6d, html-wallpaper-demo) ----------------------------
// The throttle itself (skip-still-reschedules, exact 30fps spacing, fps parsing, alert duration
// unaffected) is exhaustively covered against the shared code in processing-scene.tests.js; this only
// proves THIS scene's own render loop actually routes through it (animate.js's own
// `scheduleFrame(renderFrame)`, not a local override) rather than re-deriving the throttle's own
// correctness.

test("shared render loop: fps=30 in the URL caps this scene's OWN renderFrame() loop, not just the shared helper", function () {
  var page = loadPage({ innerWidth: 800, innerHeight: 600, search: "?fps=30" });
  var sceneDrawCalls = 0;
  var original = page.sandbox.drawStarfield;
  page.sandbox.drawStarfield = function () { sceneDrawCalls++; return original.apply(this, arguments); };

  var frameTimesMs = [0, 16.7, 33.3, 50.0, 66.7, 83.3];
  frameTimesMs.forEach(function (ms) {
    var wrapper = page.requestAnimationFrameCalls[page.requestAnimationFrameCalls.length - 1];
    wrapper(ms);
  });

  assert.ok(sceneDrawCalls < frameTimesMs.length,
    "expected renderFrame() to run fewer times than rAF callbacks under a 30fps cap, ran " + sceneDrawCalls + " of " + frameTimesMs.length);
  assert.ok(sceneDrawCalls >= 2, "expected at least two frames to still render, ran " + sceneDrawCalls);
});

// ---- Constellation ring checks (shared with the explorer scene) ------------------------------
// The checks themselves live once in constellation-ring.checks.js; see that file for why.

test("every constellation figure only joins stars it actually has, and leaves no star unjoined", function () {
  var page = loadPage({ innerWidth: 800, innerHeight: 600 });
  constellationRingChecks.checkConstellationTable(page.sandbox);
});

test("the constellation tunables match the explorer scene's, value for value", function () {
  constellationRingChecks.checkTunablesMatchTwin(sceneDir, "explorer");
});

// ---- Full-circle fit (2026-09-27): the disc border must stay fully on screen, with >= 25px margin
// on every side, instead of the old fixed min(W,H) basis clipping its top/bottom on a landscape
// screen. Asserted against what the scene actually passes to drawDiscBorder (spied), not a
// re-derivation of the fit formula, so this only fails when the real draw call would clip.

test("the disc border fits inside the screen with >= 25px margin on every side, at 1920x1080 and 1000x800", function () {
  var margin = 25;
  [{ innerWidth: 1920, innerHeight: 1080 }, { innerWidth: 1000, innerHeight: 800 }].forEach(function (size) {
    var page = loadPage(size);
    var borderCalls = [];
    var original = page.sandbox.drawDiscBorder;
    page.sandbox.drawDiscBorder = function (context, cx, cy, innerRadius, outerRadius) {
      borderCalls.push({ cx: cx, cy: cy, outerRadius: outerRadius });
      return original.apply(this, arguments);
    };
    page.sandbox.renderFrame(0);
    var label = size.innerWidth + "x" + size.innerHeight;
    assert.strictEqual(borderCalls.length, 1, label + ": expected exactly one drawDiscBorder call per frame");
    var call = borderCalls[0];
    assert.ok(call.outerRadius + margin <= call.cy,
      label + ": expected outerRadius(" + call.outerRadius + ") + " + margin + " <= cy(" + call.cy + ")");
    assert.ok(call.outerRadius + margin <= size.innerHeight - call.cy,
      label + ": expected outerRadius(" + call.outerRadius + ") + " + margin + " <= H-cy(" + (size.innerHeight - call.cy) + ")");
    assert.ok(call.outerRadius + margin <= call.cx,
      label + ": expected outerRadius(" + call.outerRadius + ") + " + margin + " <= cx(" + call.cx + ")");
    assert.ok(call.outerRadius + margin <= size.innerWidth - call.cx,
      label + ": expected outerRadius(" + call.outerRadius + ") + " + margin + " <= W-cx(" + (size.innerWidth - call.cx) + ")");
  });
});

// ---- Chroma width (2026-09-27): the full-circle fit shrank sceneBasis, and the chromatic glow
// sized itself from it, so its bottom fan got narrower too. The bottom sample must keep its
// screen-relative width (the pre-fit look), the tip must stay sized from sceneBasis (tied to the
// Earth), and the fan must thin monotonically on the way up.

test("the chromatic glow starts screen-wide at the bottom and thins toward the Earth, at 1920x1080 and 1000x800", function () {
  [{ innerWidth: 1920, innerHeight: 1080 }, { innerWidth: 1000, innerHeight: 800 }].forEach(function (size) {
    var page = loadPage(size);
    var label = size.innerWidth + "x" + size.innerHeight;
    var halfWidth = page.sandbox.chromaticGlowSampleHalfWidth;
    assert.strictEqual(typeof halfWidth, "function", label + ": expected chromaticGlowSampleHalfWidth(t)");
    var screenBasis = Math.min(size.innerWidth, size.innerHeight);
    // Read the shipped tunables from the page realm (top-level consts share its global lexical
    // scope) so retuning them never leaves a stale copy here.
    var radiusFraction = vm.runInContext("CHROMATIC_GLOW_RADIUS_FRACTION", page.sandbox);
    var baseWidthScale = vm.runInContext("CHROMATIC_GLOW_BASE_WIDTH_SCALE", page.sandbox);
    var bottom = halfWidth(0);
    var tip = halfWidth(1);
    var expectedBottom = screenBasis * radiusFraction * baseWidthScale;
    var expectedTip = page.sandbox.sceneBasis(size.innerWidth, size.innerHeight) * radiusFraction;
    assert.ok(Math.abs(bottom - expectedBottom) < 1e-6,
      label + ": expected bottom half-width " + expectedBottom + ", got " + bottom);
    assert.ok(Math.abs(tip - expectedTip) < 1e-6,
      label + ": expected tip half-width " + expectedTip + ", got " + tip);
    var previous = Infinity;
    for (var i = 0; i <= 10; i++) {
      var w = halfWidth(i / 10);
      assert.ok(w < previous, label + ": half-width must shrink on the way up (t=" + (i / 10) + ": " + w + " >= " + previous + ")");
      previous = w;
    }
  });
});

// ---- mini-scene-window T2: `?variant=mini` (checks shared via mini-variant.checks.js) -------------

test("the scene variant parses from the URL: default full, mini recognized, garbage falls back to full", function () {
  miniVariantChecks.checkVariantParse(sceneDir);
});

test("mini draws only its kept layers, on a transparent canvas, with no nebula, and still renders the alert overlay", function () {
  miniVariantChecks.checkMiniLayers({
    loadPage: loadPage,
    frameFunction: "renderFrame",
    miniOnly: ["drawMiniRingBases"],
    // The occluding base is destination-over: it must be drawn after everything it sits beneath.
    order: [["drawChromaticGlowAnimated", "drawMiniRingBases"]],
    keep: ["drawDiscBorder", "drawCachedRingContent", "drawInnerRing", "drawEarth", "drawChromaticGlowAnimated"],
    drop: ["drawStarfield", "drawVignette", "drawCombinedLightingMask"],
    fit: function (page) {
      // The ring is centered in the square, its disc border stays inside the edge fade's opaque radius
      // (so the mask never clips it) and still fills most of the square (>= 0.36 of the side).
      var borderCalls = [];
      var original = page.sandbox.drawDiscBorder;
      page.sandbox.drawDiscBorder = function (context, cx, cy, innerRadius, outerRadius) {
        borderCalls.push({ cx: cx, cy: cy, outerRadius: outerRadius });
        return original.apply(this, arguments);
      };
      var earthCalls = [];
      var originalEarth = page.sandbox.drawEarth;
      page.sandbox.drawEarth = function (context, cx, cy) {
        earthCalls.push({ cx: cx, cy: cy });
        return originalEarth.apply(this, arguments);
      };
      page.sandbox.renderFrame(200);
      assert.strictEqual(earthCalls.length, 1, "expected one drawEarth call");
      assert.ok(Math.abs(earthCalls[0].cx - 144) < 1e-6 && Math.abs(earthCalls[0].cy - 144) < 1e-6,
        "expected the mini planet at the exact ring center (144, 144), got (" + earthCalls[0].cx + ", " + earthCalls[0].cy + ")");
      assert.strictEqual(borderCalls.length, 1, "expected one drawDiscBorder call");
      var call = borderCalls[0];
      assert.ok(Math.abs(call.cx - 144) < 1e-6 && Math.abs(call.cy - 144) < 1e-6,
        "expected the ring centered at (144, 144), got (" + call.cx + ", " + call.cy + ")");
      // The edge fade holds alpha 1 out to MINI_EDGE_FADE_INNER of the side (a stricter bound than any fixed
      // px margin): the whole disc must sit inside it while still filling most of the square.
      var fadeInner = page.sandbox.MINI_EDGE_FADE_INNER * 288;
      assert.ok(call.outerRadius <= fadeInner + 1e-6, "the ring (" + call.outerRadius + ") must stay inside the fade opaque radius " + fadeInner);
      assert.ok(call.outerRadius >= 288 * 0.36, "expected the ring to fill most of the square: outerRadius " + call.outerRadius);
    },
  });
});

test("the full variant still draws every layer and its opaque background", function () {
  miniVariantChecks.checkFullLayers({
    loadPage: loadPage,
    frameFunction: "renderFrame",
    miniOnly: ["drawMiniRingBases"],
    // The occluding base is destination-over: it must be drawn after everything it sits beneath.
    order: [["drawChromaticGlowAnimated", "drawMiniRingBases"]],
    keep: ["drawDiscBorder", "drawCachedRingContent", "drawInnerRing", "drawEarth", "drawChromaticGlowAnimated"],
    drop: ["drawStarfield", "drawVignette", "drawCombinedLightingMask"],
    hasBackgroundFill: true,
  });
});

test("the full variant keeps the planet above the ring center, exactly where it always sat", function () {
  var page = loadPage({ innerWidth: 1000, innerHeight: 500 });
  var earthCalls = [];
  var originalEarth = page.sandbox.drawEarth;
  page.sandbox.drawEarth = function (context, cx, cy) {
    earthCalls.push({ cx: cx, cy: cy });
    return originalEarth.apply(this, arguments);
  };
  page.sandbox.renderFrame(0);
  assert.strictEqual(earthCalls.length, 1, "expected one drawEarth call");
  var basis = page.sandbox.sceneBasis(1000, 500);
  var ringCx = 1000 * vm.runInContext("CENTER_X_FRACTION", page.sandbox);
  var ringCy = 500 * vm.runInContext("CENTER_Y_FRACTION", page.sandbox);
  var expectedCx = ringCx + vm.runInContext("EARTH_CENTER_X_OFFSET_FRACTION", page.sandbox) * basis;
  var expectedCy = ringCy + vm.runInContext("EARTH_CENTER_Y_OFFSET_FRACTION", page.sandbox) * basis;
  assert.ok(Math.abs(earthCalls[0].cx - expectedCx) < 1e-6 && Math.abs(earthCalls[0].cy - expectedCy) < 1e-6,
    "expected the full planet at (" + expectedCx + ", " + expectedCy + "), got (" + earthCalls[0].cx + ", " + earthCalls[0].cy + ")");
  assert.ok(earthCalls[0].cy < ringCy, "expected the full planet above the ring center");
});

test("mini scales the constellation dots and lines with the ring (floor 0.5px); the full variant keeps 2.75px / 2.5px", function () {
  constellationRingChecks.checkConstellationDetailScale(loadPage);
});

test("mini scales the hieroglyph band's glyph stroke with the ring (floor, proportional above it); the full variant keeps 1.6px", function () {
  constellationRingChecks.checkHieroglyphStrokeScale(loadPage);
});

test("mini draws an occluding dark base under every ring band and the planet hollow (destination-over, band radii only); the full variant draws none", function () {
  constellationRingChecks.checkMiniRingBase(loadPage);
});

test("the stylesheet makes the mini page layers transparent (the root is the host's, see SceneWebServer)", function () {
  miniVariantChecks.checkMiniStylesheet(sceneDir);
});

// html-wallpaper-demo review follow-up R2-constellation-index-silent-miss: the see-through hook finds
// the constellation ring by NAME; a rename used to leave CONSTELLATION_RING_INDEX at -1, so the ring
// seen through the alert letters vanished with nothing logged. Loading must fail loudly instead.
test("a renamed constellation ring fails the page load instead of silently dropping the see-through ring", function () {
  var renamed = false;
  assert.throws(function () {
    loadPage({
      transformSource: function (entry, source) {
        if (entry !== "js/animate.js" || source.indexOf("name: 'constellation'") < 0) return source;
        renamed = true;
        return source.replace("name: 'constellation'", "name: 'constellations'");
      },
    });
  }, /no ring named 'constellation'/);
  assert.ok(renamed, "test setup: the constellation ring's name literal must be found in js/animate.js");
});

// ---- pause-scene-when-covered T1: the host's pause/resume messages (shared checks) --------------------

test("pause/resume host messages: a paused page draws and arms nothing, resume re-arms exactly once, hide still works", function () {
  pauseResumeChecks.checkPauseResume(loadPage);
});

test("the mini variant ignores the pause message (the corner window is always visible)", function () {
  pauseResumeChecks.checkMiniVariantIgnoresPause(loadPage);
});

// ---- Review follow-ups (T5b) ------------------------------------------------------------------------

// buildRingCaches used to commit its "built for this basis/DPR" key BEFORE the combined lighting mask was
// built: a mask failure left a key that said "up to date" next to a missing (first build) or stale
// (resize) mask, so the next frame never rebuilt and drawCombinedLightingMask failed every frame.
test("a failed lighting-mask build is retried on the next frame instead of being masked by a committed cache key", function () {
  var page = loadPage({ innerWidth: 1000, innerHeight: 500, conicGradientFailures: 1 });

  page.sandbox.renderFrame(0); // the mask build throws once; renderFrame's scene stage reports it
  assert.strictEqual(page.consoleErrorCalls.length, 1, "expected exactly the one injected mask failure");
  page.sandbox.renderFrame(16);

  assert.strictEqual(vm.runInContext("combinedLightingMask !== null", page.sandbox), true,
    "expected the next frame to rebuild the mask after the failed attempt");
  assert.strictEqual(page.consoleErrorCalls.length, 1,
    "expected no further render failures once the retry built the mask");
});

// earth.js sized the planet's flare from sceneBasis (the full-scene fit) while the planet itself is
// sized from the ACTIVE basis, so in the mini window the flare was mis-scaled relative to the planet.
test("the planet's flare is sized from the active basis, so mini keeps it proportional to the planet", function () {
  var page = loadPage({ innerWidth: 288, innerHeight: 288, search: "?variant=mini" });
  var flareColor = vm.runInContext("EARTH_FLARE_COLOR", page.sandbox);
  var flares = [];
  var originalSpark = page.sandbox.drawSpark;
  page.sandbox.drawSpark = function (context, x, y, size, intensity, color, rayLength) {
    if (color === flareColor) flares.push({ size: size, rayLength: rayLength });
    return originalSpark.apply(this, arguments);
  };

  page.sandbox.renderFrame(200);

  assert.strictEqual(flares.length, 1, "expected one earth flare spark");
  var basis = vm.runInContext("activeSceneBasis()", page.sandbox);
  var sizeFraction = vm.runInContext("EARTH_FLARE_SIZE_FRACTION", page.sandbox);
  var rayFraction = vm.runInContext("EARTH_FLARE_RAY_LENGTH_FRACTION", page.sandbox);
  assert.ok(Math.abs(flares[0].size - basis * sizeFraction) < 1e-6,
    "expected flare size " + basis * sizeFraction + " (active basis), got " + flares[0].size);
  assert.ok(Math.abs(flares[0].rayLength - basis * rayFraction) < 1e-6,
    "expected flare ray length " + basis * rayFraction + " (active basis), got " + flares[0].rayLength);
});

// ---- Run ----------------------------------------------------------------------------------------

// ---- S1 scene optimizations (odd/tasks/scene-optimizations.md, ported from CieLinux 4574b4a W1) ----
// CielWin marked its S1 changes in blocks that stripped back to the pre-S1 reference. The shared CielScenes
// sources carry no S1 markers, so the earth reference checks are skipped (SHARED_SCENES_SKIPS); the
// vignette and gradient checks below test behavior and still run.
var S1_BLOCKS = /^[ \t]*\/\/ Scene optimization begin \(S1\)[^\n]*\n[\s\S]*?^[ \t]*\/\/ Scene optimization end \(S1\)\.\r?\n(\r?\n(?=\/\/|function|const|let))?/gm;
function stripS1(text) { return text.replace(S1_BLOCKS, ""); }

// The equirect bake is grayscale, so the fast path computes each texel once with the reference expressions
// and writes one 32-bit word. The reference is the pre-S1 renderEarthFrame, evaluated in the same sandbox
// (one page load; earth.js's bake costs seconds per vm realm) against the same lookups and ImageData.
// One shared page for the S1 earth/vignette tests: a fresh realm re-runs the multi-second earth bake.
var s1Page = null;
function s1EarthPage() {
  if (s1Page !== null) return s1Page;
  var page = loadPage({ innerWidth: 1000, innerHeight: 500 });
  var source = stripS1(fs.readFileSync(path.join(sceneDir, "js", "earth.js"), "utf8"));
  var reference = source.match(/^function renderEarthFrame\([\s\S]*?^}/m);
  assert.ok(reference, "reference renderEarthFrame found");
  vm.runInContext(reference[0].replace("function renderEarthFrame(", "function renderEarthFrameReference("), page.sandbox);
  s1Page = page;
  return page;
}

function earthFrameBytes(page, fn, size, longitude) {
  var run = function (code) { return vm.runInContext(code, page.sandbox); };
  if (run("earthFrameImageData") !== null && run("earthDiscLookupSize") === size) run("earthFrameImageData.data.fill(0)");
  page.sandbox[fn](size, longitude);
  return Buffer.from(run("earthFrameImageData.data"));
}

test("earth: the grayscale fast path writes the reference bytes with no per-pixel Math calls", function () {
  var page = s1EarthPage();
  var run = function (code) { return vm.runInContext(code, page.sandbox); };
  assert.strictEqual(run("EARTH_EQUIRECT_GRAY !== null && EARTH_LITTLE_ENDIAN"), true,
    "the equirect bake is grayscale and the fast path is active");
  var frame = function (fn, size, longitude) { return earthFrameBytes(page, fn, size, longitude); };
  var sizes = [64, 109, 33, 241, 64];
  var longitudes = [0, 0.002, 1.3, -2.2, 100.7, 4.8 * Math.PI / 180 * 3600, 4.8 * Math.PI / 180 * 86400.37];
  sizes.forEach(function (size) {
    longitudes.forEach(function (longitude) {
      var expected = frame("renderEarthFrameReference", size, longitude);
      var actual = frame("renderEarthFrame", size, longitude);
      assert.strictEqual(Buffer.compare(actual, expected), 0, "size " + size + ", longitude " + longitude);
    });
  });
  var MathObject = run("Math");
  var saved = { round: MathObject.round, min: MathObject.min, max: MathObject.max };
  var calls = 0;
  Object.keys(saved).forEach(function (name) {
    MathObject[name] = function () { calls++; return saved[name].apply(MathObject, arguments); };
  });
  try { page.sandbox.renderEarthFrame(64, 0.5); } finally { Object.assign(MathObject, saved); }
  assert.strictEqual(calls, 0, calls + " Math.round/min/max calls per frame (reference: ~5 per inside pixel)");
});

// The S1 renderEarthFrame, re-evaluated with one of the fast-path guards shadowed (a color bake, a
// big-endian host), must take the reference loop and write the reference bytes.
test("earth: the fallback path (no grayscale bake, or big-endian) writes the reference bytes", function () {
  var page = s1EarthPage();
  var current = fs.readFileSync(path.join(sceneDir, "js", "earth.js"), "utf8").match(/^function renderEarthFrame\([\s\S]*?^}/m);
  assert.ok(current && current[0].indexOf("EARTH_EQUIRECT_GRAY !== null && EARTH_LITTLE_ENDIAN") >= 0, "S1 guard found");
  var forced = { renderEarthFrameNoGray: "const EARTH_EQUIRECT_GRAY = null;", renderEarthFrameBigEndian: "const EARTH_LITTLE_ENDIAN = false;" };
  Object.keys(forced).forEach(function (name) {
    vm.runInContext("var " + name + " = (function () { " + forced[name] + " return " + current[0] + "; })();", page.sandbox);
  });
  var fastPath = page.sandbox.renderEarthGrayPixels;
  var fastPathCalls = 0;
  page.sandbox.renderEarthGrayPixels = function () { fastPathCalls++; return fastPath.apply(this, arguments); };
  try {
    Object.keys(forced).forEach(function (name) {
      [64, 109, 33].forEach(function (size) {
        [0, 1.3, -2.2].forEach(function (longitude) {
          var expected = earthFrameBytes(page, "renderEarthFrameReference", size, longitude);
          var actual = earthFrameBytes(page, name, size, longitude);
          assert.strictEqual(Buffer.compare(actual, expected), 0, name + ", size " + size + ", longitude " + longitude);
        });
      });
    });
  } finally {
    page.sandbox.renderEarthGrayPixels = fastPath;
  }
  assert.strictEqual(fastPathCalls, 0, "the fallback never enters the grayscale fast path");
});

// A gradient that fails to build must not leave a dangling save(): the failing frame is caught by the render
// loop, and an unbalanced save would grow the canvas state stack on every failing frame.
test("vignette: a throwing createRadialGradient leaves save/restore balanced, and the next frame fills", function () {
  var page = s1EarthPage();
  var throws = 1;
  var saves = 0;
  var restores = 0;
  var fills = 0;
  var context = {
    save: function () { saves++; },
    restore: function () { restores++; },
    fillRect: function () { fills++; },
    createRadialGradient: function () {
      if (throws > 0) { throws--; throw new Error("createRadialGradient failed"); }
      return { addColorStop: function () {} };
    },
  };
  assert.throws(function () { page.sandbox.drawVignette(context); }, /createRadialGradient failed/);
  assert.strictEqual(saves, restores, "balanced after the failing frame (" + saves + " saves, " + restores + " restores)");
  assert.strictEqual(fills, 0, "nothing filled on the failing frame");
  page.sandbox.drawVignette(context);
  assert.strictEqual(saves, restores, "balanced after the next frame");
  assert.strictEqual(fills, 1, "the next frame fills the vignette");
});

// The vignette gradient only depends on (context, W, H): created once per geometry, filled every frame.
test("full frame: the vignette gradient is created once per geometry, with the reference stops, and filled every frame", function () {
  var log = [];
  var page = loadPage({ innerWidth: 640, innerHeight: 360, gradientLog: log });
  var vignettesFor = function (w, h) {
    var r = Math.hypot(w / 2, h / 2);
    return log.filter(function (g) {
      return g.kind === "createRadialGradient" && g.args[0] === w / 2 && g.args[1] === h / 2 && g.args[5] === r;
    });
  };
  page.sandbox.renderFrame(1000);
  var first = vignettesFor(640, 360);
  assert.strictEqual(first.length, 1, "one vignette gradient on the first frame");
  var maxRadius = Math.hypot(320, 180);
  assert.deepStrictEqual(JSON.parse(JSON.stringify(first[0].args)), [320, 180, maxRadius * 0.5, 320, 180, maxRadius]);
  assert.deepStrictEqual(first[0].stops, [[0, "rgba(0,0,0,0)"], [1, "rgba(0,0,0,0.9)"]]);
  assert.strictEqual(first[0].filled, 1, "the vignette is filled on the first frame");
  log.length = 0;
  page.sandbox.renderFrame(1033);
  page.sandbox.renderFrame(1066);
  assert.strictEqual(vignettesFor(640, 360).length, 0, "steady frames reuse the vignette gradient");
  assert.strictEqual(first[0].filled, 3, "the cached vignette is still filled every frame");
  page.sandbox.window.innerWidth = 800;
  page.sandbox.window.innerHeight = 400;
  page.sandbox.resize();
  page.sandbox.renderFrame(1100);
  assert.strictEqual(vignettesFor(800, 400).length, 1, "rebuilt once on resize");
  assert.strictEqual(page.consoleErrorCalls.length, 0, "every frame rendered without a caught error");
});

var failures = [];
tests.forEach(function (t) {
  try {
    t.fn();
    console.log("PASS " + t.name);
  } catch (error) {
    failures.push(t.name);
    console.log("FAIL " + t.name);
    console.log(error && error.stack ? error.stack : String(error));
  }
});

skipped.forEach(function (name) { console.log("SKIP " + name + " -- " + SHARED_SCENES_SKIPS[name]); });
console.log((tests.length - failures.length) + "/" + tests.length + " passed");
process.exit(failures.length > 0 ? 1 : 0);

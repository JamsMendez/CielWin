"use strict";

// Alert title reach R1 (odd/tasks/cielinux-ports.md T2, ported from CieLinux e19b3c8 + 5a92a38 and their
// tests/alert-title-reach.contract.test.mjs): the WARNING/FAILED letters reach each scene's reference ring
// (sceneAlertTitleLimits in its see-through hook) without touching it and without being stretched, and raphael's
// see-through stamps the gold ring's own outline-glyph sprites. Every R1 change lives in marked blocks that only
// add lines; stripping them restores the pre-R1 sources (pinned below by SHA-256, LF-normalized).
//
// Shared by the four scene harnesses: register(test, sceneDir, scene).

const vm = require("vm");
const fs = require("fs");
const path = require("path");
const assert = require("assert");
const crypto = require("crypto");
const alertOverlayCacheChecks = require(path.join(__dirname, "alert-overlay-cache.checks.js"));

// SHA-256 of the pre-R1 sources (573d8c5), LF-normalized: `git show 573d8c5:<file> | tr -d '\r' | sha256sum`.
const PRE_R1 = {
  "shared/js/alert-overlay.js": "dfbe9b09421d6d17d3895cc013edd36591f6fe482a1f44d3837e34a781f842c9",
  "raphael/js/see-through-hook.js": "d6f6c34f4af60e7fb5b1566393d5a347b303639796fe87d54fb13961af9f1720",
  "processing/js/see-through-hook.js": "ff0dc530ca117e5cda5849ebb2b0520b7517dd0385fcf27c7c2f30558037bd05",
  "explorer/js/see-through-hook.js": "4ee5d0bfe8a7d148f411842c48eaf25517f12f3270f91a3d298b7c12df9e62f2",
  "idle/js/see-through-hook.js": "bbc749b40b4f3986740a28eab229ea3dbd5344e0383c93f9e33c260766be1da3",
};

const R1_BLOCKS = /^[ \t]*\/\/ Linux port begin \(R1\)[^\n]*\n[\s\S]*?^[ \t]*\/\/ Linux port end \(R1\)\.\n(\n(?=\/\/|function|const|let|var))?/gm;
const normalize = (text) => text.replace(/\r\n/g, "\n");
const stripR1 = (text) => normalize(text).replace(R1_BLOCKS, "");
const sha256 = (text) => crypto.createHash("sha256").update(text).digest("hex");
const near = (actual, expected, label) =>
  assert.ok(Math.abs(actual - expected) < 1e-9, label + ": expected " + expected + ", saw " + actual);

function readWeb(sceneDir, name) { return normalize(fs.readFileSync(path.join(sceneDir, "..", name), "utf8")); }

function registerPin(test, sceneDir, names) {
  test("R1 blocks only add lines: stripping them restores the pre-R1 sources", () => {
    for (const name of names) {
      const text = readWeb(sceneDir, name);
      assert.match(text, /^[ \t]*\/\/ Linux port begin \(R1\)/m, name + " marks R1");
      assert.strictEqual(sha256(stripR1(text)), PRE_R1[name], name);
    }
  });
}

// ---- Overlay helpers, run alone against a measuring stub (advance 0.6 em per glyph, ascent 0.7 em) ----------

function reach(sceneDir, options) {
  options = options || {};
  const W = options.W || 240, H = options.H || 240;
  const blocks = readWeb(sceneDir, "shared/js/alert-overlay.js").match(R1_BLOCKS) || [];
  const helpers = blocks.find((block) => block.includes("function failureTitleLayout"));
  assert.ok(helpers, "the R1 helper block defines failureTitleLayout");
  const sandbox = { W, H, tiles: options.tiles || ["warning"], FAILURE_TITLE_FONT: "stub", Math };
  if (options.limits) sandbox.sceneAlertTitleLimits = () => options.limits;
  vm.createContext(sandbox);
  vm.runInContext(helpers, sandbox);
  const g = {
    font: "",
    measureText(text) {
      const px = Number(/(\d+(?:\.\d+)?)px/.exec(this.font)[1]);
      return { width: text.length * px * 0.6, actualBoundingBoxAscent: px * 0.7 };
    },
  };
  const frame = { x: W * 0.038, y: H * 0.064 };
  frame.w = W - frame.x * 2;
  frame.h = H - frame.y * 2;
  return { sandbox, g, frame };
}

function registerHelpers(test, sceneDir) {
  test("R1 reach applies to a single tile with a scene hook only", () => {
    assert.strictEqual(reach(sceneDir).sandbox.failureTitleLimits(), null);
    assert.strictEqual(reach(sceneDir, { tiles: ["failed", "warning"], limits: { top: 50, bottom: 190 } })
      .sandbox.failureTitleLimits(), null);
    assert.deepStrictEqual(Object.assign({}, reach(sceneDir, { limits: { top: 50, bottom: 190 } })
      .sandbox.failureTitleLimits()), { top: 50, bottom: 190 });
  });

  test("R1 title layout: the font size never depends on the limits; the reveal stops at the limit or the whole glyph", () => {
    const shallow = reach(sceneDir, { limits: { top: 30, bottom: 200 } });
    const deep = reach(sceneDir, { limits: { top: 120, bottom: 120 } });
    const a = shallow.sandbox.failureTitleLayout(shallow.g, shallow.frame, "WARNING", { top: 30, bottom: 200 });
    const b = deep.sandbox.failureTitleLayout(deep.g, deep.frame, "WARNING", { top: 120, bottom: 120 });
    assert.strictEqual(a.fontSize, b.fontSize, "no vertical stretch: size comes from the frame width");
    const ascent = a.fontSize * 0.7;
    near(shallow.frame.y - a.topOffset + ascent, 30, "shallow: the top glyph edge lands on the limit");
    near(shallow.frame.y + shallow.frame.h + a.bottomOffset - ascent, 200, "shallow: the bottom glyph edge lands on the limit");
    assert.strictEqual(b.topOffset, 0, "deep: the glyph shows whole");
    assert.strictEqual(b.bottomOffset, 0, "deep: the glyph shows whole");
    assert.ok(b.topBand >= ascent && b.bottomBand >= ascent);
    assert.ok(a.topBand >= 240 * 0.23 && a.bottomBand >= 240 * 0.23, "bands never shrink below the reference");
  });

  test("R1 letter bands reach the limit but never cross the tile middle", () => {
    const { sandbox, frame } = reach(sceneDir, { limits: { top: 80, bottom: 160 } });
    const bands = sandbox.failureTitleReachBands(frame, { top: 80, bottom: 160 });
    near(bands.top, 80 - frame.y, "top band");
    near(bands.bottom, frame.y + frame.h - 160, "bottom band");
    assert.strictEqual(sandbox.failureTitleReachBands(frame, { top: 20, bottom: 220 }).top, 240 * 0.23,
      "a shallow limit keeps the reference band");
    const capped = sandbox.failureTitleReachBands(frame, { top: 400, bottom: -100 });
    assert.strictEqual(capped.top, frame.h * 0.5);
    assert.strictEqual(capped.bottom, frame.h * 0.5);
  });
}

// ---- The real page: the overlay reads the scene's limits; mosaics keep the reference reveal -----------------

function registerOverlay(test, sceneDir) {
  test("R1 alert: a single tile bakes its letters to the scene's limits; a mosaic keeps the reference reveal", () => {
    const harness = alertOverlayCacheChecks.harness;
    const h = harness(sceneDir, { hash: "#kind=warning&duration=99999" });
    [0, 300, 900, 1500].forEach(h.tick);
    const W = 640, H = 360;
    const limits = h.evaluate("sceneAlertTitleLimits(" + W + ", " + H + ")");
    assert.deepStrictEqual(Object.assign({}, h.evaluate("failureTitleLimits()")), Object.assign({}, limits));
    const frame = { x: W * 0.038, y: H * 0.064 };
    frame.w = W - frame.x * 2;
    frame.h = H - frame.y * 2;
    // The harness measures 4 px per glyph at any size and reports no ascent (the overlay falls back to 0.72 em).
    const fontSize = 100 * (frame.w * 0.97) / ("WARNING".length * 4);
    const ascent = fontSize * 0.72;
    const top = Math.min(ascent, limits.top - frame.y), bottom = Math.min(ascent, frame.y + frame.h - limits.bottom);
    // processing's ring sits below the middle (cy = 0.515 H): at 640x360 the top limit lies deeper than the
    // reference band and the bottom one shallower, so the bottom clip keeps the reference band.
    assert.ok(top > H * 0.23 && bottom < H * 0.23, "test setup: one limit on each side of the reference band");
    const letters = h.streamOf(Object.values(h.evaluate("failureStaticCache"))[0].letters.canvas);
    const rects = letters.filter((op) => op[0] === "rect");
    assert.strictEqual(rects.length, 2, "two letter clips");
    near(rects[0][4], top, "top clip reaches the limit");
    near(rects[1][2], frame.y + frame.h - H * 0.23, "bottom clip keeps the reference band");
    near(rects[1][4], H * 0.23, "bottom clip height");
    const translates = letters.filter((op) => op[0] === "translate");
    near(translates[0][2], frame.y - (ascent - top), "top glyph edge on the limit");
    const fills = letters.filter((op) => op[0] === "fillText");
    near(fills[1][3], frame.y + frame.h + (ascent - bottom), "bottom glyph edge on the limit");
    const bands = h.evaluate("failureLetterBands")({ x: frame.x, y: frame.y, w: frame.w, h: frame.h }, W, H);
    assert.strictEqual(bands.length, 2, "two letter bands");
    assert.strictEqual(bands[0].y + bands[0].h, Math.min(H, Math.ceil(limits.top) + 2), "top band reaches the limit");
    assert.strictEqual(bands[1].y, Math.max(0, Math.floor(frame.y + frame.h - H * 0.23) - 2), "bottom band keeps the reference");
    assert.deepStrictEqual(h.errors, [], "no caught render errors");

    const mosaic = harness(sceneDir, { hash: "#tiles=warning,warning&columns=2&rows=1&gap=8&duration=99999" });
    [0, 300, 900, 1500].forEach(mosaic.tick);
    assert.strictEqual(mosaic.evaluate("failureTitleLimits()"), null, "a mosaic keeps the reference reveal");
    const mosaicRects = Object.values(mosaic.evaluate("failureStaticCache"))
      .map((entry) => mosaic.streamOf(entry.letters.canvas).filter((op) => op[0] === "rect"));
    assert.ok(mosaicRects.length >= 1, "mosaic letters baked");
    for (const tileRects of mosaicRects) {
      assert.strictEqual(tileRects.length, 2, "two letter clips per tile");
      const tileH = tileRects[0][2] / 0.064; // the clip starts at the tile frame (y = H * 0.064)
      near(tileRects[0][4], tileH * 0.23, "mosaic top clip is the reference band");
      near(tileRects[1][4], tileH * 0.23, "mosaic bottom clip is the reference band");
    }
    assert.deepStrictEqual(mosaic.errors, [], "no caught render errors");
  });
}

// ---- Each scene's hook: its limits bracket the scene's own ring, a small margin outside it --------------------

// Wraps the scene function that receives the ring center (cx, cy) this frame and returns the captured cy.
const RING_CENTER_SPY = {
  raphael: { name: "drawGlyphRings", cyArg: 1 },
  processing: { name: "drawCentralOctagon", cyArg: 1 },
  explorer: { name: "drawCachedRingContent", cyArg: 3 },
  idle: { name: "drawCachedRingContent", cyArg: 3 },
};

// The ring extent each scene's limits stop short of, from the scene's own geometry.
const RING_REACH = {
  raphael: (h, mini) => (mini
    ? h.evaluate("(glyphRingAnnuli(coreRadius(Math.min(W, H)))[1].outerRadius + GLYPH_RING_DELIMITER_WIDTH / 2) * MINI_SCENE_ZOOM")
    : h.evaluate("hexadecagonDrawnExtent(coreRadius(Math.min(W, H)), 1)")),
  // drawCentralOctagon: radius 0.168 minD, wobble up to 2.5%, widest stroke the 6.8 px chroma copy at full pulse.
  processing: (h, mini) => h.evaluate("Math.min(W, H) * 0.168 * 1.025 + 6.8 * "
    + (mini ? "(MINI_POLYGON_STROKE_PX / CENTRAL_OCTAGON_STROKE_PX)" : "1") + " * 1.72 / 2"),
  explorer: (h) => h.evaluate("HIEROGLYPH_BAND_OUTER_RADIUS_FRACTION * activeSceneBasis()"),
  idle: (h) => h.evaluate("HIEROGLYPH_BAND_OUTER_RADIUS_FRACTION * activeSceneBasis()"),
};

function registerHookLimits(test, sceneDir, scene) {
  test("R1 " + scene + " hook: the title limits stop a small margin short of the scene's reference ring (wallpaper and mini)", () => {
    for (const variant of ["full", "mini"]) {
      const width = variant === "mini" ? 240 : 1280, height = variant === "mini" ? 240 : 720;
      const h = alertOverlayCacheChecks.harness(sceneDir, { variant, width, height });
      const spy = RING_CENTER_SPY[scene];
      h.evaluate("var r1RingCenters = []; var r1RingSpied = " + spy.name + "; " + spy.name
        + " = function () { r1RingCenters.push(arguments[" + spy.cyArg + "]); return r1RingSpied.apply(this, arguments); };");
      [0, 40, 80].forEach(h.tick);
      const centers = h.evaluate("r1RingCenters");
      assert.ok(centers.length >= 1, variant + ": the scene drew its ring");
      const cy = centers[centers.length - 1];
      const W = h.evaluate("W"), H = h.evaluate("H");
      assert.strictEqual(W, width, variant + ": scene width");
      const limits = h.evaluate("sceneAlertTitleLimits(W, H)");
      const margin = Math.max(3, Math.min(W, H) * 0.012);
      const ringReach = RING_REACH[scene](h, variant === "mini");
      assert.ok(ringReach > 0, variant + ": ring reach");
      near(limits.top, cy - ringReach - margin, variant + ": top limit");
      near(limits.bottom, cy + ringReach + margin, variant + ": bottom limit");
      assert.deepStrictEqual(h.errors, [], variant + ": no caught render errors");
    }
  });
}

// ---- raphael: the see-through stamps the gold ring's own sprites --------------------------------------------

// The raphael hook, run alone against stubs: one sprite per ring slot, the stroke ring until they are baked.
function raphaelHook(sceneDir, sprites) {
  const calls = { images: [], strokes: 0 };
  const g = {
    save() {}, restore() {}, translate() {}, scale() {}, rotate() {},
    drawImage(canvas, x, y, w, h) { calls.images.push({ canvas, x, y, w, h }); },
  };
  const sandbox = {
    Math, TAU: Math.PI * 2, sprites, isMiniVariant: false, MINI_SCENE_ZOOM: 1.2,
    goldGlyphRingDrawParams: () => ({ radius: 100, rotation: 0, pool: [], count: 3, glyphSize: 4, lineWidth: 1 }),
    glyphRingOrientationAngle: (angle) => angle - Math.PI / 2,
    drawGlyphRing: () => { calls.strokes++; },
  };
  vm.createContext(sandbox);
  vm.runInContext(readWeb(sceneDir, "raphael/js/see-through-hook.js"), sandbox);
  sandbox.sceneSeeThroughLayer(g, 1920, 1080, 0.25);
  return calls;
}

function registerRaphael(test, sceneDir) {
  test("R1 raphael see-through stamps the gold ring sprites the scene draws, strokes until they are baked", () => {
    const set = [{ canvas: "a", hw: 3, hh: 5 }, { canvas: "b", hw: 4, hh: 6 }];
    const stamped = raphaelHook(sceneDir, { outlineGlyphsGold: set });
    assert.strictEqual(stamped.strokes, 0);
    assert.deepStrictEqual(stamped.images.map((i) => [i.canvas, i.x, i.y, i.w, i.h]),
      [["a", -3, -5, 6, 10], ["b", -4, -6, 8, 12]]);
    const fallback = raphaelHook(sceneDir, null);
    assert.strictEqual(fallback.strokes, 1);
    assert.strictEqual(fallback.images.length, 0);
  });
}

function register(test, sceneDir, scene) {
  const pins = [scene + "/js/see-through-hook.js"];
  if (scene === "processing") {
    pins.unshift("shared/js/alert-overlay.js");
    registerHelpers(test, sceneDir);
    registerOverlay(test, sceneDir);
  }
  registerPin(test, sceneDir, pins);
  registerHookLimits(test, sceneDir, scene);
  if (scene === "raphael") registerRaphael(test, sceneDir);
}

module.exports = { register: register, stripR1: stripR1 };

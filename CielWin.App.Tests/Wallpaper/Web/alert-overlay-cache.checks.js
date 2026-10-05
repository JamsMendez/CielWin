"use strict";

// Scene optimizations S4b (odd/tasks/scene-optimizations.md, ported from CieLinux 4574b4a W1 alert pieces + W4
// and its tests/wallpaper-optimization.contract.test.mjs): the alert overlay's per-frame work.
//   - W1: the wash, the two title letters and their drop shadow (a full-tile shadowBlur) are baked once per
//     tile size/theme/scale/title width and stamped with drawImage; released when the overlay stops.
//   - W4 (full wallpaper only; mini keeps the reference per-frame see-through, modules and backdrop copy):
//     explorer draws its rising sparks once into a canvas-sized layer that the see-through letters reuse;
//     the intersections are recolored, clipped and stamped over the two title bands only; the side modules
//     re-render only when their 100 ms counter ticks; a shown FAILED tile pixelates the scene canvas
//     directly instead of copying the whole canvas first.
// The reference is the pre-S4b code: every "Scene optimization begin/end (S4b)" block stripped (pinned below by
// SHA-256). The mocks record Canvas2D calls per canvas; they do not model pixels (the visual check is the user's).
//
// Shared by processing-scene.tests.js (overlay checks) and explorer-scene.tests.js (pins + spark layer):
// register(test, sceneDir, scene).

const vm = require("vm");
const fs = require("fs");
const path = require("path");
const assert = require("assert");
const crypto = require("crypto");
const { URLSearchParams } = require("url");

// SHA-256 of the pre-S4b sources (5b8a22b), LF-normalized: `git show 5b8a22b:<file> | tr -d '\r' | sha256sum`.
const PRE_S4B = {
  "shared/js/alert-overlay.js": "ca3ea282f62fcee9e009548ac0a9556bd018c8c2d99d7e6d10928313ef19c2d0",
  "explorer/js/animate.js": "2846bd14ec328fd814eaea7529ca8262281a57b8dfefc26f8282c93c92a23ecf",
};

const S4B_BLOCKS = /^[ \t]*\/\/ Scene optimization begin \(S4b\)[^\n]*\n[\s\S]*?^[ \t]*\/\/ Scene optimization end \(S4b\)\.\n(\n(?=\/\/|function|const|let|var))?/gm;
const normalize = (text) => text.replace(/\r\n/g, "\n");
const stripS4b = (text) => normalize(text).replace(S4B_BLOCKS, "");
const sha256 = (text) => crypto.createHash("sha256").update(text).digest("hex");

// Loads the real page (index.html's <script> order) into one vm realm. Every canvas gets its own recording
// context, so the scene canvas's call stream is separate from each offscreen layer's.
function harness(sceneDir, options) {
  options = options || {};
  const variant = options.variant || "full";
  const width = options.width || 640, height = options.height || 360, dpr = options.dpr || 1;
  const streams = new Map(), frames = [], events = {}, errors = [], created = [];
  let nextId = 0;
  const makeContext = (element) => {
    const id = nextId++, ops = [];
    streams.set(id, ops);
    const state = { globalAlpha: 1, globalCompositeOperation: "source-over" };
    const proxy = new Proxy(state, {
      get(target, name) {
        if (name in target) return target[name];
        if (name === "canvas") return element(); // a real context's back-reference to its canvas
        if (name === "measureText") return (text) => ({ width: String(text).length * 4 });
        if (name === "createImageData" || name === "getImageData") {
          return (a, b, c, d) => {
            const w = name === "getImageData" ? c : a, h = name === "getImageData" ? d : b;
            return { width: w, height: h, data: new Uint8ClampedArray(Math.max(0, w * h * 4)) };
          };
        }
        return (...args) => {
          ops.push([name, ...args]);
          if (String(name).startsWith("create")) {
            return { gradient: name, args, stops: [], addColorStop(stop, color) { this.stops.push([stop, color]); } };
          }
          return undefined;
        };
      },
      set(target, name, value) { ops.push(["=" + String(name), value]); target[name] = value; return true; },
    });
    return { id, ops, proxy };
  };
  const scene0 = makeContext(() => canvas);
  const bounds = { width, height };
  const canvas = { width: 0, height: 0, style: {}, getContext: () => scene0.proxy, getBoundingClientRect: () => bounds,
    addEventListener() {} };
  const nebula = { width: 0, height: 0, style: {}, getContext: () => null, addEventListener() {} };
  const windowMock = { devicePixelRatio: dpr, innerWidth: width, innerHeight: height,
    requestAnimationFrame: (fn) => frames.push(fn),
    addEventListener: (name, fn) => { events[name] = fn; } };
  const sandbox = vm.createContext({
    window: windowMock, URLSearchParams, performance: { now: () => 0 },
    location: { search: "?variant=" + variant + "&fps=60", hash: options.hash || "" },
    console: { log() {}, info() {}, warn() {}, error: (...args) => { errors.push(args.map(String)); } },
    document: {
      getElementById: (name) => (name === "scene" ? canvas : name === "nebula" ? nebula : null),
      createElement: () => {
        const element = { width: 0, height: 0, style: {}, contextId: null };
        let c = null;
        element.getContext = () => { if (!c) { c = makeContext(() => element); element.contextId = c.id; } return c.proxy; };
        created.push(element);
        return element;
      },
      documentElement: { classList: { add() {} } },
      addEventListener: (name, fn) => { events["document:" + name] = fn; },
      fonts: undefined,
    },
  });
  const html = fs.readFileSync(path.join(sceneDir, "index.html"), "utf8");
  for (const match of html.matchAll(/<script src="([^"]+)"><\/script>/g)) {
    const script = match[1];
    vm.runInContext(fs.readFileSync(path.join(sceneDir, script), "utf8"), sandbox, { filename: script });
  }
  const main = streams.get(scene0.id);
  // Ops recorded since `marks` (from marksOf), per stream, including the scene canvas.
  const marksOf = () => new Map(Array.from(streams, ([id, ops]) => [id, ops.length]));
  const sinceMarks = (marks) => Array.from(streams, ([id, ops]) => ops.slice(marks.get(id) || 0));
  const tick = (ms) => {
    assert.ok(frames.length >= 1, "a frame is scheduled");
    const start = main.length;
    frames.splice(0, frames.length).forEach((fn) => fn(ms));
    return main.slice(start);
  };
  const resize = (w, h) => {
    bounds.width = w; bounds.height = h; windowMock.innerWidth = w; windowMock.innerHeight = h;
    events.resize();
  };
  const streamOf = (element) => streams.get(element.contextId) || [];
  return { sandbox, main, canvas, created, errors, tick, resize, streamOf, marksOf, sinceMarks,
    evaluate: (text) => vm.runInContext(text, sandbox) };
}

const isLabel = (op) => op[0] === "fillText" && /^00:\d+ [01]{8}$/.test(String(op[1]));
const json = (value) => JSON.parse(JSON.stringify(value));
// The tile frame (drawFailureOverlay: x = W * 0.038, y = H * 0.064) and the module boxes (width max(16, W * 0.018)).
const isFrameStroke = (op, w, h) => op[0] === "strokeRect" && op[1] === w * 0.038 && op[2] === h * 0.064;
const isModuleBox = (op, w) => op[0] === "strokeRect" && op[3] === Math.max(16, w * 0.018);

function alertRun(sceneDir, hash, frames, options) {
  const h = harness(sceneDir, Object.assign({ hash: hash }, options || {}));
  const perFrame = [h.tick(0)];
  for (const ms of frames) perFrame.push(h.tick(ms));
  return { h, perFrame };
}

function staticCache(h) { return Object.values(h.evaluate("failureStaticCache")); }

function registerPins(test, sceneDir) {
  test("S4b blocks only add lines: stripping them restores the pre-S4b sources", () => {
    const webDir = path.join(sceneDir, "..");
    for (const name of Object.keys(PRE_S4B)) {
      const text = normalize(fs.readFileSync(path.join(webDir, name), "utf8"));
      assert.match(text, /^[ \t]*\/\/ Scene optimization begin \(S4b\)/m, name + " marks S4b");
      assert.strictEqual(sha256(stripS4b(text)), PRE_S4B[name], name);
    }
  });
}

function registerOverlay(test, sceneDir) {
  test("S4b alert: wash, title letters and their shadow are baked once, with the theme's text and colors", () => {
    for (const kind of ["warning", "failed"]) {
      const { h, perFrame } = alertRun(sceneDir, "#kind=" + kind + "&duration=99999", [300, 900, 1500, 1533, 1566]);
      const theme = h.evaluate("FAILURE_OVERLAY_THEMES." + kind);
      const all = Array.from(h.created, (element) => h.streamOf(element)).flat().concat(h.main);
      assert.strictEqual(all.filter((op) => op[0] === "fillText" && op[1] === theme.title).length, 2,
        kind + ": two titles (mirrored top/bottom), one bake for the whole alert");
      for (const ops of perFrame.slice(3)) {
        assert.strictEqual(ops.filter((op) => op[0] === "=shadowBlur" && op[1] > 0).length, 0,
          kind + ": no per-frame shadow on the scene canvas");
        assert.strictEqual(ops.filter((op) => op[0] === "fill" && op[1] === "evenodd").length, 0, kind + ": wash stamped");
        assert.strictEqual(ops.filter((op) => op[0] === "=globalCompositeOperation" && op[1] === "difference").length, 1,
          kind + ": rails stay per frame");
        assert.strictEqual(ops.filter((op) => isFrameStroke(op, 640, 360)).length, 1, kind + ": frame stroke per frame");
      }
      const cache = staticCache(h);
      assert.strictEqual(cache.length, 1, kind + ": one bake");
      const wash = h.streamOf(cache[0].wash.canvas);
      assert.deepStrictEqual(wash.filter((op) => op[0] === "=fillStyle"), [["=fillStyle", theme.wash]], kind + ": wash color");
      assert.strictEqual(wash.filter((op) => op[0] === "fill" && op[1] === "evenodd").length, 1);
      const letters = h.streamOf(cache[0].letters.canvas);
      assert.deepStrictEqual(letters.filter((op) => op[0] === "=fillStyle"), [["=fillStyle", theme.letters]], kind + ": letters color");
      assert.deepStrictEqual(letters.filter((op) => op[0] === "fillText").map((op) => op[1]), [theme.title, theme.title]);
      // The shadow bake is the reference letters stamp: shadow, offset, alpha 0.86, letters at the tile size.
      const shadowed = h.streamOf(cache[0].shadowed.canvas);
      assert.deepStrictEqual(json(shadowed.map((op) => (op[0] === "drawImage" ? [op[0]].concat(op.slice(2)) : op))), [
        ["setTransform", 1, 0, 0, 1, 0, 0], ["=shadowColor", "rgba(0,0,0,0.8)"], ["=shadowBlur", 360 * 0.03],
        ["=shadowOffsetY", 360 * 0.008], ["=globalAlpha", 0.86], ["drawImage", 0, 0, 640, 360]]);
      assert.strictEqual(shadowed[shadowed.length - 1][1], cache[0].letters.canvas);
      // The see-through intersections are recolored with the theme's color and clipped by the cached letters.
      const intersections = h.streamOf(h.evaluate("failureLayers.intersections").canvas);
      assert.ok(intersections.some((op) => op[0] === "=fillStyle" && op[1] === theme.intersections), kind + ": intersections color");
      assert.ok(intersections.filter((op) => op[0] === "drawImage" && op[1] === cache[0].letters.canvas).length >= 3,
        kind + ": letters clip every frame");
      assert.strictEqual(h.evaluate("failureLayers.letters"), undefined, kind + ": no per-frame letters layer");
      assert.deepStrictEqual(h.errors, [], kind + ": no caught render errors");
    }
  });

  test("S4b alert: cached layers are rebuilt on resize, title face load and alert change, and released when the alert stops", () => {
    const h = harness(sceneDir, { hash: "#kind=warning&duration=99999" });
    [0, 500, 800].forEach(h.tick);
    const first = staticCache(h)[0];
    assert.ok(first, "baked while revealing");
    h.tick(833);
    assert.strictEqual(staticCache(h).length, 1, "steady frames reuse the bake");
    h.resize(800, 400);
    h.tick(866);
    assert.strictEqual(staticCache(h).length, 2, "a resize bakes the new tile size");
    // Title face loads: a different measured width means a different bake key.
    h.evaluate("failureMeasureContext.measureText = function (text) { return { width: String(text).length * 5 }; }");
    h.tick(900);
    assert.strictEqual(staticCache(h).length, 3, "the title face bakes again");
    h.evaluate("startShowing(['failed'], 1, 1, 0, 99999)");
    [1000, 1400, 2000].forEach(h.tick);
    const titles = staticCache(h).map((entry) => h.streamOf(entry.letters.canvas).filter((op) => op[0] === "fillText")[0][1]);
    assert.deepStrictEqual(titles.filter((title) => title === "FAILED").length, 1, "the new alert bakes its own title");
    h.evaluate("hide()");
    h.tick(2100);
    assert.strictEqual(staticCache(h).length, 0, "released once the overlay stops");
    assert.strictEqual(first.wash.canvas.width, 0);
    assert.strictEqual(first.shadowed.canvas.width, 0);
    assert.strictEqual(Object.keys(h.evaluate("failureModuleCache")).length, 0, "module layers released too");
  });

  test("S4b alert: side modules re-render only when the 100 ms counter ticks, at the same labels", () => {
    const { h } = alertRun(sceneDir, "#kind=warning&duration=99999", [300, 900, 1500]);
    let marks = h.marksOf();
    const same = h.tick(1533); // counter 15 again
    assert.strictEqual(h.sinceMarks(marks).flat().filter(isLabel).length, 0, "same counter: no label drawn");
    assert.strictEqual(same.filter((op) => isFrameStroke(op, 640, 360)).length, 1, "the frame stroke stays on the scene canvas");
    assert.strictEqual(h.sinceMarks(marks).flat().filter((op) => isModuleBox(op, 640)).length, 0, "same counter: no module box drawn");
    marks = h.marksOf();
    const next = h.tick(1633); // counter 16
    const labels = h.sinceMarks(marks).flat().filter(isLabel);
    assert.strictEqual(labels.length, 8, "eight module labels when the counter ticks");
    assert.ok(labels.every((op) => /^00:16 [01]{8}$/.test(op[1])), JSON.stringify(labels.map((op) => op[1])));
    assert.strictEqual(next.filter(isLabel).length, 0, "labels live on the module layer");
    const moduleLayer = Object.values(h.evaluate("failureModuleCache"))[0].layer.canvas;
    assert.strictEqual(next.filter((op) => op[0] === "drawImage" && op[1] === moduleLayer).length, 2, "two module columns stamped");
  });

  test("S4b alert: intersections are recolored, clipped and composited over the two letter bands only", () => {
    const { h } = alertRun(sceneDir, "#kind=failed&duration=99999", [300, 900, 1500]);
    const marks = h.marksOf();
    const ops = h.tick(1533);
    const intersections = h.evaluate("failureLayers.intersections").canvas;
    const letters = staticCache(h)[0].letters.canvas;
    const layerOps = h.streamOf(intersections).slice(marks.get(intersections.contextId));
    const area = 640 * 360;
    const sourceIn = layerOps.filter((op) => op[0] === "=globalCompositeOperation" && op[1] === "source-in");
    assert.strictEqual(sourceIn.length, 2, "one source-in recolor per band");
    const clips = layerOps.filter((op) => op[0] === "drawImage" && op[1] === letters);
    assert.strictEqual(clips.length, 2, "one letters clip per band");
    assert.ok(clips.every((op) => op.length === 10 && op[4] * op[5] <= 0.6 * area), JSON.stringify(json(clips.map((op) => op.slice(2)))));
    const stamps = ops.filter((op) => op[0] === "drawImage" && op[1] === intersections);
    assert.strictEqual(stamps.length, 2, "two band stamps");
    assert.ok(stamps.every((op) => op.length === 10), "band stamps use source rects");
    assert.ok(stamps.reduce((sum, op) => sum + op[8] * op[9], 0) <= 0.6 * area);
  });

  test("S4b alert: a shown FAILED tile pixelates the scene canvas directly, no whole-canvas backdrop copy", () => {
    for (const hash of ["#kind=failed&duration=99999", "#tiles=failed,failed&columns=2&rows=1&gap=8&duration=99999"]) {
      const { h } = alertRun(sceneDir, hash, [300, 900, 1500]);
      const marks = h.marksOf();
      h.tick(1533);
      const backdrop = h.evaluate("failureLayers.backdrop");
      const touched = h.sinceMarks(marks).flat();
      if (backdrop) {
        assert.strictEqual(h.streamOf(backdrop.canvas).slice(marks.get(backdrop.canvas.contextId)).filter((op) => op[0] === "drawImage").length,
          0, hash + ": no copy");
      }
      const reads = touched.filter((op) => op[0] === "drawImage" && op[1] === h.canvas);
      assert.strictEqual(reads.length, hash.indexOf("tiles") >= 0 ? 2 : 1, hash + ": one direct downscale per failed tile");
      assert.ok(reads.every((op) => op.length === 10), hash + ": downscale crops the tile rect");
    }
    // A revealing FAILED tile keeps the reference whole-canvas copy (the tile overlays would otherwise leak in).
    const { h } = alertRun(sceneDir, "#kind=failed&duration=99999", [300]);
    const backdrop = h.evaluate("failureLayers.backdrop");
    assert.ok(backdrop && h.streamOf(backdrop.canvas).some((op) => op[0] === "drawImage" && op[1] === h.canvas), "revealing: copy kept");
  });

  test("S4b alert: mini keeps the reference see-through, per-frame modules and backdrop copy (only the static bake)", () => {
    const h = harness(sceneDir, { variant: "mini", width: 240, height: 240,
      hash: "#tiles=failed,warning&columns=2&rows=1&gap=8&duration=99999" });
    [0, 300, 900, 1500].forEach(h.tick);
    h.evaluate("var s4bIntersections = 0; var s4bReferenceIntersections = drawSeeThroughIntersections;"
      + "drawSeeThroughIntersections = function () { s4bIntersections++; return s4bReferenceIntersections.apply(this, arguments); };");
    const marks = h.marksOf();
    const ops = h.tick(1533);
    assert.strictEqual(h.evaluate("s4bIntersections"), 2, "reference see-through per tile");
    assert.strictEqual(ops.filter(isLabel).length, 16, "module labels drawn on the canvas every frame");
    const backdrop = h.evaluate("failureLayers.backdrop");
    assert.ok(h.streamOf(backdrop.canvas).slice(marks.get(backdrop.canvas.contextId)).some((op) => op[0] === "drawImage"),
      "whole-canvas backdrop copy kept");
    assert.strictEqual(staticCache(h).length, 2, "the static layers are baked per theme");
    assert.strictEqual(Object.keys(h.evaluate("failureModuleCache")).length, 0, "no module layers");
  });
}

function registerSparks(test, sceneDir) {
  // One realm (explorer's earth bake costs seconds per vm realm): warning, failed + warning tiles, hide, resize.
  test("S4b explorer alert: the rising sparks run once per frame and the see-through letters reuse the scene's spark layer", () => {
    const h = harness(sceneDir, { hash: "#kind=warning&duration=99999" });
    h.evaluate("var s4bSparkCalls = 0; var s4bReferenceSparks = drawRisingSparks;"
      + "drawRisingSparks = function (c, t) { s4bSparkCalls++; return s4bReferenceSparks(c, t); };"
      + "var s4bHookCalls = 0; var s4bReferenceHook = sceneSeeThroughLayer;"
      + "sceneSeeThroughLayer = function () { s4bHookCalls++; return s4bReferenceHook.apply(this, arguments); };");
    const layerStamps = (ops) => ops.filter((op) => op[0] === "drawImage" && op.length === 4 && op[2] === 0 && op[3] === 0
      && op[1] !== h.canvas);
    const check = (label, ms, tilesShown) => {
      h.evaluate("s4bSparkCalls = 0; s4bHookCalls = 0");
      const marks = h.marksOf();
      const ops = h.tick(ms);
      assert.strictEqual(h.evaluate("s4bSparkCalls"), 1, label + " @" + ms + ": one spark pass");
      assert.strictEqual(h.evaluate("s4bHookCalls"), 0, label + " @" + ms + ": no second spark pass through the hook");
      const stamp = ops.findIndex((op) => layerStamps([op]).length === 1);
      assert.ok(stamp > 0, label + " @" + ms + ": spark layer stamped onto the scene");
      const modes = ops.slice(0, stamp).filter((op) => op[0] === "=globalCompositeOperation");
      assert.deepStrictEqual(modes[modes.length - 1], ["=globalCompositeOperation", "lighter"], "stamped additively");
      const layer = h.evaluate("seeThroughSparkLayer").canvas;
      assert.strictEqual(ops[stamp][1], layer);
      const blits = h.sinceMarks(marks).flat().filter((op) => op[0] === "drawImage" && op[1] === layer);
      assert.strictEqual(blits.length, 1 + 2 * tilesShown, label + " @" + ms + ": one scene stamp + one blit per band");
      assert.deepStrictEqual([layer.width, layer.height], [h.canvas.width, h.canvas.height], "canvas-sized layer");
    };
    h.tick(0);
    h.tick(100);
    check("warning", 500, 1); // revealing
    check("warning", 1500, 1); // shown
    h.evaluate("startShowing(['warning', 'failed'], 2, 1, 8, 99999)");
    h.tick(1600); // failed shakes: no see-through yet
    h.tick(1900);
    check("warning+failed", 2400, 2);
    check("warning+failed", 3400, 2);
    h.resize(800, 400);
    check("resized", 3500, 2);
    const layer = h.evaluate("seeThroughSparkLayer").canvas;
    h.evaluate("hide()");
    h.evaluate("s4bSparkCalls = 0");
    const ops = h.tick(3600);
    assert.strictEqual(h.evaluate("s4bSparkCalls"), 1, "no alert: one spark pass");
    assert.strictEqual(layerStamps(ops).length, 0, "no alert: sparks drawn straight onto the canvas");
    assert.strictEqual(h.evaluate("seeThroughSparkLayer"), null, "layer released after the alert");
    assert.strictEqual(layer.width, 0);
    assert.deepStrictEqual(h.errors, [], "no caught render errors");
  });
}

function register(test, sceneDir, scene) {
  if (scene === "explorer") {
    registerPins(test, sceneDir);
    registerSparks(test, sceneDir);
  } else {
    registerOverlay(test, sceneDir);
  }
}

module.exports = { register: register, harness: harness, stripS4b: stripS4b };

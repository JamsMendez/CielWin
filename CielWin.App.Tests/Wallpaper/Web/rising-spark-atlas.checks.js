"use strict";

// Scene optimization S4c (odd/tasks/scene-optimizations.md, ported from CieLinux 4574b4a W1 spark atlas and its
// tests/wallpaper-optimization.contract.test.mjs): on the full wallpaper, explorer draws every mature rising spark
// (older than RISING_SPARK_TRAIL_SECONDS) as its two trail halves stamped from a pre-baked atlas instead of 6
// strokes + 1 head fill; young sparks keep the reference strokes and the mini page draws exactly as before.
// The reference is the pre-S4c code: every "Scene optimization begin/end (S4c)" block stripped (pinned below by
// SHA-256), evaluated in the same realm as drawRisingSparksReference. The mocks record Canvas2D calls per canvas;
// they do not rasterize, so fidelity is bounded geometrically (trail points, sizes, additive alpha); the visual
// check is the user's.
//
// Used by explorer-scene.tests.js: register(test, sceneDir). Two vm realms (full, mini): explorer's earth bake
// costs seconds per realm.

const fs = require("fs");
const path = require("path");
const assert = require("assert");
const crypto = require("crypto");
const { harness } = require(path.join(__dirname, "alert-overlay-cache.checks.js"));

// SHA-256 of the pre-S4c source (24f3941), LF-normalized: `git show 24f3941:<file> | tr -d '\r' | sha256sum`.
const PRE_S4C = {
  "js/rising-sparks.js": "f4528d22e78def33741a70a913201e5d9a6f7ee27bd58f4d98b33becf02fed33",
};

const S4C_BLOCKS = /^[ \t]*\/\/ Scene optimization begin \(S4c\)[^\n]*\n[\s\S]*?^[ \t]*\/\/ Scene optimization end \(S4c\)\.\n(\n(?=\/\/|function|const|let|var))?/gm;
const normalize = (text) => text.replace(/\r\n/g, "\n");
const stripS4c = (text) => normalize(text).replace(S4C_BLOCKS, "");
const sha256 = (text) => crypto.createHash("sha256").update(text).digest("hex");
const json = (value) => JSON.parse(JSON.stringify(value));
const count = (ops, name) => ops.filter((op) => op[0] === name).length;

// Distinct, irregular scene times (fractions, a frame step, an hour in).
const SPARK_TIMES = [0.5, 1.234, 7.7, 10, 10 + 1 / 60, 63.2, 3600.1];

// A context that records its calls (the harness's own recorder shape), optionally with a getTransform.
function recorder(extra) {
  const ops = [];
  const state = Object.assign({ globalAlpha: 1, globalCompositeOperation: "source-over" }, extra || {});
  const proxy = new Proxy(state, {
    get(target, name) {
      if (name in target) return target[name];
      return (...args) => { ops.push([name, ...args]); return undefined; };
    },
    set(target, name, value) { ops.push(["=" + String(name), value]); target[name] = value; return true; },
  });
  return { ops, proxy };
}

// Loads the page and evaluates the pre-S4c drawRisingSparks next to the current one.
function sparkPage(sceneDir, options) {
  const h = harness(sceneDir, options);
  const source = stripS4c(fs.readFileSync(path.join(sceneDir, "js", "rising-sparks.js"), "utf8"));
  const reference = source.match(/^function drawRisingSparks\([\s\S]*?^}/m);
  assert.ok(reference, "reference drawRisingSparks found");
  h.evaluate(reference[0].replace("function drawRisingSparks(", "function drawRisingSparksReference("));
  return h;
}

function setGeometry(h, width, height, dpr) {
  h.sandbox.window.devicePixelRatio = dpr;
  h.resize(width, height);
}

// The sparks drawRisingSparks strokes (reference filters and order), with their 7 trail points.
function visibleSparks(h, time) {
  return JSON.parse(h.evaluate("JSON.stringify((() => {"
    + "  const out = [];"
    + "  for (let i = 0; i < RISING_SPARK_COUNT; i++) {"
    + "    const spark = risingSparkAt(i, " + time + ", W, H);"
    + "    if (spark.age > spark.lifetime) continue;"
    + "    const alpha = risingSparkEnvelope(spark.age / spark.lifetime) * spark.brightness;"
    + "    if (alpha <= 0.01) continue;"
    + "    const trail = [];"
    + "    for (let s = 0; s <= RISING_SPARK_TRAIL_SAMPLES; s++) {"
    + "      const t = s / RISING_SPARK_TRAIL_SAMPLES;"
    + "      const p = risingSparkPosition(spark, Math.max(0, spark.age - RISING_SPARK_TRAIL_SECONDS * (1 - t)), W, H);"
    + "      trail.push(p.x, p.y);"
    + "    }"
    + "    out.push({ index: i, young: spark.age < RISING_SPARK_TRAIL_SECONDS, alpha, size: spark.size, trail });"
    + "  }"
    + "  return out;"
    + "})())"));
}

// Splits a drawRisingSparks stream into per-spark units: a sprite unit is the two halves' setTransform + alpha +
// 1..2 drawImage; a stroke unit ends with its head 'fill' (a base transform/alpha reset is dropped).
function sparkUnits(ops) {
  if (ops[0] && ops[0][0] === "getTransform") ops = ops.slice(1); // S4c reads the base transform once
  assert.deepStrictEqual(json(ops.slice(0, 3)), [["save"], ["=globalCompositeOperation", "lighter"], ["=lineCap", "round"]]);
  assert.deepStrictEqual(ops[ops.length - 1], ["restore"]);
  const body = ops.slice(3, -1), units = [];
  for (let i = 0; i < body.length;) {
    if (body[i][0] === "setTransform" && body[i + 1] && body[i + 1][0] === "=globalAlpha" && body[i + 2] && body[i + 2][0] === "drawImage") {
      const halves = [];
      for (let half = 0; half < 2; half++) {
        const transform = body[i++], alpha = body[i++][1], draws = [];
        while (body[i] && body[i][0] === "drawImage") draws.push(body[i++]);
        halves.push({ transform, alpha, draws });
      }
      units.push({ sprite: true, halves });
      continue;
    }
    const group = [];
    while (body[i][0] !== "fill") group.push(body[i++]);
    group.push(body[i++]);
    units.push({ sprite: false, ops: group.filter((op) => op[0] !== "setTransform" && op[0] !== "=globalAlpha") });
  }
  return units;
}

function registerPins(test, sceneDir) {
  test("S4c blocks only add lines: stripping them restores the pre-S4c sources", () => {
    for (const name of Object.keys(PRE_S4C)) {
      const text = normalize(fs.readFileSync(path.join(sceneDir, name), "utf8"));
      assert.match(text, /^[ \t]*\/\/ Scene optimization begin \(S4c\)/m, name + " marks S4c");
      assert.strictEqual(sha256(stripS4c(text)), PRE_S4C[name], name);
    }
  });
}

function registerFull(test, sceneDir) {
  let page = null;
  const fullPage = () => (page = page || sparkPage(sceneDir, { width: 1720, height: 720 }));

  test("S4c full sparks: mature sparks are two atlas halves, young sparks keep the reference strokes", () => {
    const h = fullPage();
    setGeometry(h, 1720, 720, 1);
    let mature = 0, young = 0, draws = 0, referenceDraws = 0;
    for (const time of SPARK_TIMES) {
      const visible = visibleSparks(h, time);
      const now = recorder(), ref = recorder();
      h.sandbox.drawRisingSparks(now.proxy, time);
      h.sandbox.drawRisingSparksReference(ref.proxy, time);
      const units = sparkUnits(now.ops), refUnits = sparkUnits(ref.ops);
      assert.strictEqual(units.length, visible.length, "t=" + time);
      assert.strictEqual(refUnits.length, visible.length, "reference t=" + time);
      assert.deepStrictEqual(units.map((unit) => !unit.sprite), visible.map((spark) => spark.young), "unit order at t=" + time);
      units.forEach((unit, k) => {
        if (unit.sprite) { mature++; return; }
        young++;
        assert.deepStrictEqual(json(unit.ops), json(refUnits[k].ops), "young spark " + visible[k].index + " at t=" + time);
      });
      assert.strictEqual(count(now.ops, "stroke"), 6 * units.filter((unit) => !unit.sprite).length);
      draws += count(now.ops, "drawImage") + count(now.ops, "stroke") + count(now.ops, "fill");
      referenceDraws += count(ref.ops, "stroke") + count(ref.ops, "fill");
    }
    assert.ok(mature > 1000 && young / (mature + young) < 0.15, mature + " mature, " + young + " young");
    assert.ok(draws < referenceDraws * 0.5, draws + " draw ops vs reference " + referenceDraws);
    console.log("  S4c: " + mature + " mature + " + young + " young sparks: " + draws + " draw ops (reference "
      + referenceDraws + " anti-aliased paths)");
  });

  test("S4c full sparks: halves follow the analytic trail and keep the additive brightness", () => {
    const h = fullPage();
    for (const [width, height, dpr] of [[3440, 1440, 1], [1720, 720, 2], [1920, 1080, 1.25]]) {
      setGeometry(h, width, height, dpr);
      const atlasDpr = h.evaluate("DPR");
      let worst = 0, checked = 0, worstSize = 0;
      const errors = [];
      for (const time of SPARK_TIMES) {
        const visible = visibleSparks(h, time);
        const now = recorder();
        h.sandbox.drawRisingSparks(now.proxy, time);
        const atlas = h.evaluate("risingSparkFullAtlas");
        sparkUnits(now.ops).forEach((unit, k) => {
          if (!unit.sprite) return;
          const spark = visible[k];
          unit.halves.forEach(({ transform, alpha, draws }, half) => {
            const [, a, b, c, d, e, f] = transform;
            // Brightness: half 0 is baked at unit gain and stamped once at alpha; half 1 (half gain) totals 2 x alpha.
            const total = alpha * draws.length * (half === 1 ? 0.5 : 1);
            assert.ok(Math.abs(total - spark.alpha) < 1e-12, "alpha " + total + " vs " + spark.alpha);
            assert.ok(alpha <= 1 && draws.length === (half === 1 && spark.alpha > 0.5 ? 2 : 1), "stamps within globalAlpha");
            const [, image, sx, sy, sw, sh, dx, dy, dw, dh] = draws[0];
            assert.strictEqual(image, atlas.canvas);
            for (const draw of draws) assert.deepStrictEqual(draw, draws[0]);
            const cell = (sy / atlas.cellHeight) * atlas.perRow + sx / atlas.cellWidth;
            assert.ok(Number.isInteger(cell), "cell origin on the grid");
            const col = cell % atlas.lengthCount, row = (cell - col) / atlas.lengthCount;
            assert.strictEqual(row % 2, half);
            const size = atlas.sizes[(row - half) / 2], length = atlas.lengths[col];
            worstSize = Math.max(worstSize, Math.abs(size - spark.size));
            assert.ok(Math.abs(size - spark.size) <= atlas.sizeStep / 2 + 1e-12, "size bucket");
            assert.strictEqual(sw, atlas.cellWidth);
            assert.strictEqual(sh, atlas.cellHeight);
            // The scene transform here is the fallback canvas scale (the recorder has no getTransform).
            for (let j = 0; j <= 3; j++) {
              const localX = dx + (atlas.pad + j / 3 * length) * atlas.dpr * dw / sw;
              const localY = dy + atlas.pad * atlas.dpr * dh / sh;
              const px = a * localX + c * localY + e, py = b * localX + d * localY + f;
              const p = (half * 3 + j) * 2;
              const scale = h.evaluate("canvasScaleX");
              const error = Math.hypot(px - spark.trail[p] * scale, py - spark.trail[p + 1] * h.evaluate("canvasScaleY"));
              errors.push(error);
              worst = Math.max(worst, error);
              // Chord endpoints within the 0.25 device px length bucket (half of it at each end, plus pixel
              // rounding of the canvas scale); inner samples within 1 device px.
              assert.ok(error <= (j === 0 || j === 3 ? 0.15 : 1) * Math.max(1, atlasDpr),
                width + "x" + height + "@" + dpr + " t=" + time + " spark " + spark.index + " point " + (half * 3 + j) + ": " + error);
            }
            checked++;
          });
        });
      }
      errors.sort((x, y) => x - y);
      const median = errors[errors.length >> 1], p99 = errors[Math.floor(errors.length * 0.99)];
      assert.ok(checked > 1000 && median < 0.1, checked + " halves, median " + median);
      console.log("  S4c " + width + "x" + height + "@" + dpr + ": " + checked + " halves, point error median "
        + median.toFixed(3) + ", p99 " + p99.toFixed(3) + ", worst " + worst.toFixed(3) + " device px; size error <= "
        + worstSize.toFixed(4));
    }
  });

  test("S4c full sparks: the atlas bakes the reference segments, half 1 at half gain, once per geometry", () => {
    const h = fullPage();
    setGeometry(h, 1720, 720, 1);
    h.sandbox.drawRisingSparks(recorder().proxy, 1);
    const atlas = h.evaluate("risingSparkFullAtlas"), before = h.created.length;
    h.sandbox.drawRisingSparks(recorder().proxy, 1.5);
    assert.strictEqual(h.evaluate("risingSparkFullAtlas"), atlas, "same atlas on the next frame");
    assert.strictEqual(h.created.length, before, "not rebaked on the next frame");
    assert.ok(atlas.canvas.width <= 4096 && atlas.canvas.width > 0 && atlas.canvas.height > 0,
      "atlas " + atlas.canvas.width + "x" + atlas.canvas.height);
    assert.strictEqual(atlas.lengthStep, 0.25, "length buckets every 0.25 device px");
    assert.strictEqual(atlas.sizeStep, 0.05, "size buckets every 0.05");
    const bake = h.streamOf(atlas.canvas);
    assert.deepStrictEqual(json(bake.slice(0, 2)), [["=globalCompositeOperation", "lighter"], ["=lineCap", "round"]]);
    const prefix = "rgba(" + h.evaluate("RISING_SPARK_COLOR") + ", ", tau = h.evaluate("TAU");
    const segmentWidth = (s) => h.evaluate("mix(0.4, 1, " + s + " / RISING_SPARK_TRAIL_SAMPLES)");
    const widths = [0, 1, 2, 3, 4, 5, 6].map(segmentWidth);
    const sparkWidth = h.evaluate("RISING_SPARK_WIDTH"), headRadius = h.evaluate("RISING_SPARK_HEAD_RADIUS");
    let i = 2;
    for (let sizeIndex = 0; sizeIndex < atlas.sizeCount; sizeIndex++) {
      for (let half = 0; half < 2; half++) {
        for (let col = 0; col < atlas.lengthCount; col++) {
          const size = atlas.sizes[sizeIndex], length = atlas.lengths[col], gain = half ? 0.5 : 1;
          const cell = (sizeIndex * 2 + half) * atlas.lengthCount + col;
          assert.deepStrictEqual(json(bake[i++]), ["setTransform", 1, 0, 0, 1, (cell % atlas.perRow) * atlas.cellWidth,
            Math.floor(cell / atlas.perRow) * atlas.cellHeight]);
          for (let k = 1; k <= 3; k++) {
            const s = half * 3 + k;
            assert.deepStrictEqual(json(bake.slice(i, i + 6)), json([["=strokeStyle", prefix + (s / 6) * gain + ")"],
              ["=lineWidth", sparkWidth * size * widths[s]], ["beginPath"],
              ["moveTo", atlas.pad + (k - 1) / 3 * length, atlas.pad], ["lineTo", atlas.pad + k / 3 * length, atlas.pad],
              ["stroke"]]));
            i += 6;
          }
          if (half === 1) {
            assert.deepStrictEqual(json(bake.slice(i, i + 4)), json([["=fillStyle", "rgba(255, 255, 255, 0.5)"], ["beginPath"],
              ["arc", atlas.pad + length, atlas.pad, headRadius * size, 0, tau], ["fill"]]));
            i += 4;
          }
        }
      }
    }
    assert.strictEqual(i, bake.length, "nothing else baked");
    // The cells cover every possible half chord at every spark size.
    assert.ok(atlas.sizes[0] <= 0.6 && atlas.sizes[atlas.sizeCount - 1] >= 1.4);
    const halfSeconds = h.evaluate("RISING_SPARK_TRAIL_SECONDS / 2");
    assert.ok(atlas.lengths[0] <= h.evaluate("RISING_SPARK_SPEED_MIN * H") * halfSeconds + 1e-9);
    assert.ok(atlas.lengths[atlas.lengthCount - 1] >= h.evaluate("RISING_SPARK_SPEED_MAX * H * Math.hypot(1, RISING_SPARK_TILT_BASE + RISING_SPARK_TILT_EDGE)") * halfSeconds - 1e-9);
    setGeometry(h, 1600, 700, 1);
    h.sandbox.drawRisingSparks(recorder().proxy, 2);
    const resized = h.evaluate("risingSparkFullAtlas");
    assert.notStrictEqual(resized, atlas, "rebuilt on resize");
    assert.strictEqual(h.created.length, before + 1, "one new atlas canvas on resize");
    setGeometry(h, 1600, 700, 2);
    h.sandbox.drawRisingSparks(recorder().proxy, 2.5);
    assert.notStrictEqual(h.evaluate("risingSparkFullAtlas"), resized, "rebuilt on a DPR change");
    assert.strictEqual(h.evaluate("risingSparkFullAtlas").dpr, 2);
  });

  test("S4c full sparks: sprites compose with the context transform (alert see-through tile layer)", () => {
    const h = fullPage();
    setGeometry(h, 1720, 720, 1);
    const plainContext = recorder(), shifted = recorder({ getTransform: () => ({ a: 1, b: 0, c: 0, d: 1, e: -100.5, f: 40 }) });
    h.sandbox.drawRisingSparks(plainContext.proxy, 7.7);
    h.sandbox.drawRisingSparks(shifted.proxy, 7.7);
    const transforms = (ops) => ops.filter((op) => op[0] === "setTransform");
    const a = transforms(plainContext.ops), b = transforms(shifted.ops);
    assert.strictEqual(a.length, b.length);
    assert.ok(a.length > 100, a.length + " sprite transforms");
    a.forEach((op, k) => {
      assert.deepStrictEqual(b[k].slice(1, 5), op.slice(1, 5));
      assert.ok(Math.abs(b[k][5] - (op[5] - 100.5)) < 1e-9 && Math.abs(b[k][6] - (op[6] + 40)) < 1e-9, "translated by the tile origin");
    });
    // A context alpha (the strokes inherit it) scales the sprites, and a young spark after sprites gets it back.
    const faded = recorder({ globalAlpha: 0.5 });
    h.sandbox.drawRisingSparks(faded.proxy, 7.7);
    const alphas = (ops) => ops.filter((op) => op[0] === "=globalAlpha").map((op) => op[1]);
    const full = alphas(plainContext.ops), half = alphas(faded.ops);
    assert.ok(full.includes(1) && half.includes(0.5), "young-spark resets restore the context alpha");
    const stampedAlpha = (ops) => {
      let alpha = 1, sum = 0;
      for (const op of ops) {
        if (op[0] === "=globalAlpha") alpha = op[1];
        else if (op[0] === "drawImage") sum += alpha;
      }
      return sum;
    };
    const fullSum = stampedAlpha(plainContext.ops), halfSum = stampedAlpha(faded.ops);
    assert.ok(Math.abs(halfSum - fullSum / 2) < 1e-6 * fullSum, "stamped alpha " + halfSum + " vs " + fullSum + " / 2");
  });

  test("S4c full frame: the scene stamps mature sparks from one atlas, built once across frames", () => {
    const h = fullPage();
    setGeometry(h, 1720, 720, 1);
    h.tick(1000);
    const atlas = h.evaluate("risingSparkFullAtlas"), before = h.created.length;
    for (const ms of [1100, 1200, 1300]) {
      const ops = h.tick(ms);
      const stamps = ops.filter((op) => op[0] === "drawImage" && op[1] === atlas.canvas);
      assert.ok(stamps.length > 400, ms + " ms: " + stamps.length + " atlas stamps on the scene canvas");
      assert.ok(count(ops, "stroke") < stamps.length, ms + " ms: young-spark strokes only");
    }
    assert.strictEqual(h.evaluate("risingSparkFullAtlas"), atlas, "same atlas across frames");
    assert.strictEqual(h.created.length, before, "no canvas created by steady frames");
    assert.deepStrictEqual(h.errors, [], "no caught render errors");
  });

  test("S4c full sparks: the atlas stays bounded over a long run of distinct timestamps", () => {
    const h = fullPage();
    setGeometry(h, 3440, 1440, 1);
    h.sandbox.drawRisingSparks(recorder().proxy, 0.25);
    const atlas = h.evaluate("risingSparkFullAtlas"), before = h.created.length;
    const cells = atlas.sizeCount * 2 * atlas.lengthCount;
    // A counting context (no recorded ops): 1500 irregular timestamps over ~3 minutes, then later hours.
    let stamps = 0, foreign = 0, outside = 0;
    const counting = {
      save() {}, restore() {}, beginPath() {}, moveTo() {}, lineTo() {}, stroke() {}, arc() {}, fill() {}, setTransform() {},
      drawImage(image, sx, sy) {
        stamps++;
        if (image !== atlas.canvas) foreign++;
        const cell = (sy / atlas.cellHeight) * atlas.perRow + sx / atlas.cellWidth;
        if (!(cell >= 0 && cell < cells)) outside++;
      },
    };
    let time = 0.25;
    for (let k = 0; k < 1500; k++) {
      time += 0.004 + ((k * 7919) % 97) / 400; // 4..246 ms steps, never repeating
      h.sandbox.drawRisingSparks(counting, time);
    }
    for (const offset of [3600, 86400.37, 1e6 + 0.5]) {
      for (let k = 0; k < 100; k++) h.sandbox.drawRisingSparks(counting, offset + k * 0.0167);
    }
    assert.ok(stamps > 100000, stamps + " stamps");
    assert.strictEqual(foreign, 0, "every stamp comes from the atlas");
    assert.strictEqual(outside, 0, "every stamp lies inside the baked cells");
    assert.strictEqual(h.evaluate("risingSparkFullAtlas"), atlas, "never rebuilt at a fixed geometry");
    assert.strictEqual(h.created.length, before, "no canvas created");
    console.log("  S4c bounded: " + stamps + " stamps from one " + atlas.canvas.width + "x" + atlas.canvas.height
      + " atlas (" + cells + " cells)");
  });

  // S4d (review WARNING on ae8b482): the atlas grows with (height x DPR)^2, and a failed bake used to throw every
  // frame (no cache), allocating another atlas canvas each time and drawing no sparks.
  test("S4d full sparks: a failed atlas bake draws the reference strokes and is attempted once per geometry", () => {
    const h = fullPage();
    const doc = h.sandbox.document, createElement = doc.createElement;
    const sabotages = [
      ["null context", 1500, 650, 1, (element) => { element.getContext = () => null; }],
      ["throwing context", 1510, 650, 1.25, (element) => { element.getContext = () => { throw new Error("canvas allocation failed"); }; }],
    ];
    try {
      for (const [label, width, height, dpr, sabotage] of sabotages) {
        setGeometry(h, width, height, dpr);
        let attempts = 0;
        doc.createElement = (name) => { const element = createElement(name); attempts++; sabotage(element); return element; };
        for (const time of SPARK_TIMES) {
          const now = recorder(), ref = recorder();
          h.sandbox.drawRisingSparks(now.proxy, time);
          h.sandbox.drawRisingSparksReference(ref.proxy, time);
          assert.deepStrictEqual(json(now.ops), json(ref.ops), label + ": reference strokes at t=" + time);
          assert.ok(count(now.ops, "stroke") > 100, label + ": sparks drawn at t=" + time);
        }
        assert.strictEqual(attempts, 1, label + ": one bake attempt for the geometry");
        assert.strictEqual(h.evaluate("risingSparkFullAtlas"), null, label + ": no atlas kept");
        // Full frames at the failed geometry: the scene's own caches build normally, the atlas is not retried.
        doc.createElement = createElement;
        const start = 5000 + 1000 * sabotages.findIndex((entry) => entry[0] === label); // the frame clock only advances
        h.tick(start);
        const before = h.created.length;
        for (const ms of [start + 100, start + 200, start + 300]) {
          const ops = h.tick(ms);
          assert.ok(count(ops, "stroke") > 100, label + " @" + ms + ": sparks stroked on the scene canvas");
        }
        assert.strictEqual(h.created.length, before, label + ": no canvas created by later frames");
        assert.deepStrictEqual(h.errors, [], label + ": no caught render errors");
      }
    } finally {
      doc.createElement = createElement;
    }
    // A working geometry bakes again.
    setGeometry(h, 1720, 720, 1);
    const now = recorder();
    h.sandbox.drawRisingSparks(now.proxy, 7.7);
    assert.ok(h.evaluate("risingSparkFullAtlas") !== null && count(now.ops, "drawImage") > 100, "atlas back on a working geometry");
  });

  test("S4d full sparks: an oversize atlas (8K at 200%, tall portrait) is not baked; the reference strokes draw", () => {
    const h = fullPage();
    for (const [width, height, dpr] of [[7680, 4320, 2], [2160, 12000, 1]]) {
      setGeometry(h, width, height, dpr);
      const before = h.created.length;
      for (const time of SPARK_TIMES.slice(0, 3)) {
        const now = recorder(), ref = recorder();
        h.sandbox.drawRisingSparks(now.proxy, time);
        h.sandbox.drawRisingSparksReference(ref.proxy, time);
        assert.deepStrictEqual(json(now.ops), json(ref.ops), width + "x" + height + "@" + dpr + ": reference strokes at t=" + time);
      }
      assert.strictEqual(h.created.length, before, width + "x" + height + "@" + dpr + ": no atlas canvas allocated");
      assert.strictEqual(h.evaluate("risingSparkFullAtlas"), null);
    }
    // The everyday sizes stay under the cap.
    for (const [width, height, dpr] of [[3440, 1440, 1], [2560, 1440, 1.5]]) {
      setGeometry(h, width, height, dpr);
      h.sandbox.drawRisingSparks(recorder().proxy, 1);
      const atlas = h.evaluate("risingSparkFullAtlas");
      assert.ok(atlas !== null, width + "x" + height + "@" + dpr + ": atlas baked");
      console.log("  S4d " + width + "x" + height + "@" + dpr + ": atlas " + atlas.canvas.width + "x" + atlas.canvas.height);
    }
  });
}

function registerMini(test, sceneDir) {
  test("S4c mini sparks are untouched (no full atlas, the reference stream)", () => {
    const h = sparkPage(sceneDir, { variant: "mini", width: 240, height: 240 });
    h.evaluate("risingSparkFullAtlasFor = () => { throw new Error('full atlas in mini'); }");
    for (const time of SPARK_TIMES) {
      const a = recorder(), b = recorder();
      h.sandbox.drawRisingSparks(a.proxy, time);
      h.sandbox.drawRisingSparksReference(b.proxy, time);
      assert.deepStrictEqual(json(a.ops), json(b.ops), "t=" + time);
      assert.strictEqual(count(a.ops, "drawImage"), 0);
    }
    h.tick(0);
    h.tick(1000);
    assert.strictEqual(h.evaluate("risingSparkFullAtlas"), null, "no atlas in mini");
    assert.deepStrictEqual(h.errors, [], "no caught render errors");
  });
}

function register(test, sceneDir) {
  registerPins(test, sceneDir);
  registerFull(test, sceneDir);
  registerMini(test, sceneDir);
}

module.exports = { register: register, stripS4c: stripS4c };

"use strict";

// Constellation-ring checks shared by idle-scene.tests.js and explorer-scene.tests.js. Both scenes
// ship their own copy of the same ring (CONSTELLATION_FIGURES and its drawing code in js/rings.js,
// its CONSTELLATION_* tunables in js/config.js), so the checks live here once and each harness
// calls them, instead of two pasted copies that could drift like the code they guard.
// ConstellationRingParityTests.cs separately keeps the two scenes' ring CODE identical.

const vm = require("vm");
const fs = require("fs");
const path = require("path");
const assert = require("assert");

// js/rings.js draws each edge as points[from] -> points[to], so an out-of-range index in the
// hand-written CONSTELLATION_FIGURES table would only surface as a TypeError while the ring bakes.
// `context` is a loaded page's vm context; `const` bindings are not properties of it, so they are
// read through the context itself.
function checkConstellationTable(context) {
  var figures = vm.runInContext("CONSTELLATION_FIGURES", context);
  var pool = vm.runInContext("CONSTELLATION_POOL", context);
  var ringSlots = vm.runInContext("CONSTELLATION_COUNT", context);

  assert.ok(figures.length > 0, "expected at least one constellation figure");
  figures.forEach(function (figure) {
    var starCount = figure.stars.length;
    assert.ok(starCount >= 2, figure.name + ": expected at least two stars, got " + starCount);
    var joined = new Array(starCount).fill(false);
    figure.edges.forEach(function (edge, e) {
      assert.strictEqual(edge.length, 2, figure.name + " edge " + e + ": expected [from, to]");
      edge.forEach(function (index) {
        assert.ok(Number.isInteger(index) && index >= 0 && index < starCount,
          figure.name + " edge " + e + ": star index " + index + " is outside 0.." + (starCount - 1));
        joined[index] = true;
      });
      assert.notStrictEqual(edge[0], edge[1], figure.name + " edge " + e + ": joins a star to itself");
    });
    var loose = joined.map(function (isJoined, i) { return isJoined ? -1 : i; }).filter(function (i) { return i >= 0; });
    assert.deepStrictEqual(loose, [], figure.name + ": stars with no edge: " + loose.join(", "));
  });

  assert.ok(ringSlots > 0, "expected the ring to hold at least one constellation");
  assert.strictEqual(pool.length, ringSlots, "expected one built figure per ring slot");
  pool.forEach(function (built, i) {
    var figure = figures[i % figures.length];
    assert.strictEqual(built.segments.length, figure.edges.length,
      "ring slot " + i + " (" + figure.name + "): expected one brush segment per edge");
  });
}

// Evaluates a scene's js/config.js on its own and returns every CONSTELLATION_* tunable by VALUE,
// so the comparison is immune to comments, line breaks, and semicolons inside strings. Only the
// declared names are found textually (line-start const/let/var, the only form config.js uses).
//
// config.js's only host dependency at load is the scene canvas:
//   const canvas = document.getElementById('scene');
//   const ctx = canvas.getContext('2d', ...);
// which makeConfigHostStub stands in for. A load failure cannot be reliably told apart as "the stub
// lacks something" versus "config.js itself is wrong" (a missing stub method is a TypeError, a typo
// in config.js is a ReferenceError), so no guess is made: the original error is reported as is,
// with the stub mentioned only as a possible cause.
var CONFIG_HOST_STUB_DESCRIPTION = "document.getElementById('scene').getContext()";
function makeConfigHostStub() {
  return {
    document: { getElementById: function () { return { getContext: function () { return {}; } }; } },
  };
}

function readConstellationTunables(configPath) {
  var source = fs.readFileSync(configPath, "utf8");
  var context = vm.createContext(makeConfigHostStub());
  try {
    vm.runInContext(source, context, { filename: configPath });
  } catch (error) {
    throw new Error(configPath + " failed to load for the tunables comparison: " + String(error) +
      ". If it now needs a host API beyond the stub (" + CONFIG_HOST_STUB_DESCRIPTION +
      "), extend makeConfigHostStub in constellation-ring.checks.js.", { cause: error });
  }

  var names = Array.from(source.matchAll(/^(?:const|let|var)\s+(CONSTELLATION_\w+)/gm), function (m) { return m[1]; });
  var values = {};
  // structuredClone re-creates each value in THIS realm: every vm context has its own
  // Array.prototype/Object.prototype, and deepStrictEqual compares prototypes, so array or object
  // tunables read straight from two contexts would never compare equal even with identical values.
  // Tunables are plain data. A function or symbol cannot be cloned, and the error names which
  // tunable instead of surfacing a bare DataCloneError. (A class instance does clone, into a plain
  // object without its prototype, which is still a fair by-value comparison for data.)
  names.forEach(function (name) {
    var value = vm.runInContext(name, context);
    try {
      values[name] = structuredClone(value);
    } catch (error) {
      throw new Error(configPath + ": " + name + " is not plain data (numbers, strings, arrays, objects), " +
        "so it cannot be compared by value. " + error.message, { cause: error });
    }
  });
  return values;
}

// The twin scene is expected as a sibling directory of sceneDir, which is how both scenes ship
// (Wallpaper/Web/idle and Wallpaper/Web/explorer).
function checkTunablesMatchTwin(sceneDir, twinScene) {
  var twinConfig = path.join(sceneDir, "..", twinScene, "js", "config.js");
  assert.ok(fs.existsSync(twinConfig),
    "expected the " + twinScene + " scene next to this one, at " + twinConfig);
  var own = readConstellationTunables(path.join(sceneDir, "js", "config.js"));
  var twin = readConstellationTunables(twinConfig);
  assert.ok(Object.keys(own).length > 0, "expected CONSTELLATION_* tunables in this scene's config.js");
  assert.deepStrictEqual(own, twin, "this scene's CONSTELLATION_* tunables differ from the " + twinScene + " scene's");
}

// mini-scene-window T2g/T2h: the constellation dots and brush-stroke lines (CONSTELLATION_DOT_RADIUS,
// CONSTELLATION_LINE_WIDTH) and the hieroglyph band's glyph stroke (HIEROGLYPH_STROKE_PX) are absolute px
// sizes, so in the ~6x smaller mini ring they read as blobs / dense hatching. Mini scales each by
// (that ring's thickness) / (its thickness in the full scene at the 3440x1440 reference screen), with a
// MINI_CONSTELLATION_MIN_PX floor. scene-wallpaper-scaling T5: the full variant scales them too below a
// 1080p screen, by min(1, thickness / its thickness at 1920x1080), with the same floor; at and above the
// 1080p ring thickness it keeps the exact tunables.

// The full-variant ring thickness at the 1920x1080 reference screen.
function wallpaperReferenceBox(full, thicknessFraction) {
  var reference = cfg(full, "WALLPAPER_CONSTELLATION_REFERENCE_SCREEN");
  return thicknessFraction * full.sandbox.sceneBasis(reference.width, reference.height);
}

function cfg(page, name) { return vm.runInContext(name, page.sandbox); }

// A recording 2D context that keeps a real save()/restore() stack of the current scale factor. Every
// arc()/lineWidth is recorded together with the scale in force, so a test can tell what was drawn
// inside a scale(boxSize) block (the dots: radius in box units) from arcs at scale 1 (boundary lines).
function makeRecordingContext() {
  var scaleFactor = 1;
  var stack = [];
  var arcs = [];
  var context = new Proxy({}, {
    get: function (target, prop) {
      if (prop === "save") return function () { stack.push(scaleFactor); };
      if (prop === "restore") return function () { scaleFactor = stack.length ? stack.pop() : 1; };
      if (prop === "scale") return function (sx) { scaleFactor *= sx; };
      if (prop === "arc") return function (x, y, r) { arcs.push({ radius: r, scale: scaleFactor }); };
      return function () {};
    },
    set: function () { return true; },
  });
  return { context: context, arcs: arcs };
}

// The dot radius in px that drawConstellationRing really draws: arcs recorded while the box scale
// (== boxSize) is in force. The circular boundary arcs are drawn after restore(), at scale 1.
function drawnDotRadiiPx(page, boxSize) {
  var rec = makeRecordingContext();
  page.sandbox.drawConstellationRing(rec.context, 0, 0, 100, 100 + boxSize, 0);
  return rec.arcs
    .filter(function (a) { return Math.abs(a.scale - boxSize) < 1e-6; }) // (100 + boxSize) - 100 is not exactly boxSize
    .map(function (a) { return a.radius * a.scale; });
}

// Every glyph one ring draw paints: the blackletter script glyphs (js/glyphs.js's drawScriptGlyph) and any stroked
// hieroglyph (drawHieroglyph) that should no longer appear on a lettered ring.
function recordGlyphDraws(page, draw) {
  var script = [];
  var strokes = 0;
  var originalScript = page.sandbox.drawScriptGlyph;
  var originalStroke = page.sandbox.drawHieroglyph;
  page.sandbox.drawScriptGlyph = function (context, glyph, x, y, size, ringAngle, color, stretchY) {
    script.push({ glyph: glyph, x: x, y: y, size: size, stretchY: stretchY === undefined ? 1 : stretchY });
  };
  page.sandbox.drawHieroglyph = function () { strokes++; };
  try {
    draw(makeRecordingContext().context);
  } finally {
    page.sandbox.drawScriptGlyph = originalScript;
    page.sandbox.drawHieroglyph = originalStroke;
  }
  return { script: script, strokes: strokes };
}

function checkConstellationDetailScale(loadPage) {
  var mini = loadPage({ innerWidth: 288, innerHeight: 288, search: "?variant=mini" });
  var full = loadPage({ innerWidth: 1000, innerHeight: 500 });
  var dot = cfg(full, "CONSTELLATION_DOT_RADIUS");
  var line = cfg(full, "CONSTELLATION_LINE_WIDTH");
  var floor = cfg(full, "MINI_CONSTELLATION_MIN_PX");
  var thickness = cfg(full, "CONSTELLATION_RING_OUTER_RADIUS_FRACTION") - cfg(full, "CONSTELLATION_RING_INNER_RADIUS_FRACTION");
  var reference = cfg(full, "MINI_CONSTELLATION_REFERENCE_SCREEN");

  // the ring thickness in the mini window, and in the full scene at the reference screen
  var miniBox = thickness * mini.sandbox.miniSceneBasis(288, 288);
  var referenceBox = thickness * full.sandbox.sceneBasis(reference.width, reference.height);
  var ratio = miniBox / referenceBox;
  assert.ok(ratio > 0 && ratio < 1, "the mini ring must be smaller than the reference ring, ratio " + ratio);

  // below the floor: the 288px window (dot*ratio and line*ratio are both under it)
  assert.ok(dot * ratio < floor && line * ratio < floor, "expected the 288px mini ring to sit under the floor");
  var sizes = mini.sandbox.constellationDetailSizes(miniBox);
  assert.strictEqual(sizes.dotRadius, floor, "mini dot radius must be the floor " + floor + ", got " + sizes.dotRadius);
  assert.strictEqual(sizes.lineWidth, floor, "mini line width must be the floor " + floor + ", got " + sizes.lineWidth);
  var tiny = mini.sandbox.constellationDetailSizes(1);
  assert.ok(tiny.dotRadius >= floor && tiny.lineWidth >= floor, "a tiny ring must never drop under the floor, got " + JSON.stringify(tiny));

  // ABOVE the floor: a ring 90% of the reference ring, where the proportional branch decides the value
  var bigBox = referenceBox * 0.9;
  var bigRatio = bigBox / referenceBox;
  assert.ok(dot * bigRatio > floor && line * bigRatio > floor, "expected the big ring to be above the floor");
  var big = mini.sandbox.constellationDetailSizes(bigBox);
  assert.ok(Math.abs(big.dotRadius - dot * bigRatio) < 1e-9, "proportional dot radius " + big.dotRadius + ", expected " + dot * bigRatio);
  assert.ok(Math.abs(big.lineWidth - line * bigRatio) < 1e-9, "proportional line width " + big.lineWidth + ", expected " + line * bigRatio);
  var bigDrawn = drawnDotRadiiPx(mini, bigBox);
  assert.ok(bigDrawn.length > 0, "expected dots to be drawn in the above-floor ring (an empty list would pass vacuously)");
  bigDrawn.forEach(function (r) {
    assert.ok(Math.abs(r - dot * bigRatio) < 1e-9, "drawn dot radius " + r + ", expected " + dot * bigRatio);
  });

  // the dots actually drawn in the 288px ring use the floored radius
  var drawn = drawnDotRadiiPx(mini, miniBox);
  assert.ok(drawn.length > 0, "expected constellation dots to be drawn");
  drawn.forEach(function (r) { assert.ok(Math.abs(r - sizes.dotRadius) < 1e-9, "drawn dot radius " + r + ", expected " + sizes.dotRadius); });

  // full: unchanged at and above the 1080p ring, proportional (with the floor) below it
  var wallBox = wallpaperReferenceBox(full, thickness);
  [wallBox, referenceBox].forEach(function (box) {
    var f = full.sandbox.constellationDetailSizes(box);
    assert.strictEqual(f.dotRadius, dot, "the full dot radius must stay " + dot);
    assert.strictEqual(f.lineWidth, line, "the full line width must stay " + line);
  });
  [40, wallBox * 0.5].forEach(function (box) {
    var f = full.sandbox.constellationDetailSizes(box);
    var r = box / wallBox;
    assert.ok(Math.abs(f.dotRadius - Math.max(floor, dot * r)) < 1e-9, "full dot radius " + f.dotRadius + " at box " + box);
    assert.ok(Math.abs(f.lineWidth - Math.max(floor, line * r)) < 1e-9, "full line width " + f.lineWidth + " at box " + box);
  });
  var fullDrawn = drawnDotRadiiPx(full, referenceBox);
  assert.ok(fullDrawn.length > 0);
  fullDrawn.forEach(function (r) { assert.ok(Math.abs(r - dot) < 1e-9, "the full drawn dot radius must stay " + dot + ", got " + r); });
}

// CielScenes blackletter script rings: the hieroglyph band, the ruler numerals, the paragraph ring and the outer glyph
// ring draw the script glyphs (never stroked hieroglyphs) in mini and full; the band's glyphs scale with the band,
// the paragraph ring has four rows and the outer ring keeps its radial stretch.
function checkLetteredRingsScript(loadPage) {
  [loadPage({ innerWidth: 288, innerHeight: 288, search: "?variant=mini" }), loadPage({ innerWidth: 1000, innerHeight: 500 })]
    .forEach(function (page) {
      var sb = page.sandbox;
      var inkHeight = 2 * cfg(page, "GLYPH_LOCAL_HALF_EXTENT");
      var scriptPool = cfg(page, "SCRIPT_TEXT_POOL");
      var numerals = cfg(page, "RULER_NUMERAL_POOL");
      var rows = cfg(page, "HIEROGLYPH_BAND_ROW_COUNT");
      var rings = {
        hieroglyph: [function (c) { sb.drawHieroglyphBand(c, 0, 0, 100, 140, 0); }, scriptPool],
        ruler: [function (c) { sb.drawRulerRing(c, 0, 0, 100, 0); }, numerals],
        paragraph: [function (c) { sb.drawParagraphRing(c, 0, 0, 100, 0); }, scriptPool],
        outer: [function (c) { sb.drawOuterGlyphRing(c, 0, 0, 100, 0); }, scriptPool],
      };
      var drawn = {};
      Object.keys(rings).forEach(function (name) {
        var rec = recordGlyphDraws(page, rings[name][0]);
        assert.strictEqual(rec.strokes, 0, name + ": no stroked hieroglyph may be drawn");
        assert.ok(rec.script.length > 0, name + ": expected script glyphs to be drawn");
        rec.script.forEach(function (call) {
          assert.ok(rings[name][1].indexOf(call.glyph) >= 0, name + ": every glyph must come from its script pool");
        });
        drawn[name] = rec.script;
      });
      drawn.hieroglyph.forEach(function (call) {
        var expected = (40 / (rows + 1)) * 0.72 * inkHeight;
        assert.ok(Math.abs(call.size - expected) < 1e-9, "band glyph size " + call.size + ", expected " + expected);
      });
      var half = recordGlyphDraws(page, function (c) { sb.drawHieroglyphBand(c, 0, 0, 100, 120, 0); }).script;
      assert.ok(Math.abs(half[0].size * 2 - drawn.hieroglyph[0].size) < 1e-9, "band glyphs must scale with the band");
      var radii = {};
      drawn.paragraph.forEach(function (call) { radii[Math.round(Math.hypot(call.x, call.y) * 1e6)] = true; });
      assert.strictEqual(Object.keys(radii).length, 4, "the paragraph ring must draw four rows");
      assert.strictEqual(cfg(page, "PARAGRAPH_RING_ROW_COUNT"), 4);
      var stretch = cfg(page, "OUTER_GLYPH_RADIAL_STRETCH");
      drawn.outer.forEach(function (call) { assert.strictEqual(call.stretchY, stretch, "the outer ring keeps its radial stretch"); });
    });
}

// Every constellation is fitted to the same height (the ring's radial direction, local y): CONSTELLATION_FIGURE_MARGIN,
// so flat figures such as Cassiopeia span the ring like the tall ones; widths never exceed that margin.
function checkConstellationHeights(context) {
  var pool = vm.runInContext("CONSTELLATION_POOL", context);
  var margin = vm.runInContext("CONSTELLATION_FIGURE_MARGIN", context);
  var figures = vm.runInContext("CONSTELLATION_FIGURES", context);
  pool.forEach(function (figure, i) {
    var xs = figure.points.map(function (p) { return p[0]; });
    var ys = figure.points.map(function (p) { return p[1]; });
    var name = figures[i % figures.length].name;
    var height = Math.max.apply(null, ys) - Math.min.apply(null, ys);
    var width = Math.max.apply(null, xs) - Math.min.apply(null, xs);
    assert.ok(Math.abs(height - margin) < 1e-9, name + ": expected height " + margin + ", got " + height);
    assert.ok(width <= margin + 1e-9, name + ": width " + width + " exceeds " + margin);
  });
}

// mini-scene-window T2i: in mini every ring band gets an occluding dark base so text from windows behind the
// topmost mini window cannot read through the bands. The base is drawn under the finished frame
// (destination-over, so it never tints or dims what is drawn) and covers exactly each band's own annulus, so
// the gaps between rings and everything outside the disc stay see-through.
function checkMiniRingBase(loadPage) {
  var mini = loadPage({ innerWidth: 288, innerHeight: 288, search: "?variant=mini" });
  var sb = mini.sandbox;
  var basis = sb.miniSceneBasis(288, 288);
  var color = cfg(mini, "MINI_RING_BASE_COLOR");
  var alpha = Number(/,\s*([\d.]+)\)$/.exec(color)[1]);
  assert.ok(alpha >= 0.85 && alpha <= 0.95, "the base alpha must hide text behind it (0.85-0.95), got " + alpha);

  var arcs = [];
  var ops = [];
  var fills = [];
  var state = { op: "source-over", style: null };
  var context = new Proxy({}, {
    get: function (target, prop) {
      if (prop === "arc") return function (x, y, r, start, end, counterclockwise) { arcs.push({ x: x, y: y, r: r, ccw: counterclockwise === true }); };
      if (prop === "fill") return function () { fills.push({ op: state.op, style: state.style }); };
      if (prop === "globalCompositeOperation") return state.op;
      if (prop === "fillStyle") return state.style;
      return function () {};
    },
    set: function (target, prop, value) {
      if (prop === "globalCompositeOperation") state.op = value;
      if (prop === "fillStyle") state.style = value;
      return true;
    },
  });
  sb.drawMiniRingBases(context, 144, 144, basis);

  assert.ok(fills.length >= 1, "expected the base to be filled");
  fills.forEach(function (f) {
    assert.strictEqual(f.op, "destination-over", "the base must be drawn destination-over (beneath the frame), got " + f.op);
    assert.strictEqual(f.style, color, "the base must use MINI_RING_BASE_COLOR");
  });
  arcs.forEach(function (a) { assert.ok(a.x === 144 && a.y === 144, "the base annuli must be centered on the ring"); });

  // arcs come as (outer, inner) pairs, one pair per band
  assert.strictEqual(arcs.length % 2, 0, "expected outer/inner arc pairs");
  // Winding is what cuts the hole: the outer circle clockwise and the inner one counter-clockwise, so the
  // single non-zero fill leaves each band's inside (the gap to the next ring) see-through.
  for (var w = 0; w < arcs.length; w += 2) {
    assert.strictEqual(arcs[w].ccw, false, "the outer circle of band " + w / 2 + " must wind clockwise");
    assert.strictEqual(arcs[w + 1].ccw, true, "the inner circle of band " + w / 2 + " must wind counter-clockwise (it cuts the hole)");
  }
  var annuli = [];
  for (var i = 0; i < arcs.length; i += 2) annuli.push([arcs[i + 1].r, arcs[i].r]);
  annuli.sort(function (a, b) { return a[0] - b[0]; });

  var rings = cfg(mini, "RING_ANIMATIONS");
  var expected = rings.map(function (r) { return [basis * r.cacheInnerFrac, basis * r.cacheOuterFrac]; });
  expected.push([basis * cfg(mini, "DISC_BORDER_INNER_RADIUS_FRACTION"), basis * cfg(mini, "DISC_BORDER_OUTER_RADIUS_FRACTION")]);
  expected.sort(function (a, b) { return a[0] - b[0]; });
  // the hollow around the planet, out to the first ring, is part of the disc too (text must not read there)
  expected.unshift([0, expected[0][0]]);
  assert.strictEqual(annuli.length, expected.length, "expected the planet hollow, one base annulus per ring band and the disc border");
  annuli.forEach(function (a, k) {
    assert.ok(Math.abs(a[0] - expected[k][0]) < 1e-6 && Math.abs(a[1] - expected[k][1]) < 1e-6,
      "base annulus " + a + " must equal the band " + expected[k]);
    assert.ok(a[0] < a[1], "each base must be a real annulus (or the planet hollow disc)");
  });
  // Never inside the planet/inner ring and never outside the disc.
  assert.strictEqual(annuli[0][0], 0, "the innermost base is the disc around the planet, from the center");
  assert.ok(annuli[annuli.length - 1][1] <= basis * cfg(mini, "DISC_BORDER_OUTER_RADIUS_FRACTION") + 1e-6, "no base outside the disc");
  // gaps stay open: the bands are disjoint, so a radius between two non-touching bands is in no annulus
  var gaps = 0;
  for (var g = 0; g + 1 < annuli.length; g++) {
    assert.ok(annuli[g][1] <= annuli[g + 1][0] + 1e-6, "base annuli must not overlap");
    if (annuli[g + 1][0] - annuli[g][1] > 1e-6) gaps++;
  }
  assert.ok(gaps >= 1, "expected at least one see-through gap between the ring bases");
}

module.exports = {
  checkMiniRingBase: checkMiniRingBase,
  checkConstellationTable: checkConstellationTable,
  checkTunablesMatchTwin: checkTunablesMatchTwin,
  checkConstellationDetailScale: checkConstellationDetailScale,
  checkLetteredRingsScript: checkLetteredRingsScript,
  checkConstellationHeights: checkConstellationHeights,
};

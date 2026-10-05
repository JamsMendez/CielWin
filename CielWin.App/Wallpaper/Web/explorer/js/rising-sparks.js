// html-wallpaper-demo D6a: copied verbatim from docs/great-sage/background-explorer/js/rising-sparks.js
// (reference-only, excluded from git -- see the feature doc, "Source material"). Not restyled:
// only this header comment was added, the source own header/body follow unchanged.

// rising-sparks.js — the explorer variant's blue layer and its field of rising star sparks.
// Loads after config.js and math.js (uses mulberry32, clamp01, smoothstep, mix). Stateless:
// every spark is derived from (slot index, time), so the field loops forever with no bookkeeping.
//
// Each of RISING_SPARK_COUNT slots owns a fixed respawn period. Within one period the slot's spark
// lives for `lifetime` seconds, then rests briefly and is replaced by a fresh spark whose origin,
// speed, and incline are rehashed from (slot, cycle). The spark rises at a constant speed; its
// horizontal velocity holds steady (an almost straight, inclined path) until BEND_START, then
// eases to zero by BEND_END so the path bends to straight up before the spark fades out.

function risingSparkRandom(index, cycle) {
  return mulberry32((RISING_SPARK_SEED ^ Math.imul(index + 1, 0x9e3779b1) ^ Math.imul(cycle + 1, 0x85ebca6b)) >>> 0);
}

function risingSparkAt(index, timeSeconds, width, height) {
  const slotRandom = risingSparkRandom(index, -1);
  const lifetime = mix(RISING_SPARK_LIFETIME_MIN_SECONDS, RISING_SPARK_LIFETIME_MAX_SECONDS, slotRandom());
  const period = lifetime * (1 + RISING_SPARK_REST_FRACTION);
  const localTime = timeSeconds + slotRandom() * period; // stagger slots so they never pulse together
  const cycle = Math.floor(localTime / period);
  const age = localTime - cycle * period;

  const random = risingSparkRandom(index, cycle);
  const x0 = random();
  const y0 = mix(RISING_SPARK_SPAWN_TOP_FRACTION, 1.02, random());
  const offCenter = (0.5 - x0) * 2; // -1 at the right edge, +1 at the left edge
  const tilt = Math.sign(offCenter || 1)
    * (RISING_SPARK_TILT_BASE + RISING_SPARK_TILT_EDGE * Math.abs(offCenter))
    * mix(0.8, 1, random());
  const bendJitter = (random() - 0.5) * 0.1;
  return {
    index,
    cycle,
    age,
    lifetime,
    period,
    x0,
    y0,
    speed: mix(RISING_SPARK_SPEED_MIN, RISING_SPARK_SPEED_MAX, random()),
    tilt,
    bendStart: RISING_SPARK_BEND_START + bendJitter,
    bendEnd: RISING_SPARK_BEND_END + bendJitter,
    size: mix(0.6, 1.4, random()),
    brightness: mix(0.45, 1, random()),
  };
}

// Normalized horizontal travel at life fraction u: the integral of (1 - smoothstep(a, b, s)) ds
// from 0 to u, i.e. full-speed drift before the bend, eased drift through it, none after.
function risingSparkDrift(u, a, b) {
  const span = b - a;
  const t = clamp01((u - a) / span);
  const easedAway = span * (t * t * t - (t * t * t * t) / 2) + Math.max(0, u - b);
  return u - easedAway;
}

function risingSparkPosition(spark, age, width, height) {
  const rise = spark.speed * height * age;
  const u = age / spark.lifetime;
  const drift = spark.tilt * spark.speed * height * spark.lifetime * risingSparkDrift(u, spark.bendStart, spark.bendEnd);
  return { x: spark.x0 * width + drift, y: spark.y0 * height - rise };
}

function risingSparkEnvelope(ageFraction) {
  return smoothstep(0, 0.1, ageFraction) * (1 - smoothstep(0.55, 1, ageFraction));
}

const RISING_SPARK_TRAIL_SAMPLES = 6;

// Scene optimization begin (S4c): odd/tasks/scene-optimizations.md (ported from CieLinux 4574b4a W1 spark atlas,
// adapted: CielWin has no mini spark atlas (CieLinux O1-O3) to build on, so the few shared terms it used live
// here, and the mini page keeps drawing every spark with the reference strokes). On the full wallpaper a mature
// spark (at least RISING_SPARK_TRAIL_SECONDS old, so its trail samples are evenly spaced in time) is drawn as its
// two trail halves (samples 0-3 and 3-6) stamped from a pre-baked atlas instead of 6 strokes + 1 head fill, each
// a per-frame anti-aliased path. Every cell keeps the reference's additive brightness: half 0 (segment alphas
// 1/6..1/2, overlapping caps sum to < 1) is baked at unit alpha; half 1 (segments 4..6 plus the head, whose
// overlaps sum up to 2) is baked at half alpha so nothing clamps in the bake, then stamped once at globalAlpha 2a
// (a <= 0.5) or twice at a. Each half is placed on its chord; its inner samples sit off the analytic trail by a
// fraction of a device px (bounded in rising-spark-atlas.checks.js). Buckets: half lengths every 0.25 device px
// (CieLinux: 0.5) and spark sizes every 0.05. Cells are laid out in rows at most 4096 device px wide. Young sparks
// keep the strokes. Sprites are placed in the context's own transform, so the alert see-through hook (a
// translated tile layer) and the S4b spark layer get the same streaks. The atlas is rebuilt only when W, H or DPR
// change.
const RISING_SPARK_FULL_SPRITE_PAD = 3; // CSS px around the streak: head radius 1.82 plus anti-aliasing
const RISING_SPARK_FULL_SIZE_MIN = 0.6; // spark.size = mix(0.6, 1.4, random()) in risingSparkAt
const RISING_SPARK_FULL_SIZE_MAX = 1.4;
const RISING_SPARK_FULL_SIZE_STEP = 0.05;
const RISING_SPARK_FULL_LENGTH_STEP_DEVICE_PX = 0.25;
const RISING_SPARK_FULL_HALF_GAIN = [1, 0.5]; // bake gain per trail half (see above)
const RISING_SPARK_FULL_ATLAS_MAX_WIDTH = 4096; // device px
const risingSparkFullTrail = new Float64Array(6); // trail samples 0, 3 and 6 (x, y)
let risingSparkFullAtlas = null;

function risingSparkFullAtlasFor(width, height, dpr) {
  const cached = risingSparkFullAtlas;
  if (cached !== null && cached.width === width && cached.height === height && cached.dpr === dpr) return cached;
  // A trail half spans half the trail seconds: its vertical extent is exactly speed * height * halfSeconds, and
  // its horizontal extent at most |tilt| times that (the drift rate never exceeds the tilt).
  const halfSeconds = RISING_SPARK_TRAIL_SECONDS / 2;
  const lengthMin = RISING_SPARK_SPEED_MIN * height * halfSeconds;
  const lengthMax = RISING_SPARK_SPEED_MAX * height * halfSeconds * Math.hypot(1, RISING_SPARK_TILT_BASE + RISING_SPARK_TILT_EDGE);
  const lengthStep = RISING_SPARK_FULL_LENGTH_STEP_DEVICE_PX / dpr;
  const lengthCount = Math.ceil((lengthMax - lengthMin) / lengthStep) + 1;
  const sizeCount = Math.round((RISING_SPARK_FULL_SIZE_MAX - RISING_SPARK_FULL_SIZE_MIN) / RISING_SPARK_FULL_SIZE_STEP) + 1;
  const lengths = new Float64Array(lengthCount), sizes = new Float64Array(sizeCount);
  for (let col = 0; col < lengthCount; col++) lengths[col] = lengthMin + col * lengthStep;
  for (let k = 0; k < sizeCount; k++) sizes[k] = RISING_SPARK_FULL_SIZE_MIN + k * RISING_SPARK_FULL_SIZE_STEP;
  const pad = RISING_SPARK_FULL_SPRITE_PAD;
  const cellWidth = Math.ceil((lengths[lengthCount - 1] + 2 * pad) * dpr);
  const cellHeight = Math.ceil(2 * pad * dpr);
  const perRow = Math.max(1, Math.floor(RISING_SPARK_FULL_ATLAS_MAX_WIDTH / cellWidth));
  const cellCount = sizeCount * 2 * lengthCount;
  const canvas = document.createElement('canvas');
  canvas.width = Math.min(cellCount, perRow) * cellWidth;
  canvas.height = Math.ceil(cellCount / perRow) * cellHeight;
  const bake = canvas.getContext('2d');
  bake.globalCompositeOperation = 'lighter';
  bake.lineCap = 'round';
  // Cell index (sizeIndex * 2 + half) * lengthCount + col: the half's tail at (pad, pad), its head end at
  // (pad + length, pad). Segment s uses the reference's t = s / RISING_SPARK_TRAIL_SAMPLES and mix(0.4, 1, t).
  for (let sizeIndex = 0; sizeIndex < sizeCount; sizeIndex++) {
    const sparkWidth = RISING_SPARK_WIDTH * sizes[sizeIndex];
    for (let half = 0; half < 2; half++) {
      const gain = RISING_SPARK_FULL_HALF_GAIN[half];
      for (let col = 0; col < lengthCount; col++) {
        const cell = (sizeIndex * 2 + half) * lengthCount + col;
        const length = lengths[col];
        bake.setTransform(dpr, 0, 0, dpr, (cell % perRow) * cellWidth, Math.floor(cell / perRow) * cellHeight);
        for (let k = 1; k <= 3; k++) {
          const t = (half * 3 + k) / RISING_SPARK_TRAIL_SAMPLES;
          bake.strokeStyle = `rgba(${RISING_SPARK_COLOR}, ${t * gain})`;
          bake.lineWidth = sparkWidth * mix(0.4, 1, t);
          bake.beginPath();
          bake.moveTo(pad + (k - 1) / 3 * length, pad);
          bake.lineTo(pad + k / 3 * length, pad);
          bake.stroke();
        }
        if (half === 1) {
          bake.fillStyle = `rgba(255, 255, 255, ${gain})`;
          bake.beginPath();
          bake.arc(pad + length, pad, RISING_SPARK_HEAD_RADIUS * sizes[sizeIndex], 0, TAU);
          bake.fill();
        }
      }
    }
  }
  risingSparkFullAtlas = { canvas, width, height, dpr, pad, cellWidth, cellHeight, perRow,
    lengthMin, lengthStep, lengthCount, lengths,
    sizeMin: RISING_SPARK_FULL_SIZE_MIN, sizeStep: RISING_SPARK_FULL_SIZE_STEP, sizeCount, sizes };
  return risingSparkFullAtlas;
}

// The context's transform when drawRisingSparks starts (the scene's device scale, the S4b layer's, or the alert
// hook's tile translation on top of it). A context without getTransform falls back to the scene scale.
function risingSparkBaseTransform(context) {
  const m = typeof context.getTransform === 'function' ? context.getTransform() : null;
  return m && typeof m.a === 'number' ? { a: m.a, b: m.b, c: m.c, d: m.d, e: m.e, f: m.f }
    : { a: canvasScaleX, b: 0, c: 0, d: canvasScaleY, e: 0, f: 0 };
}

// Trail samples 0, 3 and 6 of a spark, with the reference loop's sample-age expression.
function risingSparkFullTrailOf(spark) {
  const trail = risingSparkFullTrail;
  for (let k = 0; k < 3; k++) {
    const t = (k * 3) / RISING_SPARK_TRAIL_SAMPLES;
    const point = risingSparkPosition(spark, Math.max(0, spark.age - RISING_SPARK_TRAIL_SECONDS * (1 - t)), W, H);
    trail[k * 2] = point.x;
    trail[k * 2 + 1] = point.y;
  }
  return trail;
}

// Draws one mature spark as its two trail-half sprites: each is rotated onto its chord (samples 0-3 and 3-6) and
// centered on the chord midpoint, in the base transform.
function drawRisingSparkFullSprites(context, atlas, base, trail, size, alpha) {
  let sizeIndex = Math.round((size - atlas.sizeMin) / atlas.sizeStep);
  sizeIndex = sizeIndex < 0 ? 0 : (sizeIndex >= atlas.sizeCount ? atlas.sizeCount - 1 : sizeIndex);
  const drawWidth = atlas.cellWidth / atlas.dpr, drawHeight = atlas.cellHeight / atlas.dpr;
  for (let half = 0; half < 2; half++) {
    const o = half * 2;
    const ax = trail[o], ay = trail[o + 1], bx = trail[o + 2], by = trail[o + 3];
    const dx = bx - ax, dy = by - ay, length = Math.sqrt(dx * dx + dy * dy);
    const cos = length > 0 ? dx / length : 0, sin = length > 0 ? dy / length : -1;
    let col = Math.round((length - atlas.lengthMin) / atlas.lengthStep);
    col = col < 0 ? 0 : (col >= atlas.lengthCount ? atlas.lengthCount - 1 : col); // never hit: the range is analytic
    const cell = (sizeIndex * 2 + half) * atlas.lengthCount + col;
    const sx = (cell % atlas.perRow) * atlas.cellWidth, sy = Math.floor(cell / atlas.perRow) * atlas.cellHeight;
    const mx = (ax + bx) / 2, my = (ay + by) / 2;
    context.setTransform(base.a * cos + base.c * sin, base.b * cos + base.d * sin,
      base.c * cos - base.a * sin, base.d * cos - base.b * sin,
      base.a * mx + base.c * my + base.e, base.b * mx + base.d * my + base.f);
    // Half 1 is baked at half alpha: 2a in one stamp when that fits globalAlpha, else two stamps at a.
    const stamps = half === 1 && alpha > 0.5 ? 2 : 1;
    context.globalAlpha = half === 1 && stamps === 1 ? alpha * 2 : alpha;
    for (let stamp = 0; stamp < stamps; stamp++) {
      context.drawImage(atlas.canvas, sx, sy, atlas.cellWidth, atlas.cellHeight,
        -(atlas.pad + atlas.lengths[col] / 2), -atlas.pad, drawWidth, drawHeight);
    }
  }
}
// Scene optimization end (S4c).

function drawRisingSparks(context, timeSeconds) {
  // Scene optimization begin (S4c): the full wallpaper stamps mature sparks from the atlas (see above).
  const fullAtlas = typeof isMiniVariant !== 'undefined' && isMiniVariant ? null : risingSparkFullAtlasFor(W, H, DPR);
  const fullBase = fullAtlas === null ? null : risingSparkBaseTransform(context);
  // The strokes inherit the context's alpha (1 in every caller); sprites set globalAlpha, so they carry it.
  const fullBaseAlpha = fullAtlas === null || typeof context.globalAlpha !== 'number' ? 1 : context.globalAlpha;
  let fullSpriteState = false;
  // Scene optimization end (S4c).
  context.save();
  context.globalCompositeOperation = 'lighter';
  context.lineCap = 'round';
  for (let i = 0; i < RISING_SPARK_COUNT; i++) {
    const spark = risingSparkAt(i, timeSeconds, W, H);
    if (spark.age > spark.lifetime) continue;
    const alpha = risingSparkEnvelope(spark.age / spark.lifetime) * spark.brightness;
    if (alpha <= 0.01) continue;
    // Scene optimization begin (S4c): two half sprites per mature spark; a young spark that follows sprites first
    // restores the base transform and alpha its strokes expect.
    if (fullAtlas !== null) {
      if (spark.age >= RISING_SPARK_TRAIL_SECONDS) {
        drawRisingSparkFullSprites(context, fullAtlas, fullBase, risingSparkFullTrailOf(spark), spark.size, alpha * fullBaseAlpha);
        fullSpriteState = true;
        continue;
      }
      if (fullSpriteState) {
        context.setTransform(fullBase.a, fullBase.b, fullBase.c, fullBase.d, fullBase.e, fullBase.f);
        context.globalAlpha = fullBaseAlpha;
        fullSpriteState = false;
      }
    }
    // Scene optimization end (S4c).

    // Streak: a tapered polyline through the spark's recent positions, so the tail follows the
    // bend instead of cutting straight across it.
    let previous = risingSparkPosition(spark, Math.max(0, spark.age - RISING_SPARK_TRAIL_SECONDS), W, H);
    for (let s = 1; s <= RISING_SPARK_TRAIL_SAMPLES; s++) {
      const t = s / RISING_SPARK_TRAIL_SAMPLES;
      const sampleAge = Math.max(0, spark.age - RISING_SPARK_TRAIL_SECONDS * (1 - t));
      const point = risingSparkPosition(spark, sampleAge, W, H);
      context.strokeStyle = `rgba(${RISING_SPARK_COLOR}, ${alpha * t})`;
      context.lineWidth = RISING_SPARK_WIDTH * spark.size * mix(0.4, 1, t);
      context.beginPath();
      context.moveTo(previous.x, previous.y);
      context.lineTo(point.x, point.y);
      context.stroke();
      previous = point;
    }

    context.fillStyle = `rgba(255, 255, 255, ${alpha})`;
    context.beginPath();
    context.arc(previous.x, previous.y, RISING_SPARK_HEAD_RADIUS * spark.size, 0, TAU);
    context.fill();
  }
  context.restore();
}

// mini-scene-window T2: the mini variant's blue ring. drawBlueLayer's 'color' fill paints an opaque
// blue wherever the backdrop is transparent (and its 'screen' glow paints the whole canvas), which
// would fill the see-through window; 'source-atop' only tints pixels the ring already drew.
function drawBlueRingTint(context) {
  context.save();
  context.globalCompositeOperation = 'source-atop';
  context.globalAlpha = MINI_BLUE_TINT_ALPHA;
  context.fillStyle = BLUE_LAYER_TINT_COLOR;
  context.fillRect(0, 0, W, H);
  context.restore();
}

// Scene optimization begin (S1): odd/tasks/scene-optimizations.md (ported from CieLinux W1). The blue layer's
// glow gradient only depends on (W, H, cx, cy); it is created once per (context, geometry) and reused, like the
// vignette's (rings.js cachedVignetteGradient).
let blueLayerGlowCache = { context: null, width: -1, height: -1, cx: NaN, cy: NaN, gradient: null };

function cachedBlueLayerGlow(context, cx, cy) {
  const cache = blueLayerGlowCache;
  if (cache.context === context && cache.width === W && cache.height === H && cache.cx === cx && cache.cy === cy) return cache.gradient;
  const radius = Math.max(W, H) * BLUE_LAYER_GLOW_RADIUS_FRACTION;
  const glow = context.createRadialGradient(cx, cy, 0, cx, cy, radius);
  glow.addColorStop(0, BLUE_LAYER_GLOW_COLOR);
  glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
  blueLayerGlowCache = { context, width: W, height: H, cx, cy, gradient: glow };
  return glow;
}
// Scene optimization end (S1).

// Blue wash over the finished monochrome composition: 'color' keeps each pixel's luminance but
// takes the tint's hue/saturation, then a centered 'screen' glow brightens the middle.
function drawBlueLayer(context, cx, cy) {
  // Scene optimization begin (S1): cached glow gradient (see cachedBlueLayerGlow).
  if (typeof context.createRadialGradient === 'function') {
    context.save();
    context.globalCompositeOperation = 'color';
    context.fillStyle = BLUE_LAYER_TINT_COLOR;
    context.fillRect(0, 0, W, H);
    context.globalCompositeOperation = 'screen';
    context.fillStyle = cachedBlueLayerGlow(context, cx, cy);
    context.fillRect(0, 0, W, H);
    context.restore();
    return;
  }
  // Scene optimization end (S1).
  context.save();
  context.globalCompositeOperation = 'color';
  context.fillStyle = BLUE_LAYER_TINT_COLOR;
  context.fillRect(0, 0, W, H);

  context.globalCompositeOperation = 'screen';
  const radius = Math.max(W, H) * BLUE_LAYER_GLOW_RADIUS_FRACTION;
  const glow = context.createRadialGradient(cx, cy, 0, cx, cy, radius);
  glow.addColorStop(0, BLUE_LAYER_GLOW_COLOR);
  glow.addColorStop(1, 'rgba(0, 0, 0, 0)');
  context.fillStyle = glow;
  context.fillRect(0, 0, W, H);
  context.restore();
}

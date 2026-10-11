// Regenerates CielWin's tray/app icon: the Raphael scene in its mini variant
// (raphael/index.html?variant=mini, what WebViewAlertLayerController.SceneUrl(Raphael, "mini") builds),
// figure only, on a transparent background.
//
// Usage (from the repo root, Windows, Microsoft Edge installed, node >= 22 for the global WebSocket):
//   node tools/tray-icon/render-raphael-mini.mjs
// Optional: EDGE=<path to msedge.exe>, ICON_FRAME_MS=<scene clock in ms, default 4000>.
//
// Writes:
//   CielWin.App/Assets/raphael-mini.png  256x256 master, straight alpha (the 256 frame of the .ico)
//   CielWin.App/Assets/raphael-mini.ico  16, 20, 24, 32, 48 (32-bit BMP + AND mask) and 256 (PNG)
//
// Tray sizes (16, 20, 24) get a contrast pass after resampling (trayContrast): at those sizes the figure
// averages into a pale, half-transparent gold disc that washes out on a light taskbar, so their alpha is
// raised, the gold deepened and a thin dark outline drawn around the silhouette. 32 and up are untouched.
//
// The scene sources are the shared CielScenes submodule and are not touched. The page ships a qrc:-only
// CSP (CieLinux's Qt host), so the capture bypasses CSP through the DevTools Protocol, the file:// stand-in
// for SceneWebServer's CSP rewrite. Three layers sit behind the figure and are dropped by a script
// injected before the page loads:
//   - the opaque black mini root (styles.css, html.scene-mini, CieLinux's luma key): made transparent
//     with an injected stylesheet, the same rule SceneWebServer.HostStyle injects in the app;
//   - #nebula, the WebGL gold haze canvas: hidden by the same stylesheet;
//   - drawMiniSceneBase (raphael/js/render-loop.js), the 0.9-alpha dark disc the mini window paints under
//     its rings so desktop text does not read through: replaced with a no-op before the first frame.
// requestAnimationFrame is replaced by a manual queue so the captured frame is a fixed point on the
// scene clock (ICON_FRAME_MS) instead of whenever the screenshot happens to land. Edge runs headless
// with GPU disabled (software raster) and a transparent default background.

import { spawn } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { deflateSync, inflateSync } from "node:zlib";

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, "..", "..");
const scenePage = join(repo, "CielWin.App", "Wallpaper", "Web", "raphael", "index.html");
const assets = join(repo, "CielWin.App", "Assets");
const viewport = 512;
const frameMs = Number(process.env.ICON_FRAME_MS ?? 4000);
const icoSizes = [16, 20, 24, 32, 48, 256];
const traySizeMax = 24;

const edgeCandidates = [
  process.env.EDGE,
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
].filter(Boolean);

const injected = `(() => {
  const queue = [];
  window.requestAnimationFrame = (callback) => { queue.push(callback); return queue.length; };
  window.__iconFrame = (ms) => { const due = queue.splice(0); due.forEach((cb) => cb(ms)); return due.length; };
  document.addEventListener("DOMContentLoaded", () => {
    const style = document.createElement("style");
    style.textContent = "html.scene-mini { background: transparent !important; } #nebula { display: none !important; }";
    document.head.appendChild(style);
  });
})();`;

async function main() {
  const edge = edgeCandidates.find((path) => existsSync(path));
  if (!edge) throw new Error("Microsoft Edge not found; set EDGE=<path to msedge.exe>.");

  const png = await capture(edge);
  const image = decodePng(png);
  const square = cropToFigure(image);
  const frames = icoSizes.map((size) => {
    const frame = resize(square, size);
    return size <= traySizeMax ? trayContrast(frame) : frame;
  });

  writeFileSync(join(assets, "raphael-mini.png"), encodePng(frames.at(-1)));
  writeFileSync(join(assets, "raphael-mini.ico"), encodeIco(frames));
  console.log(`figure box ${square.width}px from a ${image.width}x${image.height} capture at scene ms ${frameMs}`);
  console.log(`wrote ${join("CielWin.App", "Assets", "raphael-mini.png")} and raphael-mini.ico (${icoSizes.join(", ")})`);
}

// --- capture through the Chrome DevTools Protocol ----------------------------------------------------

async function capture(edge) {
  const profile = mkdtempSync(join(tmpdir(), "cielwin-icon-"));
  const browser = spawn(edge, [
    "--headless=new",
    "--disable-gpu",
    "--hide-scrollbars",
    "--no-first-run",
    "--no-default-browser-check",
    "--allow-file-access-from-files",
    "--force-device-scale-factor=1",
    `--window-size=${viewport},${viewport}`,
    "--remote-debugging-port=0",
    `--user-data-dir=${profile}`,
    "about:blank",
  ], { stdio: "ignore" });

  try {
    const endpoint = await waitForDevToolsEndpoint(profile);
    const cdp = await connect(endpoint);
    const { targetId } = await cdp.send("Target.createTarget", { url: "about:blank" });
    const { sessionId } = await cdp.send("Target.attachToTarget", { targetId, flatten: true });
    const page = (method, params) => cdp.send(method, params, sessionId);

    await page("Page.enable");
    await page("Emulation.setDeviceMetricsOverride", { width: viewport, height: viewport, deviceScaleFactor: 1, mobile: false });
    await page("Emulation.setDefaultBackgroundColorOverride", { color: { r: 0, g: 0, b: 0, a: 0 } });
    await page("Page.addScriptToEvaluateOnNewDocument", { source: injected });
    await page("Page.setBypassCSP", { enabled: true });

    const loaded = cdp.once("Page.loadEventFired", sessionId);
    const url = pathToFileURL(scenePage).href + "?fps=60&variant=mini";
    await page("Page.navigate", { url });
    await loaded;

    const result = await page("Runtime.evaluate", {
      awaitPromise: true,
      returnByValue: true,
      expression: `(async () => {
        await document.fonts.ready;
        if (!document.documentElement.classList.contains("scene-mini")) throw new Error("mini variant not selected");
        window.drawMiniSceneBase = function () {};
        // A few frames on a fixed clock: the first bakes the sprites, the last is the one captured.
        let drawn = 0;
        for (const ms of [${frameMs - 50}, ${frameMs - 33}, ${frameMs - 17}, ${frameMs}]) drawn += window.__iconFrame(ms);
        return drawn;
      })()`,
    });
    if (result.exceptionDetails) throw new Error(`page script failed: ${JSON.stringify(result.exceptionDetails)}`);

    const shot = await page("Page.captureScreenshot", { format: "png", fromSurface: true });
    cdp.close();
    return Buffer.from(shot.data, "base64");
  } finally {
    browser.kill();
    await new Promise((done) => setTimeout(done, 500));
    try { rmSync(profile, { recursive: true, force: true }); } catch { /* Edge may still hold a file */ }
  }
}

async function waitForDevToolsEndpoint(profile) {
  const file = join(profile, "DevToolsActivePort");
  for (let attempt = 0; attempt < 100; attempt++) {
    if (existsSync(file)) {
      const [port, path] = readFileSync(file, "utf8").split(/\r?\n/);
      if (port && path) return `ws://127.0.0.1:${port}${path}`;
    }
    await new Promise((done) => setTimeout(done, 100));
  }
  throw new Error("Edge did not open its DevTools port.");
}

function connect(endpoint) {
  return new Promise((resolveConnection, reject) => {
    const socket = new WebSocket(endpoint);
    const pending = new Map();
    const listeners = [];
    let nextId = 1;
    socket.onerror = () => reject(new Error("DevTools connection failed."));
    socket.onmessage = (event) => {
      const message = JSON.parse(event.data);
      if (message.id && pending.has(message.id)) {
        const { resolveCall, rejectCall } = pending.get(message.id);
        pending.delete(message.id);
        if (message.error) rejectCall(new Error(`${message.error.message}`));
        else resolveCall(message.result);
      } else if (message.method) {
        for (const listener of [...listeners]) listener(message);
      }
    };
    socket.onopen = () => resolveConnection({
      send(method, params = {}, sessionId) {
        const id = nextId++;
        socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
        return new Promise((resolveCall, rejectCall) => pending.set(id, { resolveCall, rejectCall }));
      },
      once(method, sessionId) {
        return new Promise((resolveEvent) => {
          const listener = (message) => {
            if (message.method === method && message.sessionId === sessionId) {
              listeners.splice(listeners.indexOf(listener), 1);
              resolveEvent(message.params);
            }
          };
          listeners.push(listener);
        });
      },
      close() { socket.close(); },
    });
  });
}

// --- image processing (straight-alpha RGBA, 8 bits per channel) --------------------------------------

/** Crops to the square around the figure's alpha bounding box (plus a small margin), centred on it. */
function cropToFigure(image) {
  let left = image.width, top = image.height, right = -1, bottom = -1;
  for (let y = 0; y < image.height; y++) {
    for (let x = 0; x < image.width; x++) {
      if (image.data[(y * image.width + x) * 4 + 3] > 8) {
        if (x < left) left = x;
        if (x > right) right = x;
        if (y < top) top = y;
        if (y > bottom) bottom = y;
      }
    }
  }
  if (right < 0) throw new Error("The capture is fully transparent: nothing was drawn.");

  const side = Math.ceil(Math.max(right - left + 1, bottom - top + 1) * 1.04);
  const cx = (left + right + 1) / 2;
  const cy = (top + bottom + 1) / 2;
  const x0 = Math.round(cx - side / 2);
  const y0 = Math.round(cy - side / 2);
  const out = { width: side, height: side, data: new Uint8Array(side * side * 4) };
  for (let y = 0; y < side; y++) {
    for (let x = 0; x < side; x++) {
      const sx = x0 + x, sy = y0 + y;
      if (sx < 0 || sy < 0 || sx >= image.width || sy >= image.height) continue;
      const from = (sy * image.width + sx) * 4, to = (y * side + x) * 4;
      out.data.set(image.data.subarray(from, from + 4), to);
    }
  }
  return out;
}

/** Area-average resample on premultiplied colour, so transparent pixels never bleed dark fringes. */
function resize(image, size) {
  const out = { width: size, height: size, data: new Uint8Array(size * size * 4) };
  const scale = image.width / size;
  for (let y = 0; y < size; y++) {
    const y0 = y * scale, y1 = (y + 1) * scale;
    for (let x = 0; x < size; x++) {
      const x0 = x * scale, x1 = (x + 1) * scale;
      let r = 0, g = 0, b = 0, a = 0, area = 0;
      for (let sy = Math.floor(y0); sy < Math.ceil(y1); sy++) {
        const wy = Math.min(sy + 1, y1) - Math.max(sy, y0);
        for (let sx = Math.floor(x0); sx < Math.ceil(x1); sx++) {
          const wx = Math.min(sx + 1, x1) - Math.max(sx, x0);
          const w = wx * wy;
          const i = (sy * image.width + sx) * 4;
          const alpha = image.data[i + 3] / 255;
          r += image.data[i] * alpha * w;
          g += image.data[i + 1] * alpha * w;
          b += image.data[i + 2] * alpha * w;
          a += alpha * w;
          area += w;
        }
      }
      const o = (y * size + x) * 4;
      if (a > 0) {
        out.data[o] = Math.round(r / a);
        out.data[o + 1] = Math.round(g / a);
        out.data[o + 2] = Math.round(b / a);
      }
      out.data[o + 3] = Math.round((a / area) * 255);
    }
  }
  return out;
}

/**
 * Contrast pass for the tray sizes: alpha 1-(1-a)^3 fills the pale disc, a 1.5 gamma on colour deepens the gold
 * (the white-hot core stays bright), and every pixel just outside the silhouette (8-neighbourhood) gets a dark
 * warm outline composited under it, so the disc edge reads on a light taskbar and vanishes on a dark one.
 */
function trayContrast(image) {
  const { width, height } = image;
  const solidAlpha = 0.35 * 255;
  const outline = [43, 26, 6];
  const outlineAlpha = 0.9;
  const data = new Uint8Array(image.data.length);
  for (let i = 0; i < data.length; i += 4) {
    for (let k = 0; k < 3; k++) data[i + k] = Math.round(255 * Math.pow(image.data[i + k] / 255, 1.5));
    data[i + 3] = Math.round(255 * (1 - Math.pow(1 - image.data[i + 3] / 255, 3)));
  }

  const isSolid = (x, y) => x >= 0 && y >= 0 && x < width && y < height && data[(y * width + x) * 4 + 3] >= solidAlpha;
  const out = new Uint8Array(data);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (isSolid(x, y)) continue;
      let edge = false;
      for (let dy = -1; dy <= 1 && !edge; dy++) {
        for (let dx = -1; dx <= 1 && !edge; dx++) edge = isSolid(x + dx, y + dy);
      }
      if (!edge) continue;
      // Straight-alpha "pixel over outline".
      const i = (y * width + x) * 4;
      const top = data[i + 3] / 255;
      const alpha = top + outlineAlpha * (1 - top);
      for (let k = 0; k < 3; k++) {
        out[i + k] = Math.round((data[i + k] * top + outline[k] * outlineAlpha * (1 - top)) / alpha);
      }
      out[i + 3] = Math.round(alpha * 255);
    }
  }
  return { width, height, data: out };
}

// --- PNG ----------------------------------------------------------------------------------------------

function decodePng(buffer) {
  let offset = 8, width = 0, height = 0, colorType = 0, bitDepth = 0, interlace = 0;
  const idat = [];
  while (offset < buffer.length) {
    const length = buffer.readUInt32BE(offset);
    const type = buffer.toString("ascii", offset + 4, offset + 8);
    const body = buffer.subarray(offset + 8, offset + 8 + length);
    if (type === "IHDR") {
      width = body.readUInt32BE(0);
      height = body.readUInt32BE(4);
      bitDepth = body[8];
      colorType = body[9];
      interlace = body[12];
    } else if (type === "IDAT") {
      idat.push(body);
    }
    offset += 12 + length;
  }
  if (bitDepth !== 8 || interlace !== 0 || (colorType !== 6 && colorType !== 2)) {
    throw new Error(`Unsupported PNG (bit depth ${bitDepth}, colour type ${colorType}, interlace ${interlace}).`);
  }

  const channels = colorType === 6 ? 4 : 3;
  const stride = width * channels;
  const raw = inflateSync(Buffer.concat(idat));
  const pixels = new Uint8Array(height * stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? pixels[y * stride + x - channels] : 0;
      const up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
      const upLeft = y > 0 && x >= channels ? pixels[(y - 1) * stride + x - channels] : 0;
      let value = line[x];
      if (filter === 1) value += left;
      else if (filter === 2) value += up;
      else if (filter === 3) value += (left + up) >> 1;
      else if (filter === 4) value += paeth(left, up, upLeft);
      pixels[y * stride + x] = value & 0xff;
    }
  }

  const data = new Uint8Array(width * height * 4);
  for (let i = 0, j = 0; i < width * height; i++, j += channels) {
    data[i * 4] = pixels[j];
    data[i * 4 + 1] = pixels[j + 1];
    data[i * 4 + 2] = pixels[j + 2];
    data[i * 4 + 3] = channels === 4 ? pixels[j + 3] : 255;
  }
  return { width, height, data };
}

function paeth(a, b, c) {
  const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}

function encodePng(image) {
  const stride = image.width * 4;
  const raw = Buffer.alloc((stride + 1) * image.height);
  for (let y = 0; y < image.height; y++) {
    raw[y * (stride + 1)] = 0;
    raw.set(image.data.subarray(y * stride, (y + 1) * stride), y * (stride + 1) + 1);
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(image.width, 0);
  header.writeUInt32BE(image.height, 4);
  header[8] = 8;
  header[9] = 6;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function chunk(type, body) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(body.length);
  const typed = Buffer.concat([Buffer.from(type, "ascii"), body]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(typed));
  return Buffer.concat([length, typed, crc]);
}

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(buffer) {
  let c = 0xffffffff;
  for (const byte of buffer) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

// --- ICO ----------------------------------------------------------------------------------------------

/** Small frames as 32-bit BMP (with the legacy AND mask) for every loader; 256 as PNG, the Vista+ form. */
function encodeIco(frames) {
  const images = frames.map((frame) => (frame.width >= 256 ? encodePng(frame) : encodeIcoBitmap(frame)));
  const header = Buffer.alloc(6 + 16 * frames.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(frames.length, 4);
  let offset = header.length;
  frames.forEach((frame, i) => {
    const entry = 6 + 16 * i;
    header[entry] = frame.width >= 256 ? 0 : frame.width;
    header[entry + 1] = frame.height >= 256 ? 0 : frame.height;
    header[entry + 2] = 0;
    header[entry + 3] = 0;
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(images[i].length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += images[i].length;
  });
  return Buffer.concat([header, ...images]);
}

function encodeIcoBitmap(frame) {
  const { width, height, data } = frame;
  const maskStride = Math.ceil(width / 32) * 4;
  const info = Buffer.alloc(40);
  info.writeUInt32LE(40, 0);
  info.writeInt32LE(width, 4);
  info.writeInt32LE(height * 2, 8);
  info.writeUInt16LE(1, 12);
  info.writeUInt16LE(32, 14);
  info.writeUInt32LE(width * height * 4 + maskStride * height, 20);
  const pixels = Buffer.alloc(width * height * 4);
  const mask = Buffer.alloc(maskStride * height);
  for (let y = 0; y < height; y++) {
    const row = height - 1 - y;
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 4, o = (row * width + x) * 4;
      pixels[o] = data[i + 2];
      pixels[o + 1] = data[i + 1];
      pixels[o + 2] = data[i];
      pixels[o + 3] = data[i + 3];
      if (data[i + 3] === 0) mask[row * maskStride + (x >> 3)] |= 0x80 >> (x & 7);
    }
  }
  return Buffer.concat([info, pixels, mask]);
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});

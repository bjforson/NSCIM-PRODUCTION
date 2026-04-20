// v2.12.0 Phase 4 — client-side 16-bit viewer.
//
// Fetches the raw pixel buffer for a scan's chosen plane (HE / LE /
// Material) from /api/ImageProcessing/container/{id}/raw, caches it per
// (container, plane) in memory, and renders to a <canvas> with a
// JavaScript window/level function so operators see real 16-bit dynamic
// range instead of an already-compressed 8-bit JPEG.
//
// Every slider tick re-runs the window/level pass on the cached buffer
// — zero server round-trips once the buffer is loaded. A 3256×1378
// 16-bit scan re-renders in well under 100 ms on a modern browser.
//
// Exported as window.Raw16BitViewer so Blazor JS interop can call it.

(function () {
  "use strict";

  // Cache: Map<`${container}|${plane}`, {buffer, width, height, bitDepth, plane}>
  const cache = new Map();

  function cacheKey(container, plane) {
    return container + "|" + plane;
  }

  /**
   * Fetch the raw plane buffer from the API. Returns a metadata object
   * with geometry + the typed array (Uint16Array for energies, Uint8Array
   * for material). Throws on network / HTTP errors.
   */
  async function fetchPlane(apiBaseUrl, container, plane) {
    const url = `${apiBaseUrl}/api/ImageProcessing/container/${encodeURIComponent(container)}/raw?plane=${encodeURIComponent(plane)}`;
    const key = cacheKey(container, plane);
    if (cache.has(key)) return cache.get(key);

    const res = await fetch(url, { credentials: "include" });
    if (!res.ok) {
      throw new Error(`Raw plane fetch failed: HTTP ${res.status} for ${url}`);
    }

    const width    = parseInt(res.headers.get("X-Width")    || "0", 10);
    const height   = parseInt(res.headers.get("X-Height")   || "0", 10);
    const bitDepth = parseInt(res.headers.get("X-BitDepth") || "16", 10);
    const planeTag = res.headers.get("X-Plane") || plane;
    const sourceFormat = res.headers.get("X-Source-Format") || "";

    const ab = await res.arrayBuffer();
    const pixels = bitDepth <= 8
      ? new Uint8Array(ab)
      : new Uint16Array(ab);

    const entry = { pixels, width, height, bitDepth, plane: planeTag, sourceFormat };
    cache.set(key, entry);
    return entry;
  }

  /**
   * Render a cached plane to a canvas with window/level tone mapping.
   *
   * @param {HTMLCanvasElement} canvas target canvas
   * @param {object} plane           entry from fetchPlane()
   * @param {number} levelPct        center of display range (0..100 of full signal)
   * @param {number} windowPct       width of display range  (0..100, clipped to usable)
   * @param {boolean} invert         vendor-convention invert (dense=dark). Default true.
   */
  function render(canvas, plane, levelPct, windowPct, invert) {
    if (!canvas || !plane) return;
    const { pixels, width, height, bitDepth } = plane;
    const max = bitDepth <= 8 ? 255 : (1 << bitDepth) - 1;

    // Map slider % (0..100) to absolute signal range
    const level = (levelPct / 100) * max;
    const halfW = (windowPct / 100) * max / 2;
    let lo = Math.max(0, level - halfW);
    let hi = Math.min(max, level + halfW);
    if (hi - lo < 1) hi = lo + 1;

    const range = hi - lo;
    const scale = 255 / range;
    const doInvert = invert !== false; // default true

    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d");
    const imgData = ctx.createImageData(width, height);
    const out = imgData.data;

    // Hot loop. Per-pixel: subtract lo, scale, clamp, invert, write RGBA.
    // Typical 5 Mpx completes in ~20-60 ms on modern browsers.
    const n = pixels.length;
    let o = 0;
    for (let i = 0; i < n; i++) {
      let v = (pixels[i] - lo) * scale;
      if (v < 0) v = 0;
      else if (v > 255) v = 255;
      if (doInvert) v = 255 - v;
      out[o]     = v;
      out[o + 1] = v;
      out[o + 2] = v;
      out[o + 3] = 255;
      o += 4;
    }
    ctx.putImageData(imgData, 0, 0);
  }

  /**
   * Combined fetch + render path — what Blazor calls. Returns the plane
   * metadata so Blazor can display geometry (e.g. "3256×1378 @16-bit").
   */
  async function loadAndRender(canvas, apiBaseUrl, container, plane, levelPct, windowPct, invert) {
    const data = await fetchPlane(apiBaseUrl, container, plane);
    render(canvas, data, levelPct, windowPct, invert);
    return {
      width: data.width,
      height: data.height,
      bitDepth: data.bitDepth,
      plane: data.plane,
      sourceFormat: data.sourceFormat,
      bytes: data.pixels.byteLength,
    };
  }

  /**
   * Re-render from cache without re-fetching — used on slider drag.
   * Returns true if the plane was cached and re-rendered, false otherwise.
   */
  function rerenderFromCache(canvas, container, plane, levelPct, windowPct, invert) {
    const entry = cache.get(cacheKey(container, plane));
    if (!entry) return false;
    render(canvas, entry, levelPct, windowPct, invert);
    return true;
  }

  /** Clear cache for a single container (on container change) or everything. */
  function clearCache(container) {
    if (!container) {
      cache.clear();
      return;
    }
    const prefix = container + "|";
    for (const k of Array.from(cache.keys())) {
      if (k.startsWith(prefix)) cache.delete(k);
    }
  }

  window.Raw16BitViewer = {
    loadAndRender,
    rerenderFromCache,
    clearCache,
  };
})();

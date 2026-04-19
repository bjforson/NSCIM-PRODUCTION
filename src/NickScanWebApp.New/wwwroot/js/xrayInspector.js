/*
 * X-Ray Inspector — client-side canvas viewer.
 *
 * Owns: canvas rendering, pan/zoom, window/level drag, ROI drawing (rect,
 * ellipse, polygon), line-profile tool, coordinate math, and export helpers.
 *
 * Server-side pixel decoding, transforms, histograms, edge detection, and
 * analysis all happen in the Python image-splitter's /inspector blueprint,
 * proxied through the NSCIM API's /api/xray-inspector/* controller. This
 * JS module just draws what the server gives it and captures user input.
 *
 * Entry point: window.XrayInspector.init(containerId, dotnetRef)
 *   - containerId: id of a wrapper <div> that holds the <canvas>
 *   - dotnetRef: DotNetObjectReference to the host Blazor component so JS
 *     can call [JSInvokable] methods back into C# (hover readouts, ROI
 *     commits, right-click context menu actions, etc.)
 */

(function () {
    'use strict';

    /** @type {Map<string, ViewerState>} */
    const viewers = new Map();

    const TOOL_PAN = 'pan';
    const TOOL_WL = 'windowlevel';
    const TOOL_RULER = 'ruler';
    const TOOL_RECT = 'rect';
    const TOOL_ELLIPSE = 'ellipse';
    const TOOL_POLYGON = 'polygon';
    const TOOL_LINE_PROFILE = 'lineprofile';

    class ViewerState {
        constructor(containerId, dotnetRef) {
            this.containerId = containerId;
            this.dotnetRef = dotnetRef;

            this.container = document.getElementById(containerId);
            if (!this.container) {
                console.error('XrayInspector: container not found:', containerId);
                return;
            }

            // Build the canvas and overlay
            this.container.innerHTML =
                '<canvas class="xray-canvas" style="display:block;background:#111;cursor:grab;width:100%;"></canvas>' +
                '<div class="xray-overlay" style="position:absolute;pointer-events:none;top:0;left:0;right:0;bottom:0;"></div>';
            this.container.style.position = 'relative';

            this.canvas = this.container.querySelector('canvas');
            this.overlay = this.container.querySelector('.xray-overlay');
            this.ctx = this.canvas.getContext('2d', { willReadFrequently: false });

            // View state
            this.image = null;       // HTMLImageElement of current render (PNG path)
            this.imageWidth = 0;
            this.imageHeight = 0;
            this.scale = 1;
            this.offsetX = 0;
            this.offsetY = 0;

            // Raw-16-bit path: when loaded via loadRaw16Image, we keep the
            // uint16 pixel buffer on the client and re-render to an offscreen
            // canvas whenever window/level changes. This gives true 16-bit
            // dynamic-range fidelity and drag-speed W/L without round-trips.
            this.rawBuffer = null;       // Uint16Array | Uint8Array | null
            this.rawBitDepth = 0;        // 8 or 16
            this.rawCanvas = null;       // HTMLCanvasElement offscreen
            this.rawCtx = null;

            // Object-detection overlay state. Each object is
            // { index, bbox: [x,y,w,h], area, centroid: [cx,cy] }.
            this.detectedObjects = [];
            this.focusedObjectIndex = -1;

            // Window/level state (client-side preview; server renders final)
            this.windowLo = null;
            this.windowHi = null;
            this.pixelMin = 0;
            this.pixelMax = 65535;

            // Interaction state
            this.tool = TOOL_PAN;
            this.isDragging = false;
            this.dragStart = null;  // {x, y}
            this.currentRoi = null; // in-progress ROI being drawn
            this.completedRois = []; // committed ROIs
            this.polygonPoints = [];

            this._wireEvents();
            this._resizeToContainer();
            window.addEventListener('resize', () => this._resizeToContainer());
        }

        _resizeToContainer() {
            const rect = this.container.getBoundingClientRect();
            const dpr = window.devicePixelRatio || 1;
            this.canvas.width = Math.max(1, Math.floor(rect.width * dpr));
            this.canvas.height = Math.max(1, Math.floor(rect.height * dpr));
            this.canvas.style.width = rect.width + 'px';
            this.canvas.style.height = rect.height + 'px';
            this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            this._draw();
        }

        async loadImage(url, metadata) {
            // PNG path (cooked transforms, composites, edge overlays, etc).
            // Clears any raw 16-bit buffer so we don't mix paths.
            this.rawBuffer = null;
            this.rawBitDepth = 0;
            if (metadata) {
                this.imageWidth = metadata.width || 0;
                this.imageHeight = metadata.height || 0;
                this.pixelMin = metadata.pixelMin ?? 0;
                this.pixelMax = metadata.pixelMax ?? 65535;
                if (this.windowLo === null) this.windowLo = this.pixelMin;
                if (this.windowHi === null) this.windowHi = this.pixelMax;
            }
            return new Promise((resolve, reject) => {
                const img = new Image();
                img.crossOrigin = 'anonymous';
                img.onload = () => {
                    this.image = img;
                    if (!this.imageWidth) this.imageWidth = img.naturalWidth;
                    if (!this.imageHeight) this.imageHeight = img.naturalHeight;
                    this.resetView();
                    this._draw();
                    resolve();
                };
                img.onerror = (e) => reject(e);
                img.src = url;
            });
        }

        /**
         * Fetch a raw pixel buffer from /api/xray-inspector/image/...?format=bin,
         * keep the uint16 array client-side, auto-pick a sensible initial
         * window/level from percentiles, and render.
         *
         * This is the path for interactive window/level on true 16-bit data.
         * Window/level drag re-renders from the buffer without any network
         * or server round-trip.
         */
        async loadRaw16Image(url) {
            const resp = await fetch(url, { credentials: 'include' });
            if (!resp.ok) throw new Error('raw16 fetch failed: HTTP ' + resp.status);
            const buf = await resp.arrayBuffer();
            const bytes = new Uint8Array(buf);

            // Header: 16 bytes
            if (bytes.length < 16 || String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]) !== 'XRAY') {
                throw new Error('raw16: bad magic (got "' + String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]) + '")');
            }
            const view = new DataView(buf);
            const bitDepth = view.getUint8(4);
            // byte 5: channels, bytes 6-7: flags, not currently used
            const width = view.getUint32(8, /*littleEndian*/ true);
            const height = view.getUint32(12, /*littleEndian*/ true);
            const expectedBytes = width * height * (bitDepth / 8);
            if (bytes.length - 16 < expectedBytes) {
                throw new Error(`raw16: short payload (${bytes.length - 16} < ${expectedBytes})`);
            }

            // Slice out the pixel data — DataView/TypedArray reuse the same buffer
            if (bitDepth === 16) {
                this.rawBuffer = new Uint16Array(buf, 16, width * height);
                this.rawBitDepth = 16;
            } else if (bitDepth === 8) {
                this.rawBuffer = new Uint8Array(buf, 16, width * height);
                this.rawBitDepth = 8;
            } else {
                throw new Error('raw16: unsupported bit depth ' + bitDepth);
            }

            this.image = null;
            this.imageWidth = width;
            this.imageHeight = height;

            // Offscreen canvas caches the last W/L-mapped 8-bit render.
            if (!this.rawCanvas || this.rawCanvas.width !== width || this.rawCanvas.height !== height) {
                this.rawCanvas = document.createElement('canvas');
                this.rawCanvas.width = width;
                this.rawCanvas.height = height;
                this.rawCtx = this.rawCanvas.getContext('2d');
            }

            // Compute initial window (1st/99.5th percentile) from a random sample
            const [autoLo, autoHi] = this._samplePercentile(this.rawBuffer, 1.0, 99.5, 8192);
            this.pixelMin = 0;
            this.pixelMax = (bitDepth === 16) ? 65535 : 255;
            this.windowLo = autoLo;
            this.windowHi = autoHi;

            this._renderRawToOffscreen();
            // Make this.image point to the offscreen canvas so _draw can
            // treat it uniformly with the PNG path.
            this.image = this.rawCanvas;
            this.resetView();
            this._draw();
        }

        /**
         * Re-render the stored raw buffer to the offscreen canvas using
         * the current window/level. Cheap — ~10 ms for a 544x1673 ASE
         * image on a laptop CPU.
         */
        _renderRawToOffscreen(invert = false) {
            if (!this.rawBuffer || !this.rawCanvas) return;
            const w = this.imageWidth;
            const h = this.imageHeight;
            const n = w * h;
            const imgData = this.rawCtx.createImageData(w, h);
            const out = imgData.data;
            const lo = this.windowLo ?? 0;
            const hi = this.windowHi ?? (this.rawBitDepth === 16 ? 65535 : 255);
            const range = Math.max(hi - lo, 1);
            const buf = this.rawBuffer;
            for (let i = 0, o = 0; i < n; i++, o += 4) {
                let v = buf[i];
                // Window/level map to 0..255
                if (v <= lo) v = 0;
                else if (v >= hi) v = 255;
                else v = Math.round(((v - lo) / range) * 255);
                if (invert) v = 255 - v;
                out[o] = v;
                out[o + 1] = v;
                out[o + 2] = v;
                out[o + 3] = 255;
            }
            this.rawCtx.putImageData(imgData, 0, 0);
        }

        /**
         * Stochastic percentile estimate (exact is O(n log n), we only
         * need a couple of thousand samples for an initial W/L).
         */
        _samplePercentile(buffer, loPct, hiPct, nSamples) {
            const n = buffer.length;
            const k = Math.min(nSamples, n);
            const samples = new Float32Array(k);
            // Deterministic stride so the same image always gets the same initial W/L.
            const stride = Math.max(1, Math.floor(n / k));
            for (let i = 0, j = 0; i < k; i++, j += stride) {
                samples[i] = buffer[j % n];
            }
            samples.sort();
            const loIdx = Math.floor((loPct / 100) * (k - 1));
            const hiIdx = Math.floor((hiPct / 100) * (k - 1));
            return [samples[loIdx], samples[hiIdx]];
        }

        resetView() {
            if (!this.image) return;
            const rect = this.container.getBoundingClientRect();
            // Fit the image to the container
            const scaleX = rect.width / this.imageWidth;
            const scaleY = rect.height / this.imageHeight;
            this.scale = Math.min(scaleX, scaleY) * 0.95;
            this.offsetX = (rect.width - this.imageWidth * this.scale) / 2;
            this.offsetY = (rect.height - this.imageHeight * this.scale) / 2;
        }

        setTool(tool) {
            this.tool = tool;
            this.polygonPoints = [];
            switch (tool) {
                case TOOL_PAN: this.canvas.style.cursor = 'grab'; break;
                case TOOL_WL: this.canvas.style.cursor = 'ns-resize'; break;
                case TOOL_RECT:
                case TOOL_ELLIPSE:
                case TOOL_POLYGON:
                case TOOL_RULER:
                case TOOL_LINE_PROFILE:
                    this.canvas.style.cursor = 'crosshair';
                    break;
            }
        }

        clearRois() {
            this.completedRois = [];
            this.currentRoi = null;
            this.polygonPoints = [];
            this._draw();
        }

        getRois() {
            return this.completedRois.slice();
        }

        setWindowLevel(lo, hi) {
            this.windowLo = lo;
            this.windowHi = hi;
            if (this.rawBuffer) {
                // Client-side re-render from the raw uint16 buffer.
                this._renderRawToOffscreen();
                this.image = this.rawCanvas;
            }
            this._draw();
        }

        // ── Coordinate transforms ──────────────────────────────────

        _screenToImage(sx, sy) {
            return {
                x: (sx - this.offsetX) / this.scale,
                y: (sy - this.offsetY) / this.scale
            };
        }

        _imageToScreen(ix, iy) {
            return {
                x: ix * this.scale + this.offsetX,
                y: iy * this.scale + this.offsetY
            };
        }

        // ── Drawing ────────────────────────────────────────────────

        _draw() {
            const ctx = this.ctx;
            const rect = this.container.getBoundingClientRect();
            ctx.clearRect(0, 0, rect.width, rect.height);
            ctx.fillStyle = '#111';
            ctx.fillRect(0, 0, rect.width, rect.height);

            if (!this.image) return;

            // Draw the image at current pan/zoom
            ctx.imageSmoothingEnabled = this.scale < 1;
            ctx.drawImage(
                this.image,
                0, 0, this.imageWidth, this.imageHeight,
                this.offsetX, this.offsetY,
                this.imageWidth * this.scale, this.imageHeight * this.scale
            );

            // Detected-object bounding boxes (below user-drawn ROIs)
            if (this.detectedObjects && this.detectedObjects.length > 0) {
                ctx.lineWidth = 1.5;
                for (let i = 0; i < this.detectedObjects.length; i++) {
                    const obj = this.detectedObjects[i];
                    const bb = obj.bbox || obj.Bbox;
                    if (!bb) continue;
                    const tl = this._imageToScreen(bb[0], bb[1]);
                    const w = bb[2] * this.scale;
                    const h = bb[3] * this.scale;
                    const isFocused = (i === this.focusedObjectIndex);
                    ctx.strokeStyle = isFocused ? '#f97316' : 'rgba(14, 165, 233, 0.8)';
                    ctx.lineWidth = isFocused ? 3 : 1.5;
                    ctx.strokeRect(tl.x, tl.y, w, h);
                    // Small label with the index
                    this._drawLabel(tl.x + 3, tl.y + 12, '#' + (i + 1), ctx.strokeStyle);
                }
            }

            // Completed ROIs
            ctx.lineWidth = 2;
            for (let i = 0; i < this.completedRois.length; i++) {
                this._drawRoi(this.completedRois[i], '#4ade80', String(i + 1));
            }
            // In-progress ROI
            if (this.currentRoi) {
                this._drawRoi(this.currentRoi, '#facc15');
            }
            // Polygon WIP points
            if (this.tool === TOOL_POLYGON && this.polygonPoints.length > 0) {
                ctx.strokeStyle = '#facc15';
                ctx.beginPath();
                const p0 = this._imageToScreen(this.polygonPoints[0][0], this.polygonPoints[0][1]);
                ctx.moveTo(p0.x, p0.y);
                for (let i = 1; i < this.polygonPoints.length; i++) {
                    const p = this._imageToScreen(this.polygonPoints[i][0], this.polygonPoints[i][1]);
                    ctx.lineTo(p.x, p.y);
                }
                ctx.stroke();
                for (const pt of this.polygonPoints) {
                    const s = this._imageToScreen(pt[0], pt[1]);
                    ctx.fillStyle = '#facc15';
                    ctx.beginPath(); ctx.arc(s.x, s.y, 4, 0, Math.PI * 2); ctx.fill();
                }
            }
        }

        _drawRoi(roi, color, label) {
            const ctx = this.ctx;
            ctx.strokeStyle = color;
            ctx.fillStyle = color;
            ctx.lineWidth = 2;
            if (roi.kind === 'rect') {
                const tl = this._imageToScreen(roi.x, roi.y);
                const w = roi.w * this.scale;
                const h = roi.h * this.scale;
                ctx.strokeRect(tl.x, tl.y, w, h);
                if (label) this._drawLabel(tl.x + 4, tl.y + 14, label, color);
            } else if (roi.kind === 'ellipse') {
                const center = this._imageToScreen(roi.cx, roi.cy);
                ctx.beginPath();
                ctx.ellipse(center.x, center.y, roi.rx * this.scale, roi.ry * this.scale, 0, 0, Math.PI * 2);
                ctx.stroke();
                if (label) this._drawLabel(center.x + 4, center.y, label, color);
            } else if (roi.kind === 'polygon') {
                ctx.beginPath();
                const p0 = this._imageToScreen(roi.points[0][0], roi.points[0][1]);
                ctx.moveTo(p0.x, p0.y);
                for (let i = 1; i < roi.points.length; i++) {
                    const p = this._imageToScreen(roi.points[i][0], roi.points[i][1]);
                    ctx.lineTo(p.x, p.y);
                }
                ctx.closePath();
                ctx.stroke();
                if (label) this._drawLabel(p0.x + 4, p0.y + 14, label, color);
            } else if (roi.kind === 'line') {
                const p0 = this._imageToScreen(roi.x0, roi.y0);
                const p1 = this._imageToScreen(roi.x1, roi.y1);
                ctx.beginPath();
                ctx.moveTo(p0.x, p0.y);
                ctx.lineTo(p1.x, p1.y);
                ctx.stroke();
                const dx = roi.x1 - roi.x0;
                const dy = roi.y1 - roi.y0;
                const dist = Math.hypot(dx, dy).toFixed(1);
                this._drawLabel((p0.x + p1.x) / 2, (p0.y + p1.y) / 2 - 6, dist + ' px', color);
            }
        }

        _drawLabel(x, y, text, color) {
            const ctx = this.ctx;
            ctx.font = '12px sans-serif';
            const w = ctx.measureText(text).width + 8;
            ctx.fillStyle = 'rgba(0,0,0,0.65)';
            ctx.fillRect(x - 2, y - 12, w, 16);
            ctx.fillStyle = color;
            ctx.fillText(text, x + 2, y);
        }

        // ── Event wiring ───────────────────────────────────────────

        _wireEvents() {
            const c = this.canvas;

            c.addEventListener('wheel', (e) => {
                e.preventDefault();
                const rect = c.getBoundingClientRect();
                const mx = e.clientX - rect.left;
                const my = e.clientY - rect.top;
                const before = this._screenToImage(mx, my);
                const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
                this.scale *= factor;
                this.scale = Math.max(0.01, Math.min(50, this.scale));
                const after = this._screenToImage(mx, my);
                this.offsetX += (after.x - before.x) * this.scale;
                this.offsetY += (after.y - before.y) * this.scale;
                this._draw();
            }, { passive: false });

            c.addEventListener('mousedown', (e) => {
                if (e.button !== 0) return;
                const rect = c.getBoundingClientRect();
                const sx = e.clientX - rect.left;
                const sy = e.clientY - rect.top;
                const ip = this._screenToImage(sx, sy);

                this.isDragging = true;
                this.dragStart = { sx, sy, ix: ip.x, iy: ip.y, offX: this.offsetX, offY: this.offsetY,
                                    wlLo: this.windowLo, wlHi: this.windowHi };

                if (this.tool === TOOL_RECT) {
                    this.currentRoi = { kind: 'rect', x: ip.x, y: ip.y, w: 0, h: 0 };
                } else if (this.tool === TOOL_ELLIPSE) {
                    this.currentRoi = { kind: 'ellipse', cx: ip.x, cy: ip.y, rx: 0, ry: 0 };
                } else if (this.tool === TOOL_RULER || this.tool === TOOL_LINE_PROFILE) {
                    this.currentRoi = { kind: 'line', x0: ip.x, y0: ip.y, x1: ip.x, y1: ip.y };
                } else if (this.tool === TOOL_PAN) {
                    c.style.cursor = 'grabbing';
                }
            });

            c.addEventListener('mousemove', (e) => {
                const rect = c.getBoundingClientRect();
                const sx = e.clientX - rect.left;
                const sy = e.clientY - rect.top;
                const ip = this._screenToImage(sx, sy);

                // Live pixel readout (coords into image space)
                if (this.dotnetRef && this.imageWidth > 0) {
                    const ix = Math.floor(ip.x);
                    const iy = Math.floor(ip.y);
                    if (ix >= 0 && iy >= 0 && ix < this.imageWidth && iy < this.imageHeight) {
                        try { this.dotnetRef.invokeMethodAsync('OnHover', ix, iy); } catch (_) { }
                    }
                }

                if (!this.isDragging) return;

                if (this.tool === TOOL_PAN) {
                    this.offsetX = this.dragStart.offX + (sx - this.dragStart.sx);
                    this.offsetY = this.dragStart.offY + (sy - this.dragStart.sy);
                } else if (this.tool === TOOL_WL) {
                    // drag Y → window width, drag X → window level (center)
                    const range = this.pixelMax - this.pixelMin;
                    const dx = (sx - this.dragStart.sx) / c.clientWidth * range;
                    const dy = (sy - this.dragStart.sy) / c.clientHeight * range;
                    let newLo = this.dragStart.wlLo + dx - dy / 2;
                    let newHi = this.dragStart.wlHi + dx + dy / 2;
                    if (newHi - newLo < 1) newHi = newLo + 1;
                    this.windowLo = newLo;
                    this.windowHi = newHi;
                    // Client-side re-render if we have the raw uint16 buffer.
                    // This is the reason the raw16 path exists: drag-speed W/L.
                    if (this.rawBuffer) {
                        this._renderRawToOffscreen();
                        this.image = this.rawCanvas;
                    }
                    if (this.dotnetRef) {
                        try { this.dotnetRef.invokeMethodAsync('OnWindowLevelChanged', newLo, newHi); } catch (_) { }
                    }
                } else if (this.currentRoi) {
                    if (this.currentRoi.kind === 'rect') {
                        this.currentRoi.w = ip.x - this.currentRoi.x;
                        this.currentRoi.h = ip.y - this.currentRoi.y;
                    } else if (this.currentRoi.kind === 'ellipse') {
                        this.currentRoi.rx = Math.abs(ip.x - this.currentRoi.cx);
                        this.currentRoi.ry = Math.abs(ip.y - this.currentRoi.cy);
                    } else if (this.currentRoi.kind === 'line') {
                        this.currentRoi.x1 = ip.x;
                        this.currentRoi.y1 = ip.y;
                    }
                }
                this._draw();
            });

            c.addEventListener('mouseup', (e) => {
                if (e.button !== 0) return;
                this.isDragging = false;
                if (this.tool === TOOL_PAN) {
                    c.style.cursor = 'grab';
                } else if (this.currentRoi) {
                    // Normalize rect (positive w, h)
                    if (this.currentRoi.kind === 'rect') {
                        if (this.currentRoi.w < 0) { this.currentRoi.x += this.currentRoi.w; this.currentRoi.w = -this.currentRoi.w; }
                        if (this.currentRoi.h < 0) { this.currentRoi.y += this.currentRoi.h; this.currentRoi.h = -this.currentRoi.h; }
                        if (this.currentRoi.w > 1 && this.currentRoi.h > 1) {
                            this.completedRois.push(this.currentRoi);
                            this._notifyRoiCommit(this.currentRoi);
                        }
                    } else if (this.currentRoi.kind === 'ellipse') {
                        if (this.currentRoi.rx > 1 && this.currentRoi.ry > 1) {
                            this.completedRois.push(this.currentRoi);
                            this._notifyRoiCommit(this.currentRoi);
                        }
                    } else if (this.currentRoi.kind === 'line') {
                        this.completedRois.push(this.currentRoi);
                        this._notifyRoiCommit(this.currentRoi);
                    }
                    this.currentRoi = null;
                    this._draw();
                }
            });

            c.addEventListener('click', (e) => {
                if (this.tool !== TOOL_POLYGON) return;
                const rect = c.getBoundingClientRect();
                const ip = this._screenToImage(e.clientX - rect.left, e.clientY - rect.top);
                this.polygonPoints.push([ip.x, ip.y]);
                this._draw();
            });

            c.addEventListener('dblclick', (e) => {
                if (this.tool !== TOOL_POLYGON) return;
                if (this.polygonPoints.length >= 3) {
                    const roi = { kind: 'polygon', points: this.polygonPoints.slice() };
                    this.completedRois.push(roi);
                    this._notifyRoiCommit(roi);
                }
                this.polygonPoints = [];
                this._draw();
            });
        }

        _notifyRoiCommit(roi) {
            if (!this.dotnetRef) return;
            try { this.dotnetRef.invokeMethodAsync('OnRoiCommitted', JSON.stringify(roi)); }
            catch (_) { }
        }
    }

    // Public API exposed to Blazor
    window.XrayInspector = {
        init(containerId, dotnetRef) {
            const existing = viewers.get(containerId);
            if (existing) return;
            viewers.set(containerId, new ViewerState(containerId, dotnetRef));
        },
        dispose(containerId) {
            viewers.delete(containerId);
        },
        loadImage(containerId, url, metadata) {
            const v = viewers.get(containerId);
            return v ? v.loadImage(url, metadata) : Promise.reject('no viewer');
        },
        loadRaw16Image(containerId, url) {
            const v = viewers.get(containerId);
            return v ? v.loadRaw16Image(url) : Promise.reject('no viewer');
        },
        setTool(containerId, tool) {
            const v = viewers.get(containerId);
            if (v) v.setTool(tool);
        },
        resetView(containerId) {
            const v = viewers.get(containerId);
            if (v) { v.resetView(); v._draw(); }
        },
        clearRois(containerId) {
            const v = viewers.get(containerId);
            if (v) v.clearRois();
        },
        getRois(containerId) {
            const v = viewers.get(containerId);
            return v ? v.getRois() : [];
        },
        setWindowLevel(containerId, lo, hi) {
            const v = viewers.get(containerId);
            if (v) v.setWindowLevel(lo, hi);
        },
        setDetectedObjects(containerId, objects) {
            const v = viewers.get(containerId);
            if (!v) return;
            v.detectedObjects = objects || [];
            v.focusedObjectIndex = -1;
            v._draw();
        },
        clearDetectedObjects(containerId) {
            const v = viewers.get(containerId);
            if (!v) return;
            v.detectedObjects = [];
            v.focusedObjectIndex = -1;
            v._draw();
        },
        focusDetectedObject(containerId, index) {
            const v = viewers.get(containerId);
            if (!v) return;
            v.focusedObjectIndex = index;
            // Auto-zoom/pan to the focused object so the user sees it
            const obj = v.detectedObjects[index];
            if (obj) {
                const bb = obj.bbox || obj.Bbox;
                if (bb) {
                    const rect = v.container.getBoundingClientRect();
                    // Target: fit object to 40% of viewport, leave headroom
                    const targetScale = Math.min(
                        (rect.width * 0.4) / Math.max(bb[2], 1),
                        (rect.height * 0.4) / Math.max(bb[3], 1),
                        8
                    );
                    v.scale = targetScale;
                    const cx = bb[0] + bb[2] / 2;
                    const cy = bb[1] + bb[3] / 2;
                    v.offsetX = rect.width / 2 - cx * v.scale;
                    v.offsetY = rect.height / 2 - cy * v.scale;
                }
            }
            v._draw();
        },
        downloadBlob(bytes, filename, contentType) {
            const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }
    };
})();

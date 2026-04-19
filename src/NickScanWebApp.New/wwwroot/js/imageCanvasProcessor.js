/**
 * Canvas-based image processing pipeline for X-ray image analysis.
 * Provides pixel-level manipulation: Window/Level, edge detection, pseudocolor,
 * histogram, sharpen, and magnifying glass.
 */
window.ImageCanvasProcessor = (function () {
    'use strict';

    let _canvas = null;
    let _ctx = null;
    let _originalPixels = null;   // Uint8ClampedArray — never mutated
    let _currentPixels = null;    // working copy for compositing filters
    let _imgWidth = 0;
    let _imgHeight = 0;
    let _dotNetRef = null;

    // Active filter state
    let _windowLevel = null;      // { window, level } or null
    let _activeColorMap = null;   // string name or null
    let _activeConvolution = null;// string name or null
    let _histEqEnabled = false;
    let _isCanvasVisible = false;
    let _gamma = 1.0;             // density/gamma (1.0 = neutral, >1 = denser, <1 = lighter)
    let _autoEnhance = false;     // percentile-stretch auto-enhance
    let _autoEnhanceLut = null;   // cached LUT for auto-enhance

    // Window/Level drag state
    let _wlDragging = false;
    let _wlStartX = 0;
    let _wlStartY = 0;
    let _wlStartWindow = 255;
    let _wlStartLevel = 128;
    let _wlWindow = 255;
    let _wlLevel = 128;

    // Loupe state
    let _loupeCanvas = null;
    let _loupeCtx = null;
    let _loupeActive = false;
    let _loupeSize = 180;
    let _loupeZoom = 3;

    // ─── Color Maps (256 entries each: [R, G, B]) ───

    function buildHotMap() {
        const lut = new Array(256);
        for (let i = 0; i < 256; i++) {
            const t = i / 255;
            lut[i] = [
                Math.min(255, Math.floor(t * 3 * 255)),
                Math.min(255, Math.max(0, Math.floor((t - 0.33) * 3 * 255))),
                Math.min(255, Math.max(0, Math.floor((t - 0.67) * 3 * 255)))
            ];
        }
        return lut;
    }

    function buildJetMap() {
        const lut = new Array(256);
        for (let i = 0; i < 256; i++) {
            const t = i / 255;
            let r, g, b;
            if (t < 0.125) { r = 0; g = 0; b = 0.5 + t * 4; }
            else if (t < 0.375) { r = 0; g = (t - 0.125) * 4; b = 1; }
            else if (t < 0.625) { r = (t - 0.375) * 4; g = 1; b = 1 - (t - 0.375) * 4; }
            else if (t < 0.875) { r = 1; g = 1 - (t - 0.625) * 4; b = 0; }
            else { r = 1 - (t - 0.875) * 2; g = 0; b = 0; }
            lut[i] = [Math.floor(r * 255), Math.floor(g * 255), Math.floor(b * 255)];
        }
        return lut;
    }

    function buildBoneMap() {
        const lut = new Array(256);
        for (let i = 0; i < 256; i++) {
            const t = i / 255;
            const base_val = t * 0.875;
            lut[i] = [
                Math.floor(Math.min(1, base_val + (t > 0.75 ? (t - 0.75) * 0.5 : 0)) * 255),
                Math.floor(Math.min(1, base_val + (t > 0.375 && t <= 0.75 ? (t - 0.375) * 0.5 : (t > 0.75 ? 0.1875 : 0))) * 255),
                Math.floor(Math.min(1, base_val + (t <= 0.375 ? t * 0.5 : 0.1875)) * 255)
            ];
        }
        return lut;
    }

    function buildRainbowMap() {
        const lut = new Array(256);
        for (let i = 0; i < 256; i++) {
            const h = (i / 255) * 300; // 0-300 degrees (purple to red)
            const s = 1, v = 1;
            const c = v * s;
            const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
            const m = v - c;
            let r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else { r = x; g = 0; b = c; }
            lut[i] = [Math.floor((r + m) * 255), Math.floor((g + m) * 255), Math.floor((b + m) * 255)];
        }
        return lut;
    }

    const COLOR_MAPS = {
        hot: null, jet: null, bone: null, rainbow: null
    };
    function getColorMap(name) {
        if (!COLOR_MAPS[name]) {
            switch (name) {
                case 'hot': COLOR_MAPS.hot = buildHotMap(); break;
                case 'jet': COLOR_MAPS.jet = buildJetMap(); break;
                case 'bone': COLOR_MAPS.bone = buildBoneMap(); break;
                case 'rainbow': COLOR_MAPS.rainbow = buildRainbowMap(); break;
            }
        }
        return COLOR_MAPS[name];
    }

    // ─── Convolution Kernels ───

    const KERNELS = {
        sobelX: [[-1, 0, 1], [-2, 0, 2], [-1, 0, 1]],
        sobelY: [[-1, -2, -1], [0, 0, 0], [1, 2, 1]],
        laplacian: [[0, -1, 0], [-1, 4, -1], [0, -1, 0]],
        sharpen: [[0, -1, 0], [-1, 5, -1], [0, -1, 0]],
        emboss: [[-2, -1, 0], [-1, 1, 1], [0, 1, 2]]
    };

    function applyConvolution3x3(srcData, width, height, kernel) {
        const dst = new Uint8ClampedArray(srcData.length);
        for (let y = 1; y < height - 1; y++) {
            for (let x = 1; x < width - 1; x++) {
                let r = 0, g = 0, b = 0;
                for (let ky = -1; ky <= 1; ky++) {
                    for (let kx = -1; kx <= 1; kx++) {
                        const idx = ((y + ky) * width + (x + kx)) * 4;
                        const w = kernel[ky + 1][kx + 1];
                        r += srcData[idx] * w;
                        g += srcData[idx + 1] * w;
                        b += srcData[idx + 2] * w;
                    }
                }
                const i = (y * width + x) * 4;
                dst[i] = Math.min(255, Math.max(0, r));
                dst[i + 1] = Math.min(255, Math.max(0, g));
                dst[i + 2] = Math.min(255, Math.max(0, b));
                dst[i + 3] = 255;
            }
        }
        return dst;
    }

    function applySobelEdge(srcData, width, height) {
        const gx = applyConvolution3x3(srcData, width, height, KERNELS.sobelX);
        const gy = applyConvolution3x3(srcData, width, height, KERNELS.sobelY);
        const dst = new Uint8ClampedArray(srcData.length);
        for (let i = 0; i < srcData.length; i += 4) {
            const mag = Math.min(255, Math.sqrt(gx[i] * gx[i] + gy[i] * gy[i]));
            dst[i] = dst[i + 1] = dst[i + 2] = mag;
            dst[i + 3] = 255;
        }
        return dst;
    }

    // ─── Histogram ───

    function computeHistogram(pixels) {
        const hist = new Uint32Array(256);
        for (let i = 0; i < pixels.length; i += 4) {
            const gray = Math.round(0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2]);
            hist[gray]++;
        }
        return hist;
    }

    function histogramEqualize(srcData, width, height) {
        const hist = computeHistogram(srcData);
        const totalPixels = width * height;
        const cdf = new Float64Array(256);
        cdf[0] = hist[0];
        for (let i = 1; i < 256; i++) cdf[i] = cdf[i - 1] + hist[i];
        const cdfMin = cdf.find(v => v > 0);
        const lut = new Uint8Array(256);
        for (let i = 0; i < 256; i++) {
            lut[i] = Math.round(((cdf[i] - cdfMin) / (totalPixels - cdfMin)) * 255);
        }
        const dst = new Uint8ClampedArray(srcData.length);
        for (let i = 0; i < srcData.length; i += 4) {
            dst[i] = lut[srcData[i]];
            dst[i + 1] = lut[srcData[i + 1]];
            dst[i + 2] = lut[srcData[i + 2]];
            dst[i + 3] = 255;
        }
        return dst;
    }

    // ─── Core Pipeline ───

    // Build percentile-stretch LUT for auto-enhance (1st..99th percentile -> full 0..255 range)
    function buildAutoEnhanceLut(pixels) {
        const hist = computeHistogram(pixels);
        const total = pixels.length / 4;
        const loCut = total * 0.01, hiCut = total * 0.99;
        let cum = 0, lo = 0, hi = 255;
        for (let i = 0; i < 256; i++) { cum += hist[i]; if (cum >= loCut) { lo = i; break; } }
        cum = 0;
        for (let i = 0; i < 256; i++) { cum += hist[i]; if (cum >= hiCut) { hi = i; break; } }
        if (hi <= lo) { hi = Math.min(255, lo + 1); }
        const scale = 255 / (hi - lo);
        const lut = new Uint8Array(256);
        for (let i = 0; i < 256; i++) {
            lut[i] = Math.min(255, Math.max(0, Math.round((i - lo) * scale)));
        }
        return lut;
    }

    // Gamma LUT cache keyed by gamma value
    let _gammaLutValue = -1;
    let _gammaLut = null;
    function getGammaLut(gamma) {
        if (gamma === _gammaLutValue && _gammaLut) return _gammaLut;
        const lut = new Uint8Array(256);
        const inv = 1 / gamma;
        for (let i = 0; i < 256; i++) {
            lut[i] = Math.min(255, Math.max(0, Math.round(Math.pow(i / 255, inv) * 255)));
        }
        _gammaLutValue = gamma;
        _gammaLut = lut;
        return lut;
    }

    function processPixels() {
        if (!_originalPixels || !_ctx) return;

        let pixels = new Uint8ClampedArray(_originalPixels);

        // 0. Auto-enhance (percentile stretch) — runs first so downstream filters see normalized range
        if (_autoEnhance) {
            if (!_autoEnhanceLut) _autoEnhanceLut = buildAutoEnhanceLut(_originalPixels);
            const lut = _autoEnhanceLut;
            for (let i = 0; i < pixels.length; i += 4) {
                pixels[i] = lut[pixels[i]];
                pixels[i + 1] = lut[pixels[i + 1]];
                pixels[i + 2] = lut[pixels[i + 2]];
            }
        }

        // 0b. Gamma (density) — applied after auto-enhance so operator controls remain responsive
        if (_gamma !== 1.0) {
            const lut = getGammaLut(_gamma);
            for (let i = 0; i < pixels.length; i += 4) {
                pixels[i] = lut[pixels[i]];
                pixels[i + 1] = lut[pixels[i + 1]];
                pixels[i + 2] = lut[pixels[i + 2]];
            }
        }

        // 1. Window/Level
        if (_windowLevel) {
            const w = _wlWindow;
            const l = _wlLevel;
            const lo = l - w / 2;
            const scale = 255 / w;
            for (let i = 0; i < pixels.length; i += 4) {
                pixels[i] = Math.min(255, Math.max(0, (pixels[i] - lo) * scale));
                pixels[i + 1] = Math.min(255, Math.max(0, (pixels[i + 1] - lo) * scale));
                pixels[i + 2] = Math.min(255, Math.max(0, (pixels[i + 2] - lo) * scale));
            }
        }

        // 2. Histogram equalization
        if (_histEqEnabled) {
            pixels = histogramEqualize(pixels, _imgWidth, _imgHeight);
        }

        // 3. Convolution (edge detection, sharpen)
        if (_activeConvolution) {
            switch (_activeConvolution) {
                case 'sobel': pixels = applySobelEdge(pixels, _imgWidth, _imgHeight); break;
                case 'laplacian': pixels = applyConvolution3x3(pixels, _imgWidth, _imgHeight, KERNELS.laplacian); break;
                case 'sharpen': pixels = applyConvolution3x3(pixels, _imgWidth, _imgHeight, KERNELS.sharpen); break;
                case 'emboss': pixels = applyConvolution3x3(pixels, _imgWidth, _imgHeight, KERNELS.emboss); break;
            }
        }

        // 4. Pseudocolor mapping
        if (_activeColorMap) {
            const lut = getColorMap(_activeColorMap);
            if (lut) {
                for (let i = 0; i < pixels.length; i += 4) {
                    const gray = Math.round(0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2]);
                    const color = lut[gray];
                    pixels[i] = color[0];
                    pixels[i + 1] = color[1];
                    pixels[i + 2] = color[2];
                }
            }
        }

        _currentPixels = pixels;
        const imgData = new ImageData(pixels, _imgWidth, _imgHeight);
        _ctx.putImageData(imgData, 0, 0);
    }

    // ─── Public API ───

    function finishInit() {
        _wlWindow = 255;
        _wlLevel = 128;
        _windowLevel = null;
        _activeColorMap = null;
        _activeConvolution = null;
        _histEqEnabled = false;
        _gamma = 1.0;
        _autoEnhance = false;
        _autoEnhanceLut = null;
        return true;
    }

    // Try to read pixels from an already-loaded <img> element. Fails silently on cross-origin taint.
    async function tryInitFromImgElement(imgElement) {
        if (!imgElement || !imgElement.complete || !imgElement.naturalWidth) return false;
        try {
            const bmp = await createImageBitmap(imgElement);
            _imgWidth = bmp.width;
            _imgHeight = bmp.height;
            const maxDim = 2048;
            if (_imgWidth > maxDim || _imgHeight > maxDim) {
                const scale = maxDim / Math.max(_imgWidth, _imgHeight);
                _imgWidth = Math.floor(_imgWidth * scale);
                _imgHeight = Math.floor(_imgHeight * scale);
            }
            _canvas.width = _imgWidth;
            _canvas.height = _imgHeight;
            _ctx.drawImage(bmp, 0, 0, _imgWidth, _imgHeight);
            // This throws SecurityError if canvas is tainted — which means cross-origin without CORS.
            _originalPixels = new Uint8ClampedArray(_ctx.getImageData(0, 0, _imgWidth, _imgHeight).data);
            _currentPixels = new Uint8ClampedArray(_originalPixels);
            console.log('[Canvas] Initialized from <img> element', _imgWidth, 'x', _imgHeight);
            return true;
        } catch (e) {
            console.warn('[Canvas] initFromImage failed (will fall back to fetch):', e.message);
            return false;
        }
    }

    // Load a cross-origin image with crossOrigin="anonymous" so canvas isn't tainted.
    // Requires the API to send Access-Control-Allow-Origin header for the image bytes.
    function loadViaCorsImage(imageUrl) {
        return new Promise((resolve, reject) => {
            const img = new Image();
            img.crossOrigin = 'anonymous';
            const timer = setTimeout(() => { img.src = ''; reject(new Error('CORS image load timeout')); }, 30000);
            img.onload = () => {
                clearTimeout(timer);
                try {
                    _imgWidth = img.naturalWidth; _imgHeight = img.naturalHeight;
                    const maxDim = 2048;
                    if (_imgWidth > maxDim || _imgHeight > maxDim) {
                        const scale = maxDim / Math.max(_imgWidth, _imgHeight);
                        _imgWidth = Math.floor(_imgWidth * scale);
                        _imgHeight = Math.floor(_imgHeight * scale);
                    }
                    _canvas.width = _imgWidth; _canvas.height = _imgHeight;
                    _ctx.drawImage(img, 0, 0, _imgWidth, _imgHeight);
                    _originalPixels = new Uint8ClampedArray(_ctx.getImageData(0, 0, _imgWidth, _imgHeight).data);
                    _currentPixels = new Uint8ClampedArray(_originalPixels);
                    resolve(true);
                } catch (e) {
                    reject(e);
                }
            };
            img.onerror = () => { clearTimeout(timer); reject(new Error('CORS image failed (no Access-Control-Allow-Origin header?)')); };
            img.src = imageUrl;
        });
    }

    // Fetch with timeout + one retry on transient failures
    async function fetchImageWithRetry(imageUrl, timeoutMs) {
        const tryOnce = async () => {
            const controller = new AbortController();
            const timer = setTimeout(() => controller.abort(), timeoutMs);
            try {
                const resp = await fetch(imageUrl, { signal: controller.signal, cache: 'force-cache' });
                if (!resp.ok) throw new Error('HTTP ' + resp.status + ' ' + resp.statusText);
                return await resp.blob();
            } finally {
                clearTimeout(timer);
            }
        };
        try {
            return await tryOnce();
        } catch (e1) {
            console.warn('[Canvas] Fetch attempt 1 failed:', e1.message, '- retrying in 800ms');
            await new Promise(r => setTimeout(r, 800));
            return await tryOnce();
        }
    }

    return {
        /**
         * Initialize the canvas from a same-origin image URL (e.g. from app's image proxy).
         * Fetches the image and draws it onto the canvas for pixel access — no CORS issues.
         */
        initWithUrl: async function (canvasElement, imageUrl, dotNetRef, imgElement) {
            if (!canvasElement) throw new Error('canvas element is null (component not yet rendered)');
            if (!imageUrl) throw new Error('image URL is empty (no current image selected)');

            _canvas = canvasElement;
            _ctx = _canvas.getContext('2d', { willReadFrequently: true });
            _dotNetRef = dotNetRef;

            // PATH A: reuse the already-loaded <img> element when same-origin (no double fetch).
            // Cross-origin images taint the canvas — we silently fall through to PATH B.
            if (imgElement && await tryInitFromImgElement(imgElement)) {
                return finishInit();
            }

            // Reset canvas to clear any taint from the failed Path A attempt
            _canvas.width = 1; _canvas.height = 1;

            // PATH B: load via Image() with crossOrigin=anonymous (bypasses proxy when API sends CORS).
            // The image API endpoint is [AllowAnonymous]; if it ALSO sends Access-Control-Allow-Origin,
            // this works without any proxy at all.
            const directUrl = imgElement && imgElement.src ? imgElement.src : imageUrl;
            try {
                const ok = await loadViaCorsImage(directUrl);
                if (ok) { console.log('[Canvas] Initialized via CORS Image()', _imgWidth, 'x', _imgHeight); return finishInit(); }
            } catch (e) {
                console.warn('[Canvas] CORS Image() path failed:', e.message);
            }

            // PATH C: fetch through proxy (with retry + timeout) — last resort
            try {
                console.log('[Canvas] Fetching image via proxy:', imageUrl);
                const blob = await fetchImageWithRetry(imageUrl, 45000);
                const bmp = await createImageBitmap(blob);

                _imgWidth = bmp.width;
                _imgHeight = bmp.height;
                const maxDim = 2048;
                if (_imgWidth > maxDim || _imgHeight > maxDim) {
                    const scale = maxDim / Math.max(_imgWidth, _imgHeight);
                    _imgWidth = Math.floor(_imgWidth * scale);
                    _imgHeight = Math.floor(_imgHeight * scale);
                }

                _canvas.width = _imgWidth;
                _canvas.height = _imgHeight;
                _ctx.drawImage(bmp, 0, 0, _imgWidth, _imgHeight);
                _originalPixels = new Uint8ClampedArray(_ctx.getImageData(0, 0, _imgWidth, _imgHeight).data);
                _currentPixels = new Uint8ClampedArray(_originalPixels);
                console.log('[Canvas] Initialized via proxy fetch', _imgWidth, 'x', _imgHeight);
                return finishInit();
            } catch (e) {
                const msg = '[Canvas] All init paths failed. Last error: ' + e.message + '. URL: ' + imageUrl;
                console.error(msg);
                throw new Error(e.message + ' (proxy: ' + imageUrl.substring(0, 80) + '...)');
            }
        },

        /** @deprecated Use initWithUrl with proxy URL from Blazor */
        init: async function (canvasElement, imgElement, dotNetRef) {
            if (!imgElement || !imgElement.src) return false;
            return window.ImageCanvasProcessor.initWithUrl(canvasElement, imgElement.src, dotNetRef, imgElement);
        },

        // ─── Density / Gamma ───
        setGamma: function (gamma) {
            _gamma = Math.max(0.2, Math.min(4.0, gamma || 1.0));
            processPixels();
            return _gamma;
        },
        adjustGamma: function (delta) {
            _gamma = Math.max(0.2, Math.min(4.0, _gamma * (1 + (delta || 0))));
            processPixels();
            return _gamma;
        },
        resetGamma: function () {
            _gamma = 1.0;
            processPixels();
            return _gamma;
        },
        getGamma: function () { return _gamma; },

        // ─── Auto-Enhance (percentile stretch 1..99%) ───
        setAutoEnhance: function (enabled) {
            _autoEnhance = !!enabled;
            if (!_autoEnhance) _autoEnhanceLut = null; // force recompute when re-enabled
            processPixels();
            return _autoEnhance;
        },
        isAutoEnhance: function () { return _autoEnhance; },

        // ─── State Snapshots (for undo/redo) ───
        getStateSnapshot: function () {
            return {
                windowLevel: _windowLevel ? { window: _wlWindow, level: _wlLevel } : null,
                colorMap: _activeColorMap,
                convolution: _activeConvolution,
                histEq: _histEqEnabled,
                gamma: _gamma,
                autoEnhance: _autoEnhance
            };
        },
        applyStateSnapshot: function (s) {
            if (!s) return;
            _windowLevel = s.windowLevel ? true : null;
            _wlWindow = s.windowLevel ? s.windowLevel.window : 255;
            _wlLevel = s.windowLevel ? s.windowLevel.level : 128;
            _activeColorMap = s.colorMap || null;
            _activeConvolution = s.convolution || null;
            _histEqEnabled = !!s.histEq;
            _gamma = s.gamma || 1.0;
            if (!!s.autoEnhance !== _autoEnhance) {
                _autoEnhance = !!s.autoEnhance;
                _autoEnhanceLut = null;
            }
            processPixels();
        },

        isVisible: function () { return _isCanvasVisible; },

        /**
         * Apply a named filter. Passing null/empty disables that category.
         * @param {string} category - 'colorMap'|'convolution'|'histEq'|'windowLevel'
         * @param {string|null} value - filter name or null to disable
         * @param {object} params - optional params (e.g. { window: 200, level: 100 })
         */
        applyFilter: function (category, value, params) {
            switch (category) {
                case 'colorMap':
                    _activeColorMap = value || null;
                    break;
                case 'convolution':
                    _activeConvolution = value || null;
                    break;
                case 'histEq':
                    _histEqEnabled = !!value;
                    break;
                case 'windowLevel':
                    if (value) {
                        _windowLevel = true;
                        if (params) {
                            _wlWindow = params.window ?? 255;
                            _wlLevel = params.level ?? 128;
                        }
                    } else {
                        _windowLevel = null;
                        _wlWindow = 255;
                        _wlLevel = 128;
                    }
                    break;
            }
            processPixels();
        },

        resetAll: function () {
            _windowLevel = null;
            _activeColorMap = null;
            _activeConvolution = null;
            _histEqEnabled = false;
            _wlWindow = 255;
            _wlLevel = 128;
            _gamma = 1.0;
            _autoEnhance = false;
            _autoEnhanceLut = null;
            processPixels();
        },

        // ─── Window/Level Drag ───

        attachWindowLevelDrag: function (containerElement) {
            if (!containerElement) return;

            const onContextMenu = (e) => e.preventDefault();

            const onMouseDown = (e) => {
                if (e.button !== 2) return; // right-click only
                e.preventDefault();
                _wlDragging = true;
                _wlStartX = e.clientX;
                _wlStartY = e.clientY;
                _wlStartWindow = _wlWindow;
                _wlStartLevel = _wlLevel;
                if (!_windowLevel) _windowLevel = true;
            };

            const onMouseMove = (e) => {
                if (!_wlDragging) return;
                const dx = e.clientX - _wlStartX;
                const dy = e.clientY - _wlStartY;
                _wlWindow = Math.max(1, Math.min(510, _wlStartWindow + dx));
                _wlLevel = Math.max(0, Math.min(255, _wlStartLevel - dy));
                processPixels();
                if (_dotNetRef) {
                    _dotNetRef.invokeMethodAsync('OnWindowLevelChanged', Math.round(_wlWindow), Math.round(_wlLevel));
                }
            };

            const onMouseUp = (e) => {
                if (e.button === 2) _wlDragging = false;
            };

            containerElement.addEventListener('contextmenu', onContextMenu);
            containerElement.addEventListener('mousedown', onMouseDown);
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);

            containerElement._wlCleanup = () => {
                containerElement.removeEventListener('contextmenu', onContextMenu);
                containerElement.removeEventListener('mousedown', onMouseDown);
                document.removeEventListener('mousemove', onMouseMove);
                document.removeEventListener('mouseup', onMouseUp);
            };
        },

        detachWindowLevelDrag: function (containerElement) {
            if (containerElement && containerElement._wlCleanup) {
                containerElement._wlCleanup();
                delete containerElement._wlCleanup;
            }
        },

        // ─── Histogram ───

        getHistogramData: function () {
            if (!_currentPixels) return null;
            const hist = computeHistogram(_currentPixels);
            return Array.from(hist);
        },

        renderHistogram: function (histCanvas) {
            if (!histCanvas || !_currentPixels) return;
            const hist = computeHistogram(_currentPixels);
            const ctx = histCanvas.getContext('2d');
            const w = histCanvas.width = 256;
            const h = histCanvas.height = 100;
            ctx.fillStyle = '#1a1a1a';
            ctx.fillRect(0, 0, w, h);
            const maxVal = Math.max(...hist);
            if (maxVal === 0) return;
            ctx.fillStyle = '#4caf50';
            for (let i = 0; i < 256; i++) {
                const barH = (hist[i] / maxVal) * h;
                ctx.fillRect(i, h - barH, 1, barH);
            }
            // draw window/level indicators
            if (_windowLevel) {
                const lo = Math.max(0, _wlLevel - _wlWindow / 2);
                const hi = Math.min(255, _wlLevel + _wlWindow / 2);
                ctx.strokeStyle = 'rgba(255, 152, 0, 0.8)';
                ctx.lineWidth = 1;
                ctx.beginPath(); ctx.moveTo(lo, 0); ctx.lineTo(lo, h); ctx.stroke();
                ctx.beginPath(); ctx.moveTo(hi, 0); ctx.lineTo(hi, h); ctx.stroke();
                ctx.fillStyle = 'rgba(255, 152, 0, 0.15)';
                ctx.fillRect(lo, 0, hi - lo, h);
            }
        },

        // ─── Magnifying Glass / Loupe ───

        initLoupe: function (loupeCanvasElement, size, zoom) {
            _loupeCanvas = loupeCanvasElement;
            _loupeCtx = loupeCanvasElement?.getContext('2d');
            _loupeSize = size || 180;
            _loupeZoom = zoom || 3;
            if (_loupeCanvas) {
                _loupeCanvas.width = _loupeSize;
                _loupeCanvas.height = _loupeSize;
                _loupeCanvas.style.display = 'none';
            }
        },

        attachLoupe: function (containerElement) {
            if (!containerElement || !_loupeCanvas) return;
            _loupeActive = true;

            const onMove = (e) => {
                if (!_loupeActive || !_canvas) return;
                // Use the container's rect for mouse positioning
                const containerRect = containerElement.getBoundingClientRect();
                // Use the canvas or image for pixel source
                const sourceEl = _canvas || containerElement.querySelector('img');
                if (!sourceEl) return;
                const sourceRect = sourceEl.getBoundingClientRect();
                const scaleX = _imgWidth / sourceRect.width;
                const scaleY = _imgHeight / sourceRect.height;
                const cx = (e.clientX - sourceRect.left) * scaleX;
                const cy = (e.clientY - sourceRect.top) * scaleY;

                // Don't render if cursor is outside the image area
                if (cx < 0 || cy < 0 || cx > _imgWidth || cy > _imgHeight) {
                    _loupeCanvas.style.opacity = '0';
                    return;
                }

                const srcSize = _loupeSize / _loupeZoom;
                const sx = cx - srcSize / 2;
                const sy = cy - srcSize / 2;

                _loupeCtx.clearRect(0, 0, _loupeSize, _loupeSize);
                _loupeCtx.save();
                _loupeCtx.beginPath();
                _loupeCtx.arc(_loupeSize / 2, _loupeSize / 2, _loupeSize / 2, 0, Math.PI * 2);
                _loupeCtx.clip();

                _loupeCtx.drawImage(_canvas, sx, sy, srcSize, srcSize, 0, 0, _loupeSize, _loupeSize);

                _loupeCtx.restore();
                _loupeCtx.strokeStyle = '#ff9800';
                _loupeCtx.lineWidth = 3;
                _loupeCtx.beginPath();
                _loupeCtx.arc(_loupeSize / 2, _loupeSize / 2, _loupeSize / 2 - 1, 0, Math.PI * 2);
                _loupeCtx.stroke();

                // Position relative to the outer container
                _loupeCanvas.style.opacity = '1';
                _loupeCanvas.style.left = (e.clientX - containerRect.left + 20) + 'px';
                _loupeCanvas.style.top = (e.clientY - containerRect.top - _loupeSize / 2) + 'px';
            };

            containerElement.addEventListener('mousemove', onMove);

            containerElement._loupeCleanup = () => {
                containerElement.removeEventListener('mousemove', onMove);
            };
        },

        detachLoupe: function (containerElement) {
            _loupeActive = false;
            if (containerElement && containerElement._loupeCleanup) {
                containerElement._loupeCleanup();
                delete containerElement._loupeCleanup;
            }
        },

        // ─── Utilities ───

        getWindowLevel: function () {
            return { window: Math.round(_wlWindow), level: Math.round(_wlLevel) };
        },

        getDimensions: function () {
            return { width: _imgWidth, height: _imgHeight };
        },

        dispose: function () {
            _canvas = null; _ctx = null;
            _originalPixels = null; _currentPixels = null;
            _loupeCanvas = null; _loupeCtx = null;
            _dotNetRef = null;
        }
    };
})();

/**
 * Image Analysis Tools — ruler (measurement), action history (undo/redo).
 * Companion module to imageCanvasProcessor.js and imageAnalysisViewer.js.
 */
window.ImageAnalysisTools = (function () {
    'use strict';

    // ─── Ruler ────────────────────────────────────────────────────────────
    // Attaches an SVG overlay to the image container. Two clicks define a
    // measurement line; distance is displayed in mm using a configurable
    // mm-per-pixel (scanner-specific calibration).

    const _rulers = new WeakMap(); // containerEl -> state

    function ensureSvg(container) {
        let svg = container.querySelector(':scope > svg.ias-ruler-layer');
        if (!svg) {
            svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.classList.add('ias-ruler-layer');
            svg.setAttribute('style', 'position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:40;');
            container.appendChild(svg);
        }
        return svg;
    }

    function clearSvg(svg) { while (svg.firstChild) svg.removeChild(svg.firstChild); }

    function drawRuler(svg, p1, p2, distanceMm) {
        clearSvg(svg);
        const ns = 'http://www.w3.org/2000/svg';
        const line = document.createElementNS(ns, 'line');
        line.setAttribute('x1', p1.x); line.setAttribute('y1', p1.y);
        line.setAttribute('x2', p2.x); line.setAttribute('y2', p2.y);
        line.setAttribute('stroke', '#ffeb3b');
        line.setAttribute('stroke-width', '2');
        line.setAttribute('stroke-dasharray', '6,3');
        svg.appendChild(line);

        // Endpoint markers
        for (const p of [p1, p2]) {
            const c = document.createElementNS(ns, 'circle');
            c.setAttribute('cx', p.x); c.setAttribute('cy', p.y);
            c.setAttribute('r', 4);
            c.setAttribute('fill', '#ffeb3b');
            c.setAttribute('stroke', '#000'); c.setAttribute('stroke-width', '1');
            svg.appendChild(c);
        }

        // Label (mid-point)
        const mx = (p1.x + p2.x) / 2, my = (p1.y + p2.y) / 2;
        const bg = document.createElementNS(ns, 'rect');
        const label = (distanceMm >= 1000)
            ? (distanceMm / 1000).toFixed(2) + ' m'
            : Math.round(distanceMm) + ' mm';
        bg.setAttribute('x', mx - 32); bg.setAttribute('y', my - 22);
        bg.setAttribute('width', 64); bg.setAttribute('height', 18);
        bg.setAttribute('fill', 'rgba(0,0,0,0.75)');
        bg.setAttribute('rx', 3);
        svg.appendChild(bg);
        const txt = document.createElementNS(ns, 'text');
        txt.setAttribute('x', mx); txt.setAttribute('y', my - 9);
        txt.setAttribute('text-anchor', 'middle');
        txt.setAttribute('fill', '#ffeb3b');
        txt.setAttribute('font-size', '12');
        txt.setAttribute('font-weight', '600');
        txt.textContent = label;
        svg.appendChild(txt);
    }

    function attachRuler(container, mmPerPixel) {
        if (!container) return;
        detachRuler(container);
        const svg = ensureSvg(container);
        clearSvg(svg);

        const state = {
            svg: svg,
            points: [],
            mmPerPixel: mmPerPixel || 1.0,
            container: container
        };

        const onClick = (e) => {
            const rect = container.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;

            if (state.points.length === 2) {
                state.points = [];
                clearSvg(svg);
            }
            state.points.push({ x, y });
            if (state.points.length === 1) {
                // Show the starting dot
                const ns = 'http://www.w3.org/2000/svg';
                const c = document.createElementNS(ns, 'circle');
                c.setAttribute('cx', x); c.setAttribute('cy', y);
                c.setAttribute('r', 4);
                c.setAttribute('fill', '#ffeb3b');
                c.setAttribute('stroke', '#000'); c.setAttribute('stroke-width', '1');
                svg.appendChild(c);
            } else if (state.points.length === 2) {
                const [p1, p2] = state.points;
                const dxPx = p2.x - p1.x, dyPx = p2.y - p1.y;
                // Account for the rendered-vs-natural image scale by comparing container and natural img sizes
                const img = container.querySelector('img');
                let scale = 1;
                if (img && img.naturalWidth && img.clientWidth) {
                    scale = img.naturalWidth / img.clientWidth;
                }
                const distancePx = Math.hypot(dxPx, dyPx) * scale;
                const distanceMm = distancePx * state.mmPerPixel;
                drawRuler(svg, p1, p2, distanceMm);
            }
        };

        const onKey = (e) => {
            if (e.key === 'Escape') {
                state.points = [];
                clearSvg(svg);
            }
        };

        // Overlay needs pointer events for clicking, but we use the container
        // (pointer-events:none on svg) and listen on container itself.
        container.addEventListener('click', onClick);
        document.addEventListener('keydown', onKey);
        state.cleanup = () => {
            container.removeEventListener('click', onClick);
            document.removeEventListener('keydown', onKey);
            clearSvg(svg);
            if (svg.parentNode) svg.parentNode.removeChild(svg);
        };
        _rulers.set(container, state);
    }

    function detachRuler(container) {
        const state = _rulers.get(container);
        if (state && state.cleanup) state.cleanup();
        _rulers.delete(container);
    }

    function clearRuler(container) {
        const state = _rulers.get(container);
        if (state) {
            state.points = [];
            clearSvg(state.svg);
        }
    }

    // ─── Action History (Undo / Redo) ────────────────────────────────────
    // Command-pattern stacks. Each entry is an opaque "state" snapshot from
    // the host Razor component that knows how to restore itself.

    const _history = {
        undo: [],  // past states
        redo: [],  // future states (populated on undo)
        max: 50
    };

    function pushState(state) {
        if (!state) return;
        _history.undo.push(state);
        if (_history.undo.length > _history.max) _history.undo.shift();
        _history.redo.length = 0; // any new action clears redo
    }

    function undo() {
        if (_history.undo.length <= 1) return null; // keep initial state
        const current = _history.undo.pop();
        _history.redo.push(current);
        return _history.undo[_history.undo.length - 1];
    }

    function redo() {
        if (_history.redo.length === 0) return null;
        const next = _history.redo.pop();
        _history.undo.push(next);
        return next;
    }

    function undoAll() {
        if (_history.undo.length <= 1) return null;
        while (_history.undo.length > 1) {
            _history.redo.push(_history.undo.pop());
        }
        return _history.undo[0];
    }

    function redoAll() {
        if (_history.redo.length === 0) return null;
        while (_history.redo.length > 0) {
            _history.undo.push(_history.redo.pop());
        }
        return _history.undo[_history.undo.length - 1];
    }

    function reset() { _history.undo.length = 0; _history.redo.length = 0; }
    function canUndo() { return _history.undo.length > 1; }
    function canRedo() { return _history.redo.length > 0; }
    function size() { return { undo: _history.undo.length, redo: _history.redo.length }; }

    // Persist the action list so "Reload Actions" can replay from DB later.
    function serialize() {
        try { return JSON.stringify(_history.undo); } catch { return null; }
    }
    function deserialize(json) {
        try {
            const arr = JSON.parse(json);
            if (Array.isArray(arr)) {
                _history.undo = arr;
                _history.redo = [];
                return _history.undo[_history.undo.length - 1] || null;
            }
        } catch { }
        return null;
    }

    return {
        // Ruler
        attachRuler: attachRuler,
        detachRuler: detachRuler,
        clearRuler: clearRuler,
        // History
        pushState: pushState,
        undo: undo,
        redo: redo,
        undoAll: undoAll,
        redoAll: redoAll,
        resetHistory: reset,
        canUndo: canUndo,
        canRedo: canRedo,
        historySize: size,
        serializeHistory: serialize,
        deserializeHistory: deserialize
    };
})();

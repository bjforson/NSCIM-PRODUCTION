/**
 * Image Analysis Viewer - JS Interop Module
 * Handles performance-critical operations client-side to avoid SignalR round-trips.
 */

window.ImageAnalysisViewer = {

    /**
     * Safe download: fetches image as blob and triggers browser download.
     * @param {string} url - Image URL to download
     * @param {string} fileName - Suggested filename
     */
    downloadImage: function (url, fileName) {
        fetch(url)
            .then(r => r.blob())
            .then(blob => {
                const a = document.createElement('a');
                a.href = URL.createObjectURL(blob);
                a.download = fileName;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(a.href);
            })
            .catch(err => console.error('[ImageViewer] Download failed:', err));
    },

    /**
     * Print image in a new window.
     * @param {string} url - Image URL
     * @param {string} title - Window title
     */
    printImage: function (url, title) {
        const w = window.open('', '_blank');
        if (!w) return;
        w.document.write(
            `<html><head><title>Print - ${title}</title></head>` +
            `<body style="margin:0;display:flex;justify-content:center;align-items:center;min-height:100vh;">` +
            `<img src="${url}" style="max-width:100%;max-height:100vh;" />` +
            `</body></html>`
        );
        w.document.close();
        w.focus();
        setTimeout(() => w.print(), 500);
    },

    /**
     * Toggle native browser fullscreen.
     * @param {boolean} enter - true to enter, false to exit
     */
    toggleFullscreen: function (enter) {
        if (enter) {
            document.documentElement.requestFullscreen().catch(() => { });
        } else if (document.fullscreenElement) {
            document.exitFullscreen().catch(() => { });
        }
    },

    /**
     * Attach scroll-to-zoom on an element. Calls back to .NET with delta.
     * @param {DotNetObjectReference} dotNetRef - .NET object reference
     * @param {HTMLElement} element - Container element
     */
    _wheelHandlers: new WeakMap(),
    attachWheelZoom: function (dotNetRef, element) {
        if (!element) return;
        // Send scroll direction (+1 zoom in, -1 zoom out). C# applies a small
        // multiplicative factor so zoom feels smooth at all levels.
        const handler = function (e) {
            e.preventDefault();
            const direction = e.deltaY > 0 ? -1 : 1;
            dotNetRef.invokeMethodAsync('OnScrollZoom', direction);
        };
        element.addEventListener('wheel', handler, { passive: false });
        ImageAnalysisViewer._wheelHandlers.set(element, handler);
    },

    /**
     * Detach scroll-to-zoom listener.
     */
    detachWheelZoom: function (element) {
        if (!element) return;
        const handler = ImageAnalysisViewer._wheelHandlers.get(element);
        if (handler) {
            element.removeEventListener('wheel', handler);
            ImageAnalysisViewer._wheelHandlers.delete(element);
        }
    },

    /**
     * Attach keyboard shortcuts. Calls back to .NET with action name.
     * @param {DotNetObjectReference} dotNetRef
     */
    _keyHandler: null,
    attachKeyboardShortcuts: function (dotNetRef) {
        if (ImageAnalysisViewer._keyHandler) {
            document.removeEventListener('keydown', ImageAnalysisViewer._keyHandler);
        }
        ImageAnalysisViewer._keyHandler = function (e) {
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT' || e.target.isContentEditable) return;
            let action = null;
            switch (e.key) {
                case '+': case '=': action = 'ZoomIn'; break;
                case '-': case '_': action = 'ZoomOut'; break;
                case '0': action = 'ResetZoom'; break;
                case 'r': case 'R': action = e.shiftKey ? 'RotateLeft' : 'RotateRight'; break;
                case 'g': case 'G': action = 'ToggleGrayscale'; break;
                case 'i': case 'I': action = 'ToggleInvert'; break;
                case 'n': case 'N': action = 'DecisionNormal'; break;
                case 'a': case 'A': action = 'DecisionAbnormal'; break;
                case 'd': case 'D': action = 'ToggleDrawMode'; break;
                case 'e': case 'E': action = 'ToggleEnhanced'; break;
                case 'm': case 'M': action = 'ToggleLoupe'; break;
                case 'h': case 'H': action = 'ToggleHistogram'; break;
                case 'w': case 'W': action = 'ToggleWindowLevel'; break;
                case 'Escape': action = 'Close'; break;
                case 'F11': e.preventDefault(); action = 'Fullscreen'; break;
            }
            if (action) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnKeyboardAction', action);
            }
        };
        document.addEventListener('keydown', ImageAnalysisViewer._keyHandler);
    },

    detachKeyboardShortcuts: function () {
        if (ImageAnalysisViewer._keyHandler) {
            document.removeEventListener('keydown', ImageAnalysisViewer._keyHandler);
            ImageAnalysisViewer._keyHandler = null;
        }
    },

    /**
     * Get the natural dimensions of a loaded image element.
     * @param {HTMLElement} imgElement
     * @returns {{ naturalWidth: number, naturalHeight: number, clientWidth: number, clientHeight: number }}
     */
    getImageDimensions: function (imgElement) {
        if (!imgElement) return { naturalWidth: 0, naturalHeight: 0, clientWidth: 0, clientHeight: 0 };
        return {
            naturalWidth: imgElement.naturalWidth,
            naturalHeight: imgElement.naturalHeight,
            clientWidth: imgElement.clientWidth,
            clientHeight: imgElement.clientHeight
        };
    }
};

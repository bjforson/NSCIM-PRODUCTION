/**
 * Lightweight image magnifier — attaches a circular magnifying glass to any <img> element.
 * No canvas dependency; works by creating a positioned <div> with background-image.
 */
window.ImageMagnifier = (function () {
    'use strict';

    const instances = new Map(); // element -> cleanup function

    function attach(imgElement, opts) {
        if (!imgElement) return;
        detach(imgElement); // clean up any previous instance

        const size = (opts && opts.size) || 200;
        const zoom = (opts && opts.zoom) || 2.5;

        // Create the magnifier lens
        const lens = document.createElement('div');
        lens.className = 'img-magnifier-lens';
        Object.assign(lens.style, {
            position: 'absolute',
            width: size + 'px',
            height: size + 'px',
            borderRadius: '50%',
            border: '3px solid rgba(100, 181, 246, 0.8)',
            boxShadow: '0 0 12px rgba(0,0,0,0.4), inset 0 0 8px rgba(0,0,0,0.1)',
            backgroundRepeat: 'no-repeat',
            pointerEvents: 'none',
            zIndex: '50',
            display: 'none',
            cursor: 'none',
            transition: 'opacity 0.15s ease'
        });

        // Insert lens as sibling to img inside its positioned parent
        const parent = imgElement.parentElement;
        if (parent) {
            // Ensure parent is positioned
            const pos = getComputedStyle(parent).position;
            if (pos === 'static' || pos === '') {
                parent.style.position = 'relative';
            }
            parent.appendChild(lens);
        }

        function onMove(e) {
            const rect = imgElement.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;

            // Hide if outside image bounds
            if (x < 0 || y < 0 || x > rect.width || y > rect.height) {
                lens.style.display = 'none';
                return;
            }

            lens.style.display = 'block';

            // Compute background properties
            const bgWidth = rect.width * zoom;
            const bgHeight = rect.height * zoom;
            const bgX = -(x * zoom - size / 2);
            const bgY = -(y * zoom - size / 2);

            lens.style.backgroundImage = 'url("' + imgElement.src + '")';
            lens.style.backgroundSize = bgWidth + 'px ' + bgHeight + 'px';
            lens.style.backgroundPosition = bgX + 'px ' + bgY + 'px';

            // Position lens near cursor (offset to not obscure view)
            lens.style.left = (x + 15) + 'px';
            lens.style.top = (y - size / 2) + 'px';

            // Clamp to parent bounds
            const pRect = parent.getBoundingClientRect();
            const lensLeft = parseFloat(lens.style.left);
            const lensTop = parseFloat(lens.style.top);
            if (lensLeft + size > pRect.width) {
                lens.style.left = (x - size - 15) + 'px';
            }
            if (lensTop < 0) lens.style.top = '0px';
            if (lensTop + size > pRect.height) lens.style.top = (pRect.height - size) + 'px';
        }

        function onLeave() {
            lens.style.display = 'none';
        }

        imgElement.addEventListener('mousemove', onMove);
        imgElement.addEventListener('mouseleave', onLeave);
        if (parent) {
            parent.addEventListener('mouseleave', onLeave);
        }

        instances.set(imgElement, () => {
            imgElement.removeEventListener('mousemove', onMove);
            imgElement.removeEventListener('mouseleave', onLeave);
            if (parent) {
                parent.removeEventListener('mouseleave', onLeave);
                if (lens.parentNode === parent) parent.removeChild(lens);
            }
        });
    }

    function detach(imgElement) {
        if (!imgElement) return;
        const cleanup = instances.get(imgElement);
        if (cleanup) {
            cleanup();
            instances.delete(imgElement);
        }
    }

    function detachAll() {
        instances.forEach((cleanup) => cleanup());
        instances.clear();
    }

    return { attach, detach, detachAll };
})();

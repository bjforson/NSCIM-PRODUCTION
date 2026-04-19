/**
 * Split Review hover coordinate tracker.
 * Shows a crosshair + x,y readout (in original image pixels) when the
 * operator hovers over the scan image in the expanded review panel.
 *
 * Called from Blazor via JSInterop:
 *   await JS.InvokeVoidAsync("splitReviewHover.init", elementId, imageWidth, imageHeight);
 *   splitReviewHover.destroy(elementId);
 */
window.splitReviewHover = {
    _instances: {},

    init: function (containerId, imageWidth, imageHeight) {
        const container = document.getElementById(containerId);
        if (!container) return;

        // Find the <img> inside the container
        const img = container.querySelector('img');
        if (!img) return;

        // Create overlay elements
        const overlay = document.createElement('div');
        overlay.className = 'split-hover-overlay';
        overlay.style.cssText = 'position:absolute;top:0;left:0;right:0;bottom:0;cursor:crosshair;z-index:10;pointer-events:all;';

        const vLine = document.createElement('div');
        vLine.style.cssText = 'position:absolute;top:0;bottom:0;width:2px;background:rgba(76,175,80,0.85);pointer-events:none;display:none;z-index:11;';

        const hLine = document.createElement('div');
        hLine.style.cssText = 'position:absolute;left:0;right:0;height:2px;background:rgba(76,175,80,0.85);pointer-events:none;display:none;z-index:11;';

        const label = document.createElement('div');
        label.style.cssText = 'position:absolute;background:rgba(0,0,0,0.85);color:#a5d6a7;padding:4px 10px;border-radius:4px;font-family:monospace;font-size:14px;font-weight:bold;pointer-events:none;display:none;z-index:12;border:1px solid #4caf50;white-space:nowrap;';

        // Make sure the container is positioned for absolute children
        const parentDiv = img.parentElement;
        if (parentDiv) {
            parentDiv.style.position = 'relative';
            parentDiv.appendChild(overlay);
            parentDiv.appendChild(vLine);
            parentDiv.appendChild(hLine);
            parentDiv.appendChild(label);
        }

        const onMove = function (e) {
            const rect = img.getBoundingClientRect();
            const cssX = e.clientX - rect.left;
            const cssY = e.clientY - rect.top;

            // Convert CSS pixels to image pixels
            const scaleX = imageWidth / rect.width;
            const scaleY = imageHeight / rect.height;
            const imgX = Math.round(cssX * scaleX);
            const imgY = Math.round(cssY * scaleY);

            if (imgX < 0 || imgX >= imageWidth || imgY < 0 || imgY >= imageHeight) {
                vLine.style.display = 'none';
                hLine.style.display = 'none';
                label.style.display = 'none';
                return;
            }

            // Position crosshair lines relative to the image element
            const offsetLeft = img.offsetLeft;
            const offsetTop = img.offsetTop;

            vLine.style.display = 'block';
            vLine.style.left = (offsetLeft + cssX) + 'px';
            vLine.style.top = offsetTop + 'px';
            vLine.style.height = rect.height + 'px';

            hLine.style.display = 'block';
            hLine.style.left = offsetLeft + 'px';
            hLine.style.top = (offsetTop + cssY) + 'px';
            hLine.style.width = rect.width + 'px';

            // Position label near cursor
            label.style.display = 'block';
            label.textContent = `x=${imgX}  y=${imgY}`;
            let labelX = offsetLeft + cssX + 14;
            let labelY = offsetTop + cssY + 14;
            // Keep label on screen
            if (labelX + 150 > parentDiv.offsetWidth) labelX = offsetLeft + cssX - 160;
            if (labelY + 30 > parentDiv.offsetHeight) labelY = offsetTop + cssY - 40;
            label.style.left = labelX + 'px';
            label.style.top = labelY + 'px';
        };

        const onLeave = function () {
            vLine.style.display = 'none';
            hLine.style.display = 'none';
            label.style.display = 'none';
        };

        overlay.addEventListener('mousemove', onMove);
        overlay.addEventListener('mouseleave', onLeave);

        this._instances[containerId] = { overlay, vLine, hLine, label, onMove, onLeave, parentDiv };
    },

    destroy: function (containerId) {
        const inst = this._instances[containerId];
        if (!inst) return;
        inst.overlay.removeEventListener('mousemove', inst.onMove);
        inst.overlay.removeEventListener('mouseleave', inst.onLeave);
        if (inst.parentDiv) {
            inst.parentDiv.removeChild(inst.overlay);
            inst.parentDiv.removeChild(inst.vLine);
            inst.parentDiv.removeChild(inst.hLine);
            inst.parentDiv.removeChild(inst.label);
        }
        delete this._instances[containerId];
    }
};

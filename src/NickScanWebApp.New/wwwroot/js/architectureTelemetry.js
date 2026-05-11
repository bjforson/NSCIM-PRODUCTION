window.architectureTelemetry = {
    post: function (frameId, payload) {
        const frame = document.getElementById(frameId);
        if (!frame || !frame.contentWindow) {
            return;
        }

        frame.contentWindow.postMessage(payload, window.location.origin);
    }
};

// Decision transition animations for Image Analysis workflow
window.DecisionTransitions = {
    // Flash overlay when a decision is saved (green for Normal, red for Abnormal)
    showDecisionSaved: function (decision) {
        var color = decision === 'Normal' ? 'rgba(76,175,80,0.3)' : 'rgba(244,67,54,0.3)';
        var icon = decision === 'Normal' ? '\u2713' : '\u2717';
        var overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;width:100vw;height:100vh;z-index:99999;display:flex;align-items:center;justify-content:center;background:' + color + ';pointer-events:none;animation:fadeIn 0.2s ease-out;';
        var iconEl = document.createElement('div');
        iconEl.style.cssText = 'font-size:80px;color:white;text-shadow:0 0 20px rgba(0,0,0,0.3);animation:bounceIn 0.5s ease-out;';
        iconEl.textContent = icon;
        overlay.appendChild(iconEl);
        document.body.appendChild(overlay);
        setTimeout(function () { overlay.style.opacity = '0'; overlay.style.transition = 'opacity 0.3s'; }, 600);
        setTimeout(function () { overlay.remove(); }, 900);
    },

    // Transition card when loading the next image in a multi-container group
    showNextImageTransition: function (containerNumber, currentIndex, totalCount) {
        var overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;width:100vw;height:100vh;z-index:99999;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,0.5);pointer-events:none;';
        var card = document.createElement('div');
        card.style.cssText = 'background:white;border-radius:12px;padding:24px 40px;text-align:center;box-shadow:0 8px 32px rgba(0,0,0,0.3);animation:bounceIn 0.4s ease-out;';
        card.innerHTML = '<div style="font-size:14px;color:#666;margin-bottom:8px;">Loading next image</div>' +
            '<div style="font-size:18px;font-weight:700;color:#1a237e;">' + currentIndex + ' of ' + totalCount + '</div>' +
            '<div style="font-size:13px;color:#999;margin-top:4px;">' + containerNumber + '</div>' +
            '<div style="height:3px;background:#e0e0e0;border-radius:2px;margin-top:12px;overflow:hidden;"><div style="height:100%;background:linear-gradient(90deg,#1a237e,#3949ab);width:0;animation:progressSweep 0.6s ease-out forwards;"></div></div>';
        overlay.appendChild(card);
        document.body.appendChild(overlay);
        setTimeout(function () { overlay.style.opacity = '0'; overlay.style.transition = 'opacity 0.2s'; }, 600);
        setTimeout(function () { overlay.remove(); }, 800);
    },

    // Toast notification after bulk decision completes
    showBulkComplete: function (count, decision) {
        var toast = document.createElement('div');
        var bg = decision === 'Normal' ? '#2e7d32' : '#c62828';
        toast.style.cssText = 'position:fixed;top:20px;right:20px;z-index:99999;background:' + bg + ';color:white;padding:16px 24px;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,0.3);font-size:14px;font-weight:600;animation:slideDown 0.3s ease-out;';
        toast.textContent = count + ' images marked ' + decision + ' \u2014 Bulk decision complete';
        document.body.appendChild(toast);
        setTimeout(function () { toast.style.opacity = '0'; toast.style.transition = 'opacity 0.3s'; }, 2200);
        setTimeout(function () { toast.remove(); }, 2500);
    }
};

// Inject keyframe animations if not already present
if (!document.getElementById('decision-transition-styles')) {
    var style = document.createElement('style');
    style.id = 'decision-transition-styles';
    style.textContent =
        '@keyframes fadeIn { from { opacity:0; } to { opacity:1; } } ' +
        '@keyframes bounceIn { 0% { transform:scale(0.3); opacity:0; } 50% { transform:scale(1.05); } 100% { transform:scale(1); opacity:1; } } ' +
        '@keyframes progressSweep { from { width:0; } to { width:100%; } } ' +
        '@keyframes slideDown { from { transform:translateY(-20px); opacity:0; } to { transform:translateY(0); opacity:1; } }';
    document.head.appendChild(style);
}

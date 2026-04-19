// Inactivity Tracker - Monitors user activity and reports to .NET
// Also enforces timeout client-side so logout works even when the Blazor circuit is disconnected
let dotNetReference = null;
let activityEvents = ['mousedown', 'mousemove', 'keypress', 'scroll', 'touchstart', 'click'];
let lastActivityTime = Date.now();
let throttleTimeout = null;
let clientCheckInterval = null;
let timeoutMs = 0;

// Initialize activity tracking
export function initializeActivityTracking(dotNetRef, inactivityTimeoutMs) {
    dotNetReference = dotNetRef;
    timeoutMs = inactivityTimeoutMs || 0;
    lastActivityTime = Date.now();

    // Add event listeners for all activity events
    activityEvents.forEach(event => {
        document.addEventListener(event, handleActivity, { passive: true });
    });

    // Client-side inactivity check (every 30s) - works even if SignalR circuit is dead
    if (clientCheckInterval) clearInterval(clientCheckInterval);
    if (timeoutMs > 0) {
        clientCheckInterval = setInterval(checkClientInactivity, 30000);
        console.log('✅ Inactivity tracker initialized - timeout: ' + Math.round(timeoutMs / 60000) + ' min (client-side enforcement enabled)');
    } else {
        console.log('✅ Inactivity tracker initialized - monitoring user activity (no client-side timeout)');
    }
}

// Handle activity with throttling (max once per second)
function handleActivity() {
    const now = Date.now();
    
    // Only record activity if it's been more than 1 second since last activity
    if (now - lastActivityTime > 1000) {
        lastActivityTime = now;
        
        // Clear any pending throttle
        if (throttleTimeout) {
            clearTimeout(throttleTimeout);
        }
        
        // Throttle the .NET call
        throttleTimeout = setTimeout(() => {
            if (dotNetReference) {
                try {
                    dotNetReference.invokeMethodAsync('RecordActivity');
                } catch (error) {
                    console.error('Error recording activity:', error);
                }
            }
        }, 100);
    }
}

// Client-side inactivity check - redirects to login independent of Blazor circuit
function checkClientInactivity() {
    if (timeoutMs > 0 && (Date.now() - lastActivityTime) >= timeoutMs) {
        console.warn('⏰ Client-side inactivity timeout reached (' + Math.round(timeoutMs / 60000) + ' min) - redirecting to login');
        cleanup();
        sessionStorage.clear();
        window.location.href = '/login?reason=inactivity';
    }
}

// Cleanup function
export function cleanup() {
    activityEvents.forEach(event => {
        document.removeEventListener(event, handleActivity);
    });

    if (throttleTimeout) {
        clearTimeout(throttleTimeout);
    }

    if (clientCheckInterval) {
        clearInterval(clientCheckInterval);
        clientCheckInterval = null;
    }

    dotNetReference = null;
    console.log('🧹 Inactivity tracker cleaned up');
}


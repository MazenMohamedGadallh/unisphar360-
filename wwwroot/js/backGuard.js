// Back-button guard, armed on Home only ("/" or "/dashboard") while signed in.
//
// .NET (MainLayout.SyncBackGuardAsync) starts the guard when the shell
// renders Home for an authenticated user with a resolved auth state, and
// stops it when leaving Home, on logout, or on dispose — so Back between
// inner pages (/chat, /analytics, /university, ...) is never touched.
//
// Protocol: while armed, exactly one tagged sentinel entry sits on top of
// the history stack (pushState; replaceState refreshes it in place instead
// of stacking a duplicate). A Back press on Home pops the sentinel without
// navigating (same URL): seeing a real Home entry underneath, we re-push
// the sentinel (stay on Home) and ask .NET to show the "still signed in"
// modal. Pops that land ON the sentinel are arrivals at Home (Back from an
// inner page, or Forward): allowed silently, the sentinel stays live.
// History entries cannot be deleted, so stale sentinels below are simply
// consumed as silent arrivals; the live one always guards Home.

let popHandler = null;
let dotNetRef = null;
let live = false;

function currentPath() {
    try {
        return new URL(window.location.href).pathname.toLowerCase();
    } catch {
        return "";
    }
}

function isHomePath(p) {
    return p === "" || p === "/" || p === "/dashboard";
}

function currentIsSentinel() {
    try {
        return !!(history.state && history.state.backGuard === true);
    } catch {
        return false;
    }
}

export function startBackGuard(dotNet) {
    // Re-arming while armed must not stack sentinels. If the current entry
    // already is our sentinel (arrival landed on it), refresh it in place.
    if (popHandler !== null) {
        try {
            if (currentIsSentinel()) {
                history.replaceState({ backGuard: true }, "");
                live = true;
            } else {
                history.pushState({ backGuard: true }, "");
                live = true;
            }
        } catch {
            // History unavailable: guard stays off rather than break Back.
            live = false;
            return;
        }
        if (dotNetRef && dotNetRef !== dotNet) {
            try { dotNetRef.dispose(); } catch { /* Already disposed. */ }
        }
        dotNetRef = dotNet;
        return;
    }
    dotNetRef = dotNet;
    try {
        if (currentIsSentinel()) {
            history.replaceState({ backGuard: true }, "");
        } else {
            history.pushState({ backGuard: true }, "");
        }
        live = true;
    } catch {
        // History unavailable: stay unguarded rather than break the page.
        live = false;
        return;
    }
    popHandler = async (event) => {
        const landedOnSentinel = !!(event && event.state && event.state.backGuard === true);
        if (landedOnSentinel) {
            // Arrival at Home onto our sentinel (Back from an inner page,
            // or Forward): allow silently, keep the sentinel live.
            live = true;
            return;
        }
        // Landed on a real entry: our live sentinel (if any) was popped.
        if (!live) return;
        live = false;
        // Only Home is guarded — inner pages keep fully native Back.
        if (!isHomePath(currentPath())) return;
        // Back pressed on Home: do NOT navigate. Stay (exactly one fresh
        // sentinel on top) and ask .NET for the "still signed in" modal.
        // Pressing Back again re-pops it, so the modal shows again.
        try {
            history.pushState({ backGuard: true }, "");
        } catch {
            return;
        }
        live = true;
        try {
            await dotNetRef.invokeMethodAsync("OnBackPressed");
        } catch {
            // Circuit gone; nothing to notify.
        }
    };
    window.addEventListener("popstate", popHandler);
}

export function stopBackGuard() {
    if (popHandler) {
        window.removeEventListener("popstate", popHandler);
        popHandler = null;
    }
    live = false;
    if (dotNetRef) {
        try {
            dotNetRef.dispose();
        } catch {
            // Already disposed.
        }
        dotNetRef = null;
    }
}

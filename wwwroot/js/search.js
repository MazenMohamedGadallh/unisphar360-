// Topbar search helpers: element focus + global shortcuts (Ctrl+K, "/").
// The "/" shortcut never fires while typing in an input, textarea, select
// or content-editable element.

let keyHandler = null;
let dotNetRef = null;

export function focusElement(id) {
    try {
        document.getElementById(id)?.focus();
    } catch {
        // Element not rendered (e.g. anonymous layout): nothing to focus.
    }
}

export function registerSearchKeys(dotNet) {
    unregisterSearchKeys();
    dotNetRef = dotNet;
    keyHandler = async (e) => {
        const tag = (e.target?.tagName || "").toLowerCase();
        const typing = tag === "input" || tag === "textarea" || tag === "select" || e.target?.isContentEditable;
        if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K")) {
            e.preventDefault();
            try {
                await dotNetRef.invokeMethodAsync("FocusSearch");
            } catch {
                // Circuit gone.
            }
        } else if (e.key === "/" && !typing) {
            e.preventDefault();
            try {
                await dotNetRef.invokeMethodAsync("FocusSearch");
            } catch {
                // Circuit gone.
            }
        }
    };
    window.addEventListener("keydown", keyHandler);
}

export function unregisterSearchKeys() {
    if (keyHandler) {
        window.removeEventListener("keydown", keyHandler);
        keyHandler = null;
    }
    if (dotNetRef) {
        try {
            dotNetRef.dispose();
        } catch {
            // Already disposed.
        }
        dotNetRef = null;
    }
}

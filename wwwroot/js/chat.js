// Chat scroll helpers: only auto-scroll when the user is already near
// the bottom, so streaming never yanks them away from older messages.
export function shouldAutoScroll(id) {
    try {
        const el = document.getElementById(id);
        if (!el) return true;
        return el.scrollHeight - el.scrollTop - el.clientHeight < 80;
    } catch {
        return true;
    }
}

export function scrollToBottom(id) {
    try {
        const el = document.getElementById(id);
        if (el) el.scrollTop = el.scrollHeight;
    } catch {
        // Non-fatal.
    }
}

// Enter sends the chat message; Shift+Enter inserts a newline.
// Blazor's @onkeydown:preventDefault cannot vary per key, so the newline
// for plain Enter is suppressed here (bound once per textarea).
export function preventChatEnterNewline(id) {
    try {
        const el = document.getElementById(id);
        if (!el || el.dataset.enterBound) return;
        el.dataset.enterBound = "1";
        el.addEventListener("keydown", (e) => {
            if (e.key === "Enter" && !e.shiftKey) e.preventDefault();
        });
    } catch {
        // Non-fatal.
    }
}

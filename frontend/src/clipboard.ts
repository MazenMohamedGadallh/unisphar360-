/** Best-effort clipboard copy (matches the Blazor chat.js helper). */
export function copyToClipboard(text: string): void {
  try {
    void navigator.clipboard.writeText(text).catch(() => {
      // Clipboard unavailable (permissions): no-op.
    });
  } catch {
    // Clipboard unavailable: no-op.
  }
}

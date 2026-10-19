// Research Hub download helper. A plain browser navigation cannot attach the
// JWT bearer token, so the Blazor page fetches the PDF through the authorized
// HttpClient and hands the bytes to this helper, which writes a Blob download.
// This file never sees server paths and never stores tokens.
export function downloadFile(fileName, base64, contentType) {
    try {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        const blob = new Blob([bytes], { type: contentType || 'application/pdf' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName || 'download.pdf';
        document.body.appendChild(a);
        a.click();
        a.remove();
        // Give the browser a tick before releasing the object URL.
        setTimeout(() => URL.revokeObjectURL(url), 2000);
    } catch (err) {
        // The page surfaces a generic error; nothing else to do here.
        if (window.console) console.warn('downloadFile failed', err);
    }
}

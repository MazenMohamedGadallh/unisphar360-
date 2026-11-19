// Downloads a file that was fetched with the Blazor HttpClient's bearer token.
// The API streams the file itself; this module turns the JS stream reference
// into a Blob and triggers the browser's save dialog.
export async function downloadFile(fileName, contentType, streamReference) {
    const buffer = await streamReference.arrayBuffer();
    const blob = new Blob([buffer], { type: contentType || "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.style.display = "none";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export async function downloadPdf(fileName, streamReference) {
    return downloadFile(fileName, "application/pdf", streamReference);
}

// Saves a file the server built, as in Microsoft's Blazor file download guide:
// the bytes arrive as a stream reference and become a temporary object URL.
export async function saveFile(fileName, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName ?? "";
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
}

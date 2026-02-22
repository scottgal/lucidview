const inputEl = document.getElementById("mermaid-input");
const applyButton = document.getElementById("apply");
const statusEl = document.getElementById("status");
const diagramEl = document.getElementById("diagram-component");

function setStatus(message) {
    statusEl.textContent = message;
}

function applyInput() {
    if (!diagramEl) {
        setStatus("Component not found.");
        return;
    }

    diagramEl.mermaid = inputEl.value;
    setStatus("Applied Mermaid source to component.");
}

if (diagramEl) {
    diagramEl.addEventListener("rendered", (event) => {
        const detail = event.detail ?? {};
        const length = detail.svgLength ?? "?";
        setStatus(`Rendered via <naiad-diagram> (${length} SVG chars).`);
    });

    diagramEl.addEventListener("rendererror", (event) => {
        const detail = event.detail ?? {};
        setStatus(`Render failed: ${detail.message ?? "unknown error"}`);
    });
}

if (applyButton) {
    applyButton.addEventListener("click", applyInput);
}
applyInput();

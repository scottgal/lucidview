const loadedScripts = new Map();

async function ensureLoaded(scriptUrl) {
  if (typeof customElements !== "undefined" && customElements.get("naiad-diagram")) {
    return;
  }

  if (!scriptUrl || !scriptUrl.trim()) {
    throw new Error("Naiad script URL is required.");
  }

  const normalizedUrl = new URL(scriptUrl, document.baseURI).toString();
  if (!loadedScripts.has(normalizedUrl)) {
    const loadPromise = new Promise((resolve, reject) => {
      let script = document.querySelector(`script[data-naiad-web-component][src="${normalizedUrl}"]`);
      if (!script) {
        script = document.createElement("script");
        script.type = "module";
        script.src = normalizedUrl;
        script.dataset.naiadWebComponent = "true";
        script.onload = () => resolve();
        script.onerror = () => reject(new Error(`Failed to load Naiad script: ${normalizedUrl}`));
        document.head.appendChild(script);
      } else {
        resolve();
      }
    });
    loadedScripts.set(normalizedUrl, loadPromise);
  }

  await loadedScripts.get(normalizedUrl);
  if (typeof customElements !== "undefined") {
    await customElements.whenDefined("naiad-diagram");
  }
}

async function updateDiagram(element, mermaid, optionsJson, attrs) {
  if (!element) {
    return;
  }

  applyAttribute(element, "theme", attrs?.theme);
  applyBooleanAttribute(element, "theme-responsive", attrs?.themeResponsive !== false);
  applyBooleanAttribute(element, "fit-width", attrs?.fitWidth !== false);
  applyBooleanAttribute(element, "status-hidden", attrs?.statusHidden === true);
  applyBooleanAttribute(element, "rerender-on-resize", attrs?.rerenderOnResize === true);
  applyBooleanAttribute(element, "show-menu", attrs?.showMenu === true);
  applyNumberAttribute(element, "cache-size", attrs?.cacheSize);
  applyNumberAttribute(element, "render-debounce", attrs?.renderDebounce);
  applyAttribute(element, "download-filename", attrs?.downloadFileName);

  element.mermaid = mermaid ?? "";
  if (typeof optionsJson === "string" && optionsJson.trim()) {
    try {
      element.options = JSON.parse(optionsJson);
    } catch {
      element.options = optionsJson;
    }
  } else {
    element.options = null;
  }

  if (typeof element.render === "function") {
    await element.render();
  }
}

function applyAttribute(element, attributeName, value) {
  if (value === null || value === undefined || value === "") {
    element.removeAttribute(attributeName);
    return;
  }

  element.setAttribute(attributeName, `${value}`);
}

function applyBooleanAttribute(element, attributeName, enabled) {
  if (enabled) {
    element.setAttribute(attributeName, "");
    return;
  }

  element.removeAttribute(attributeName);
}

function applyNumberAttribute(element, attributeName, value) {
  if (Number.isFinite(value)) {
    element.setAttribute(attributeName, `${value}`);
    return;
  }

  element.removeAttribute(attributeName);
}

export const naiadBlazor = {
  ensureLoaded,
  updateDiagram
};

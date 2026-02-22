import { NaiadClient } from "./naiad-client.js";

let sharedClient = null;
let sharedClientInitPromise = null;

const systemDarkQuery =
  typeof window !== "undefined" && typeof window.matchMedia === "function"
    ? window.matchMedia("(prefers-color-scheme: dark)")
    : null;

const MAX_MERMAID_SOURCE_CHARS = 100_000;
const MAX_OPTIONS_JSON_CHARS = 16_384;
const MAX_RENDERED_SVG_CHARS = 2_000_000;
const MAX_RENDER_CACHE_ENTRIES = 64;

const DISALLOWED_SVG_TAGS = new Set([
  "script",
  "iframe",
  "object",
  "embed",
  "link",
  "meta"
]);

const ALLOWED_FOREIGN_OBJECT_TAGS = new Set([
  "div",
  "span",
  "p",
  "b",
  "strong",
  "i",
  "em",
  "u",
  "small",
  "br",
  "code",
  "pre",
  "ul",
  "ol",
  "li"
]);

async function getSharedClient() {
  if (sharedClient) {
    return sharedClient;
  }

  if (!sharedClientInitPromise) {
    sharedClientInitPromise = (async () => {
      const client = new NaiadClient();
      await client.init();
      sharedClient = client;
      return client;
    })().catch((error) => {
      sharedClientInitPromise = null;
      throw error;
    });
  }

  return sharedClientInitPromise;
}

const template = document.createElement("template");
template.innerHTML = `
<style>
  :host {
    --naiad-bg: #ffffff;
    --naiad-border-color: #d1d5db;
    --naiad-border-radius: 8px;
    --naiad-padding: 10px;
    --naiad-min-height: 120px;
    --naiad-status-color: #4b5563;
    --naiad-status-font: 12px/1.4 Segoe UI, Arial, sans-serif;
    --naiad-status-margin: 0 0 8px;
    --naiad-error-bg: #fef2f2;
    --naiad-error-color: #991b1b;
    --naiad-error-font: 12px/1.4 Consolas, "Courier New", monospace;
    --naiad-error-radius: 6px;
    --naiad-error-padding: 8px;
    --naiad-error-margin: 0;
    --naiad-diagram-min-height: 80px;
    --naiad-svg-max-width: 100%;
    --naiad-toolbar-gap: 8px;
    --naiad-toolbar-margin: 0 0 8px;
    --naiad-button-bg: #f8fafc;
    --naiad-button-color: #111827;
    --naiad-button-border: #d1d5db;
    --naiad-button-radius: 6px;
    --naiad-button-padding: 6px 10px;
    --naiad-button-font: 12px/1.2 Segoe UI, Arial, sans-serif;
    --naiad-button-hover-bg: #f1f5f9;

    display: block;
    border: 1px solid var(--naiad-border-color);
    border-radius: var(--naiad-border-radius);
    background: var(--naiad-bg);
    padding: var(--naiad-padding);
    overflow: auto;
    min-height: var(--naiad-min-height);
  }

  #toolbar {
    display: flex;
    flex-wrap: wrap;
    gap: var(--naiad-toolbar-gap);
    margin: var(--naiad-toolbar-margin);
  }

  :host(:not([show-menu])) #toolbar {
    display: none;
  }

  #toolbar button {
    appearance: none;
    border: 1px solid var(--naiad-button-border);
    border-radius: var(--naiad-button-radius);
    background: var(--naiad-button-bg);
    color: var(--naiad-button-color);
    padding: var(--naiad-button-padding);
    font: var(--naiad-button-font);
    cursor: pointer;
  }

  #toolbar button:hover {
    background: var(--naiad-button-hover-bg);
  }

  #status {
    font: var(--naiad-status-font);
    color: var(--naiad-status-color);
    margin: var(--naiad-status-margin);
  }

  :host([status-hidden]) #status {
    display: none;
  }

  #error {
    display: none;
    margin: var(--naiad-error-margin);
    padding: var(--naiad-error-padding);
    border-radius: var(--naiad-error-radius);
    background: var(--naiad-error-bg);
    color: var(--naiad-error-color);
    font: var(--naiad-error-font);
    white-space: pre-wrap;
  }

  #diagram {
    min-height: var(--naiad-diagram-min-height);
  }

  #diagram svg {
    max-width: var(--naiad-svg-max-width);
  }

  :host([fit-width]) #diagram svg {
    width: 100%;
    height: auto;
  }
</style>
<div id="toolbar" part="toolbar">
  <slot name="menu">
    <button type="button" id="action-save-svg" part="menu-button">Save SVG</button>
    <button type="button" id="action-save-png" part="menu-button">Save PNG</button>
  </slot>
</div>
<div id="status" part="status" aria-live="polite">Initializing Naiad runtime...</div>
<pre id="error" part="error" role="alert" aria-live="assertive"></pre>
<div id="diagram" part="diagram"></div>
`;

class NaiadDiagramElement extends HTMLElement {
  static get observedAttributes() {
    return [
      "mermaid",
      "options",
      "theme",
      "theme-responsive",
      "cache-size",
      "fit-width",
      "rerender-on-resize",
      "render-debounce"
    ];
  }

  #statusEl;
  #errorEl;
  #diagramEl;
  #saveSvgButton;
  #savePngButton;
  #observer;
  #resizeObserver;
  #renderToken = 0;
  #optionsObject = null;
  #renderQueued = false;
  #renderDebounceTimer = null;
  #renderCache = new Map();
  #lastWidth = 0;
  #lastHeight = 0;

  #onThemeChanged = () => {
    if (this.#usesAutoTheme()) {
      this.#requestRender();
    }
  };

  #onSaveSvgClick = () => {
    void this.#runMenuAction("download-svg");
  };

  #onSavePngClick = () => {
    void this.#runMenuAction("download-png");
  };

  constructor() {
    super();
    const shadow = this.attachShadow({ mode: "open" });
    shadow.appendChild(template.content.cloneNode(true));
    this.#statusEl = shadow.getElementById("status");
    this.#errorEl = shadow.getElementById("error");
    this.#diagramEl = shadow.getElementById("diagram");
    this.#saveSvgButton = shadow.getElementById("action-save-svg");
    this.#savePngButton = shadow.getElementById("action-save-png");
  }

  connectedCallback() {
    this.#upgradeProperty("mermaid");
    this.#upgradeProperty("options");
    this.#upgradeProperty("theme");
    this.#upgradeProperty("cacheSize");
    this.#upgradeProperty("downloadFileName");
    this.#setupTextObserver();
    this.#setupResizeObserver();
    this.#setupActionButtons();
    this.#setupThemeListener();
    this.#requestRender();
  }

  disconnectedCallback() {
    if (this.#observer) {
      this.#observer.disconnect();
      this.#observer = null;
    }
    if (this.#resizeObserver) {
      this.#resizeObserver.disconnect();
      this.#resizeObserver = null;
    }
    this.#teardownActionButtons();
    this.#teardownThemeListener();
    this.#clearDebounceTimer();
  }

  attributeChangedCallback(name, oldValue, newValue) {
    if (oldValue === newValue) {
      return;
    }

    if (name === "fit-width") {
      this.#applyFitWidth();
      return;
    }

    if (name === "cache-size") {
      this.#trimCache();
    }

    if (name === "mermaid" || name === "options" || name === "theme" || name === "theme-responsive" || name === "cache-size") {
      this.#requestRender();
    }
  }

  get mermaid() {
    return this.getAttribute("mermaid") ?? this.#inlineCode();
  }

  set mermaid(value) {
    if (value == null) {
      this.removeAttribute("mermaid");
      return;
    }
    this.setAttribute("mermaid", value);
  }

  get options() {
    if (this.#optionsObject !== null) {
      return this.#optionsObject;
    }

    const raw = this.getAttribute("options");
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  }

  set options(value) {
    if (value == null) {
      this.#optionsObject = null;
      this.removeAttribute("options");
      return;
    }

    if (typeof value === "string") {
      this.#optionsObject = null;
      this.setAttribute("options", value);
      return;
    }

    this.#optionsObject = value;
    this.#requestRender();
  }

  get theme() {
    return this.getAttribute("theme") ?? "auto";
  }

  set theme(value) {
    if (value == null || value === "") {
      this.removeAttribute("theme");
      return;
    }
    this.setAttribute("theme", String(value));
  }

  get cacheSize() {
    return this.#cacheSize();
  }

  set cacheSize(value) {
    if (value == null) {
      this.removeAttribute("cache-size");
      return;
    }
    this.setAttribute("cache-size", String(value));
  }

  get downloadFileName() {
    return this.getAttribute("download-filename") ?? "diagram";
  }

  set downloadFileName(value) {
    if (value == null || value === "") {
      this.removeAttribute("download-filename");
      return;
    }
    this.setAttribute("download-filename", value);
  }

  async render() {
    await this.#renderNow();
  }

  getSvgMarkup() {
    return this.#diagramEl.querySelector("svg")?.outerHTML ?? "";
  }

  async toSvgBlob() {
    const svg = this.getSvgMarkup();
    if (!svg) {
      throw new Error("No rendered diagram available to export.");
    }
    return new Blob([svg], { type: "image/svg+xml;charset=utf-8" });
  }

  async toPngBlob(options = null) {
    const svgBlob = await this.toSvgBlob();
    const svgElement = this.#diagramEl.querySelector("svg");
    if (!svgElement) {
      throw new Error("No rendered diagram available to export.");
    }

    const scale =
      options && Number.isFinite(options.scale) && options.scale > 0
        ? options.scale
        : 2;
    const background =
      options && typeof options.background === "string"
        ? options.background
        : "transparent";

    const { width, height } = this.#resolveSvgSize(svgElement);
    const renderWidth = Math.max(1, Math.ceil(width * scale));
    const renderHeight = Math.max(1, Math.ceil(height * scale));

    const svgUrl = URL.createObjectURL(svgBlob);
    try {
      const image = await this.#loadImage(svgUrl);
      const canvas = document.createElement("canvas");
      canvas.width = renderWidth;
      canvas.height = renderHeight;
      const ctx = canvas.getContext("2d");
      if (!ctx) {
        throw new Error("Unable to initialize canvas context for PNG export.");
      }

      if (background && background !== "transparent") {
        ctx.fillStyle = background;
        ctx.fillRect(0, 0, renderWidth, renderHeight);
      }

      ctx.drawImage(image, 0, 0, renderWidth, renderHeight);

      const pngBlob = await new Promise((resolve, reject) => {
        canvas.toBlob((blob) => {
          if (blob) {
            resolve(blob);
            return;
          }
          reject(new Error("Failed to encode PNG blob."));
        }, "image/png");
      });

      return pngBlob;
    } finally {
      URL.revokeObjectURL(svgUrl);
    }
  }

  async downloadSvg(fileName = null) {
    const exportFileName = this.#normalizeFileName(fileName, "svg");
    return this.#runExport("svg", exportFileName, () => this.toSvgBlob(), true);
  }

  async downloadPng(fileName = null, options = null) {
    const exportFileName = this.#normalizeFileName(fileName, "png");
    return this.#runExport("png", exportFileName, () => this.toPngBlob(options), true);
  }

  async getBuiltInSkinPacks() {
    const client = await getSharedClient();
    return client.listSkinPacks();
  }

  async #runExport(format, fileName, createBlob, triggerDownload) {
    const beforeExport = new CustomEvent("beforeexport", {
      detail: { format, fileName },
      cancelable: true,
      bubbles: true,
      composed: true
    });

    if (!this.dispatchEvent(beforeExport)) {
      return null;
    }

    try {
      const blob = await createBlob();
      if (triggerDownload) {
        this.#triggerDownload(blob, fileName);
      }

      this.dispatchEvent(
        new CustomEvent("afterexport", {
          detail: {
            format,
            fileName,
            byteLength: blob.size,
            mimeType: blob.type
          },
          bubbles: true,
          composed: true
        })
      );

      return blob;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.dispatchEvent(
        new CustomEvent("exporterror", {
          detail: { format, fileName, message },
          bubbles: true,
          composed: true
        })
      );
      throw error;
    }
  }

  async #runMenuAction(action) {
    const menuAction = new CustomEvent("menuaction", {
      detail: { action },
      cancelable: true,
      bubbles: true,
      composed: true
    });

    if (!this.dispatchEvent(menuAction)) {
      return;
    }

    if (action === "download-svg") {
      await this.downloadSvg();
      return;
    }

    if (action === "download-png") {
      await this.downloadPng();
    }
  }

  #triggerDownload(blob, fileName) {
    const blobUrl = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = blobUrl;
    anchor.download = fileName;
    anchor.style.display = "none";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(blobUrl);
  }

  #resolveSvgSize(svgElement) {
    const rect = svgElement.getBoundingClientRect();
    let width = rect.width;
    let height = rect.height;

    if (!(width > 0 && height > 0)) {
      const viewBox = svgElement.getAttribute("viewBox");
      if (viewBox) {
        const values = viewBox
          .split(/[ ,]+/)
          .map((v) => Number.parseFloat(v))
          .filter((v) => Number.isFinite(v));
        if (values.length === 4) {
          width = values[2];
          height = values[3];
        }
      }
    }

    if (!(width > 0 && height > 0)) {
      width = 1024;
      height = 768;
    }

    return { width, height };
  }

  #normalizeFileName(fileName, extension) {
    const rawName = (fileName ?? this.downloadFileName ?? "diagram").trim();
    const base = rawName === "" ? "diagram" : rawName;
    const sanitized = base.replace(/[<>:"/\\|?*\u0000-\u001F]/g, "-");
    if (sanitized.toLowerCase().endsWith(`.${extension}`)) {
      return sanitized;
    }
    return `${sanitized}.${extension}`;
  }

  #loadImage(url) {
    return new Promise((resolve, reject) => {
      const image = new Image();
      image.onload = () => resolve(image);
      image.onerror = () => reject(new Error("Failed to decode rendered SVG for PNG export."));
      image.src = url;
    });
  }

  #requestRender() {
    const debounceMs = this.#renderDebounceMs();
    if (debounceMs > 0) {
      this.#clearDebounceTimer();
      this.#renderDebounceTimer = setTimeout(() => {
        this.#renderDebounceTimer = null;
        void this.#renderNow();
      }, debounceMs);
      return;
    }

    if (this.#renderQueued) {
      return;
    }
    this.#renderQueued = true;
    queueMicrotask(() => {
      this.#renderQueued = false;
      void this.#renderNow();
    });
  }

  async #renderNow() {
    const token = ++this.#renderToken;
    const source = this.mermaid.trim();
    if (!source) {
      this.#setStatus("No Mermaid source.");
      this.#diagramEl.innerHTML = "";
      this.#hideError();
      return;
    }

    if (source.length > MAX_MERMAID_SOURCE_CHARS) {
      this.#diagramEl.innerHTML = "";
      this.#showError(`Mermaid source exceeds ${MAX_MERMAID_SOURCE_CHARS} characters.`);
      this.#setStatus("Render failed");
      return;
    }

    this.#setStatus("Rendering...");
    this.#hideError();

    try {
      const { options, theme } = this.#buildRenderConfig();
      const cacheKey = this.#cacheKey(source, options);
      const cachedSvg = this.#cacheGet(cacheKey);
      if (cachedSvg) {
        this.#diagramEl.innerHTML = cachedSvg;
        this.#applyFitWidth();
        this.#setStatus("Rendered");
        this.#emitRendered(source, cachedSvg.length, 0, true, theme);
        return;
      }

      const client = await getSharedClient();
      if (token !== this.#renderToken) {
        return;
      }

      const start = performance.now();
      const rawSvg = client.renderSvg(source, options);
      const svg = this.#sanitizeSvgMarkup(rawSvg);
      const renderMs = performance.now() - start;

      if (token !== this.#renderToken) {
        return;
      }

      this.#diagramEl.innerHTML = svg;
      this.#applyFitWidth();
      this.#cacheSet(cacheKey, svg);
      this.#setStatus("Rendered");
      this.#emitRendered(source, svg.length, renderMs, false, theme);
    } catch (error) {
      if (token !== this.#renderToken) {
        return;
      }

      const message = error instanceof Error ? error.message : String(error);
      this.#diagramEl.innerHTML = "";
      this.#showError(message);
      this.#setStatus("Render failed");

      this.dispatchEvent(
        new CustomEvent("rendererror", {
          detail: { message, mermaid: source },
          bubbles: true,
          composed: true
        })
      );
    }
  }

  #emitRendered(mermaid, svgLength, renderMs, cached, theme) {
    this.dispatchEvent(
      new CustomEvent("rendered", {
        detail: {
          mermaid,
          svgLength,
          renderMs,
          cached,
          theme
        },
        bubbles: true,
        composed: true
      })
    );
  }

  #buildRenderConfig() {
    const parsed = this.#resolveOptionsObject();
    const options = parsed ? { ...parsed } : {};
    const forcedTheme = this.#resolveThemeOverride();
    let theme = forcedTheme;

    if (forcedTheme) {
      options.theme = forcedTheme;
    } else if (typeof options.theme === "string" && options.theme.trim() !== "") {
      theme = options.theme.trim();
    } else if (this.#isThemeResponsive()) {
      theme = this.#prefersDark() ? "dark" : "default";
      options.theme = theme;
    }

    const finalOptions = Object.keys(options).length > 0 ? options : null;
    return { options: finalOptions, theme };
  }

  #resolveThemeOverride() {
    const themeAttr = (this.getAttribute("theme") ?? "").trim().toLowerCase();
    if (!themeAttr || themeAttr === "auto" || themeAttr === "system") {
      return null;
    }
    if (themeAttr === "light") {
      return "default";
    }
    return themeAttr;
  }

  #resolveOptionsObject() {
    if (this.#optionsObject !== null) {
      if (typeof this.#optionsObject !== "object") {
        throw new Error("The `options` property must be an object, string, or null.");
      }
      return this.#optionsObject;
    }

    const raw = this.getAttribute("options");
    if (!raw) {
      return null;
    }

    if (raw.length > MAX_OPTIONS_JSON_CHARS) {
      throw new Error(`The \`options\` attribute exceeds ${MAX_OPTIONS_JSON_CHARS} characters.`);
    }

    try {
      const parsed = JSON.parse(raw);
      if (parsed == null || typeof parsed !== "object") {
        throw new Error("The `options` attribute must decode to a JSON object.");
      }
      return parsed;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      throw new Error(`Invalid options JSON in \`options\` attribute: ${message}`);
    }
  }

  #cacheSize() {
    const raw = this.getAttribute("cache-size");
    if (!raw) {
      return 24;
    }

    const parsed = Number.parseInt(raw, 10);
    if (!Number.isFinite(parsed) || parsed < 0) {
      return 24;
    }

    return Math.min(parsed, MAX_RENDER_CACHE_ENTRIES);
  }

  #cacheKey(mermaid, options) {
    const optionsKey = options ? JSON.stringify(options) : "null";
    return `${mermaid}\n---\n${optionsKey}`;
  }

  #cacheGet(key) {
    const value = this.#renderCache.get(key);
    if (!value) {
      return null;
    }

    // LRU refresh.
    this.#renderCache.delete(key);
    this.#renderCache.set(key, value);
    return value;
  }

  #cacheSet(key, value) {
    const cap = this.#cacheSize();
    if (cap <= 0) {
      return;
    }

    this.#renderCache.set(key, value);
    this.#trimCache();
  }

  #trimCache() {
    const cap = this.#cacheSize();
    if (cap <= 0) {
      this.#renderCache.clear();
      return;
    }

    while (this.#renderCache.size > cap) {
      const firstKey = this.#renderCache.keys().next().value;
      this.#renderCache.delete(firstKey);
    }
  }

  #renderDebounceMs() {
    const raw = this.getAttribute("render-debounce");
    if (!raw) {
      return 0;
    }

    const parsed = Number.parseInt(raw, 10);
    if (!Number.isFinite(parsed) || parsed < 0) {
      return 0;
    }

    return parsed;
  }

  #setupTextObserver() {
    if (this.#observer) {
      this.#observer.disconnect();
    }

    this.#observer = new MutationObserver(() => {
      if (!this.hasAttribute("mermaid")) {
        this.#requestRender();
      }
    });

    this.#observer.observe(this, {
      characterData: true,
      childList: true,
      subtree: true
    });
  }

  #setupResizeObserver() {
    if (typeof ResizeObserver === "undefined") {
      return;
    }

    this.#resizeObserver = new ResizeObserver((entries) => {
      const first = entries[0];
      if (!first) {
        return;
      }

      const width = Math.round(first.contentRect.width);
      const height = Math.round(first.contentRect.height);
      if (width === this.#lastWidth && height === this.#lastHeight) {
        return;
      }

      this.#lastWidth = width;
      this.#lastHeight = height;

      this.dispatchEvent(
        new CustomEvent("resized", {
          detail: { width, height },
          bubbles: true,
          composed: true
        })
      );

      if (this.hasAttribute("fit-width")) {
        this.#applyFitWidth();
      }

      if (this.hasAttribute("rerender-on-resize")) {
        this.#requestRender();
      }
    });

    this.#resizeObserver.observe(this);
  }

  #setupThemeListener() {
    if (!systemDarkQuery) {
      return;
    }
    systemDarkQuery.addEventListener("change", this.#onThemeChanged);
  }

  #teardownThemeListener() {
    if (!systemDarkQuery) {
      return;
    }
    systemDarkQuery.removeEventListener("change", this.#onThemeChanged);
  }

  #usesAutoTheme() {
    if (!this.#isThemeResponsive()) {
      return false;
    }

    const themeAttr = (this.getAttribute("theme") ?? "").trim().toLowerCase();
    if (themeAttr && themeAttr !== "auto" && themeAttr !== "system") {
      return false;
    }

    const parsed = this.options;
    if (parsed && typeof parsed === "object" && typeof parsed.theme === "string" && parsed.theme.trim() !== "") {
      return false;
    }

    return true;
  }

  #isThemeResponsive() {
    const attr = this.getAttribute("theme-responsive");
    if (attr === "false" || attr === "0") {
      return false;
    }
    return true;
  }

  #prefersDark() {
    return !!(systemDarkQuery && systemDarkQuery.matches);
  }

  #setupActionButtons() {
    this.#saveSvgButton?.addEventListener("click", this.#onSaveSvgClick);
    this.#savePngButton?.addEventListener("click", this.#onSavePngClick);
  }

  #teardownActionButtons() {
    this.#saveSvgButton?.removeEventListener("click", this.#onSaveSvgClick);
    this.#savePngButton?.removeEventListener("click", this.#onSavePngClick);
  }

  #setStatus(message) {
    this.#statusEl.textContent = message;
  }

  #hideError() {
    this.#errorEl.style.display = "none";
    this.#errorEl.textContent = "";
  }

  #showError(message) {
    this.#errorEl.style.display = "block";
    this.#errorEl.textContent = message;
  }

  #inlineCode() {
    return (this.textContent ?? "").trim();
  }

  #upgradeProperty(name) {
    if (!Object.prototype.hasOwnProperty.call(this, name)) {
      return;
    }

    const value = this[name];
    delete this[name];
    this[name] = value;
  }

  #applyFitWidth() {
    if (!this.hasAttribute("fit-width")) {
      return;
    }

    const svg = this.#diagramEl.querySelector("svg");
    if (!svg) {
      return;
    }

    svg.removeAttribute("width");
    svg.removeAttribute("height");
    svg.style.width = "100%";
    svg.style.height = "auto";
    svg.style.display = "block";
  }

  #sanitizeSvgMarkup(svgMarkup) {
    if (typeof svgMarkup !== "string" || svgMarkup.trim() === "") {
      throw new Error("Renderer returned an empty SVG payload.");
    }

    if (svgMarkup.length > MAX_RENDERED_SVG_CHARS) {
      throw new Error(`Rendered SVG exceeds ${MAX_RENDERED_SVG_CHARS} characters.`);
    }

    const parser = new DOMParser();
    const doc = parser.parseFromString(svgMarkup, "image/svg+xml");
    if (doc.querySelector("parsererror")) {
      throw new Error("Renderer returned invalid SVG markup.");
    }

    const root = doc.documentElement;
    if (!root || root.tagName.toLowerCase() !== "svg") {
      throw new Error("Renderer output is not an SVG document.");
    }

    const nodes = [root, ...Array.from(root.querySelectorAll("*"))];
    for (const element of nodes) {
      const tagName = element.tagName.toLowerCase();
      if (DISALLOWED_SVG_TAGS.has(tagName)) {
        element.remove();
        continue;
      }

      const insideForeignObject =
        tagName !== "foreignobject" && !!element.closest("foreignObject");
      if (insideForeignObject && !ALLOWED_FOREIGN_OBJECT_TAGS.has(tagName)) {
        element.remove();
        continue;
      }

      if (tagName === "style") {
        element.textContent = this.#sanitizeCssText(element.textContent ?? "");
      }

      for (const attribute of Array.from(element.attributes)) {
        const name = attribute.name;
        const lowerName = name.toLowerCase();
        const value = attribute.value ?? "";

        if (lowerName.startsWith("on")) {
          element.removeAttribute(name);
          continue;
        }

        if (lowerName === "style") {
          const sanitizedStyle = this.#sanitizeCssText(value);
          if (sanitizedStyle === "") {
            element.removeAttribute(name);
          } else {
            element.setAttribute(name, sanitizedStyle);
          }
          continue;
        }

        if (lowerName === "href" || lowerName === "xlink:href" || lowerName === "src") {
          if (!this.#isSafeUrl(value)) {
            element.removeAttribute(name);
          }
          continue;
        }

        if (this.#containsUnsafeToken(value)) {
          element.removeAttribute(name);
        }
      }
    }

    return new XMLSerializer().serializeToString(root);
  }

  #sanitizeCssText(cssText) {
    if (!cssText) {
      return "";
    }

    let sanitized = cssText;
    sanitized = sanitized.replace(/expression\s*\(/gi, "blocked(");
    sanitized = sanitized.replace(/javascript\s*:/gi, "blocked:");
    sanitized = sanitized.replace(/vbscript\s*:/gi, "blocked:");
    sanitized = sanitized.replace(/@import[^;]*;?/gi, "");
    sanitized = sanitized.replace(/@charset[^;]*;?/gi, "");
    sanitized = sanitized.replace(/@namespace[^;]*;?/gi, "");
    sanitized = sanitized.replace(
      /url\s*\(\s*(['"]?)\s*(?:javascript:|vbscript:|data:text\/html|data:application\/xhtml\+xml)/gi,
      "url($1blocked:"
    );
    return sanitized.trim();
  }

  #containsUnsafeToken(value) {
    if (!value) {
      return false;
    }

    const normalized = value.replace(/\s+/g, "").toLowerCase();
    return normalized.includes("javascript:") ||
      normalized.includes("vbscript:") ||
      normalized.includes("data:text/html") ||
      normalized.includes("data:application/xhtml+xml") ||
      normalized.includes("<script") ||
      normalized.includes("expression(");
  }

  #isSafeUrl(urlValue) {
    if (!urlValue) {
      return false;
    }

    const value = urlValue.trim();
    if (value === "") {
      return false;
    }

    if (value.startsWith("#")) {
      return true;
    }

    if (/^(https?:|mailto:|tel:|\/)/i.test(value)) {
      return !this.#containsUnsafeToken(value);
    }

    return !this.#containsUnsafeToken(value) && !/^(?:data|javascript|vbscript):/i.test(value);
  }

  #clearDebounceTimer() {
    if (this.#renderDebounceTimer) {
      clearTimeout(this.#renderDebounceTimer);
      this.#renderDebounceTimer = null;
    }
  }
}

if (!customElements.get("naiad-diagram")) {
  customElements.define("naiad-diagram", NaiadDiagramElement);
}

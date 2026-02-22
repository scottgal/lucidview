# `@naiad/web-component-complete`

Naiad Mermaid rendering as a standard Web Component for plain HTML pages.

It provides a custom element:

- `<naiad-diagram>`

The package includes:

- `naiad-web-component.js` (custom element definition)
- `naiad-client.js` (runtime/client wrapper)
- `/_framework/*` (.NET WASM runtime and assemblies)
- `all-diagrams-web-component.html` (all-diagram web-component test harness)
- `all-diagrams-test.md` (fallback all-diagrams markdown source)

## Install

```bash
npm install @naiad/web-component-complete
```

## Plain HTML Usage

```html
<!doctype html>
<html lang="en">
<body>
  <naiad-diagram>
flowchart LR
  A --> B
  B --> C
  </naiad-diagram>

  <script type="module" src="/node_modules/@naiad/web-component-complete/dist/naiad-web-component.js"></script>
</body>
</html>
```

## API

### Attributes

- `mermaid`: Mermaid source string. If omitted, inner text is used.
- `options`: JSON string matching Naiad `RenderOptions`.
  - Supports `skinPack` built-ins: `"default"`, `"daisyui"`, `"material"`, `"material3"`, `"fluent2"`, `"glass"` (plus aliases such as `"fluent"` / `"material-3"`), or host-enabled file-pack source.
- `theme`: `auto` (default), `light`, `dark`, or a Naiad theme name.
- `theme-responsive`: boolean-like attribute. Defaults to enabled. Set `theme-responsive="false"` to disable OS theme sync.
- `fit-width`: boolean attribute. When present, rendered SVG fills host width.
- `status-hidden`: boolean attribute. When present, hides status line.
- `cache-size`: render cache entries (default `24`, `0` disables cache).
- `render-debounce`: debounce in milliseconds for scheduled renders.
- `rerender-on-resize`: rerender when host size changes.
- `show-menu`: shows built-in `Save SVG` / `Save PNG` menu buttons.
- `download-filename`: base filename used by export methods/menu (default `diagram`).

`fit-width` also removes intrinsic SVG `width`/`height` attributes after render so CSS width control is consistent across host layouts.

### Properties

- `element.mermaid: string`
- `element.options: object | string | null`
- `element.theme: string`
- `element.cacheSize: number`
- `element.downloadFileName: string`
- `element.render(): Promise<void>`

### Methods

- `element.getSvgMarkup(): string`
- `element.toSvgBlob(): Promise<Blob>`
- `element.toPngBlob({ scale?, background? }): Promise<Blob>`
- `element.downloadSvg(fileName?): Promise<Blob | null>`
- `element.downloadPng(fileName?, { scale?, background? }): Promise<Blob | null>`
- `element.getBuiltInSkinPacks(): Promise<string[]>`

This package includes embedded skin packs in the WASM runtime profile and does not require external `skins/*` assets.

### Events

- `rendered`
- `rendererror`
- `resized`
- `menuaction` (cancelable)
- `beforeexport` (cancelable)
- `afterexport`
- `exporterror`

### Pluggable Menu

If you want custom controls, provide your own slotted menu and call methods directly:

```html
<naiad-diagram id="diagram" show-menu>
  <div slot="menu">
    <button id="btn-svg">Export SVG</button>
    <button id="btn-png">Export PNG</button>
  </div>
flowchart LR
  A --> B
</naiad-diagram>
<script type="module">
  const el = document.getElementById("diagram");
  document.getElementById("btn-svg").addEventListener("click", () => el.downloadSvg("custom.svg"));
  document.getElementById("btn-png").addEventListener("click", () => el.downloadPng("custom.png", { scale: 2 }));
</script>
```

Example:

```js
const diagram = document.querySelector("naiad-diagram");
diagram.addEventListener("rendered", (e) => {
  console.log("svg chars:", e.detail.svgLength);
  console.log("cached:", e.detail.cached, "theme:", e.detail.theme);
});
diagram.addEventListener("rendererror", (e) => {
  console.error(e.detail.message);
});
diagram.addEventListener("beforeexport", (e) => {
  if (e.detail.format === "png") {
    console.log("Preparing PNG export:", e.detail.fileName);
  }
});
```

## Mermaid Compatibility

Flowchart input accepts both multiline Mermaid and semicolon-separated statements:

```txt
flowchart LR; A[Start] --> B[Work]; B --> C[End]
```

Naiad also supports Mermaid-compatible comment directives (ignored by Mermaid.js):

```txt
%% naiad: skinPack=daisyui, theme=dark
flowchart LR
  A --> B
```

Or JSON form:

```txt
%% naiad: {"skinPack":"material","curvedEdges":false}
flowchart LR
  A --> B
```

## Styling

The component uses Shadow DOM and exposes two styling surfaces:

- CSS custom properties (theme tokens)
- `::part(...)` selectors

### CSS Custom Properties

Set these on `naiad-diagram`:

| Variable | Default | Purpose |
| --- | --- | --- |
| `--naiad-bg` | `#ffffff` | Host background |
| `--naiad-border-color` | `#d1d5db` | Host border color |
| `--naiad-border-radius` | `8px` | Host corner radius |
| `--naiad-padding` | `10px` | Host inner padding |
| `--naiad-min-height` | `120px` | Host min height |
| `--naiad-status-color` | `#4b5563` | Status text color |
| `--naiad-status-font` | `12px/1.4 Segoe UI, Arial, sans-serif` | Status font |
| `--naiad-status-margin` | `0 0 8px` | Status margin |
| `--naiad-error-bg` | `#fef2f2` | Error panel background |
| `--naiad-error-color` | `#991b1b` | Error text color |
| `--naiad-error-font` | `12px/1.4 Consolas, "Courier New", monospace` | Error font |
| `--naiad-error-radius` | `6px` | Error panel radius |
| `--naiad-error-padding` | `8px` | Error panel padding |
| `--naiad-error-margin` | `0` | Error panel margin |
| `--naiad-diagram-min-height` | `80px` | Diagram area min height |
| `--naiad-svg-max-width` | `100%` | Max SVG width |

### `::part` Styling

Available parts:

- `status`
- `error`
- `diagram`

Example:

```css
naiad-diagram::part(status) {
  font-weight: 700;
  letter-spacing: 0.02em;
}

naiad-diagram::part(error) {
  border: 1px solid #ef4444;
}

naiad-diagram::part(diagram) {
  background: linear-gradient(180deg, #fafafa, #ffffff);
}
```

### Theme Example (Dark)

```css
naiad-diagram.theme-dark {
  --naiad-bg: #0f172a;
  --naiad-border-color: #334155;
  --naiad-status-color: #cbd5e1;
  --naiad-error-bg: #3f1d1d;
  --naiad-error-color: #fecaca;
  --naiad-svg-max-width: 100%;
}
```

### Layout Example

```html
<naiad-diagram
  fit-width
  style="
    --naiad-border-radius: 14px;
    --naiad-padding: 16px;
    --naiad-min-height: 260px;
  ">
flowchart LR
  Browser --> Naiad
  Naiad --> SVG
</naiad-diagram>
```

## Build This Package From Source

From `Naiad/src/Naiad.Wasm.Npm`:

```bash
npm run build
```

What it does:

- Runs `dotnet publish` for `Naiad.Wasm.Host`
- Copies published `wwwroot` into `dist/`

Preview packed files:

```bash
npm run pack:preview
```

## All-Diagrams Harness

Use the packaged all-diagrams harness to validate web-component rendering across diagram families:

```html
<iframe src="/node_modules/@naiad/web-component-complete/dist/all-diagrams-web-component.html"></iframe>
```

## Blazor Integration

Use the `Naiad.Blazor` wrapper and point it at this package profile:

```csharp
using Naiad.Blazor;

builder.Services.AddNaiadBlazor(options =>
{
    options.ScriptUrl = NaiadBlazorProfiles.Complete;
});
```

Query params:

- `skin`: apply one built-in skin pack to all rendered diagrams.
- `theme`: `auto`, `light`, or `dark`.

Example:

```txt
all-diagrams-web-component.html?skin=material3&theme=dark
```

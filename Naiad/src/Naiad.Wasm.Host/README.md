# Naiad.Wasm.Host

Minimal browser harness that exposes Naiad rendering exports from the host assembly and calls them from JavaScript.

Host startup registers optional skin plugins:

- `Naiad.Skins.Cats` (`cats`)
- `Naiad.Skins.Showcase` (`prism3d`, `neon`, `sunset`)

Includes a reusable client module at `wwwroot/naiad-client.js` with:

- `init()`
- `health()`
- `detectDiagramType(mermaid)`
- `renderSvg(mermaid, options?)`
- `renderSvgDocument(mermaid, options?)`
- `listSkinPacks()`

Includes a standard custom element at `wwwroot/naiad-web-component.js`:

- `<naiad-diagram mermaid="flowchart LR&#10;  A --> B"></naiad-diagram>`

Notable UX features in the web component:

- Auto theme responsiveness (`theme="auto"` + `theme-responsive`)
- Resize awareness (`resized` event, optional `rerender-on-resize`)
- Built-in save menu (`show-menu`) with SVG/PNG export hooks and methods
- Mermaid-compatible Naiad directives in comments (`%% naiad: ...`)

You can also provide Mermaid as inner text:

```html
<naiad-diagram>
%% naiad: skinPack=daisyui
flowchart LR
  A --> B
</naiad-diagram>
```

## Build

```bash
dotnet publish Naiad/src/Naiad.Wasm.Host/Naiad.Wasm.Host.csproj -c Release
```

## Run Locally

Serve the published `wwwroot` directory with any static file server:

```bash
python -m http.server 8080 --directory Naiad/src/Naiad.Wasm.Host/bin/Release/net10.0-browser/publish/wwwroot
```

Open `http://localhost:8080` and click **Render**.

Plain web-component-only page:

- `http://localhost:8080/plain-web-component.html`

Showcase skin demo page:

- `http://localhost:8080/showcase-skins-demo.html?skin=prism3d&theme=dark`

For full styling guidance (CSS variables, `::part`, theme examples), see:

- `Naiad/src/Naiad.Wasm.Npm/README.md`

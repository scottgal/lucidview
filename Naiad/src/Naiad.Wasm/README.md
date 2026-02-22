# Naiad.Wasm

Browser-facing WebAssembly export surface for Naiad.

This project targets `net10.0-browser` and exposes JavaScript-callable methods via `[JSExport]`:

- `RenderSvg`
- `RenderSvgDocumentJson`
- `DetectDiagramType`
- `Health`

It references the core `Naiad` library so parsing/layout/rendering remains in one place.

# Markdown -> SVG Renderer Plan (C# / Naiad Fork)

## Goal
Build a deterministic, fully managed C# markdown-to-SVG renderer with no browser dependency, then integrate it into a fork of Naiad.

## Why This Approach
- Keeps rendering server-friendly and reproducible.
- Enables first-class graph routing and layout control.
- Avoids runtime JS engines for Mermaid-like output.

## Core Stack
- `Markdig` for markdown parsing to AST.
- `Microsoft.Msagl` + `Microsoft.Msagl.Drawing` for graph layout/routing (MSR Automatic Graph Layout).
- `SkiaSharp` + HarfBuzz shaping for text measurement.
- Custom SVG writer (or thin adapter) for strict output control.

## Architecture
1. Parse markdown into a typed IR:
   - Paragraphs, headings, lists, tables, code blocks.
   - Diagram blocks (` ```mermaid `, ` ```graph `, future `dot`).
2. Layout text blocks in C#:
   - Measure runs with exact font metrics.
   - Resolve line wraps, spacing, and anchors.
3. Graph pipeline (MSAGL path):
   - Convert diagram IR to MSAGL graph objects.
   - Use Sugiyama/MDS layout based on diagram type.
   - Route edges with MSAGL routers (spline/orthogonal).
   - Capture node bounds + edge geometry as renderer primitives.
4. SVG emission:
   - Render text, paths, markers, fills, classes.
   - Emit stable IDs and CSS class hooks for theming.
5. Caching:
   - Hash markdown block + theme + width + font config.
   - Cache per-diagram and per-document SVG fragments.

## Naiad Fork Integration
- Add an `ISvgDiagramRenderer` abstraction in the fork.
- Default implementation: `MsaglSvgDiagramRenderer`.
- Wire diagram fences to renderer dispatch before final SVG compose.
- Preserve existing non-diagram markdown rendering behavior.

## Delivery Phases
1. Spike (1-2 days):
   - Single graph block -> MSAGL -> SVG proof.
2. Vertical slice (3-5 days):
   - Markdown parse + diagram extraction + embedded SVG output.
3. Production hardening (1-2 weeks):
   - Theming, font fallback, error surfaces, snapshot tests.
4. Compatibility pass:
   - Map top Mermaid subset features to IR.
   - Clear diagnostics for unsupported syntax.

## Risks and Controls
- Font metric drift: lock shaping path to Skia/HarfBuzz and snapshot outputs.
- Mermaid feature parity: ship subset first; add feature flags + graceful fallback.
- Large graph performance: cap node count and expose timeout/cancel tokens.

## Immediate Next Tasks
1. Create `n-aiad-svg` branch/fork with renderer interfaces.
2. Add MSAGL prototype service and snapshot tests.
3. Implement fenced-diagram extraction in markdown IR layer.

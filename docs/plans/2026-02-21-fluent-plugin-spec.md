# Spec: `Fluent` Plugin for Mermaid Authoring

Date: 2026-02-21  
Status: Draft v1

## 1. Purpose

Add a developer-friendly fluent authoring plugin for Mermaid diagrams in C#, with:

1. Typed fluent builders for diagram creation.
2. `TryParse`-style APIs for converting Mermaid text into a typed model without throwing.
3. Structured diagnostics (errors/warnings/info) with machine-readable codes and source spans.

This plugin is additive and does not replace existing `Mermaid.Render(string, RenderOptions?)`.

## 2. Scope

In scope (v1):

1. Plugin package: `MermaidSharp.Fluent` (or `Naiad.Fluent` if naming stays under Naiad).
2. Flowchart deep support (nodes, edges, subgraphs, classes, styles).
3. Multi-diagram plugin contracts that cover all currently supported Mermaid/Naiad diagram families.
4. Common parse/validation/diagnostic contracts used by all diagram types.
5. Deterministic Mermaid text emission from typed models.

Out of scope (v1):

1. Full syntax parity for every Mermaid diagram type.
2. Runtime plugin loading from external assemblies.
3. UI-specific integration concerns.

## 3. Design Goals

1. Developer ergonomics
   - IntelliSense-first API shape.
   - Safe defaults, discoverable options, minimal boilerplate.

2. Reliability
   - No exceptions for normal user-input issues.
   - Repeatable output for snapshot tests and caching.

3. Extensibility
   - New diagram families can plug in with consistent contracts.

4. Composability
   - Fluent output feeds existing renderer directly:
   - `Mermaid.Render(diagram.ToMermaid(), options)`.

## 4. High-Level Architecture

`MermaidSharp.Fluent` contains 4 layers:

1. Domain Model (immutable AST)
   - `MermaidDiagram`
   - `FlowchartDiagram`
   - statements (`NodeStatement`, `EdgeStatement`, `SubgraphStatement`, etc.)

2. Fluent Builders (mutable authoring layer)
   - `FlowchartBuilder`
   - `SubgraphBuilder`
   - `ClassDefBuilder`

3. Parsing + Validation
   - plugin-specific parser adapters
   - shared validation engine
   - structured diagnostics collector

4. Serialization
   - deterministic Mermaid text writer
   - formatting options (indent/newline/ordering policy)

## 5. Plugin Model

The Fluent plugin model targets diagram-family extensibility in-process.

```csharp
public interface IFluentDiagramPlugin
{
    DiagramType DiagramType { get; }

    // For text -> typed model
    ParseResult<MermaidDiagram> TryParse(
        string source,
        ParseOptions? options = null);

    // For typed model -> canonical Mermaid text
    SerializeResult TrySerialize(
        MermaidDiagram diagram,
        SerializeOptions? options = null);

    // For fluent builder entrypoint
    IFluentBuilderFactory BuilderFactory { get; }
}
```

Default registry (internal singleton by default):

```csharp
public interface IFluentPluginRegistry
{
    bool TryGet(DiagramType type, out IFluentDiagramPlugin plugin);
    IReadOnlyCollection<IFluentDiagramPlugin> Plugins { get; }
}
```

## 6. Public API (Developer Entry Points)

### 6.1 Fluent construction

```csharp
var diagram = MermaidFluent
    .Flowchart(Direction.LR, flow => flow
        .Node("A", "Start")
        .Node("B", "Process")
        .Subgraph("sg1", "Worker Pool", sg => sg
            .Direction(Direction.TB)
            .Node("W1", "Worker 1")
            .Node("W2", "Worker 2")
            .Edge("W1", "W2"))
        .Edge("A", "B")
        .Edge("B", "sg1"))
    .Build();
```

### 6.1.1 Additional diagram entry points

```csharp
var seq = MermaidFluent.Sequence(s => s
    .Participant("Client")
    .Participant("API")
    .Message("Client", "API", "GET /items"))
    .Build();

var cls = MermaidFluent.ClassDiagram(c => c
    .Class("Order", x => x.Property("Id", "Guid").Method("Submit()"))
    .Class("Invoice")
    .Relationship("Order", "Invoice", ClassRelation.Composition))
    .Build();
```

### 6.2 Parsing with no-throw contract

```csharp
if (MermaidFluent.TryParse(source, out var diagram, out var diagnostics))
{
    // diagram is non-null and valid enough for serialize/render
}
else
{
    // inspect diagnostics (codes, spans, suggestions)
}
```

### 6.3 Strongly typed parse result

```csharp
ParseResult<FlowchartDiagram> result = MermaidFluent.Flowchart.TryParse(source);
if (!result.Success)
{
    foreach (var d in result.Diagnostics.Items)
    {
        Console.WriteLine($"{d.Code}: {d.Message} @ {d.Span.StartLine}:{d.Span.StartColumn}");
    }
}
```

## 7. `TryParse` Contract

Rules:

1. Never throw for malformed user Mermaid text.
2. Throw only for programmer misuse (for example `source == null` if nullability contract disallows it).
3. Always return diagnostics (possibly empty).
4. `Success == true` means:
   - at least one diagram type matched
   - model is semantically valid for v1 guarantees
5. `Success == false` means:
   - `Value` is `null`
   - diagnostics include at least one `Error`

Result types:

```csharp
public sealed record ParseResult<T>(
    bool Success,
    T? Value,
    DiagnosticBag Diagnostics);

public sealed record SerializeResult(
    bool Success,
    string? Mermaid,
    DiagnosticBag Diagnostics);
```

## 8. Structured Diagnostics

### 8.1 Diagnostic model

```csharp
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SourceSpan(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public sealed record MermaidDiagnostic(
    string Code,                 // e.g. FLUENT001, PARSE014, VALID102
    DiagnosticSeverity Severity,
    string Message,              // human readable
    SourceSpan? Span,            // null when not source-bound
    string? Path,                // optional source path
    string? NodeId,              // optional domain id
    string? Suggestion,          // fix hint
    IReadOnlyDictionary<string, object?>? Metadata);
```

### 8.2 Diagnostic bag behavior

1. Preserve insertion order.
2. Expose severity counts.
3. Support filtering by severity/code prefix.
4. Export to JSON for tooling.

```csharp
public sealed class DiagnosticBag
{
    public IReadOnlyList<MermaidDiagnostic> Items { get; }
    public bool HasErrors { get; }
    public int ErrorCount { get; }
    public int WarningCount { get; }
}
```

### 8.3 Initial code families

1. `PARSE###` tokenization/grammar diagnostics.
2. `VALID###` semantic/graph diagnostics.
3. `FLUENT###` fluent-builder usage diagnostics.
4. `SER###` serialization diagnostics.

## 9. Diagram Family Coverage

This section defines coverage expectations for all diagram families already represented in `DiagramType`.

Support tiers:

1. Tier A: full fluent builder + `TryParse` + semantic validation + deterministic serialize.
2. Tier B: fluent builder + deterministic serialize + basic parse diagnostics.
3. Tier C: parse/diagnostics + passthrough typed wrapper (no full fluent surface yet).

### 9.1 Coverage matrix (initial plan)

1. Flowchart: Tier A (v1).
2. Sequence: Tier B (v1.1 target).
3. Class: Tier B (v1.1 target).
4. State: Tier B (v1.2 target).
5. EntityRelationship: Tier B (v1.2 target).
6. Gantt: Tier C (v1.x).
7. Pie: Tier C (v1.x).
8. GitGraph: Tier C (v1.x).
9. Mindmap: Tier C (v1.x).
10. Timeline: Tier C (v1.x).
11. UserJourney: Tier C (v1.x).
12. Quadrant: Tier C (v1.x).
13. XYChart: Tier C (v1.x).
14. Sankey: Tier C (v1.x).
15. Block: Tier C (v1.x).
16. Kanban: Tier C (v1.x).
17. Packet: Tier C (v1.x).
18. Requirement: Tier C (v1.x).
19. Architecture: Tier C (v1.x).
20. Radar: Tier C (v1.x).
21. Treemap: Tier C (v1.x).
22. C4Context/C4Container/C4Component/C4Deployment: Tier C (single C4 plugin with subtype model).
23. Bpmn: Tier C (XML-first adapter + diagnostics normalization).
24. ZenUML: explicitly unsupported until renderer support exists.

### 9.2 Per-type `TryParse` requirements

All diagram plugins must provide:

1. `TryParse(string, ...)` no-throw behavior.
2. Typed result (`ParseResult<TDiagram>`).
3. Structured diagnostics with at least:
   - one parser code family for that diagram
   - line/column spans where available
   - actionable suggestion text for common mistakes

### 9.3 Cross-diagram shared requirements

1. `TryParseAny` helper should auto-detect type and dispatch plugin:

```csharp
ParseResult<MermaidDiagram> result = MermaidFluent.TryParseAny(source);
```

2. `TrySerialize` must be available for every plugin, even if fluent builder is not yet Tier A/B.
3. Diagnostics must be schema-stable across all diagram plugins.

## 10. Flowchart v1 Functional Requirements

Required operations:

1. Diagram header and direction.
2. Node add/update (`id`, text, shape optional).
3. Edge add (`from`, `to`, type, optional label).
4. Subgraph nesting with id + title.
5. Class definitions and class assignment.
6. Style assignment (minimal supported subset for v1).

Validation:

1. Duplicate id collision across node/subgraph kinds -> `VALID001`.
2. Missing edge endpoint -> `VALID002`.
3. Invalid direction token -> `VALID003`.
4. Disallowed reserved token usage -> `VALID004`.
5. Subgraph direction caveat (externally linked) -> warning `VALID101`.

## 11. Output Formats and Export Contract

The Fluent plugin must support source + rendered outputs through one export surface.

### 11.1 Required outputs (v1)

1. Mermaid source (`.mmd` / `.mermaid`)
   - canonical Mermaid text from typed model.
2. SVG (`.svg`)
   - primary vector artifact.
3. PNG (`.png`)
   - primary raster artifact.

### 11.2 Additional researched outputs (v1.x)

1. PDF (`.pdf`) as secondary vector format.
2. JPG/JPEG (`.jpg`, `.jpeg`) for lossy raster.
3. WEBP (`.webp`) for web-optimized raster.
4. Optional platform-specific vector adapters (`.xps`) behind capability checks.

### 11.3 Export API

```csharp
public enum MermaidOutputFormat
{
    Mermaid, // .mmd
    Svg,
    Png,
    Pdf,
    Jpeg,
    Webp,
    Xps
}

public sealed record ExportOptions(
    float Scale = 1f,
    int Quality = 100,
    string? Background = null,
    bool EmbedFonts = false);

public sealed record ExportResult(
    bool Success,
    MermaidOutputFormat Format,
    byte[]? Bytes,
    string? Text,
    string? MimeType,
    DiagnosticBag Diagnostics);
```

```csharp
ExportResult result = MermaidFluent.Export(diagram, MermaidOutputFormat.Png, options);
```

### 11.4 Capability discovery

```csharp
public interface IOutputCapabilities
{
    bool Supports(MermaidOutputFormat format);
    IReadOnlyCollection<MermaidOutputFormat> Formats { get; }
}
```

If a format is requested but unavailable on current runtime/platform:

1. Return `Success = false`.
2. Emit `SER4xx` diagnostic with format + platform metadata.
3. Never throw for unsupported format requests.

### 11.5 Pipeline model

1. Typed model -> Mermaid source (`ToMermaid()`).
2. Mermaid source -> SVG via existing Naiad/Mermaid renderer.
3. SVG -> target format adapter.
   - PNG/JPEG/WEBP via Skia encoding path.
   - PDF/XPS via vector document adapters.

### 11.6 Source-informed rationale (research summary)

1. Mermaid CLI positions SVG/PNG/PDF as baseline outputs.
2. SkiaSharp exposes many encoded raster formats (`Png`, `Jpeg`, `Webp`, `Gif`, `Bmp`, `Heif`, `Avif`, `Jpegxl`, etc.).
3. SkiaSharp has first-class PDF document generation APIs.
4. Svg.Skia documents direct SVG-to-PNG/PDF/XPS conversion and converter formats (`png`, `jpg`, `jpeg`, `webp`, `pdf`, `xps`).

Inference for this spec:

1. Keep v1 guaranteed set small (`mmd`, `svg`, `png`) for reliability.
2. Implement `pdf`, `jpeg`, `webp` as v1.x with strong test coverage.
3. Gate `xps` and other advanced formats behind runtime capability checks.

## 12. Deterministic Serialization

Rules:

1. Preserve builder insertion order for statements.
2. Stable property ordering for style/class directives.
3. Canonical newline handling (default `\n`).
4. Round-trip target:
   - `TryParse(Serialize(diagram))` should be semantically equivalent.

## 13. Developer Experience Requirements

1. No forced exception handling for common parse failures (`TryParse` everywhere).
2. Rich error display data:
   - line/column span
   - code
   - suggestion
   - metadata
3. Builder discoverability:
   - narrow context methods (`SubgraphBuilder` cannot call unrelated APIs).
4. Debug support:
   - `ToDebugString()` for AST nodes
   - `.ToJson()` for diagnostics

## 14. Compatibility and Integration

1. Existing `Mermaid` static APIs remain unchanged.
2. Fluent plugin outputs Mermaid text accepted by current renderers.
3. No runtime dependency from core rendering to fluent package.
4. Versioning:
   - semver contract for public fluent API and diagnostic schema.

## 15. Testing Strategy

1. Unit tests
   - builder chain semantics
   - `TryParse` success/failure behavior
   - diagnostics completeness and code stability

2. Snapshot tests
   - serialized Mermaid text for known fluent definitions

3. Round-trip tests
   - parse -> serialize -> parse semantic equivalence

4. Negative corpus tests
   - malformed Mermaid input with expected structured errors

## 16. Rollout Plan

1. Milestone 1
   - Core contracts (`ParseResult`, `DiagnosticBag`, diagnostic schema)
   - Plugin registry and `TryParseAny` dispatch
   - Flowchart builder + serializer (Tier A foundation)

2. Milestone 2
   - Flowchart `TryParse` adapter + semantic validator
   - full diagnostics coverage for Flowchart
   - Sequence/Class Tier B builder + serializer

3. Milestone 3
   - State/ER Tier B support
   - Tier C adapters for remaining types (parse + diagnostics + typed wrappers)

4. Milestone 4
   - Selective Tier upgrades by demand (Gantt/C4/Architecture likely first)
   - Docs and migration guidance

## 17. Open Questions

1. Package name: `MermaidSharp.Fluent` vs `Naiad.Fluent`.
2. Whether plugin registry is public or internal in v1.
3. Strict vs lenient parse mode defaults.
4. Whether to include auto-fix helpers in v1 (for example normalized ids).
5. Priority order for Tier C -> Tier B upgrades by real usage.
6. Whether `Heif`/`Avif`/`Jpegxl` should be officially exposed in public API or treated as experimental.

## 18. References

1. Mermaid CLI output formats (`svg`, `png`, `pdf`): https://github.com/mermaid-js/mermaid-cli
2. SkiaSharp encoded image formats (`SKEncodedImageFormat`): https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skencodedimageformat?view=skiasharp-2.88
3. SkiaSharp PDF generation (`SKDocument.CreatePdf`): https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skdocument.createpdf?view=skiasharp-2.88
4. Svg.Skia conversion capabilities (`png`, `jpg`, `jpeg`, `webp`, `pdf`, `xps`): https://github.com/wieslawsoltes/Svg.Skia
5. SVG as vector graphics standard (W3C): https://www.w3.org/TR/SVG2/


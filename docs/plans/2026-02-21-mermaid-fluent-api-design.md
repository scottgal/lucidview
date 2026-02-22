# Mermaid Fluent API Design Research (C#)

Date: 2026-02-21

## Goal

Design a structured, type-safe fluent API for authoring Mermaid diagrams directly in C#, starting with flowcharts (including subgraphs), while scaling to other Mermaid diagram types.

## Best-in-Class Fluent API Patterns (References)

1. EF Core ModelBuilder
   - Pattern: scoped fluent contexts (`modelBuilder.Entity<T>()...`) and deterministic "last call wins" semantics.
   - Ref: https://learn.microsoft.com/en-us/ef/core/modeling/

2. ASP.NET Core Minimal APIs RouteGroupBuilder
   - Pattern: a group object that is both a container and a convention target, then chained customization on the same object.
   - Ref: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0
   - Ref: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.endpointroutebuilderextensions.mapgroup?view=aspnetcore-10.0

3. Polly ResiliencePipelineBuilder
   - Pattern: additive, ordered chain with explicit `.Build()` to finalize immutable output.
   - Ref: https://www.pollydocs.org/getting-started.html

4. Serilog LoggerConfiguration
   - Pattern: discoverable chain of feature namespaces (`WriteTo`, `Enrich`, etc.), ending in `.CreateLogger()`.
   - Ref: https://github.com/serilog/serilog

5. FluentAssertions
   - Pattern: chaining with typed continuation points (`.Which`, `.And`) to keep context and avoid ambiguous next steps.
   - Ref: https://fluentassertions.com/introduction

6. Elastic .NET Client (Fluent + Object Initializer duality)
   - Pattern: provide both fluent lambdas and object model initialization for advanced/serialized scenarios.
   - Ref: https://www.elastic.co/docs/reference/elasticsearch/clients/dotnet/query

7. Mermaid Flowchart Spec
   - Domain constraints to model directly: graph direction, subgraphs with explicit id/title, edges to/from subgraphs, and direction limitations for externally linked subgraphs.
   - Ref: https://mermaid.js.org/syntax/flowchart.html

## Implications for Mermaid Fluent API

1. Use scoped builders
   - `FlowchartBuilder` root.
   - `SubgraphBuilder` nested context.
   - Chain returns current scope where possible.

2. Keep ordering explicit
   - Emission order follows call order.
   - Last assignment wins for mutable properties (label/style/class per id), similar to EF Core behavior.

3. Make build/finalize explicit
   - `Build()` returns immutable diagram AST.
   - `ToMermaid()` serializes AST.

4. Prefer ids as first-class keys
   - `NodeId` and `SubgraphId` value types (or validated strings) avoid accidental mismatches.

5. Support two authoring styles
   - Fluent DSL for most users.
   - Object model/AST constructors for generation pipelines.

6. Represent Mermaid constraints in validators
   - Validate impossible states early.
   - Surface warnings (not just errors) for Mermaid-specific behavior (for example subgraph direction ignored when externally linked).

## Proposed API Shape (Flowchart First)

```csharp
var diagram = MermaidDsl
    .Flowchart(Direction.LR, flow => flow
        .Node("A", "Start")
        .Node("B", "Process")
        .Node("C", "End")
        .Edge("A", "B")
        .Edge("B", "C")
        .Subgraph("sg_orders", "Order Processing", sg => sg
            .Direction(Direction.TB)
            .Node("D", "Validate")
            .Node("E", "Charge")
            .Edge("D", "E"))
        .Edge("B", "sg_orders")     // edge to subgraph id
        .ClassDef("critical", c => c.Fill("#ffe6e6").Stroke("#cc0000"))
        .Class("E", "critical")
    )
    .Build();

string mermaid = diagram.ToMermaid();
string svg = Mermaid.Render(mermaid);
```

### Builder API sketch

```csharp
public static class MermaidDsl
{
    public static FlowchartBuilder Flowchart(Direction direction, Action<FlowchartBuilder> configure);
    public static SequenceBuilder Sequence(Action<SequenceBuilder> configure);
    public static ClassDiagramBuilder ClassDiagram(Action<ClassDiagramBuilder> configure);
}

public sealed class FlowchartBuilder
{
    public FlowchartBuilder Direction(Direction direction);
    public FlowchartBuilder Node(string id, string? text = null, NodeShape? shape = null);
    public FlowchartBuilder Edge(string from, string to, EdgeType type = EdgeType.Arrow, string? label = null);
    public FlowchartBuilder Subgraph(string id, string title, Action<SubgraphBuilder> configure);
    public FlowchartBuilder ClassDef(string name, Action<ClassDefBuilder> configure);
    public FlowchartBuilder Class(string nodeOrSubgraphId, params string[] classNames);
    public MermaidDiagram Build();
}

public sealed class SubgraphBuilder
{
    public SubgraphBuilder Direction(Direction direction);
    public SubgraphBuilder Node(string id, string? text = null, NodeShape? shape = null);
    public SubgraphBuilder Edge(string from, string to, EdgeType type = EdgeType.Arrow, string? label = null);
    public SubgraphBuilder Subgraph(string id, string title, Action<SubgraphBuilder> configure);
}
```

## Why this shape matches Mermaid and C#

1. Subgraphs are explicit domain objects
   - Mermaid supports `subgraph title ... end` plus explicit subgraph ids.

2. Group-level chain mirrors route-group style
   - Like `MapGroup`, subgraph scope supports scoped configuration and nested definitions.

3. Immutable built artifact supports rendering/caching
   - Works cleanly with existing `Mermaid.Render` and snapshot tests.

4. Deterministic ordering supports predictable output diffs
   - Critical for golden-file tests and doc generation.

## Multi-Diagram Strategy

Use shared infrastructure + per-diagram fluent facades:

1. Shared
   - `MermaidDiagram` base immutable AST
   - `IMermaidStatement`
   - common text escaping/identifier validation
   - stable serializer

2. Per diagram
   - `FlowchartBuilder`, `SequenceBuilder`, `ClassDiagramBuilder`, etc.
   - each emits typed statements into its own AST subtype

3. Single render path
   - `diagram.ToMermaid()` -> existing `Mermaid.Render(...)`

## Validation Rules to Include Early

1. Duplicate ids
   - error on conflicting kind (node vs subgraph).
2. Edge endpoints
   - must reference existing node/subgraph ids by default (option to allow forward references).
3. Direction warnings
   - warn when subgraph direction likely ignored due to external links.
4. Reserved token pitfalls
   - warn for known Mermaid footguns (`end`, `o`/`x` edge shorthand ambiguities).

## Suggested Rollout

1. Phase 1
   - Implement `FlowchartBuilder`, `SubgraphBuilder`, serializer, validator.
   - Add snapshot tests that compare generated Mermaid text.

2. Phase 2
   - Add classDefs, styles, link styles, click directives.

3. Phase 3
   - Add second diagram type (Sequence) to validate cross-diagram abstractions.

4. Phase 4
   - Public API hardening: naming cleanup, docs, migration notes.


# Tulip Graph Format Support

Naiad provides native support for the **Tulip** graph visualization framework's TLP file format, enabling interoperability with Tulip's powerful graph analysis tools.

## What is Tulip?

[Tulip](https://tulip.labri.fr) is an information visualization framework developed by the University of Bordeaux, designed to create, manipulate, and visualize massive graphs (1M+ nodes, 5M+ edges). It's widely used in:

- Social network analysis
- Biological network visualization
- Software dependency analysis
- Knowledge graph exploration

## TLP Format Overview

TLP (Tulip format) is an S-expression based text format for describing graphs:

```tlp
(tlp "2.3"
  (nodes 0..4)           ; Node range
  (edge 0 0 1)           ; Edge: id, source, target
  (property 0 string "viewLabel"
    (node 0 "Label")
  )
)
```

## Supported Features

| Feature | Status | Notes |
|---------|--------|-------|
| Node definitions | ✅ | Ranges (`0..4`) and explicit lists |
| Edge definitions | ✅ | Directed edges with IDs |
| String properties | ✅ | Labels, annotations |
| Color properties | ✅ | RGBA format `(r,g,b,a)` |
| Size properties | ✅ | 3D size `(w,h,d)` |
| Layout properties | ✅ | 3D coordinates `(x,y,z)` |
| Clusters | ✅ | Hierarchical grouping |
| Comments | ✅ | Semicolon-prefixed lines |
| Metadata | ✅ | Author, date, comments |

## API Usage

### Parsing TLP Files

```csharp
using MermaidSharp.Formats;

var tlpContent = File.ReadAllText("graph.tlp");
var graph = TlpParser.Parse(tlpContent);

// Access nodes
foreach (var node in graph.Nodes)
{
    Console.WriteLine($"Node {node.Id}");
}

// Access edges
foreach (var edge in graph.Edges)
{
    Console.WriteLine($"Edge {edge.Id}: {edge.Source} -> {edge.Target}");
}

// Access properties
if (graph.Properties.TryGetValue("viewLabel", out var labelProp))
{
    var label = labelProp.NodeValues[nodeId];
}
```

### Converting to Mermaid

```csharp
using MermaidSharp.Formats;
using Naiad.RenderCli;

var graph = TlpParser.Parse(tlpContent);
var mermaidCode = TlpRenderer.ConvertToMermaid(graph);
var svg = Mermaid.Render(mermaidCode);
```

### CLI Usage

```bash
# Convert TLP to SVG
dotnet run -- tlp graph.tlp graph.svg

# Convert TLP to Mermaid syntax
dotnet run -- tlp graph.tlp graph.mmd
```

## Sample Files

### Social Network Analysis

![Social Network](sample-social-network.svg)

A social network with 20 members organized into 4 communities, showing:

- **Node colors**: Community membership (Blue, Green, Orange, Red)
- **Node sizes**: Degree centrality
- **Edges**: Friendships (within community) and bridges (between communities)

```tlp
(tlp "2.3"
  (author "Naiad Documentation")
  (comments "Social network analysis with community detection")
  
  (nodes 0..19)
  
  ; Community 1 edges
  (edge 0 0 1)
  (edge 1 0 2)
  ; ... more edges
  
  ; Node colors by community
  (property 1 color "viewColor"
    (node 0 "(66,133,244,255)")  ; Blue - Community 1
    (node 5 "(52,168,83,255)")   ; Green - Community 2
    ; ... more colors
  )
)
```

### Software Dependencies

![Dependencies](sample-dependencies.svg)

A layered software architecture showing 16 packages:

- **Purple**: Application layer
- **Blue**: Core libraries
- **Green**: Data layer
- **Orange**: Service layer
- **Red**: API layer
- **Cyan**: UI layer

### Family Tree

![Family Tree](sample-family-tree.svg)

A three-generation genealogy with:

- **Blue nodes**: Male family members
- **Pink nodes**: Female family members
- **Clusters**: Generational groupings

## TLP Format Reference

### Basic Structure

```tlp
(tlp "version"
  ; metadata
  ; nodes
  ; edges  
  ; properties
  ; clusters
)
```

### Nodes

```tlp
; Range notation
(nodes 0..99)

; Explicit list
(nodes 0 1 2 5 10 15)

; Mixed
(nodes 0..4 10 20..25)
```

### Edges

```tlp
(edge id source target)
```

### Properties

```tlp
(property clusterId type "name"
  (default "nodeDefault" "edgeDefault")
  (node id "value")
  (edge id "value")
)
```

#### Property Types

| Type | Format | Example |
|------|--------|---------|
| `string` | Text | `"Label"` |
| `bool` | Boolean | `true` / `false` |
| `int` | Integer | `42` |
| `double` | Float | `3.14159` |
| `color` | RGBA | `(255,128,0,255)` |
| `size` | 3D Size | `(10,10,1)` |
| `layout` | 3D Coord | `(100,200,0)` |

### Clusters

```tlp
(cluster id
  (nodes 0 1 2 3)
  (edges 0 1 2)
  ; Nested clusters
  (cluster subId
    (nodes 0 1)
  )
)
```

### Comments

```tlp
; This is a comment
(nodes 0..9)  ; Inline comment not supported
```

## Integration with Mermaid Diagrams

You can also generate TLP from Mermaid flowcharts:

```csharp
using MermaidSharp.Formats;

var exporter = new TlpExporter();
var tlpContent = exporter.Export(flowchartModel);
File.WriteAllText("output.tlp", tlpContent);
```

This enables a workflow where you:
1. Create diagrams in Mermaid syntax
2. Export to TLP for advanced analysis in Tulip
3. Import modified TLP back to visualize changes

## Export Format

The exported TLP preserves:

- Node IDs and labels
- Edge connections
- Subgraph structure (as clusters)
- Basic styling information

```csharp
var exporter = new TlpExporter();
var tlp = exporter.Export(flowchartModel);
// Produces:
// (tlp "2.3"
//   (date "2025-02-22")
//   (nodes 0 1 2 3)
//   (edge 0 0 1)
//   (property 0 string "viewLabel"
//     (node 0 "Start")
//     (node 1 "Process")
//   )
// )
```

## Related Diagram Types

Naiad also includes Tulip-inspired visualization types:

| Type | Syntax | Use Case |
|------|--------|----------|
| **parallelcoords** | `axis A,B,C` | Multivariate data |
| **dendrogram** | `leaf "A","B"` | Hierarchical clustering |
| **bubblepack** | `"Node":value` | Circle packing |
| **voronoi** | `site "A" at x,y` | Spatial partitioning |

See the main documentation for details on these diagram types.

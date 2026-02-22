# Flowchart Layout Parity with mermaid.js

**Date**: 2026-02-20
**Goal**: Get Naiad flowchart layout at least as good as mermaid.js, fix connector stacking

## Problem

When multiple edges enter or exit the same node, connectors stack/overlap instead of spreading cleanly. Both fan-in (many→one) and fan-out (one→many) are affected. Channel spacing for sibling edges uses fixed 20px regardless of target spread.

## Deliverables

### 1. Complex Flowchart Test Cases (10-15 new samples)

- **fan-in-5**: 5 nodes all pointing to 1 target
- **fan-out-5**: 1 node branching to 5 targets
- **diamond-cascade**: Chained decisions with reconvergence
- **nested-subgraphs-3-deep**: 3 levels of nesting with cross-subgraph edges
- **all-edge-types**: solid, dotted, thick, bidirectional, circle-end, cross-end with labels
- **all-shapes**: every NodeShape in one diagram
- **self-loop-plus-back-edge**: self-loop + back-edge + forward edges on same nodes
- **long-labels-mixed**: wide nodes alongside narrow ones
- **classDef-subgraph-combo**: classDef + style directives within subgraphs
- **wide-fan-reconverge**: fan-out then all branches reconverge to single node
- **parallel-chains**: multiple independent chains side by side
- **cross-link-ranks**: edges that skip ranks (A→D skipping B,C layer)
- **bidirectional-cycle**: A↔B↔C↔A with labels

### 2. Fix Connector Stacking

**Entry offsets (AssignEntryOffset)**:
- Sort incoming edges by source node's cross-axis position
- Assign offsets so left-most source gets left-most entry point
- Prevents edge crossings at entry

**Exit offsets (AssignExitPort)**:
- Already sorts by target cross-axis position (good)
- Scale channel spacing proportionally to target spread
- Minimum 15px, maximum proportional to gap between outermost targets

**Channel spacing (ComputeChannelOffset)**:
- Replace fixed 20px with adaptive spacing based on:
  - Distance between source and target ranks
  - Number of siblings
  - Available space between ranks
- Clamp to [12px, 30px] range

### 3. CLI Enhancements

- `--flowcharts-only` flag to skip non-flowchart samples
- `--only <name>` flag to render single diagram
- Show mermaid source code in HTML comparison report
- Auto-open report in browser after generation

## Iteration Workflow

```
fix code → dotnet run -- compare ./out --flowcharts-only → inspect HTML → repeat
```

## Files to Modify

- `Naiad/src/Naiad/Diagrams/Flowchart/FlowchartRenderer.cs` - connector fixes
- `Naiad/src/Naiad.RenderCli/Program.cs` - new samples + CLI flags

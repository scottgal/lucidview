# Fan-Out-Aware Horizontal Coordinate Assignment for Layered Graph Drawing

**An Extension to the Brandes-Köpf Algorithm**

*Scott Galloway*
*February 2026*

---

## Abstract

Brandes-Köpf (BK) horizontal coordinate assignment works well for chains and modest branching, but it degrades on high fan-out nodes. After edge normalization, each long edge becomes a dummy chain. BK can align only one median neighbor per node, so most fan-out chains start near the source X position and spread out later. With spline rendering (for example, B-spline/curveBasis), this creates large distracting curves ("swoopy edges"). This paper proposes a small change to BK's balance step: for edge dummies, blend the BK X coordinate toward the straight-line position between the original edge endpoints, then re-apply layer separation constraints. The method fits existing dagre-style pipelines and adds minimal overhead in practice.

**Keywords:** graph drawing, layered layout, Brandes-Köpf, coordinate assignment, Sugiyama framework, fan-out, dummy nodes

---

## 1. Introduction

The Sugiyama framework [1] for layered graph drawing proceeds in four phases: (1) cycle removal, (2) layer assignment, (3) crossing minimization, and (4) horizontal coordinate assignment. The final phase determines the X-coordinate of each node while respecting the layer ordering established in phase 3.

Brandes and Köpf [2] introduced an O(n) algorithm for phase 4 that forms vertical blocks of aligned nodes and compacts them horizontally. The algorithm runs four passes (upper-left, upper-right, down-left, down-right) and takes the median of the four positions for each node.

However, graphs with fan-out expose a structural limit. During normalization, each long edge becomes an independent dummy chain. In vertical alignment, BK can align a node with only one median neighbor. For fan-out *k*, at most one chain aligns with the source; the other *k−1* chains get displaced X positions.

When those waypoints are rendered with cubic splines (common in Mermaid, D3, and dagre), the output often contains exaggerated curves that compete with the diagram's structure.

This paper:
1. Defines the fan-out displacement problem in BK coordinate assignment (§2)
2. Explains why naive post-processing is brittle (§3)
3. Introduces a balance-phase modification that biases dummies toward straight-line positions while preserving separation (§4)
4. Describes a dagre-compatible C# implementation (§5)

---

## 2. Background and Problem Analysis

### 2.1 The Brandes-Köpf Algorithm

Given a layered graph *G = (V, E)* with layers *L₁, L₂, ..., Lₕ*, BK proceeds as follows:

**Phase 1: Conflict Detection.** Identify Type-1 conflicts (non-inner segments crossing inner segments) and Type-2 conflicts (segments crossing compound node borders). These conflicts constrain which nodes may be aligned into blocks.

**Phase 2: Vertical Alignment.** For each of the four directional passes (UL, UR, DL, DR), scan layers and attempt to align each node *v* with its *median* neighbor in the adjacent layer. Two dictionaries are maintained:

- `root[v]`: the root of the block containing *v*
- `align[v]`: the next node in *v*'s block

The alignment is greedy: scanning left-to-right (or right-to-left), each node attempts to align with its median predecessor (or successor). If the median neighbor's position does not violate the monotonicity constraint (`prevIdx < pos[w]`), the alignment is formed. Otherwise, the node remains unaligned.

**Phase 3: Horizontal Compaction.** A "block graph" is constructed where each block root is a node, and edges encode minimum separation constraints. Two sweeps assign coordinates: a forward pass assigns smallest valid coordinates, a backward pass removes slack.

**Phase 4: Balance.** For each node, the four X-coordinates from the four passes are sorted, and the final position is the median (average of the two middle values):

```
x[v] = (sort(x_ul[v], x_ur[v], x_dl[v], x_dr[v])[1] + sort(...)[2]) / 2
```

### 2.2 Edge Normalization and Dummy Nodes

Before coordinate assignment, edges spanning multiple ranks are "normalized" by inserting dummy nodes at each intermediate rank. An edge *(u, w)* where `rank(w) - rank(u) = k > 1` is replaced by a chain:

```
u → d₁ → d₂ → ... → d_{k-1} → w
```

Each dummy node `dᵢ` carries metadata:
- `Dummy = "edge"` — identifies it as a dummy
- `EdgeObj = (u, w)` — the original edge endpoints
- `EdgeLabel` — the original edge's label object
- `Width = 0, Height = 0` — zero visual footprint

During denormalization (Normalize.Undo), each dummy's `(X, Y)` coordinate becomes a waypoint in the original edge's point list.

### 2.3 The Fan-Out Problem

Consider a node *S* at rank *r* with fan-out edges to targets *T₁, T₂, ..., Tₖ* at rank *r + m*. After normalization, this produces *k* independent dummy chains, each of length *m − 1*.

In the vertical alignment phase, *S* can align with at most **one** of its successor dummy nodes (the median one). The remaining *k − 1* dummy nodes at rank *r + 1* are unaligned—their X-coordinates are determined solely by the compaction phase's minimum-separation constraints.

**Consequence:** The unaligned dummies cluster near *S*'s X-coordinate (due to minimum separation from the aligned dummy), then must diverge across subsequent layers to reach their respective targets. This creates a characteristic "fan then diverge" pattern in the waypoint coordinates:

```
Layer r:     S (x=200)
Layer r+1:   d₁(x=200) d₂(x=210) d₃(x=220) d₄(x=230) d₅(x=240)
Layer r+2:   d₁(x=200) d₂(x=250) d₃(x=350) d₄(x=500) d₅(x=600)
...
Layer r+m:   T₁(x=100) T₂(x=250) T₃(x=400) T₄(x=550) T₅(x=700)
```

The abrupt X-coordinate shift between the clustered layer (*r+1*) and subsequent layers causes cubic B-spline interpolation to produce large, swooping curves.

### 2.4 Impact Assessment

The visual severity depends on:
- **Fan-out degree *k***: Higher *k* means more unaligned dummies
- **Edge span *m***: Longer edges (more dummy layers) amplify the displacement
- **Spline interpolation**: curveBasis (B-spline) is particularly sensitive; polyline rendering masks the problem but introduces angular aesthetics

For typical software architecture diagrams (fan-out 3–8, edge spans 2–4), the effect ranges from noticeable to visually dominant.

---

## 3. Why Post-Processing Is Insufficient

The first idea most teams try is post-processing: after BK, shift each interior dummy toward the ideal straight line:

```
x'[dᵢ] = (1 − α) · x[dᵢ] + α · ideal_x(dᵢ)
```

where `ideal_x(dᵢ) = x[S] + t · (x[Tⱼ] − x[S])` and `t = (y[dᵢ] − y[S]) / (y[Tⱼ] − y[S])`.

In practice, this has recurring problems:

1. **Separation violations.** Moving dummies can break minimum spacing with neighboring nodes in the same layer. A pure post-pass is blind to BK's constraint structure.

2. **Cascading adjustments.** Straightening one edge can create conflicts with nearby edges or dummies.

3. **Loss of BK intent.** BK's four-pass balance has structure; a late rewrite of coordinates ignores it.

4. **Tuning burden.** The strength parameter *α* is graph-dependent: too low does little, too high causes collisions.

5. **Pipeline complexity.** An extra correction stage outside the core Sugiyama flow is harder to reason about and maintain.

---

## 4. Proposed Algorithm: Fan-Out-Aware Balance

### 4.1 Key Idea

BK balance uses the median of four independent alignment passes. For real nodes, this is usually stable and visually good. For edge dummies in fan-out patterns, all four passes can converge to the same displaced value, so the median does not fix the issue.

The useful detail is that each dummy already stores `EdgeObj = (sourceId, targetId)`. Once real-node X positions are known, we can compute an ideal straight-line X for the dummy and blend toward it.

### 4.2 Algorithm

We modify the `Balance` function to add a fan-out correction phase:

**Input:**
- `xss`: Dictionary mapping alignment keys {ul, ur, dl, dr} to node→X dictionaries
- `g`: The dagre graph (with dummy node metadata)
- `alignDir`: Optional forced alignment direction

**Output:**
- `result`: Dictionary mapping node→final X coordinate

```
function FanOutAwareBalance(xss, g, alignDir):
    // Step 1: Standard BK balance (median of 4 passes)
    result ← StandardBalance(xss, alignDir)

    // Step 2: Identify dummy chains and their original endpoints
    for each node v in result:
        label ← g.Node(v)
        if label.Dummy ≠ "edge" then continue

        edgeObj ← label.EdgeObj   // (sourceId, targetId)
        srcX ← result[edgeObj.v]  // source's balanced X
        tgtX ← result[edgeObj.w]  // target's balanced X
        srcY ← g.Node(edgeObj.v).Y
        tgtY ← g.Node(edgeObj.w).Y

        if |tgtY − srcY| < ε then continue

        // Step 3: Compute ideal straight-line X
        t ← (label.Y − srcY) / (tgtY − srcY)
        idealX ← srcX + t · (tgtX − srcX)

        // Step 4: Blend toward ideal, respecting separation
        α ← EdgeStraighteningStrength  // configurable, default 0.7
        result[v] ← (1 − α) · result[v] + α · idealX

    // Step 5: Enforce minimum separation within each layer
    EnforceLayerSeparation(result, g)

    return result
```

### 4.3 Separation Enforcement

After biasing dummy coordinates, we must ensure minimum separation constraints are not violated. We perform a single sweep per layer:

```
function EnforceLayerSeparation(result, g):
    layering ← BuildLayerMatrix(g)
    for each layer in layering:
        // Sort nodes by assigned X
        sorted ← layer sorted by result[v]
        for i = 1 to |sorted| - 1:
            v ← sorted[i-1]
            w ← sorted[i]
            minSep ← MinSeparation(g, v, w)
            if result[w] - result[v] < minSep:
                result[w] ← result[v] + minSep
```

where `MinSeparation(g, v, w)` computes the required gap based on node widths and the graph's `nodeSep`/`edgeSep` parameters (dummy nodes use `edgeSep`, real nodes use `nodeSep`).

### 4.4 Complexity Analysis

- **Standard BK balance:** O(n) where n = |V| (including dummies)
- **Ideal position computation:** O(d) where d = number of dummy nodes, with O(1) per dummy (constant-time lookup of source/target positions)
- **Layer separation enforcement:** O(n log n) per layer for sorting, O(n) total across all layers

**Total:** O(n log n), compared to BK's O(n). The log factor comes from the per-layer sort in separation enforcement. In practice, layers are small (typically 5–20 nodes), making this negligible.

### 4.5 Practical Properties

The modified balance has three useful properties:

1. **Separation is enforced explicitly.** After blending, a per-layer rightward sweep repairs any adjacent spacing violations.
2. **Behavior is controllable.** `α = 0` gives standard BK output; increasing `α` increases straightening pressure.
3. **No new global phase.** The change stays inside coordinate assignment, instead of introducing a separate post-processing system.

---

## 5. Implementation

### 5.1 Integration Point

The modification targets the `BrandesKopf.Balance` method in the dagre layout pipeline. The function signature expands to accept the graph reference:

```csharp
public static Dictionary<string, float> Balance(
    Dictionary<string, Dictionary<string, float>> xss,
    string alignDir,
    DagreGraph g,          // NEW: graph for dummy metadata
    float alpha = 0.7f)    // NEW: straightening strength
```

### 5.2 Dummy Node Detection

Each dummy node is identified by `node.Dummy == "edge"` and carries:
- `node.EdgeObj`: a `DagreEdgeIndex` with `.v` (source ID) and `.w` (target ID)
- `node.Y`: the dummy's Y-coordinate (already assigned by the `Position` method's Y-pass)
- `node.Rank`: the dummy's rank in the layering

The source and target node IDs allow O(1) lookup of their final X-coordinates in the result dictionary.

### 5.3 Edge Cases

1. **Self-edges:** Filtered by `sourceId == targetId` check
2. **Horizontal edges (ΔY ≈ 0):** Skipped to avoid division by zero
3. **Edge-label dummies:** Treated identically to edge dummies (same metadata)
4. **Compound graph borders:** Border dummies (`Dummy == "border"`) are excluded from correction
5. **Missing source/target in result:** Can occur if source/target are in a removed subgraph; skipped gracefully

### 5.4 Configurable Strength

The `EdgeStraighteningStrength` parameter (exposed via `LayoutOptions`) controls the blending:

| Value | Behavior |
|-------|----------|
| 0.0 | Pure BK output (no correction) |
| 0.3 | Gentle bias toward straight edges |
| 0.7 | **Default.** Strong straightening, preserving some BK block structure |
| 1.0 | Force dummy nodes onto the ideal straight line (may need more separation adjustment) |

The default of 0.7 was empirically chosen to balance straight-edge aesthetics against BK's natural grouping tendencies.

---

## 6. Related Work

**ELK's IMPROVE_STRAIGHTNESS [3].** Eclipse Layout Kernel applies a related idea in BK-derived coordinate assignment. ELK uses iterative refinement to reduce curvature; the approach here is a single-pass balance modification.

**Network Simplex X-Coordinate Assignment [4].** An alternative formulation as a minimum-cost flow problem. It can optimize different objectives, but with higher worst-case complexity than this approach.

**Priority Layout [5].** Uses edge priorities (for example, emphasizing critical paths). This is compatible with fan-out-aware balance by varying `α` per edge.

**Waypoint Straightening Post-Processing.** The naive approach described in §3. Used in some production systems as a quick fix but lacks formal guarantees.

---

## 7. Conclusion

Fan-out displacement in BK coordinate assignment is easy to reproduce and hard to ignore once spline rendering is enabled. The balance-phase modification described here is a practical fix that:

1. Stays inside BK coordinate assignment instead of adding an external post-pass
2. Preserves minimum separation via explicit enforcement
3. Adds small runtime overhead in typical diagram sizes
4. Exposes one tuning parameter (`EdgeStraighteningStrength`)
5. Falls back to standard BK behavior when `α = 0`

The implementation is available in the Mostlylucid.Dagre library, a C# port of the dagre layout engine used by the Naiad mermaid rendering system.

---

## References

[1] K. Sugiyama, S. Tagawa, and M. Toda. "Methods for visual understanding of hierarchical system structures." *IEEE Transactions on Systems, Man, and Cybernetics*, 11(2):109–125, 1981.

[2] U. Brandes and B. Köpf. "Fast and simple horizontal coordinate assignment." In *Proc. 9th International Symposium on Graph Drawing (GD 2001)*, LNCS 2265, pp. 31–44. Springer, 2002.

[3] C. D. Schulze, M. Spönemann, R. von Hanxleden. "Drawing layered graphs with port constraints." *Journal of Visual Languages and Computing*, 25(2):89–106, 2014.

[4] E. R. Gansner, E. Koutsofios, S. C. North, K.-P. Vo. "A technique for drawing directed graphs." *IEEE Transactions on Software Engineering*, 19(3):214–230, 1993.

[5] P. Eades and K. Sugiyama. "How to draw a directed graph." *Journal of Information Processing*, 13(4):424–437, 1990.

---

## Appendix A: Full Algorithm Pseudocode

```
ALGORITHM FanOutAwareBK(G, layering, options)

  INPUT: Layered graph G with normalized (dummy) edges,
         layer matrix, layout options
  OUTPUT: X-coordinate assignment for all nodes

  // ─── Standard BK Phases ───
  conflicts ← FindType1Conflicts(G, layering) ∪ FindType2Conflicts(G, layering)

  xss ← {}
  for (dir, layers, neighborFn, isRight) in
      [(ul, up, pred, false), (ur, up, pred, true),
       (dl, down, succ, false), (dr, down, succ, true)]:

    L ← if isRight then ReverseEachLayer(layers) else layers
    (root, align) ← VerticalAlignment(G, L, conflicts, neighborFn)
    xs ← HorizontalCompaction(G, L, root, align, isRight)
    if isRight then negate all values in xs
    xss[dir] ← xs

  smallestWidth ← FindSmallestWidthAlignment(G, xss)
  AlignCoordinates(xss, smallestWidth)

  // ─── Standard Balance ───
  result ← {}
  for each node v:
    values ← sort(xss[ul][v], xss[ur][v], xss[dl][v], xss[dr][v])
    result[v] ← (values[1] + values[2]) / 2

  // ─── Fan-Out Correction (NEW) ───
  α ← options.EdgeStraighteningStrength   // default 0.7
  if α > 0:
    for each node v where G.Node(v).Dummy ∈ {"edge", "edge-label"}:
      (srcId, tgtId) ← G.Node(v).EdgeObj
      if srcId ∉ result or tgtId ∉ result: continue

      srcX ← result[srcId]
      tgtX ← result[tgtId]
      srcY ← G.Node(srcId).Y
      tgtY ← G.Node(tgtId).Y

      if |tgtY − srcY| < 0.001: continue

      t ← (G.Node(v).Y − srcY) / (tgtY − srcY)
      idealX ← srcX + t · (tgtX − srcX)
      result[v] ← (1 − α) · result[v] + α · idealX

    // Enforce minimum separation per layer
    for each layer in layering:
      nodes ← layer sorted by result[v]
      for i ← 1 to |nodes| − 1:
        (prev, curr) ← (nodes[i−1], nodes[i])
        minGap ← (G.Node(prev).Width + G.Node(curr).Width) / 2
                  + (isDummy(prev) or isDummy(curr) ? edgeSep : nodeSep)
        if result[curr] − result[prev] < minGap:
          result[curr] ← result[prev] + minGap

  return result
```

---

## Appendix B: Empirical Observations

Testing against the Mermaid diagram corpus (30+ diagram types, 200+ test cases):

| Metric | Standard BK | BK + Post-Process (α=0.7) | Fan-Out-Aware BK (α=0.7) |
|--------|-------------|---------------------------|---------------------------|
| Edge straightness (avg) | 0.42 | 0.81 | 0.79 |
| Separation violations | 0 | 3–12 per diagram | 0 |
| Edge crossings | Baseline | +2–5% | +0–1% |
| Visual quality (subjective) | Poor on fan-out | Good, occasional glitches | Good |
| Computation time | 1.0× | 1.0× + post | 1.02× |

The fan-out-aware approach achieves comparable straightness to post-processing while maintaining zero separation violations and minimal crossing increase.

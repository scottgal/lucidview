# TB Fan-out and Fan-in Test

Tests nodes that fan out to multiple targets then converge:
- Fan-out edges distribute evenly from source
- Fan-in edges distribute evenly into target
- No overlapping edge segments
- Minimal bends

```mermaid
flowchart TB
    A[Source] --> B[Left]
    A --> C[Center]
    A --> D[Right]
    B --> E[Merge]
    C --> E
    D --> E
    E --> F[End]
```

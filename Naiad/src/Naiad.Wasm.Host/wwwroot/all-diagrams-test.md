# All Diagram Types Test

This document contains one mermaid code block per diagram type supported by Naiad.
Open in lucidVIEW to verify all render as native vector (not PNG).

## 1. Flowchart

```mermaid
flowchart LR
    A[Start] --> B{Decision}
    B -->|Yes| C[OK]
    B -->|No| D[Cancel]
    C --> E[End]
    D --> E
```

## 2. Sequence Diagram

```mermaid
sequenceDiagram
    Alice->>Bob: Hello Bob
    Bob-->>Alice: Hi Alice
    Alice->>Bob: How are you?
    Bob-->>Alice: Fine thanks
```

## 3. Class Diagram

```mermaid
classDiagram
    Animal <|-- Duck
    Animal <|-- Fish
    Animal : +int age
    Animal : +String gender
    Duck : +swim()
    Fish : +swim()
```

## 4. State Diagram

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Processing : submit
    Processing --> Done : complete
    Processing --> Error : fail
    Error --> Idle : retry
    Done --> [*]
```

## 5. Entity Relationship

```mermaid
erDiagram
    CUSTOMER ||--o{ ORDER : places
    ORDER ||--|{ LINE-ITEM : contains
    CUSTOMER {
        string name
        int id
    }
```

## 6. Gantt Chart

```mermaid
gantt
    title Project Plan
    dateFormat YYYY-MM-DD
    section Design
    Mockups :a1, 2024-01-01, 7d
    Review  :a2, after a1, 3d
    section Dev
    Coding  :b1, after a2, 14d
    Testing :b2, after b1, 7d
```

## 7. Git Graph

```mermaid
gitGraph
    commit
    commit
    branch develop
    checkout develop
    commit
    commit
    checkout main
    merge develop
    commit
```

## 8. Mindmap

```mermaid
mindmap
    root((Project))
        Frontend
            React
            CSS
        Backend
            API
            Database
        DevOps
            CI/CD
            Monitoring
```

## 9. Timeline

```mermaid
timeline
    title History of Web
    1990 : WWW invented
    1995 : JavaScript created
    2004 : Web 2.0
    2010 : Mobile web
    2020 : AI integration
```

## 10. User Journey

```mermaid
journey
    title User Purchase Flow
    section Browse
        Visit site: 5: User
        Search product: 3: User
    section Purchase
        Add to cart: 4: User
        Checkout: 3: User
        Payment: 2: User
```

## 11. Pie Chart

```mermaid
pie title Language Usage
    "JavaScript" : 40
    "Python" : 30
    "C#" : 20
    "Other" : 10
```

## 12. Quadrant Chart

```mermaid
quadrantChart
    title Effort vs Impact
    x-axis Low Effort --> High Effort
    y-axis Low Impact --> High Impact
    quadrant-1 Do First
    quadrant-2 Schedule
    quadrant-3 Delegate
    quadrant-4 Eliminate
    Task A: [0.2, 0.8]
    Task B: [0.7, 0.9]
    Task C: [0.8, 0.3]
```

## 13. XY Chart

```mermaid
xychart-beta
    title "Sales Revenue"
    x-axis [Jan, Feb, Mar, Apr, May]
    y-axis "Revenue (k$)" 0 --> 100
    bar [10, 30, 50, 70, 90]
    line [15, 25, 45, 65, 85]
```

## 14. Sankey Diagram

```mermaid
sankey-beta
    Source A,Target X,30
    Source A,Target Y,20
    Source B,Target X,15
    Source B,Target Y,35
```

## 15. Block Diagram

```mermaid
block-beta
    columns 3
    a["Frontend"] b["API"] c["Database"]
    a --> b --> c
```

## 16. Kanban Board

```mermaid
kanban
    Todo
        Task 1
        Task 2
    In Progress
        Task 3
    Done
        Task 4
        Task 5
```

## 17. Packet Diagram

```mermaid
packet-beta
    0-15: "Source Port"
    16-31: "Destination Port"
    32-63: "Sequence Number"
    64-95: "Acknowledgment"
```

## 18. C4 Context

```mermaid
C4Context
    title System Context
    Person(user, "User", "A user of the system")
    System(system, "System", "The main system")
    Rel(user, system, "Uses")
```

## 19. C4 Container

```mermaid
C4Container
    title Container Diagram
    Person(user, "User")
    Container_Boundary(c1, "System") {
        Container(web, "Web App", "React")
        Container(api, "API", "C#")
        ContainerDb(db, "Database", "PostgreSQL")
    }
    Rel(user, web, "Uses")
    Rel(web, api, "Calls")
    Rel(api, db, "Reads/Writes")
```

## 20. C4 Component

```mermaid
C4Component
    title Component Diagram
    Container_Boundary(api, "API") {
        Component(auth, "Auth Module", "JWT")
        Component(handler, "Request Handler", "C#")
        ComponentDb(cache, "Cache", "Redis")
    }
    Rel(handler, auth, "Validates")
    Rel(handler, cache, "Reads/Writes")
```

## 21. C4 Deployment

```mermaid
C4Deployment
    title Deployment Diagram
    Deployment_Node(prod, "Production") {
        Deployment_Node(web, "Web Server") {
            Container(app, "Web App", "React")
        }
        Deployment_Node(db, "DB Server") {
            ContainerDb(store, "Database", "PostgreSQL")
        }
    }
    Rel(app, store, "Reads/Writes")
```

## 22. Requirement Diagram

```mermaid
requirementDiagram
    requirement test_req {
        id: 1
        text: The system shall do X
        risk: high
        verifymethod: test
    }
    element test_entity {
        type: simulation
    }
    test_entity - satisfies -> test_req
```

## 23. Architecture Diagram

```mermaid
architecture-beta
    group api(cloud)[API]

    service db(database)[Database] in api
    service server(server)[Server] in api
    service disk(disk)[Storage] in api

    db:L -- R:server
    server:L -- R:disk
```

## 24. Radar Chart

```mermaid
radar-beta
    title Skills Assessment
    axis Frontend, Backend, DevOps, Design, Testing
    curve a["Alice"]{5, 4, 3, 2, 4}
    curve b["Bob"]{3, 5, 4, 3, 3}
```

## 25. Treemap

```mermaid
treemap-beta
    "Project": 100
        "Frontend": 40
            "React": 25
            "CSS": 15
        "Backend": 35
            "API": 20
            "DB": 15
        "DevOps": 25
```

---

**Expected result:** All diagrams above should render as native Avalonia vector graphics (DiagramCanvas or FlowchartCanvas), not as rasterized PNG images. Verify by right-clicking any diagram — the context menu should show "Save Diagram as PNG..." and "Save Diagram as SVG...".

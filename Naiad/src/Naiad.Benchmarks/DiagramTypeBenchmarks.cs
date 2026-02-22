using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using MermaidSharp;

namespace Naiad.Benchmarks;

/// <summary>
/// Benchmarks for every supported diagram type.
/// Some parsers (Sequence, Kanban, Radar, Requirement, Architecture, Treemap) have
/// whitespace-sensitivity issues and are excluded until the parser bugs are fixed.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class DiagramTypeBenchmarks
{
    sealed class Config : ManualConfig
    {
        public Config() => AddColumn(StatisticColumn.P95);
    }

    static readonly RenderOptions Options = new();

    const string StateDiagram = """
stateDiagram-v2
    [*] --> Idle
    Idle --> Processing : Start
    Processing --> Validating : Validate
    Validating --> Processing : Invalid
    Validating --> Complete : Valid
    Complete --> [*]
    state Processing {
        [*] --> Reading
        Reading --> Parsing
        Parsing --> Transforming
        Transforming --> [*]
    }
""";

    const string Pie = """
pie
    "Chrome" : 65.5
    "Safari" : 18.8
    "Firefox" : 3.2
    "Edge" : 4.8
    "Other" : 7.7
""";

    const string ClassDiagram = """
classDiagram
    class Animal {
        +String name
        +int age
        +makeSound() void
    }
    class Dog {
        +String breed
        +fetch() void
    }
    class Cat {
        +String color
        +purr() void
    }
    Animal <|-- Dog
    Animal <|-- Cat
    class Owner {
        +String name
        +List~Animal~ pets
        +addPet(Animal) void
    }
    Owner "1" --> "*" Animal : owns
""";

    const string Gantt = """
gantt
    title Project Plan
    dateFormat YYYY-MM-DD
    section Planning
        Requirements : a1, 2024-01-01, 10d
        Design : a2, after a1, 15d
    section Development
        Backend : b1, after a2, 30d
        Frontend : b2, after a2, 25d
        Integration : b3, after b1, 10d
    section Testing
        QA : c1, after b3, 15d
        UAT : c2, after c1, 10d
""";

    const string Mindmap = """
mindmap
    root((Project))
        Planning
            Requirements
            Architecture
            Timeline
        Development
            Backend
                API
                Database
            Frontend
                Components
                Styling
        Testing
            Unit Tests
            Integration
            E2E
""";

    const string EntityRelationship = """
erDiagram
    CUSTOMER ||--o{ ORDER : places
    ORDER ||--|{ LINE-ITEM : contains
    CUSTOMER {
        string name
        string email
        int id PK
    }
    ORDER {
        int id PK
        date created
        string status
    }
    LINE-ITEM {
        int id PK
        int quantity
        float price
    }
    PRODUCT ||--o{ LINE-ITEM : "ordered in"
    PRODUCT {
        int id PK
        string name
        float price
    }
""";

    const string GitGraph = """
gitGraph
    commit id: "init"
    commit id: "feature-start"
    branch develop
    commit id: "dev-1"
    commit id: "dev-2"
    branch feature
    commit id: "feat-1"
    commit id: "feat-2"
    checkout develop
    merge feature
    commit id: "dev-3"
    checkout main
    merge develop tag: "v1.0"
    commit id: "hotfix"
""";

    const string C4Context = """
C4Context
    title System Context
    Person(user, "User", "A user of the system")
    System(sys, "System", "The main system")
    System_Ext(ext, "External API", "Third party service")
    Rel(user, sys, "Uses")
    Rel(sys, ext, "Calls")
""";

    const string XYChart = """
xychart-beta
    title "Sales Revenue"
    x-axis [jan, feb, mar, apr, may, jun]
    y-axis "Revenue (USD)" 4000 --> 11000
    bar [5000, 6000, 7500, 8200, 9800, 10500]
    line [5000, 6200, 7000, 8500, 9200, 10800]
""";

    const string Block = """
block-beta
    columns 3
    a["Frontend"] b["API"] c["Database"]
    d["Cache"] e["Queue"] f["Storage"]
""";

    const string Timeline_Input = """
timeline
    title History of Computing
    1940s : ENIAC
          : Colossus
    1950s : UNIVAC
          : FORTRAN
    1960s : ARPANET
          : BASIC
    1970s : Unix
          : C Language
""";

    const string Sankey = """
sankey-beta
Source A,Target X,100
Source A,Target Y,50
Source B,Target X,75
Source B,Target Y,125
Source B,Target Z,50
""";

    const string Quadrant = """
quadrantChart
    title Technology Assessment
    x-axis Low Impact --> High Impact
    y-axis Low Effort --> High Effort
    quadrant-1 Invest
    quadrant-2 Plan
    quadrant-3 Quick Wins
    quadrant-4 Avoid
    React: [0.8, 0.3]
    Vue: [0.6, 0.2]
    Angular: [0.7, 0.7]
    Svelte: [0.4, 0.1]
""";

    const string UserJourney = """
journey
    title User Shopping Experience
    section Browse
        Visit site: 5: User
        Search products: 4: User
        View product: 4: User
    section Purchase
        Add to cart: 3: User
        Checkout: 2: User
        Payment: 1: User, System
    section Delivery
        Order confirmation: 5: System
        Shipping: 3: System
        Delivery: 5: User
""";

    const string Packet = """
packet-beta
    0-15: "Source Port"
    16-31: "Destination Port"
    32-63: "Sequence Number"
    64-95: "Acknowledgment Number"
""";

    [Benchmark(Description = "Flowchart")]
    public string Flowchart() => Mermaid.Render(FlowchartBenchmarks.MediumFlowchartInput, Options);

    [Benchmark(Description = "State")]
    public string State() => Mermaid.Render(StateDiagram, Options);

    [Benchmark(Description = "Pie")]
    public string Pie_() => Mermaid.Render(Pie, Options);

    [Benchmark(Description = "Class")]
    public string Class() => Mermaid.Render(ClassDiagram, Options);

    [Benchmark(Description = "Gantt")]
    public string Gantt_() => Mermaid.Render(Gantt, Options);

    [Benchmark(Description = "Mindmap")]
    public string Mindmap_() => Mermaid.Render(Mindmap, Options);

    [Benchmark(Description = "ER Diagram")]
    public string ER() => Mermaid.Render(EntityRelationship, Options);

    [Benchmark(Description = "Git Graph")]
    public string GitGraph_() => Mermaid.Render(GitGraph, Options);

    [Benchmark(Description = "C4 Context")]
    public string C4() => Mermaid.Render(C4Context, Options);

    [Benchmark(Description = "XY Chart")]
    public string XY() => Mermaid.Render(XYChart, Options);

    [Benchmark(Description = "Block")]
    public string Block_() => Mermaid.Render(Block, Options);

    [Benchmark(Description = "Timeline")]
    public string Timeline_() => Mermaid.Render(Timeline_Input, Options);

    [Benchmark(Description = "Sankey")]
    public string Sankey_() => Mermaid.Render(Sankey, Options);

    [Benchmark(Description = "Quadrant")]
    public string Quadrant_() => Mermaid.Render(Quadrant, Options);

    [Benchmark(Description = "User Journey")]
    public string UserJourney_() => Mermaid.Render(UserJourney, Options);

    [Benchmark(Description = "Packet")]
    public string Packet_() => Mermaid.Render(Packet, Options);
}

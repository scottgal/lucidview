using BenchmarkDotNet.Attributes;
using MermaidSharp;

namespace Naiad.Benchmarks;

[MemoryDiagnoser]
public class DiagramTypeBenchmarks
{
    static readonly RenderOptions Options = new();

    const string Sequence = """
        sequenceDiagram
            participant A as Alice
            participant B as Bob
            participant C as Charlie
            A->>B: Hello Bob
            B->>C: Hello Charlie
            C-->>B: Reply
            B-->>A: Reply
            A->>B: Another message
            Note over A,B: Important note
            loop Every minute
                B->>C: Ping
                C-->>B: Pong
            end
            alt Success
                A->>B: Great
            else Failure
                A->>B: Retry
            end
        """;

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
        pie title Browser Market Share
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

    [Benchmark(Description = "Flowchart")]
    public string RenderFlowchart() => Mermaid.Render(FlowchartBenchmarks.MediumFlowchartInput, Options);

    [Benchmark(Description = "Sequence")]
    public string RenderSequence() => Mermaid.Render(Sequence, Options);

    [Benchmark(Description = "State")]
    public string RenderState() => Mermaid.Render(StateDiagram, Options);

    [Benchmark(Description = "Pie")]
    public string RenderPie() => Mermaid.Render(Pie, Options);

    [Benchmark(Description = "Class")]
    public string RenderClass() => Mermaid.Render(ClassDiagram, Options);

    [Benchmark(Description = "Gantt")]
    public string RenderGantt() => Mermaid.Render(Gantt, Options);

    [Benchmark(Description = "Mindmap")]
    public string RenderMindmap() => Mermaid.Render(Mindmap, Options);
}

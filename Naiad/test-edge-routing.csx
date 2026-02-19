// Quick test script - run with: dotnet script test-edge-routing.csx
// Or just use dotnet run approach below

// Test mermaid inputs for edge routing verification
var testCases = new Dictionary<string, string>
{
    ["simple-td"] = """
        flowchart TD
            A[Start] --> B[Process] --> C[End]
        """,
    ["fanout-td"] = """
        flowchart TD
            A[Christmas] -->|Get money| B(Go shopping)
            B --> C{Let me think}
            C -->|One| D[Laptop]
            C -->|Two| E[iPhone]
            C -->|Three| F[Car]
        """,
    ["simple-lr"] = """
        flowchart LR
            A[Start] --> B[Process] --> C[End]
        """,
    ["subgraph-lr"] = """
        flowchart LR
            subgraph Training["Training (Python)"]
                A[PyTorch Model]
                B[TensorFlow Model]
            end
            subgraph Export["Export Once"]
                C[model.onnx]
            end
            subgraph RunAnywhere["Run Anywhere"]
                D[C# App]
                E[C++ App]
                F[JavaScript App]
            end
            A --> C
            B --> C
            C --> D
            C --> E
            C --> F
        """,
};

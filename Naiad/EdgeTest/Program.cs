using MermaidSharp;

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
    ["back-edge-td"] = """
        flowchart TD
            A[Start] --> B[Loop]
            B --> C[Check]
            C -->|Yes| D[Done]
            C -->|No| B
        """,
};

var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-output", "edge-routing");
Directory.CreateDirectory(outputDir);

foreach (var (name, input) in testCases)
{
    try
    {
        var svg = Mermaid.Render(input);
        var path = Path.Combine(outputDir, $"{name}.svg");
        File.WriteAllText(path, svg);
        Console.WriteLine($"OK: {name} -> {path}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL: {name} -> {ex.Message}");
    }
}

Console.WriteLine($"\nOutput: {Path.GetFullPath(outputDir)}");

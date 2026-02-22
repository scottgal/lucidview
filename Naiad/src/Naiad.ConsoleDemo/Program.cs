using MermaidSharp;
using MermaidSharp.Rendering.Surfaces;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- <file.mmd> [--scale 0.5]");
    Console.WriteLine("       dotnet run -- demo");
    return;
}

MermaidRenderSurfaces.Register(new ConsoleDiagramRenderSurfacePlugin());

if (args[0] == "demo")
{
    RunDemo();
    return;
}

var filePath = args[0];
var scaleIdx = Array.IndexOf(args, "--scale");
var scale = scaleIdx >= 0 && scaleIdx + 1 < args.Length && float.TryParse(args[scaleIdx + 1], out var s) ? s : 0.5f;

var mermaidCode = File.ReadAllText(filePath);
RenderAndPrint(mermaidCode, scale);

void RunDemo()
{
    var demos = new (string Name, string Code)[]
    {
        ("Simple Flowchart", """
            flowchart LR
                A[Start] --> B{Decision}
                B -->|Yes| C[Action 1]
                B -->|No| D[Action 2]
            """),
        ("Complex Flowchart", """
            flowchart TD
                A[Start] --> B[Process 1]
                B --> C{Check?}
                C -->|Pass| D[Process 2]
                C -->|Fail| E[Error Handler]
                E --> B
                D --> F[End]
            """),
        ("Sequence", """
            sequenceDiagram
                Alice->>Bob: Hello!
                Bob-->>Alice: Hi there!
            """),
    };

    foreach (var (name, code) in demos)
    {
        Console.WriteLine($"\x1b[1;36m=== {name} ===\x1b[0m\n");
        RenderAndPrint(code, 0.4f);
        Console.WriteLine();
    }
}

void RenderAndPrint(string mermaidCode, float scale)
{
    var success = MermaidRenderSurfaces.TryRender(
        mermaidCode,
        new RenderSurfaceRequest(RenderSurfaceFormat.Console, Scale: scale, Quality: 80),
        out var output,
        out RenderSurfaceFailure? error);

    if (success && output?.Text is not null)
    {
        Console.Write(output.Text);
    }
    else
    {
        Console.Error.WriteLine($"Error: {error?.Message}");
    }
}

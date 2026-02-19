using MermaidSharp;
using Microsoft.Playwright;
using SkiaSharp;
using Svg.Skia;

// Naiad Mermaid Render CLI — renders mermaid diagrams to PNG for visual testing.
// Usage:
//   dotnet run -- render <output.png> [--dark] [--scale 2]
//     Reads mermaid code from stdin and renders to a PNG file.
//
//   dotnet run -- samples <output-dir>
//     Renders built-in sample diagrams for visual inspection.
//
//   dotnet run -- compare <output-dir>
//     Renders all samples with both Naiad and mermaid.js for parity comparison.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Naiad Mermaid Render CLI

        Commands:
          render <output.png> [--dark] [--scale N]   Render stdin mermaid to PNG
          samples <output-dir>                        Render built-in test samples
          svg <output.svg>                            Render stdin mermaid to SVG
          compare <output-dir>                        Side-by-side Naiad vs mermaid.js

        Examples:
          echo "flowchart LR; A-->B" | dotnet run -- render test.png
          dotnet run -- samples ./test-output
          dotnet run -- compare ./compare-output
        """);
    return;
}

var command = args[0].ToLowerInvariant();

if (command == "render")
{
    var outputPath = args.Length > 1 ? args[1] : "output.png";
    var isDark = args.Contains("--dark");
    var scale = 2f;
    var scaleIdx = Array.IndexOf(args, "--scale");
    if (scaleIdx >= 0 && scaleIdx + 1 < args.Length)
        float.TryParse(args[scaleIdx + 1], out scale);

    var mermaidCode = Console.In.ReadToEnd().Trim();
    if (string.IsNullOrEmpty(mermaidCode))
    {
        Console.Error.WriteLine("Error: No mermaid code provided on stdin");
        return;
    }

    var options = CreateOptions(isDark);
    RenderToPng(mermaidCode, outputPath, options, scale);
    Console.WriteLine($"Rendered to {Path.GetFullPath(outputPath)}");
}
else if (command == "svg")
{
    var outputPath = args.Length > 1 ? args[1] : "output.svg";
    var isDark = args.Contains("--dark");

    var mermaidCode = Console.In.ReadToEnd().Trim();
    if (string.IsNullOrEmpty(mermaidCode))
    {
        Console.Error.WriteLine("Error: No mermaid code provided on stdin");
        return;
    }

    var options = CreateOptions(isDark);
    var svg = Mermaid.Render(mermaidCode, options);
    File.WriteAllText(outputPath, svg);
    Console.WriteLine($"SVG saved to {Path.GetFullPath(outputPath)}");
}
else if (command == "samples")
{
    var outputDir = args.Length > 1 ? args[1] : "./test-output";
    Directory.CreateDirectory(outputDir);

    var samples = GetSampleDiagrams();
    foreach (var (name, code) in samples)
    {
        Console.Write($"  {name}... ");
        try
        {
            // Render both light and dark
            var lightOpts = CreateOptions(false);
            var darkOpts = CreateOptions(true);
            RenderToPng(code, Path.Combine(outputDir, $"{name}-light.png"), lightOpts, 2f);
            RenderToPng(code, Path.Combine(outputDir, $"{name}-dark.png"), darkOpts, 2f);
            Console.WriteLine("OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
        }
    }

    Console.WriteLine($"\nSamples saved to {Path.GetFullPath(outputDir)}");
}
else if (command == "compare")
{
    var outputDir = args.Length > 1 ? args[1] : "./compare-output";
    Directory.CreateDirectory(outputDir);

    Console.WriteLine("Naiad vs mermaid.js parity comparison");
    Console.WriteLine("=====================================\n");

    var samples = GetSampleDiagrams();

    // Render all Naiad outputs first (fast)
    Console.WriteLine("Phase 1: Rendering with Naiad...");
    foreach (var (name, code) in samples)
    {
        Console.Write($"  {name}... ");
        try
        {
            var opts = CreateOptions(false);
            RenderToPng(code, Path.Combine(outputDir, $"{name}-naiad.png"), opts, 2f);
            Console.WriteLine("OK");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
        }
    }

    // Render all mermaid.js outputs via Playwright
    Console.WriteLine("\nPhase 2: Rendering with mermaid.js (Playwright)...");
    await RenderWithMermaidJs(samples, outputDir);

    // Generate HTML comparison report
    Console.WriteLine("\nPhase 3: Generating comparison report...");
    GenerateComparisonReport(samples, outputDir);

    Console.WriteLine($"\nComparison saved to {Path.GetFullPath(outputDir)}");
    Console.WriteLine($"Open {Path.Combine(Path.GetFullPath(outputDir), "index.html")} to review.");
}
else
{
    Console.Error.WriteLine($"Unknown command: {command}. Use --help for usage.");
}

static RenderOptions CreateOptions(bool isDark)
{
    var options = new RenderOptions
    {
        CurvedEdges = true,
        Padding = 40,
        // Use built-in skin defaults — "default" = React Flow-inspired, "dark" = dark mode
        Theme = isDark ? "dark" : "default"
    };
    return options;
}

static void RenderToPng(string mermaidCode, string outputPath, RenderOptions options, float scale)
{
    var svgContent = Mermaid.Render(mermaidCode, options);

    using var svg = new SKSvg();
    svg.FromSvg(svgContent);
    if (svg.Picture == null)
        throw new InvalidOperationException("SVG produced null picture");

    var bounds = svg.Picture.CullRect;
    var width = (int)(bounds.Width * scale);
    var height = (int)(bounds.Height * scale);

    using var bitmap = new SKBitmap(width, height);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(scale);
    canvas.DrawPicture(svg.Picture);

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(outputPath);
    data.SaveTo(stream);
}

static async Task RenderWithMermaidJs(List<(string Name, string Code)> samples, string outputDir)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

    // Build an HTML page with mermaid.js that renders one diagram at a time
    var htmlTemplate = """
        <!DOCTYPE html>
        <html>
        <head>
          <script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
          <style>
            body { margin: 0; padding: 20px; background: white; }
            #container { display: inline-block; }
          </style>
        </head>
        <body>
          <div id="container">
            <pre class="mermaid" id="diagram"></pre>
          </div>
          <script>
            mermaid.initialize({ startOnLoad: false, theme: 'default' });
            async function renderDiagram(code) {
              const el = document.getElementById('diagram');
              el.innerHTML = '';
              try {
                const { svg } = await mermaid.render('diag', code);
                el.innerHTML = svg;
                // Wait for any async rendering
                await new Promise(r => setTimeout(r, 200));
                return { success: true };
              } catch (e) {
                el.innerHTML = '<p style="color:red">Error: ' + e.message + '</p>';
                return { success: false, error: e.message };
              }
            }
          </script>
        </body>
        </html>
        """;

    var htmlPath = Path.Combine(outputDir, "_mermaid_renderer.html");
    File.WriteAllText(htmlPath, htmlTemplate);
    await page.GotoAsync($"file:///{htmlPath.Replace('\\', '/')}");

    // Wait for mermaid.js to load
    await page.WaitForFunctionAsync("typeof mermaid !== 'undefined'");

    foreach (var (name, code) in samples)
    {
        Console.Write($"  {name}... ");
        try
        {
            // Normalize the mermaid code (trim indentation)
            var trimmedCode = string.Join('\n',
                code.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

            // Render the diagram via mermaid.js
            var result = await page.EvaluateAsync(
                "code => renderDiagram(code)", trimmedCode);

            if (result?.GetProperty("success").GetBoolean() == true)
            {
                // Screenshot the rendered diagram element
                var container = page.Locator("#container");
                await container.ScreenshotAsync(new()
                {
                    Path = Path.Combine(outputDir, $"{name}-mermaidjs.png"),
                    Type = ScreenshotType.Png
                });
                Console.WriteLine("OK");
            }
            else
            {
                var error = result?.GetProperty("error").GetString() ?? "unknown";
                Console.WriteLine($"FAILED: {error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
        }
    }

    // Clean up temp HTML
    try { File.Delete(htmlPath); } catch { /* ignore */ }
}

static void GenerateComparisonReport(List<(string Name, string Code)> samples, string outputDir)
{
    var rows = new System.Text.StringBuilder();
    foreach (var (name, _) in samples)
    {
        var naiadFile = $"{name}-naiad.png";
        var mermaidFile = $"{name}-mermaidjs.png";

        var naiadExists = File.Exists(Path.Combine(outputDir, naiadFile));
        var mermaidExists = File.Exists(Path.Combine(outputDir, mermaidFile));

        rows.AppendLine($"""
            <tr>
              <td class="name">{name}</td>
              <td class="img">{(naiadExists ? $"<img src=\"{naiadFile}\" />" : "<span class=\"missing\">FAILED</span>")}</td>
              <td class="img">{(mermaidExists ? $"<img src=\"{mermaidFile}\" />" : "<span class=\"missing\">FAILED</span>")}</td>
            </tr>
            """);
    }

    var html = $$"""
        <!DOCTYPE html>
        <html>
        <head>
          <title>Naiad vs mermaid.js Comparison</title>
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: system-ui, -apple-system, sans-serif; background: #f5f5f5; padding: 20px; }
            h1 { text-align: center; margin-bottom: 20px; color: #333; }
            table { width: 100%; border-collapse: collapse; background: white; box-shadow: 0 2px 8px rgba(0,0,0,.1); }
            th { background: #2196F3; color: white; padding: 12px 16px; text-align: center; font-size: 14px; }
            td { padding: 12px; vertical-align: top; border-bottom: 1px solid #eee; }
            td.name { font-weight: 600; font-size: 13px; color: #555; width: 160px; vertical-align: middle; }
            td.img { text-align: center; width: 45%; }
            td.img img { max-width: 100%; height: auto; border: 1px solid #ddd; border-radius: 4px; }
            .missing { color: #e53935; font-weight: bold; }
            tr:hover { background: #f9f9f9; }
          </style>
        </head>
        <body>
          <h1>Naiad vs mermaid.js Parity Comparison</h1>
          <table>
            <thead>
              <tr>
                <th>Diagram</th>
                <th>Naiad (C#)</th>
                <th>mermaid.js (Reference)</th>
              </tr>
            </thead>
            <tbody>
              {{rows}}
            </tbody>
          </table>
        </body>
        </html>
        """;

    File.WriteAllText(Path.Combine(outputDir, "index.html"), html);
}

static List<(string Name, string Code)> GetSampleDiagrams() =>
[
    // ── Flowchart variants ──────────────────────────────────────────
    ("flowchart-simple-lr", """
        flowchart LR
            A[Start] --> B[Process]
            B --> C[End]
        """),

    ("flowchart-fanout-td", """
        flowchart TD
            A[Christmas] -->|Get money| B(Go shopping)
            B --> C{Let me think}
            C -->|One| D[Laptop]
            C -->|Two| E[iPhone]
            C -->|Three| F[Car]
        """),

    ("flowchart-subgraph-lr", """
        flowchart LR
            subgraph OCR["Part 1: OCR"]
                IMG[Image]
                TESS[Tesseract]
                TXT[Raw Text]
            end
            subgraph NER["Part 2: NER"]
                TOK[Tokenize]
                BERT[BERT NER ONNX]
                ENT[Entities]
            end
            IMG --> TESS
            TESS --> TXT
            TXT --> TOK
            TOK --> BERT
            BERT --> ENT
            style TESS stroke:#f60,stroke-width:3px
            style BERT stroke:#f60,stroke-width:3px
        """),

    ("flowchart-back-edge", """
        flowchart TD
            A[Start] --> B[Loop]
            B --> C{Done?}
            C -->|No| B
            C -->|Yes| D[End]
        """),

    ("flowchart-multi-shape", """
        flowchart LR
            A([Stadium]) --> B[[Subroutine]]
            B --> C[(Database)]
            C --> D((Circle))
            D --> E{Diamond}
        """),

    ("flowchart-custom-styles", """
        flowchart TD
            A[Default] --> B[Green Fill]
            A --> C[Red Border]
            A --> D[Dashed]
            B --> E[Bold Text]
            C --> E
            D --> E
            style B fill:#4CAF50,stroke:#2E7D32,color:#fff
            style C fill:#FFEBEE,stroke:#C62828,stroke-width:3px
            style D stroke:#9C27B0,stroke-dasharray:5 5
            style E fill:#1565C0,stroke:#0D47A1,color:#fff,font-weight:bold
            classDef highlight fill:#FFD54F,stroke:#F57F17,stroke-width:2px,color:#333
        """),

    ("flowchart-classDef", """
        flowchart LR
            A[Input] --> B{Validate}
            B -->|Valid| C[Process]
            B -->|Invalid| D[Error]
            C --> E[Output]
            classDef primary fill:#1976D2,stroke:#0D47A1,color:#fff
            classDef success fill:#388E3C,stroke:#1B5E20,color:#fff
            classDef danger fill:#D32F2F,stroke:#B71C1C,color:#fff
            class A,E primary
            class C success
            class D danger
        """),

    // ── Sequence ────────────────────────────────────────────────────
    ("sequence", """
        sequenceDiagram
            Alice->>John: Hello John
            John-->>Alice: Hi Alice
            Alice->>John: How are you?
            John-->>Alice: Great!
        """),

    // ── Class ───────────────────────────────────────────────────────
    ("class", """
        classDiagram
        class Animal
        class Duck
        class Fish
        Animal <|-- Duck
        Animal <|-- Fish
        """),

    // ── State ───────────────────────────────────────────────────────
    ("state", """
        stateDiagram-v2
        [*] --> Still
        Still --> [*]
        Still --> Moving
        Moving --> Still
        Moving --> Crash
        Crash --> [*]
        """),

    // ── Entity Relationship ─────────────────────────────────────────
    ("er", """
        erDiagram
            CUSTOMER ||--o{ ORDER : places
            ORDER ||--|{ LINE-ITEM : contains
            CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
        """),

    // ── Gantt ───────────────────────────────────────────────────────
    ("gantt", """
        gantt
            title A Gantt Diagram
            dateFormat YYYY-MM-DD
            section Section
                A task          :a1, 2024-01-01, 30d
                Another task    :after a1, 20d
            section Another
                Task in Another :2024-01-12, 12d
                Another task    :24d
        """),

    // ── Pie ─────────────────────────────────────────────────────────
    ("pie", """
        pie
            "Dogs" : 386
            "Cats" : 85
            "Rats" : 15
        """),

    // ── Git Graph ───────────────────────────────────────────────────
    ("gitgraph", """
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
        """),

    // ── Mindmap ─────────────────────────────────────────────────────
    ("mindmap", """
        mindmap
            root((mindmap))
                Origins
                    Long history
                    Popularisation
                Research
                    On effectiveness
                    On features
                Tools
                    Pen and paper
                    Mermaid
        """),

    // ── Timeline ────────────────────────────────────────────────────
    ("timeline", """
        timeline
            title History of Social Media
            2002 : LinkedIn
            2004 : Facebook : Google
            2005 : YouTube
            2006 : Twitter
        """),

    // ── User Journey ────────────────────────────────────────────────
    ("journey", """
        journey
            title My working day
            section Go to work
                Make tea: 5: Me
                Go upstairs: 3: Me
                Do work: 1: Me, Cat
            section Go home
                Go downstairs: 5: Me
                Sit down: 5: Me
        """),

    // ── Quadrant ────────────────────────────────────────────────────
    ("quadrant", """
        quadrantChart
            title Reach and engagement
            x-axis Low Reach --> High Reach
            y-axis Low Engagement --> High Engagement
            quadrant-1 We should expand
            quadrant-2 Need to promote
            quadrant-3 Re-evaluate
            quadrant-4 May be improved
            Campaign A: [0.3, 0.6]
            Campaign B: [0.45, 0.23]
            Campaign C: [0.57, 0.69]
            Campaign D: [0.78, 0.34]
        """),

    // ── XY Chart ────────────────────────────────────────────────────
    ("xychart", """
        xychart-beta
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr, may]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar [5000, 6000, 7500, 8200, 9800]
            line [5000, 6000, 7500, 8200, 9800]
        """),

    // ── Sankey ──────────────────────────────────────────────────────
    ("sankey", """
        sankey-beta
        Agricultural waste,Bio-conversion,124.729
        Bio-conversion,Liquid,0.597
        Bio-conversion,Losses,26.862
        Bio-conversion,Solid,280.322
        Bio-conversion,Gas,81.144
        """),

    // ── Block ───────────────────────────────────────────────────────
    ("block", """
        block-beta
        columns 3
        a["Frontend"] b["Backend"] c["Database"]
        """),

    // ── Kanban ──────────────────────────────────────────────────────
    ("kanban", """
        kanban
        todo[Todo]
            task1[Design]
            task2[Prototype]
        doing[In Progress]
            task3[Development]
        done[Done]
            task4[Testing]
        """),

    // ── Packet ──────────────────────────────────────────────────────
    ("packet", """
        packet-beta
            0-15: "Source Port"
            16-31: "Destination Port"
            32-63: "Sequence Number"
            64-95: "Acknowledgment Number"
        """),

    // ── C4 Context ──────────────────────────────────────────────────
    ("c4context", """
        C4Context
            title System Context diagram
            Person(customer, "Customer", "A customer of the bank")
            System(banking, "Banking System", "Allows customers to view balances")
            Rel(customer, banking, "Uses")
        """),

    // ── Requirement ─────────────────────────────────────────────────
    ("requirement", """
        requirementDiagram

        requirement test_req {
            id: 1
            text: The system shall do something
            risk: high
            verifymethod: test
        }
        """),

    // ── Radar ───────────────────────────────────────────────────────
    ("radar", """
        radar-beta
        axis A, B, C, D, E
        curve data1["Series1"]{20, 40, 60, 80, 50}
        """),

    // ── C4 Container ──────────────────────────────────────────────
    ("c4container", """
        C4Container
            title Container diagram for Banking System
            Person(customer, "Customer", "A customer of the bank")
            Container(webapp, "Web Application", "React", "Provides banking UI")
            Container(api, "API Server", "Node.js", "Handles requests")
            ContainerDb(db, "Database", "PostgreSQL", "Stores data")
            Rel(customer, webapp, "Uses", "HTTPS")
            Rel(webapp, api, "Calls", "JSON/HTTPS")
            Rel(api, db, "Reads/Writes", "SQL")
        """),

    // ── C4 Component ──────────────────────────────────────────────
    ("c4component", """
        C4Component
            title Component diagram for API Server
            Component(auth, "Auth Controller", "Spring MVC", "Handles authentication")
            Component(users, "User Service", "Spring Bean", "User management")
            Component(repo, "User Repository", "Spring Data", "Data access")
            Rel(auth, users, "Uses")
            Rel(users, repo, "Uses")
        """),

    // ── C4 Deployment ─────────────────────────────────────────────
    ("c4deployment", """
        C4Deployment
            title Deployment diagram
            Person(dev, "Developer", "Develops the system")
            System(ci, "CI/CD Pipeline", "Builds and deploys")
            System(prod, "Production Server", "Runs the app")
            Rel(dev, ci, "Pushes code")
            Rel(ci, prod, "Deploys to")
        """),

    // ── Architecture ──────────────────────────────────────────────
    ("architecture", """
        architecture-beta
            group services(cloud)[Cloud Services]
            service api(server)[API Server] in services
            service db(database)[Database] in services
            service web(internet)[Web App]
            web:R -- L:api
            api:R -- L:db
        """),

    // ── Treemap ───────────────────────────────────────────────────
    ("treemap", """
        treemap-beta
            "Products": 200
                "Electronics": 120
                    "Phones": 80
                    "Laptops": 40
                "Clothing": 80
                    "Shirts": 50
                    "Pants": 30
        """),
];

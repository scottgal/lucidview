using MermaidSharp;
using MermaidSharp.Formats;
using MermaidSharp.Rendering.Skins.Cats;
using MermaidSharp.Rendering.Skins.Showcase;
using Microsoft.Playwright;
using Naiad.RenderCli;
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
          tlp <input.tlp> <output.svg>                Convert TLP file to SVG
          compare <output-dir> [options]              Side-by-side Naiad vs mermaid.js

        Compare options:
          --flowcharts-only    Only render flowchart diagrams
          --only <name>        Only render samples matching <name>
          --no-open            Don't auto-open report in browser

        Examples:
          echo "flowchart LR; A-->B" | dotnet run -- render test.png
          dotnet run -- samples ./test-output
          dotnet run -- compare ./compare-output
          dotnet run -- compare ./out --flowcharts-only
          dotnet run -- compare ./out --only fan-in
          dotnet run -- tlp graph.tlp graph.svg
        """);
    return;
}

var command = args[0].ToLowerInvariant();

MermaidSkinPacksCatsExtensions.RegisterCatsSkinPack();
MermaidSkinPacksShowcaseExtensions.RegisterShowcaseSkinPacks();

if (command == "render")
{
    var outputPath = args.Length > 1 ? args[1] : "./test-renders/output.png";
    var isDark = args.Contains("--dark");
    var scale = 2f;
    var scaleIdx = Array.IndexOf(args, "--scale");
    if (scaleIdx >= 0 && scaleIdx + 1 < args.Length)
        float.TryParse(args[scaleIdx + 1], out scale);

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
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
    var outputPath = args.Length > 1 ? args[1] : "./test-renders/output.svg";
    var isDark = args.Contains("--dark");

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
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
else if (command == "tlp")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: tlp <input.tlp> <output.svg>");
        return;
    }

    var inputPath = args[1];
    var outputPath = args[2];

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return;
    }

    var tlpContent = File.ReadAllText(inputPath);
    var graph = TlpParser.Parse(tlpContent);
    var mermaidCode = TlpRenderer.ConvertToMermaid(graph);
    var options = CreateOptions(false);
    options.MaxInputSize = mermaidCode.Length + 10000;
    options.MaxNodes = 50000;
    options.MaxEdges = 250000;
    options.MaxComplexity = 500000;
    var svg = Mermaid.Render(mermaidCode, options);
    File.WriteAllText(outputPath, svg);
    Console.WriteLine($"TLP converted to {Path.GetFullPath(outputPath)}");
}
else if (command == "samples")
{
    var outputDir = args.Length > 1 ? args[1] : "./test-renders";
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
    var outputDir = args.Length > 1 ? args[1] : "./test-renders/compare";
    Directory.CreateDirectory(outputDir);

    var flowchartsOnly = args.Contains("--flowcharts-only");
    var onlyName = "";
    var onlyIdx = Array.IndexOf(args, "--only");
    if (onlyIdx >= 0 && onlyIdx + 1 < args.Length)
        onlyName = args[onlyIdx + 1];
    var noOpen = args.Contains("--no-open");

    Console.WriteLine("Naiad vs mermaid.js parity comparison");
    Console.WriteLine("=====================================\n");

    var samples = GetSampleDiagrams();

    // Apply filters
    if (!string.IsNullOrEmpty(onlyName))
    {
        samples = samples.Where(s => s.Name.Contains(onlyName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (samples.Count == 0)
        {
            Console.Error.WriteLine($"No samples matching '{onlyName}'. Available:");
            foreach (var (n, _) in GetSampleDiagrams())
                Console.Error.WriteLine($"  {n}");
            return;
        }
        Console.WriteLine($"Filtered to {samples.Count} sample(s) matching '{onlyName}'");
    }
    else if (flowchartsOnly)
    {
        samples = samples.Where(s => s.Name.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"Filtered to {samples.Count} flowchart sample(s)");
    }

    // Render all Naiad outputs first (fast) — save SVG directly
    Console.WriteLine("\nPhase 1: Rendering with Naiad...");
    foreach (var (name, code) in samples)
    {
        Console.Write($"  {name}... ");
        try
        {
            var opts = CreateOptions(false);
            var svg = Mermaid.Render(code, opts);
            File.WriteAllText(Path.Combine(outputDir, $"{name}-naiad.svg"), svg);
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

    var reportPath = Path.Combine(Path.GetFullPath(outputDir), "index.html");
    Console.WriteLine($"\nComparison saved to {Path.GetFullPath(outputDir)}");
    Console.WriteLine($"Open {reportPath} to review.");

    // Auto-open in default browser
    if (!noOpen)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = reportPath,
                UseShellExecute = true
            });
        }
        catch { /* ignore if browser launch fails */ }
    }
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
        // Use built-in skin defaults — "default" = mermaid.js matching, "dark" = dark mode
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

    // Build an HTML page with mermaid.js that returns raw SVG strings
    var htmlTemplate = """
        <!DOCTYPE html>
        <html>
        <head>
          <script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
        </head>
        <body>
          <script>
            mermaid.initialize({ startOnLoad: false, theme: 'default' });
            async function renderDiagram(code) {
              try {
                const { svg } = await mermaid.render('diag', code);
                return { success: true, svg: svg };
              } catch (e) {
                return { success: false, error: e.message };
              }
            }
          </script>
        </body>
        </html>
        """;

    var htmlPath = Path.GetFullPath(Path.Combine(outputDir, "_mermaid_renderer.html"));
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

            // Render the diagram via mermaid.js — get raw SVG string
            var result = await page.EvaluateAsync(
                "code => renderDiagram(code)", trimmedCode);

            if (result?.GetProperty("success").GetBoolean() == true)
            {
                var svg = result.Value.GetProperty("svg").GetString() ?? "";
                File.WriteAllText(Path.Combine(outputDir, $"{name}-mermaidjs.svg"), svg);
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
    foreach (var (name, code) in samples)
    {
        var naiadSvgFile = Path.Combine(outputDir, $"{name}-naiad.svg");
        var mermaidSvgFile = Path.Combine(outputDir, $"{name}-mermaidjs.svg");

        var naiadSvg = File.Exists(naiadSvgFile) ? File.ReadAllText(naiadSvgFile) : null;
        var mermaidSvg = File.Exists(mermaidSvgFile) ? File.ReadAllText(mermaidSvgFile) : null;

        // Trim indentation from source code for display
        var trimmedCode = string.Join('\n',
            code.Split('\n').Select(l => l.TrimStart()).Where(l => l.Length > 0));
        var escapedCode = trimmedCode
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        var naiadCell = naiadSvg != null
            ? $"<div class=\"svg-container\">{naiadSvg}</div>"
            : "<span class=\"missing\">FAILED</span>";
        var mermaidCell = mermaidSvg != null
            ? $"<div class=\"svg-container\">{mermaidSvg}</div>"
            : "<span class=\"missing\">FAILED</span>";

        rows.AppendLine($"""
            <tr>
              <td class="name">
                <strong>{name}</strong>
                <details class="code-details">
                  <summary>source</summary>
                  <pre class="code">{escapedCode}</pre>
                </details>
              </td>
              <td class="img">{naiadCell}</td>
              <td class="bench" data-engine="naiad">-</td>
              <td class="img">{mermaidCell}</td>
              <td class="bench" data-engine="mermaid">-</td>
            </tr>
            """);
    }

    var html = $$"""
        <!DOCTYPE html>
        <html>
        <head>
          <title>Naiad vs mermaid.js Comparison &amp; Benchmark</title>
          <script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: system-ui, -apple-system, sans-serif; background: #f5f5f5; padding: 20px; }
            h1 { text-align: center; margin-bottom: 8px; color: #333; }
            h2 { text-align: center; margin-bottom: 16px; color: #555; font-size: 16px; font-weight: 400; }
            table { width: 100%; border-collapse: collapse; background: white; box-shadow: 0 2px 8px rgba(0,0,0,.1); margin-bottom: 24px; }
            th { background: #2196F3; color: white; padding: 12px 16px; text-align: center; font-size: 14px; }
            th.naiad-col { background: #1565C0; }
            th.mermaid-col { background: #4CAF50; }
            td { padding: 12px; vertical-align: top; border-bottom: 1px solid #eee; }
            td.name { font-weight: 600; font-size: 13px; color: #555; width: 160px; vertical-align: top; }
            td.img { text-align: center; width: 30%; vertical-align: top; }
            td.bench { text-align: center; width: 10%; vertical-align: middle; font-size: 12px; }
            .svg-container { border: 1px solid #ddd; border-radius: 4px; padding: 8px; background: white; overflow: auto; max-height: 400px; }
            .svg-container svg { max-width: 100%; height: auto; display: block; }
            .missing { color: #e53935; font-weight: bold; }
            tr:hover { background: #f9f9f9; }
            .code-details { margin-top: 8px; }
            .code-details summary { cursor: pointer; color: #1976D2; font-size: 12px; }
            .code { background: #263238; color: #EEFFFF; padding: 10px; border-radius: 4px;
                     font-size: 11px; overflow-x: auto; white-space: pre; margin-top: 4px;
                     max-height: 300px; overflow-y: auto; }
            .wc-status { margin: 0 0 12px; padding: 10px 12px; border-radius: 8px; border: 1px solid #d1d5db; background: #fff; color: #1f2937; }
            .wc-status.error { border-color: #dc2626; background: #fef2f2; color: #991b1b; }
            td.img.naiad-live-cell { min-width: 320px; }
            td.img.naiad-live-cell naiad-diagram { display: block; width: 100%; --naiad-min-height: 180px; --naiad-padding: 8px; }
            td.img.mermaid-live-cell { min-width: 320px; }
            td.img.mermaid-live-cell .mermaid-live { border: 1px solid #ddd; border-radius: 4px; padding: 8px; background: white; overflow: auto; max-height: 400px; }
            td.img.mermaid-live-cell .mermaid-live svg { max-width: 100%; height: auto; display: block; }
            .bench-time { font-weight: 700; font-size: 14px; }
            .bench-ops { color: #777; font-size: 11px; }
            .bench-winner { color: #2E7D32; }
            .bench-loser { color: #C62828; }
            .bench-controls { text-align: center; margin: 16px 0; }
            .bench-controls button { padding: 10px 24px; font-size: 14px; border: none; border-radius: 6px;
              background: #1565C0; color: white; cursor: pointer; margin: 0 8px; }
            .bench-controls button:hover { background: #0D47A1; }
            .bench-controls button:disabled { background: #90CAF9; cursor: not-allowed; }
            .bench-controls select { padding: 8px 12px; font-size: 14px; border-radius: 6px; border: 1px solid #ccc; }
            .bench-summary { text-align: center; margin: 16px 0; padding: 16px; background: white;
              border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.1); }
            .bench-summary h3 { margin-bottom: 8px; }
            .bench-bar { display: inline-block; height: 20px; border-radius: 4px; min-width: 4px; vertical-align: middle; }
            .bench-bar.naiad { background: #1565C0; }
            .bench-bar.mermaid { background: #4CAF50; }
            #benchmark-progress { margin: 8px 0; }
          </style>
        </head>
        <body>
          <h1>Naiad vs mermaid.js</h1>
          <h2>Visual Comparison &amp; Performance Benchmark</h2>

          <div id="status-container"></div>

          <div class="bench-controls">
            <label>Iterations: <select id="bench-iterations">
              <option value="10">10 (quick)</option>
              <option value="100" selected>100</option>
              <option value="500">500</option>
              <option value="1000">1000</option>
            </select></label>
            <button id="bench-btn" disabled>Run Benchmark</button>
            <span id="bench-progress-text"></span>
          </div>
          <div id="benchmark-summary" class="bench-summary" style="display:none"></div>

          <table>
            <thead>
              <tr>
                <th>Diagram</th>
                <th class="naiad-col">Naiad WASM</th>
                <th>Naiad (ms)</th>
                <th class="mermaid-col">mermaid.js</th>
                <th>mermaid.js (ms)</th>
              </tr>
            </thead>
            <tbody>
              {{rows}}
            </tbody>
          </table>

          <script type="module">
            // ── Status ─────────────────────────────────────────────────
            const statusContainer = document.getElementById('status-container');
            function addStatus(text, isError = false) {
              const p = document.createElement('p');
              p.className = 'wc-status' + (isError ? ' error' : '');
              p.textContent = text;
              statusContainer.appendChild(p);
              return p;
            }

            // ── Naiad Web Component ────────────────────────────────────
            const loadCandidates = [
              '../Naiad/src/Naiad.Wasm.Npm/dist/naiad-web-component.js',
              '/Naiad/src/Naiad.Wasm.Npm/dist/naiad-web-component.js',
              './naiad-web-component.js'
            ];

            let naiadClient = null;

            async function loadNaiadComponent() {
              for (const candidate of loadCandidates) {
                try {
                  const mod = await import(candidate);
                  // Try to get the client for benchmarking
                  if (mod.NaiadClient) {
                    naiadClient = new mod.NaiadClient();
                    await naiadClient.init();
                  }
                  return candidate;
                } catch { /* try next */ }
              }
              throw new Error('Could not load naiad-web-component.js');
            }

            // ── mermaid.js init ────────────────────────────────────────
            mermaid.initialize({ startOnLoad: false, theme: 'default' });
            let mermaidIdCounter = 0;

            async function mermaidRender(code) {
              const id = `mdiag${mermaidIdCounter++}`;
              const { svg } = await mermaid.render(id, code);
              return svg;
            }

            // ── Upgrade rows to live renderers ─────────────────────────
            async function upgradeRows() {
              const rows = document.querySelectorAll('tbody tr');
              let naiadOk = 0, mermaidOk = 0;

              for (const row of rows) {
                const code = row.querySelector('pre.code')?.textContent?.trim() ?? '';
                if (!code) continue;

                const name = row.querySelector('td.name strong')?.textContent?.trim() ?? '';
                const cells = row.querySelectorAll('td.img');
                const naiadCell = cells[0];
                const mermaidCell = cells[1];

                // Upgrade Naiad cell to live web component
                if (naiadCell) {
                  naiadCell.classList.add('naiad-live-cell');
                  naiadCell.innerHTML = '';
                  const diagram = document.createElement('naiad-diagram');
                  diagram.setAttribute('fit-width', '');
                  diagram.setAttribute('show-menu', '');
                  diagram.setAttribute('theme', 'light');
                  diagram.setAttribute('download-filename', `${name}-naiad`);
                  diagram.textContent = code;
                  naiadCell.appendChild(diagram);
                  naiadOk++;
                }

                // Upgrade mermaid.js cell to live render
                if (mermaidCell) {
                  mermaidCell.classList.add('mermaid-live-cell');
                  try {
                    const svg = await mermaidRender(code);
                    mermaidCell.innerHTML = `<div class="mermaid-live">${svg}</div>`;
                    mermaidOk++;
                  } catch (e) {
                    mermaidCell.innerHTML = `<span class="missing">mermaid.js error: ${e.message}</span>`;
                  }
                }
              }

              addStatus(`Live rendering: ${naiadOk} Naiad WASM + ${mermaidOk} mermaid.js diagrams`);
            }

            // ── Benchmark ──────────────────────────────────────────────
            const benchBtn = document.getElementById('bench-btn');
            const benchIterSelect = document.getElementById('bench-iterations');
            const benchProgressText = document.getElementById('bench-progress-text');
            const benchSummary = document.getElementById('benchmark-summary');

            benchBtn.addEventListener('click', runBenchmark);

            async function runBenchmark() {
              const iterations = parseInt(benchIterSelect.value);
              benchBtn.disabled = true;
              benchProgressText.textContent = 'Warming up...';

              const rows = document.querySelectorAll('tbody tr');
              const results = [];
              let completed = 0;
              const total = rows.length;

              for (const row of rows) {
                const code = row.querySelector('pre.code')?.textContent?.trim() ?? '';
                const name = row.querySelector('td.name strong')?.textContent?.trim() ?? '';
                if (!code) continue;

                const benchCells = row.querySelectorAll('td.bench');
                const naiadBenchCell = benchCells[0];
                const mermaidBenchCell = benchCells[1];

                completed++;
                benchProgressText.textContent = `Benchmarking ${completed}/${total}: ${name}...`;

                // Allow UI to update
                await new Promise(r => setTimeout(r, 10));

                // Benchmark Naiad WASM
                let naiadMs = null;
                if (naiadClient) {
                  try {
                    // Warmup
                    naiadClient.renderSvg(code);
                    const start = performance.now();
                    for (let i = 0; i < iterations; i++) {
                      naiadClient.renderSvg(code);
                    }
                    naiadMs = (performance.now() - start) / iterations;
                  } catch { naiadMs = null; }
                }

                // Benchmark mermaid.js
                let mermaidMs = null;
                try {
                  // Warmup
                  await mermaidRender(code);
                  const start = performance.now();
                  for (let i = 0; i < iterations; i++) {
                    await mermaidRender(code);
                  }
                  mermaidMs = (performance.now() - start) / iterations;
                } catch { mermaidMs = null; }

                // Update cells
                if (naiadBenchCell) {
                  if (naiadMs !== null) {
                    const winner = mermaidMs !== null && naiadMs <= mermaidMs;
                    naiadBenchCell.innerHTML = `<span class="bench-time ${winner ? 'bench-winner' : ''}">${naiadMs.toFixed(2)}ms</span><br><span class="bench-ops">${(1000/naiadMs).toFixed(0)} ops/s</span>`;
                  } else {
                    naiadBenchCell.innerHTML = '<span class="missing">N/A</span>';
                  }
                }
                if (mermaidBenchCell) {
                  if (mermaidMs !== null) {
                    const winner = naiadMs !== null && mermaidMs <= naiadMs;
                    mermaidBenchCell.innerHTML = `<span class="bench-time ${winner ? 'bench-winner' : ''}">${mermaidMs.toFixed(2)}ms</span><br><span class="bench-ops">${(1000/mermaidMs).toFixed(0)} ops/s</span>`;
                  } else {
                    mermaidBenchCell.innerHTML = '<span class="missing">N/A</span>';
                  }
                }

                results.push({ name, naiadMs, mermaidMs });
              }

              // Summary
              const validResults = results.filter(r => r.naiadMs !== null && r.mermaidMs !== null);
              if (validResults.length > 0) {
                const avgNaiad = validResults.reduce((s, r) => s + r.naiadMs, 0) / validResults.length;
                const avgMermaid = validResults.reduce((s, r) => s + r.mermaidMs, 0) / validResults.length;
                const naiadWins = validResults.filter(r => r.naiadMs <= r.mermaidMs).length;
                const ratio = avgMermaid / avgNaiad;

                benchSummary.style.display = '';
                benchSummary.innerHTML = `
                  <h3>Benchmark Results (${iterations} iterations each)</h3>
                  <p style="margin:8px 0">
                    <span class="bench-bar naiad" style="width:${Math.min(200, 200/ratio)}px"></span>
                    <strong>Naiad WASM:</strong> ${avgNaiad.toFixed(2)}ms avg
                    &nbsp;&nbsp;vs&nbsp;&nbsp;
                    <span class="bench-bar mermaid" style="width:${Math.min(200, 200*ratio/ratio)}px"></span>
                    <strong>mermaid.js:</strong> ${avgMermaid.toFixed(2)}ms avg
                  </p>
                  <p>Naiad wins ${naiadWins}/${validResults.length} diagrams.
                  ${ratio > 1 ? `Naiad is <strong>${ratio.toFixed(1)}x faster</strong> on average.` :
                    `mermaid.js is <strong>${(1/ratio).toFixed(1)}x faster</strong> on average.`}</p>
                `;
              }

              benchProgressText.textContent = 'Done!';
              benchBtn.disabled = false;
            }

            // ── Initialize ─────────────────────────────────────────────
            try {
              await loadNaiadComponent();
              addStatus('Naiad WASM loaded successfully');
              benchBtn.disabled = false;
            } catch (e) {
              addStatus(`Naiad WASM failed to load: ${e.message}`, true);
            }

            await upgradeRows();
          </script>
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

    // ── Complex flowchart stress tests ────────────────────────────
    ("flowchart-fan-in-5", """
        flowchart TD
            A[Source 1] --> F[Collector]
            B[Source 2] --> F
            C[Source 3] --> F
            D[Source 4] --> F
            E[Source 5] --> F
            F --> G[Result]
        """),

    ("flowchart-fan-out-5", """
        flowchart TD
            A[Router] --> B[Target 1]
            A --> C[Target 2]
            A --> D[Target 3]
            A --> E[Target 4]
            A --> F[Target 5]
        """),

    ("flowchart-diamond-cascade", """
        flowchart TD
            A[Start] --> B{Check 1}
            B -->|Yes| C{Check 2}
            B -->|No| D[Fail 1]
            C -->|Yes| E{Check 3}
            C -->|No| F[Fail 2]
            E -->|Yes| G[Success]
            E -->|No| H[Fail 3]
            D --> I[Log Error]
            F --> I
            H --> I
            I --> J[End]
            G --> J
        """),

    ("flowchart-nested-subgraphs-3", """
        flowchart LR
            subgraph Outer["System"]
                subgraph Middle["Service Layer"]
                    subgraph Inner["Core"]
                        A[Engine] --> B[Cache]
                    end
                    C[API] --> A
                    B --> D[Store]
                end
                E[Gateway] --> C
                D --> F[Monitor]
            end
            G[Client] --> E
            F --> H[Alert]
        """),

    ("flowchart-all-edge-types", """
        flowchart LR
            A[Solid] --> B[Arrow]
            B -.-> C[Dotted]
            C ==> D[Thick]
            D <--> E[Bidirectional]
            E --o F[Circle End]
            F --x G[Cross End]
            G -->|labeled| H[With Label]
            H -.->|dotted label| I[End]
        """),

    ("flowchart-all-shapes", """
        flowchart TD
            A[Rectangle] --> B(Rounded)
            B --> C([Stadium])
            C --> D[[Subroutine]]
            D --> E[(Database)]
            E --> F((Circle))
            F --> G{Diamond}
            G --> H{{Hexagon}}
            H --> I>Asymmetric]
            I --> J(((Double Circle)))
        """),

    ("flowchart-self-loop-back-edge", """
        flowchart TD
            A[Init] --> B[Process]
            B --> B
            B --> C{Valid?}
            C -->|No| B
            C -->|Yes| D[Save]
            D --> E{More?}
            E -->|Yes| A
            E -->|No| F[Done]
        """),

    ("flowchart-long-labels-mixed", """
        flowchart TD
            A[Short] --> B[This is a very long label that should wrap or expand the node significantly]
            B --> C[OK]
            A --> D[X]
            D --> E[Another moderately long label for testing]
            E --> C
        """),

    ("flowchart-classdef-subgraph", """
        flowchart TD
            subgraph Frontend["Frontend Layer"]
                A[React App] --> B[State Manager]
                B --> C[API Client]
            end
            subgraph Backend["Backend Layer"]
                D[REST API] --> E[Service]
                E --> F[(PostgreSQL)]
            end
            C --> D
            classDef frontend fill:#61DAFB,stroke:#21A0C9,color:#000
            classDef backend fill:#68D391,stroke:#38A169,color:#000
            classDef database fill:#F6AD55,stroke:#DD6B20,color:#000
            class A,B,C frontend
            class D,E backend
            class F database
        """),

    ("flowchart-wide-fan-reconverge", """
        flowchart TD
            Start[Start] --> A[Branch A]
            Start --> B[Branch B]
            Start --> C[Branch C]
            Start --> D[Branch D]
            A --> Process[Merge Point]
            B --> Process
            C --> Process
            D --> Process
            Process --> End[End]
        """),

    ("flowchart-parallel-chains", """
        flowchart TD
            subgraph Chain1["Pipeline 1"]
                A1[Input] --> B1[Transform] --> C1[Output]
            end
            subgraph Chain2["Pipeline 2"]
                A2[Input] --> B2[Transform] --> C2[Output]
            end
            subgraph Chain3["Pipeline 3"]
                A3[Input] --> B3[Transform] --> C3[Output]
            end
            C1 --> D[Aggregator]
            C2 --> D
            C3 --> D
        """),

    ("flowchart-cross-rank-edges", """
        flowchart TD
            A[Start] --> B[Step 1]
            B --> C[Step 2]
            C --> D[Step 3]
            D --> E[Step 4]
            A --> D
            B --> E
            A --> E
        """),

    ("flowchart-bidirectional-cycle", """
        flowchart LR
            A[Service A] <-->|sync| B[Service B]
            B <-->|sync| C[Service C]
            C <-->|sync| A
            A --> D[Monitor]
            B --> D
            C --> D
        """),

    ("flowchart-heavy-text-nodes", """
        flowchart TD
            A[User Authentication<br/>and Authorization<br/>Service Module] --> B[Request Validation<br/>Input Sanitization<br/>Rate Limiting Layer]
            B --> C{Content Type<br/>Router and<br/>Dispatcher}
            C -->|JSON| D[JSON Processing<br/>Engine with Schema<br/>Validation Support]
            C -->|XML| E[XML Parser with<br/>DTD Validation and<br/>Namespace Resolution]
            C -->|Binary| F[Binary Stream<br/>Handler with<br/>Chunk Processing]
            D --> G[Response Builder<br/>with Compression<br/>and Cache Headers]
            E --> G
            F --> G
            G --> H[Load Balancer<br/>Health Check and<br/>Circuit Breaker]
        """),

    ("flowchart-complex-pipeline", """
        flowchart TD
            subgraph Ingest["Data Ingestion"]
                S1[Kafka] --> S2{Validate}
                S2 -->|Valid| S3[Parse]
                S2 -->|Invalid| S4[Dead Letter]
            end
            subgraph Process["Processing"]
                P1[Enrich] --> P2[Transform]
                P2 --> P3{Route}
                P3 -->|Type A| P4[Handler A]
                P3 -->|Type B| P5[Handler B]
                P3 -->|Type C| P6[Handler C]
            end
            subgraph Store["Storage"]
                D1[(Primary DB)]
                D2[(Archive)]
                D3[Search Index]
            end
            S3 --> P1
            P4 --> D1
            P5 --> D1
            P6 --> D1
            D1 --> D2
            D1 --> D3
            S4 --> D2
            style S4 fill:#FFCDD2,stroke:#B71C1C
            classDef db fill:#E3F2FD,stroke:#1565C0
            class D1,D2,D3 db
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

    // ── Parallel Coordinates (Tulip-style) ─────────────────────────
    ("parallelcoords", """
        parallelcoords
            title "Car Comparison"
            axis Price, MPG, Horsepower, Weight, Safety
            dataset "Sedan"{22000, 32, 180, 3200, 5}
            dataset "SUV"{35000, 22, 260, 4500, 4}
            dataset "Sports"{55000, 18, 400, 3800, 3}
            dataset "Compact"{18000, 38, 140, 2800, 5}
        """),

    // ── Dendrogram (Clustering) ───────────────────────────────────
    ("dendrogram", """
        dendrogram
            title "Species Clustering"
            leaf "Dog", "Cat", "Whale", "Shark", "Eagle"
            merge "Dog"-"Cat":0.3
            merge "Whale"-"Shark":0.5
            merge "DogCat"-"WhaleShark":0.8
            merge "DogCatWhaleShark"-"Eagle":1.2
        """),

    // ── Bubble Pack ───────────────────────────────────────────────
    ("bubblepack", """
        bubblepack
            "Market"
                "Tech": 1000
                    "Software": 600
                    "Hardware": 400
                "Finance": 800
                    "Banking": 500
                    "Insurance": 300
                "Healthcare": 600
        """),

    // ── Voronoi ────────────────────────────────────────────────────
    ("voronoi", """
        voronoi
            title "Service Territories"
            site "North" at 150, 100
            site "South" at 150, 300
            site "East" at 300, 200
            site "West" at 50, 200
            site "Central" at 150, 200
        """),

    // ── Geo ────────────────────────────────────────────────────────
    ("geo", """
        geo
            title "UK Town Markers"
            country uk
            town "London" color=#ef4444 size=6
            town "Manchester" color=#3b82f6
            town "Edinburgh" color=#22c55e
        """),
];

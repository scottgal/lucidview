namespace Tests.WebComponent;

[TestFixture]
public class NaiadNpmDistPlaywrightTests : PageTest
{
    [SetUp]
    public void SetUpPageDiagnostics()
    {
        Page.Console += (_, msg) => TestContext.Progress.WriteLine($"[console:{msg.Type}] {msg.Text}");
        Page.PageError += (_, msg) => TestContext.Progress.WriteLine($"[pageerror] {msg}");
    }

    [Test]
    public Task NpmDistComplete_ShouldRenderWebComponent() =>
        AssertNpmDistRenders("Naiad.Wasm.Npm");

    [Test]
    public Task NpmDistMermaid_ShouldRenderWebComponent() =>
        AssertNpmDistRenders("Naiad.Wasm.Npm.Mermaid");

    [TestCase("Naiad.Wasm.Npm")]
    [TestCase("Naiad.Wasm.Npm.Mermaid")]
    public async Task NpmDist_ShouldSanitizeUnsafeSvgPayloads(string packageFolder)
    {
        var source = """
            flowchart LR
              A[Start] --> B[End]
              style A fill:url(javascript:alert(1)),stroke:#333,stroke-width:2px
            """;

        var svgMarkup = await RenderNpmDistAndGetSvg(packageFolder, source);
        Assert.That(svgMarkup, Does.Not.Contain("onload="));
        Assert.That(svgMarkup, Does.Not.Contain("javascript:"));
        Assert.That(svgMarkup, Does.Not.Contain("<script"));
    }

    static string GetRepoRoot() =>
        Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));

    async Task AssertNpmDistRenders(string packageFolder)
    {
        var svgMarkup = await RenderNpmDistAndGetSvg(packageFolder, """
            flowchart LR
              A --> B
              B --> C
            """);
        Assert.That(svgMarkup.Length, Is.GreaterThan(0));
    }

    async Task<string> RenderNpmDistAndGetSvg(string packageFolder, string mermaidSource)
    {
        var repoRoot = GetRepoRoot();
        var distRoot = Path.Combine(repoRoot, "Naiad", "src", packageFolder, "dist");

        if (!Directory.Exists(distRoot))
            Assert.Fail($"Npm dist directory not found: {distRoot}. Run npm build first.");

        var harnessFileName = $"__playwright-harness-{Guid.NewGuid():N}.html";
        var harnessPath = Path.Combine(distRoot, harnessFileName);
        await File.WriteAllTextAsync(harnessPath,
            $"""
            <!doctype html>
            <html lang="en">
            <body>
              <naiad-diagram>
            {mermaidSource}
              </naiad-diagram>
              <script type="module" src="./naiad-web-component.js"></script>
            </body>
            </html>
            """);

        await using var server = new StaticFileServer(distRoot);
        try
        {
            server.Start();

            await Page.GotoAsync($"{server.BaseUrl}/{harnessFileName}");

            await Page.WaitForFunctionAsync(
                """
                () => {
                  const el = document.querySelector('naiad-diagram');
                  if (!el?.shadowRoot) return false;
                  const status = el.shadowRoot.querySelector('#status')?.textContent?.trim();
                  return status === 'Rendered' || status === 'Render failed';
                }
                """);

            var status = await Page.EvaluateAsync<string>(
                """
                () => {
                  const el = document.querySelector('naiad-diagram');
                  return el?.shadowRoot?.querySelector('#status')?.textContent?.trim() ?? '';
                }
                """);

            if (status == "Render failed")
            {
                var error = await Page.EvaluateAsync<string>(
                    """
                    () => {
                      const el = document.querySelector('naiad-diagram');
                      return el?.shadowRoot?.querySelector('#error')?.textContent?.trim() ?? '';
                    }
                    """);
                Assert.Fail($"Npm dist web component render failed: {error}");
            }

            Assert.That(status, Is.EqualTo("Rendered"));

            var svgLength = await Page.EvaluateAsync<int>(
                """
                () => {
                  const el = document.querySelector('naiad-diagram');
                  const svg = el?.shadowRoot?.querySelector('#diagram svg');
                  return svg?.outerHTML?.length ?? 0;
                }
                """);

            Assert.That(svgLength, Is.GreaterThan(0));
            return await Page.EvaluateAsync<string>(
                """
                () => {
                  const el = document.querySelector('naiad-diagram');
                  const svg = el?.shadowRoot?.querySelector('#diagram svg');
                  return svg?.outerHTML ?? '';
                }
                """);
        }
        finally
        {
            File.Delete(harnessPath);
        }
    }

    [Test]
    public async Task CompareReportPage_ShouldUpgradeToLiveWebComponents()
    {
        var repoRoot = GetRepoRoot();
        await using var repoServer = new StaticFileServer(repoRoot);
        repoServer.Start();

        await Page.GotoAsync($"{repoServer.BaseUrl}/compare-output/index.html");

        await Page.WaitForFunctionAsync(
            """
            () => {
              const status = document.querySelector('.wc-status');
              if (!status) return false;
              const text = status.textContent ?? '';
              return text.includes('Live Naiad web components loaded:') || text.includes('Web component load failed:');
            }
            """);

        var statusText = await Page.EvaluateAsync<string>("() => document.querySelector('.wc-status')?.textContent?.trim() ?? ''");
        Assert.That(statusText, Does.Not.Contain("Web component load failed:"));
        Assert.That(statusText, Does.Contain("Live Naiad web components loaded:"));

        var loadedCount = await Page.EvaluateAsync<int>(
            """
            () => document.querySelectorAll('tbody td.img.naiad-live-cell naiad-diagram').length
            """);
        Assert.That(loadedCount, Is.GreaterThan(0), $"Unexpected status: {statusText}");
    }
}

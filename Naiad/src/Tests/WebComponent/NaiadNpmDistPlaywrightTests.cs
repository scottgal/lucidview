namespace Tests.WebComponent;

[TestFixture]
public class NaiadNpmDistPlaywrightTests : PageTest
{
    StaticFileServer? _server;
    string _baseUrl = "";

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        var distRoot = Path.Combine(repoRoot, "Naiad", "src", "Naiad.Wasm.Npm", "dist");

        if (!Directory.Exists(distRoot))
            Assert.Fail($"Npm dist directory not found: {distRoot}. Run npm build first.");

        _server = new StaticFileServer(distRoot);
        _server.Start();
        _baseUrl = _server.BaseUrl;

        await Task.CompletedTask;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
    }

    [SetUp]
    public void SetUpPageDiagnostics()
    {
        Page.Console += (_, msg) => TestContext.Progress.WriteLine($"[console:{msg.Type}] {msg.Text}");
        Page.PageError += (_, msg) => TestContext.Progress.WriteLine($"[pageerror] {msg}");
    }

    [Test]
    public async Task NpmDist_PlainPage_ShouldRenderWebComponent()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");

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
    }

    [Test]
    public async Task CompareReportPage_ShouldUpgradeToLiveWebComponents()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
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

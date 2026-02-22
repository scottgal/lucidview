using System.Diagnostics;
using System.Text.Json;

namespace Tests.WebComponent;

[TestFixture]
public class NaiadWebComponentPlaywrightTests : PageTest
{
    static StaticFileServer? _server;
    static string _baseUrl = "";
    static string _wwwroot = "";
    static readonly JsonSerializerOptions JsonCaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _wwwroot = ResolvePublishWwwroot();
        EnsurePublishedWwwroot(_wwwroot);

        _server = new StaticFileServer(_wwwroot);
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
    public async Task NaiadClient_HealthAndDetect_ShouldWork()
    {
        await Page.GotoAsync($"{_baseUrl}/client-diagnostics.html");

        await Page.WaitForFunctionAsync("() => !!window.__naiadInit");
        var initReady = await Page.EvaluateAsync<bool>(
            """
            async () => {
              try {
                await window.__naiadInit;
                return !!window.__naiadClient;
              } catch (error) {
                window.__naiadInitError = error?.message ?? String(error);
                return false;
              }
            }
            """);
        if (!initReady)
        {
            var initError = await Page.EvaluateAsync<string>("() => window.__naiadInitError ?? 'unknown init error'");
            throw new AssertionException($"Naiad client init failed: {initError}");
        }

        var health = await Page.EvaluateAsync<string>("() => window.__naiadClient.health()");
        Assert.That(health, Is.EqualTo("ok"));

        const string mermaid = """
            flowchart LR
              A --> B
            """;

        var kind = await Page.EvaluateAsync<string>("(source) => window.__naiadClient.detectDiagramType(source)", mermaid);
        Assert.That(kind, Is.EqualTo("Flowchart"));

        var render = await Page.EvaluateAsync<string>("(source) => window.__naiadClient.renderSvg(source)", mermaid);
        Assert.That(render, Does.Contain("<svg"));
        Assert.That(render, Does.Contain("A"));
    }

    [Test]
    public async Task NaiadClient_ShouldApplyExtendedNaiadDirectiveThemeAndSkinPack()
    {
        await EnsureNaiadClientReadyAsync();

        var result = await Page.EvaluateAsync<string>(
            """
            () => {
              const baseMermaid = 'flowchart LR\n  A[Node] --> B[Node]';
              const themeDirective = '%% naiad: theme=dark\n' + baseMermaid;
              const skinDirective = '%% naiad: skinPack=glass\n' + baseMermaid;
              const jsonDirective = '%% naiad: {"skinPack":"material3","theme":"dark"}\n' + baseMermaid;

              const baseSvg = window.__naiadClient.renderSvg(baseMermaid);
              const themeSvg = window.__naiadClient.renderSvg(themeDirective);
              const skinSvg = window.__naiadClient.renderSvg(skinDirective);
              const jsonSvg = window.__naiadClient.renderSvg(jsonDirective);

              return JSON.stringify({
                baseLen: baseSvg.length,
                themeLen: themeSvg.length,
                skinLen: skinSvg.length,
                jsonLen: jsonSvg.length,
                themeChanged: baseSvg !== themeSvg,
                skinChanged: baseSvg !== skinSvg,
                jsonChanged: baseSvg !== jsonSvg,
                allSvg: [baseSvg, themeSvg, skinSvg, jsonSvg].every(x => x.includes('<svg'))
              });
            }
            """);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("allSvg").GetBoolean(), Is.True, result);
        Assert.That(root.GetProperty("themeChanged").GetBoolean(), Is.True, result);
        Assert.That(root.GetProperty("skinChanged").GetBoolean(), Is.True, result);
        Assert.That(root.GetProperty("jsonChanged").GetBoolean(), Is.True, result);
        Assert.That(root.GetProperty("themeLen").GetInt32(), Is.GreaterThan(0), result);
        Assert.That(root.GetProperty("skinLen").GetInt32(), Is.GreaterThan(0), result);
        Assert.That(root.GetProperty("jsonLen").GetInt32(), Is.GreaterThan(0), result);
    }

    [Test]
    public async Task NaiadClient_Directives_ShouldOverrideProvidedRenderOptions()
    {
        await EnsureNaiadClientReadyAsync();

        var result = await Page.EvaluateAsync<string>(
            """
            () => {
              const source = 'flowchart LR\n  A[Node] --> B[Node]';
              const themedSource = '%% naiad: theme=dark\n' + source;
              const skinnedSource = '%% naiad: skinPack=fluent\n' + source;

              const defaultThemed = window.__naiadClient.renderSvg(source, { theme: 'default' });
              const forcedDark = window.__naiadClient.renderSvg(themedSource, { theme: 'default' });

              const defaultSkinned = window.__naiadClient.renderSvg(source, { skinPack: 'default' });
              const forcedSkin = window.__naiadClient.renderSvg(skinnedSource, { skinPack: 'default' });

              return JSON.stringify({
                themeOverrideChanged: defaultThemed !== forcedDark,
                skinOverrideChanged: defaultSkinned !== forcedSkin,
                forcedDarkHasSvg: forcedDark.includes('<svg'),
                forcedSkinHasSvg: forcedSkin.includes('<svg')
              });
            }
            """);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("forcedDarkHasSvg").GetBoolean(), Is.True, result);
        Assert.That(root.GetProperty("forcedSkinHasSvg").GetBoolean(), Is.True, result);
        Assert.That(root.GetProperty("themeOverrideChanged").GetBoolean(), Is.True, result);
        Assert.That(root.GetProperty("skinOverrideChanged").GetBoolean(), Is.True, result);
    }

    [Test]
    public async Task PlainPage_ShouldRenderDiagramFromInnerText()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        var status = await GetShadowTextAsync("#status");
        Assert.That(status, Is.EqualTo("Rendered"));

        var svgMarkup = await GetShadowInnerHtmlAsync("#diagram");
        Assert.That(svgMarkup, Does.Contain("<svg"));
        Assert.That(svgMarkup, Does.Contain("Browser"));
    }

    [Test]
    public async Task Component_ShouldRenderWithAllBuiltInSkinPacks()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        var summary = await Page.EvaluateAsync<string>(
            """
            async () => {
              const el = document.querySelector('naiad-diagram');
              const packs = await el.getBuiltInSkinPacks();
              const results = [];

              async function renderWithPack(pack) {
                return await new Promise((resolve) => {
                  const onRendered = (event) => {
                    cleanup();
                    resolve({
                      pack,
                      ok: true,
                      svgLength: event?.detail?.svgLength ?? 0
                    });
                  };

                  const onError = (event) => {
                    cleanup();
                    resolve({
                      pack,
                      ok: false,
                      error: event?.detail?.message ?? "Unknown render error"
                    });
                  };

                  const cleanup = () => {
                    el.removeEventListener('rendered', onRendered);
                    el.removeEventListener('rendererror', onError);
                  };

                  el.addEventListener('rendered', onRendered);
                  el.addEventListener('rendererror', onError);
                  el.options = { skinPack: pack, theme: "light" };
                  el.mermaid = "flowchart LR\n  A[Alpha] --> B[Beta]";
                });
              }

              for (const pack of packs) {
                results.push(await renderWithPack(pack));
              }

              return JSON.stringify(results);
            }
            """);

        var results = JsonSerializer.Deserialize<List<SkinPackRenderResult>>(
            summary,
            JsonCaseInsensitiveOptions) ?? [];
        var failures = new List<string>();
        foreach (var item in results)
        {
            if (!item.Ok || item.SvgLength <= 0)
                failures.Add($"{item.Pack}: {(string.IsNullOrWhiteSpace(item.Error) ? $"svgLength={item.SvgLength}" : item.Error)}");
        }

        Assert.That(failures, Is.Empty, "Available skin packs failed in WASM: " + string.Join("; ", failures));
    }

    [Test]
    public async Task Component_ShouldEmitRenderedEvent_WhenMermaidChanges()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__naiadRendered = null;
              const el = document.querySelector('naiad-diagram');
              el.addEventListener('rendered', (e) => window.__naiadRendered = e.detail);
              el.mermaid = 'flowchart LR\n  X[Input] --> Y[Output]';
            }
            """);

        await Page.WaitForFunctionAsync("() => !!window.__naiadRendered && window.__naiadRendered.svgLength > 0");
        var renderedSource = await Page.EvaluateAsync<string>("() => window.__naiadRendered.mermaid");
        Assert.That(renderedSource, Does.Contain("X[Input]"));
    }

    [Test]
    public async Task Component_ShouldRenderSemicolonSeparatedFlowchart()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              const el = document.querySelector('naiad-diagram');
              el.mermaid = 'flowchart LR; A[Start] --> B[Work]; B --> C[End]';
            }
            """);

        await WaitForDiagramSvgAsync();
        var svgMarkup = await GetShadowInnerHtmlAsync("#diagram");
        Assert.That(svgMarkup, Does.Contain("Start"));
        Assert.That(svgMarkup, Does.Contain("End"));
    }

    [Test]
    public async Task Component_ShouldEmitRenderError_ForInvalidMermaid()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__naiadError = null;
              const el = document.querySelector('naiad-diagram');
              el.addEventListener('rendererror', (e) => window.__naiadError = e.detail);
              el.mermaid = 'not-a-valid-mermaid-graph';
            }
            """);

        await Page.WaitForFunctionAsync("() => !!window.__naiadError && !!window.__naiadError.message");
        var status = await GetShadowTextAsync("#status");
        var errorDisplay = await GetShadowCssAsync("#error", "display");
        Assert.That(status, Is.EqualTo("Render failed"));
        Assert.That(errorDisplay, Is.EqualTo("block"));
    }

    [Test]
    public async Task Component_ShouldEmitRenderError_ForInvalidOptionsJson()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__naiadOptionsError = null;
              const el = document.querySelector('naiad-diagram');
              el.addEventListener('rendererror', (e) => window.__naiadOptionsError = e.detail.message);
              el.setAttribute('options', '{"broken": }');
            }
            """);

        await Page.WaitForFunctionAsync("() => !!window.__naiadOptionsError");
        var errorMessage = await Page.EvaluateAsync<string>("() => window.__naiadOptionsError");
        Assert.That(errorMessage, Does.Contain("Invalid options JSON"));
    }

    [Test]
    public async Task Component_ShouldApplyStatusHidden_AndFitWidth()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              const el = document.querySelector('naiad-diagram');
              el.setAttribute('status-hidden', '');
              el.setAttribute('fit-width', '');
              el.mermaid = 'flowchart LR\n  A --> B --> C';
            }
            """);

        await WaitForDiagramSvgAsync();
        var statusDisplay = await GetShadowCssAsync("#status", "display");
        Assert.That(statusDisplay, Is.EqualTo("none"));

        var fitWidth = await Page.EvaluateAsync<string>(
            """
            () => {
              const el = document.querySelector('naiad-diagram');
              const root = el.shadowRoot;
              const host = root.querySelector('#diagram');
              const svg = root.querySelector('#diagram svg');
              if (!host || !svg) return "0|0|missing|missing";
              const hostWidth = host.getBoundingClientRect().width;
              const svgWidth = svg.getBoundingClientRect().width;
              const computedWidth = getComputedStyle(svg).width;
              const inlineWidth = svg.style.width || "(none)";
              return `${hostWidth}|${svgWidth}|${computedWidth}|${inlineWidth}`;
            }
            """);
        var parts = fitWidth.Split('|');
        var svgWidth = double.Parse(parts[1]);
        var inlineWidth = parts[3];
        Assert.That(svgWidth, Is.GreaterThan(0), $"metrics={fitWidth}");
        Assert.That(inlineWidth, Is.EqualTo("100%"), $"metrics={fitWidth}");
    }

    [Test]
    public async Task Component_ShouldUseDarkTheme_WhenSystemPrefersDark()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__naiadTheme = null;
              const el = document.querySelector('naiad-diagram');
              el.removeAttribute('theme');
              el.removeAttribute('options');
              el.addEventListener('rendered', (e) => window.__naiadTheme = e.detail.theme);
              el.mermaid = 'flowchart LR\n  A --> B';
            }
            """);

        await Page.WaitForFunctionAsync("() => window.__naiadTheme === 'dark'");
        var theme = await Page.EvaluateAsync<string>("() => window.__naiadTheme");
        Assert.That(theme, Is.EqualTo("dark"));
    }

    [Test]
    public async Task Component_ShouldHonorExplicitLightTheme_WhenSystemPrefersDark()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__naiadTheme = null;
              const el = document.querySelector('naiad-diagram');
              el.setAttribute('theme', 'light');
              el.removeAttribute('options');
              el.addEventListener('rendered', (e) => window.__naiadTheme = e.detail.theme);
              el.mermaid = 'flowchart LR\n  L[Light] --> T[Theme]';
            }
            """);

        await Page.WaitForFunctionAsync("() => window.__naiadTheme === 'default'");
        var theme = await Page.EvaluateAsync<string>("() => window.__naiadTheme");
        Assert.That(theme, Is.EqualTo("default"));
    }

    [Test]
    public async Task Component_ShouldHonorOptionsTheme_WhenThemeIsAuto()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__naiadTheme = null;
              const el = document.querySelector('naiad-diagram');
              el.setAttribute('theme', 'auto');
              el.options = { theme: 'dark' };
              el.addEventListener('rendered', (e) => window.__naiadTheme = e.detail.theme);
              el.mermaid = 'flowchart LR\n  O[Options] --> D[Dark]';
            }
            """);

        await Page.WaitForFunctionAsync("() => window.__naiadTheme === 'dark'");
        var theme = await Page.EvaluateAsync<string>("() => window.__naiadTheme");
        Assert.That(theme, Is.EqualTo("dark"));
    }

    [Test]
    public async Task Component_ShouldExportSvgAndPngBlobs()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        var exportSummary = await Page.EvaluateAsync<string>(
            """
            async () => {
              const el = document.querySelector('naiad-diagram');
              const svg = await el.toSvgBlob();
              const png = await el.toPngBlob({ scale: 1 });
              return `${svg.type}|${png.type}|${svg.size > 0}|${png.size > 0}`;
            }
            """);

        Assert.That(exportSummary, Is.EqualTo("image/svg+xml;charset=utf-8|image/png|true|true"));
    }

    [Test]
    public async Task Menu_ShouldEmitCancelableBeforeExport()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__beforeExport = null;
              window.__afterExport = null;
              const el = document.querySelector('naiad-diagram');
              el.setAttribute('show-menu', '');
              el.addEventListener('beforeexport', (e) => {
                window.__beforeExport = e.detail.format;
                e.preventDefault();
              });
              el.addEventListener('afterexport', (e) => {
                window.__afterExport = e.detail.format;
              });
              el.shadowRoot.querySelector('#action-save-svg').click();
            }
            """);

        await Page.WaitForFunctionAsync("() => window.__beforeExport === 'svg'");
        var afterExport = await Page.EvaluateAsync<string>("() => window.__afterExport ?? ''");
        Assert.That(afterExport, Is.EqualTo(""));
    }

    [Test]
    public async Task Component_ShouldEmitResized_AndRerenderWhenEnabled()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              window.__resizeCount = 0;
              window.__renderCount = 0;
              const el = document.querySelector('naiad-diagram');
              el.setAttribute('rerender-on-resize', '');
              el.addEventListener('resized', () => window.__resizeCount++);
              el.addEventListener('rendered', () => window.__renderCount++);
              el.style.display = 'block';
              el.style.width = '320px';
            }
            """);

        await Page.WaitForTimeoutAsync(50);
        await Page.EvaluateAsync("() => { document.querySelector('naiad-diagram').style.width = '520px'; }");
        await Page.WaitForFunctionAsync("() => window.__resizeCount > 0 && window.__renderCount > 0");

        var counts = await Page.EvaluateAsync<int[]>("() => [window.__resizeCount, window.__renderCount]");
        Assert.That(counts[0], Is.GreaterThan(0));
        Assert.That(counts[1], Is.GreaterThan(0));
    }

    [Test]
    public async Task Component_ShouldRespectCssCustomProperties()
    {
        await Page.GotoAsync($"{_baseUrl}/plain-web-component.html");
        await WaitForDiagramSvgAsync();

        await Page.EvaluateAsync(
            """
            () => {
              const el = document.querySelector('naiad-diagram');
              el.style.setProperty('--naiad-bg', 'rgb(1, 2, 3)');
              el.style.setProperty('--naiad-status-color', 'rgb(9, 10, 11)');
            }
            """);

        var hostBg = await Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('naiad-diagram')).backgroundColor");
        var statusColor = await GetShadowCssAsync("#status", "color");
        Assert.That(hostBg, Is.EqualTo("rgb(1, 2, 3)"));
        Assert.That(statusColor, Is.EqualTo("rgb(9, 10, 11)"));
    }

    [Test]
    public async Task IndexDemo_ShouldUpdateDiagramFromTextarea()
    {
        await Page.GotoAsync($"{_baseUrl}/index.html");
        await WaitForDiagramSvgAsync("#diagram-component");

        const string mermaid = """
            flowchart LR
              Input --> Validate
              Validate --> Persist
            """;

        await Page.FillAsync("#mermaid-input", mermaid);
        await Page.ClickAsync("#apply");

        await Page.WaitForFunctionAsync(
            """
            () => {
              const status = document.querySelector('#status');
              return status && status.textContent.includes('Rendered via <naiad-diagram>');
            }
            """);

        var svgMarkup = await GetShadowInnerHtmlAsync("#diagram", "#diagram-component");
        Assert.That(svgMarkup, Does.Contain("Validate"));
        Assert.That(svgMarkup, Does.Contain("<svg"));
    }

    [Test]
    public Task AllDiagramsPage_ShouldRenderAllDiagramsWithWebComponent() =>
        AssertAllDiagramsPageAsync($"{_baseUrl}/all-diagrams-web-component.html");

    [Test]
    public async Task CatSkinsDemo_ShouldRenderCatShapes()
    {
        await Page.GotoAsync($"{_baseUrl}/cat-skins-demo.html?skin=cats&theme=dark");
        await WaitForDiagramSvgAsync("#cat-diagram");

        var summary = await Page.EvaluateAsync<string>(
            """
            async () => {
              const el = document.querySelector('#cat-diagram');
              const packs = el ? await el.getBuiltInSkinPacks() : [];
              const paths = Array.from(
                el?.shadowRoot?.querySelectorAll('#diagram path.flow-node-shape, #diagram .node path, #diagram g.node path') ?? []);
              const dValues = paths.map((path) => path.getAttribute('d') ?? '');
              const hasCatHead = dValues.some((d) => d.includes('M10 24L22 8L35 20H65L78 8L90 24V50Q90 60 80 60H20Q10 60 10 50Z'));
              const hasCatPaw = dValues.some((d) => d.includes('M50 24A11 11 0 1 1 50 46A11 11 0 1 1 50 24Z'));
              const hasCatFish = dValues.some((d) => d.includes('M4 30L20 18H56L70 8V20L96 12V48L70 40V52L56 42H20Z'));
              return JSON.stringify({
                packs,
                hasCatHead,
                hasCatPaw,
                hasCatFish,
                nodePaths: dValues
              });
            }
            """);

        using var json = JsonDocument.Parse(summary);
        var root = json.RootElement;
        var packs = root.GetProperty("packs").EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var hasCatHead = root.GetProperty("hasCatHead").GetBoolean();
        var hasCatPaw = root.GetProperty("hasCatPaw").GetBoolean();
        var hasCatFish = root.GetProperty("hasCatFish").GetBoolean();

        Assert.That(packs, Does.Contain("cats"), $"Expected 'cats' in available packs. summary={summary}");
        Assert.That(hasCatHead && hasCatPaw && hasCatFish, Is.True, $"Expected cat skin SVG paths were not rendered. summary={summary}");
    }

    [TestCase("prism3d", "skin-rectangle-prism-grad")]
    [TestCase("neon", "skin-rectangle-neon-grad")]
    [TestCase("sunset", "skin-rectangle-sunset-grad")]
    public async Task ShowcaseSkinsDemo_ShouldRenderAdvancedSkinDefs(string skin, string expectedToken)
    {
        await Page.GotoAsync($"{_baseUrl}/showcase-skins-demo.html?skin={Uri.EscapeDataString(skin)}&theme=dark");
        await WaitForDiagramSvgAsync("#skin-diagram");

        var svgMarkup = await GetShadowInnerHtmlAsync("#diagram", "#skin-diagram");
        Assert.That(svgMarkup, Does.Contain("<svg"), "Expected rendered SVG output.");
        Assert.That(svgMarkup, Does.Contain(expectedToken), $"Expected defs token not found for skin '{skin}'.");
        Assert.That(
            svgMarkup,
            Does.Contain($"fill:url(#{expectedToken})"),
            $"Expected node layer to reference gradient '{expectedToken}' for skin '{skin}'.");
    }

    [TestCase("daisyui")]
    [TestCase("material3")]
    [TestCase("fluent2")]
    [TestCase("glass")]
    [TestCase("fluent")]
    [TestCase("material-3")]
    [TestCase("cats")]
    [TestCase("prism3d")]
    [TestCase("neon")]
    [TestCase("sunset")]
    public Task AllDiagramsPage_ShouldRenderAllDiagramsWithSkinPack(string skinPack) =>
        AssertAllDiagramsPageAsync($"{_baseUrl}/all-diagrams-web-component.html?skin={Uri.EscapeDataString(skinPack)}");

    [TestCase("light")]
    [TestCase("dark")]
    [TestCase("auto")]
    public Task AllDiagramsPage_ShouldRenderAllDiagramsForTheme(string theme) =>
        AssertAllDiagramsPageAsync($"{_baseUrl}/all-diagrams-web-component.html?theme={Uri.EscapeDataString(theme)}");

    async Task WaitForDiagramSvgAsync(string hostSelector = "naiad-diagram")
    {
        await Page.WaitForFunctionAsync(
            """
            (selector) => {
              const el = document.querySelector(selector);
              if (!el?.shadowRoot) return false;
              const hasSvg = !!el.shadowRoot.querySelector('#diagram svg');
              const status = el.shadowRoot.querySelector('#status')?.textContent?.trim();
              return hasSvg || status === 'Render failed';
            }
            """,
            hostSelector);

        var status = await GetShadowTextAsync("#status", hostSelector);
        if (status == "Render failed")
        {
            var error = await GetShadowTextAsync("#error", hostSelector);
            throw new AssertionException($"Component render failed. Error: {error}");
        }
    }

    Task<string> GetShadowTextAsync(string shadowSelector, string hostSelector = "naiad-diagram") =>
        Page.EvaluateAsync<string>(
            """
            ({ hostSelector, shadowSelector }) => {
              const host = document.querySelector(hostSelector);
              const target = host?.shadowRoot?.querySelector(shadowSelector);
              return target?.textContent?.trim() ?? '';
            }
            """,
            new { hostSelector, shadowSelector });

    Task<string> GetShadowInnerHtmlAsync(string shadowSelector, string hostSelector = "naiad-diagram") =>
        Page.EvaluateAsync<string>(
            """
            ({ hostSelector, shadowSelector }) => {
              const host = document.querySelector(hostSelector);
              const target = host?.shadowRoot?.querySelector(shadowSelector);
              return target?.innerHTML ?? '';
            }
            """,
            new { hostSelector, shadowSelector });

    Task<string> GetShadowCssAsync(string shadowSelector, string cssProperty, string hostSelector = "naiad-diagram") =>
        Page.EvaluateAsync<string>(
            """
            ({ hostSelector, shadowSelector, cssProperty }) => {
              const host = document.querySelector(hostSelector);
              const target = host?.shadowRoot?.querySelector(shadowSelector);
              return target ? getComputedStyle(target).getPropertyValue(cssProperty).trim() : '';
            }
            """,
            new { hostSelector, shadowSelector, cssProperty });

    async Task EnsureNaiadClientReadyAsync()
    {
        await Page.GotoAsync($"{_baseUrl}/client-diagnostics.html");
        await Page.WaitForFunctionAsync("() => !!window.__naiadInit");
        var initReady = await Page.EvaluateAsync<bool>(
            """
            async () => {
              try {
                await window.__naiadInit;
                return !!window.__naiadClient;
              } catch (error) {
                window.__naiadInitError = error?.message ?? String(error);
                return false;
              }
            }
            """);
        if (!initReady)
        {
            var initError = await Page.EvaluateAsync<string>("() => window.__naiadInitError ?? 'unknown init error'");
            throw new AssertionException($"Naiad client init failed: {initError}");
        }
    }

    async Task AssertAllDiagramsPageAsync(string url)
    {
        await Page.GotoAsync(url);

        await Page.WaitForFunctionAsync(
            """
            () => {
              const result = window.__naiadAllDiagramsResult;
              return !!result && result.done === true;
            }
            """);

        var summaryJson = await Page.EvaluateAsync<string>("() => JSON.stringify(window.__naiadAllDiagramsResult)");
        var summary = JsonSerializer.Deserialize<AllDiagramsRenderSummary>(
            summaryJson,
            JsonCaseInsensitiveOptions) ?? new AllDiagramsRenderSummary();

        Assert.That(summary.Total, Is.GreaterThanOrEqualTo(20), $"Unexpected diagram count. summary={summaryJson}");
        Assert.That(summary.Failures, Is.Empty, "All-diagrams page has render failures: " + summaryJson);
    }

    static string ResolvePublishWwwroot()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(
            repoRoot,
            "Naiad",
            "src",
            "Naiad.Wasm.Host",
            "bin",
            "Debug",
            "net10.0-browser",
            "wwwroot");
    }

    static void EnsurePublishedWwwroot(string wwwroot)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        var componentPath = Path.Combine(wwwroot, "naiad-web-component.js");
        var projectPath = Path.Combine(repoRoot, "Naiad", "src", "Naiad.Wasm.Host", "Naiad.Wasm.Host.csproj");

        var buildInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        buildInfo.ArgumentList.Add("build");
        buildInfo.ArgumentList.Add(projectPath);
        buildInfo.ArgumentList.Add("-c");
        buildInfo.ArgumentList.Add("Debug");
        buildInfo.ArgumentList.Add("-v");
        buildInfo.ArgumentList.Add("minimal");
        buildInfo.ArgumentList.Add("--no-restore");

        using var build = Process.Start(buildInfo) ?? throw new InvalidOperationException("Failed to start dotnet build");
        var stdOut = build.StandardOutput.ReadToEnd();
        var stdErr = build.StandardError.ReadToEnd();
        build.WaitForExit();
        if (build.ExitCode != 0)
            throw new InvalidOperationException($"dotnet build failed.\nSTDOUT:\n{stdOut}\nSTDERR:\n{stdErr}");

        NormalizeWwwrootForPlaywright(wwwroot, repoRoot);

        if (!File.Exists(componentPath))
            throw new FileNotFoundException($"Expected component script not found after normalization: {componentPath}");
    }

    static void NormalizeWwwrootForPlaywright(string wwwroot, string repoRoot)
    {
        // Playwright is currently stable with Debug framework assets in this repository.
        var debugWwwroot = Path.Combine(
            repoRoot,
            "Naiad",
            "src",
            "Naiad.Wasm.Host",
            "bin",
            "Debug",
            "net10.0-browser",
            "wwwroot");
        var debugFramework = Path.Combine(debugWwwroot, "_framework");
        var publishFramework = Path.Combine(wwwroot, "_framework");
        if (Directory.Exists(debugFramework) &&
            !Path.GetFullPath(debugFramework).Equals(Path.GetFullPath(publishFramework), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(publishFramework))
                Directory.Delete(publishFramework, recursive: true);
            CopyDirectory(debugFramework, publishFramework);
        }

        var sourceWwwroot = Path.Combine(repoRoot, "Naiad", "src", "Naiad.Wasm.Host", "wwwroot");
        foreach (var file in new[]
                 {
                     "app.css",
                     "all-diagrams-test.md",
                     "all-diagrams-web-component.html",
                     "all-diagrams-web-component.js",
                     "cat-skins-demo.html",
                     "client-diagnostics.html",
                     "index.html",
                     "main.js",
                     "naiad-client.js",
                     "naiad-web-component.js",
                     "plain-web-component.html",
                     "showcase-skins-demo.html"
                 })
        {
            var source = Path.Combine(sourceWwwroot, file);
            var target = Path.Combine(wwwroot, file);
            if (File.Exists(source))
                File.Copy(source, target, overwrite: true);
        }

        var canonicalAllDiagramsPath = Path.Combine(repoRoot, "docs", "ALL_DIAGRAMS_TEST.md");
        var targetAllDiagramsPath = Path.Combine(wwwroot, "all-diagrams-test.md");
        if (File.Exists(canonicalAllDiagramsPath))
            File.Copy(canonicalAllDiagramsPath, targetAllDiagramsPath, overwrite: true);

        var sourceTestRenders = Path.Combine(repoRoot, "Naiad", "src", "test-renders");
        var targetTestRenders = Path.Combine(wwwroot, "test-renders");
        if (Directory.Exists(sourceTestRenders))
        {
            if (Directory.Exists(targetTestRenders))
                Directory.Delete(targetTestRenders, recursive: true);
            CopyDirectory(sourceTestRenders, targetTestRenders);
        }
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var targetFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var targetDirectory = Path.Combine(destination, Path.GetFileName(directory));
            CopyDirectory(directory, targetDirectory);
        }
    }

    sealed class SkinPackRenderResult
    {
        public string Pack { get; set; } = "";
        public bool Ok { get; set; }
        public int SvgLength { get; set; }
        public string? Error { get; set; }
    }

    sealed class AllDiagramsRenderSummary
    {
        public bool Done { get; set; }
        public int Total { get; set; }
        public List<AllDiagramsRenderResult> Failures { get; set; } = [];
        public List<AllDiagramsRenderResult> Passes { get; set; } = [];
    }

    sealed class AllDiagramsRenderResult
    {
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
    }
}

# lucidVIEW-FULL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a sibling `MarkdownViewer.Full` Avalonia exe that exercises the preview StyloExtract LLM + Playwright stack against real-world web pages, so the upstream library can iterate with a tight dogfood feedback loop. Lean `MarkdownViewer` stays bit-identical, AOT-clean, and single-file.

**Architecture:** New `MarkdownViewer.Full/` sibling project file-links lean source (same pattern `MarkdownViewer.Browser` already uses for `DiagramCanvas.cs`), excluding `HtmlToMarkdownService.cs`. FULL provides its own `HtmlToMarkdownServiceFull` that uses `Mostlylucid.StyloExtract.Core`'s `ILayoutExtractor` with the SQLite template store, `PlaywrightHtmlFetcher` for SPA retries, and `LlamaSharpTextProvider` for LLM template induction. Two `#if FULL` join points in linked lean source: DI wiring in `App.axaml.cs`, status-bar slot + F2 keybind in `MainWindow.axaml.cs`.

**Tech Stack:** .NET 10, Avalonia 11.3, `Mostlylucid.StyloExtract.Core/Llm.LlamaSharp/Playwright/Templates` (preview), `LLamaSharp` + `LLamaSharp.Backend.Cpu`, `Microsoft.Playwright`, `Microsoft.Extensions.DependencyInjection`, xUnit.

## Global Constraints

These apply to every task. Pulled verbatim from the spec.

- **Lean Release behaviour stays unchanged.** No edits to `MarkdownViewer.csproj` package refs. Lean source touches are permitted ONLY when every change is runtime-neutral in Release — that means: no new code paths exercised when `FULL` is not defined, any new XAML element starts `IsVisible="False"`, any new code-behind handler stubbed to no-op in lean. Permitted lean touches enumerated by task:
  - **Task 1** — introduce `IHtmlToMarkdownService` interface; `HtmlToMarkdownService` implements it + adds `ConvertAsync` wrapper that delegates to existing sync `Convert`. Field type in `MainWindow.axaml.cs:29` changes from concrete to interface. Two call sites flip `Convert` → `await ConvertAsync`. Zero behavioural change.
  - **Task 4** — new shared helper `MarkdownViewer/Services/HtmlPreProcessor.cs` (lift `PromoteHtmxLinks` + `TagMermaidPres` out of `HtmlToMarkdownService`, call them from there). `MainWindow.axaml.cs:29` gains a `#if FULL` field-initialiser swap.
  - **Task 7** — `MainWindow.axaml` gains `IsVisible="False"` Help-menu slots (Diagnostics + 3 subitems); `MainWindow.axaml.cs` gains stub click handlers (no-op in lean) + a `#if FULL` first-run-dialog post in the constructor.
  - **Task 8** — `MainWindow.axaml` gains an `IsVisible="False"` status-bar `TextBlock` (`ExtractionStatusText`) + a stub `OnExtractionStatusClicked` handler; `MainWindow.axaml.cs` gains a `#if FULL` ctor block that flips visibility, subscribes to telemetry, and binds F2.
  - Any task that would add a non-`#if FULL` code path to lean that runs in Release is a defect — escalate, don't ship.
- **FULL is allowed to be fat and non-AOT.** `PublishSingleFile=false`, `PublishReadyToRun=false`, `PublishTrimmed=false`. LlamaSharp and Playwright packages are explicitly `IsAotCompatible=false`.
- **`RootNamespace=MarkdownViewer`** in FULL csproj so shared file-linked source resolves the same namespaces.
- **Settings & state files** live under `AppPaths.LocalState` (FULL-only helper), never colliding with lean's `MarkdownViewer/` settings folder. Per-platform: `%LOCALAPPDATA%\lucidVIEW-FULL\` (Windows), `~/Library/Application Support/lucidVIEW-FULL/` (macOS), `${XDG_STATE_HOME:-~/.local/state}/lucidview-full/` (Linux).
- **Stylobot model-bootstrap pattern** is the reference: lazy auto-download on first call, single `SemaphoreSlim`-serialised init + inference, HF identifier OR local `.gguf` path accepted in `ModelPath`. Pattern source: `stylobot/src/Mostlylucid.BotDetection.Llm.LlamaSharp/LlamaSharpProviderOptions.cs` + `LlamaSharpLlmProvider.cs`.
- **CLI verbs exit without opening the UI.** Parsed in `Program.cs` before Avalonia starts.
- **Preview package versions** are pinned at implementation time by reading `stylobot-extract/Directory.Packages.props` first (sibling repo is source of truth). Use the placeholder `<preview>` in code samples below until that lookup happens.
- **Cut a new StyloExtract alpha if the preview API doesn't fit.** The sibling `stylobot-extract` repo is under our control. If a task hits a missing public API or a wrong shape (e.g. an extension method we need to register a service, an `ExtractionResult` property we need to read), pop a new alpha from `stylobot-extract`, bump the version in FULL's csproj, and continue. Do not work around the upstream by reflecting into internals from FULL.
- **Tests for LLM/Playwright** are off CI initially. Use `[Trait("Category", "RequiresLlm")]` / `[Trait("Category", "RequiresPlaywright")]` and filter via `dotnet test --filter "Category!=RequiresLlm&Category!=RequiresPlaywright"`.
- **No release builds for FULL** until explicitly approved. Debug-only artifacts on CI.
- **UI changes verified via `Mostlylucid.Avalonia.UITesting`** when behavior changes (per project memory). The FULL csproj keeps the Debug-only `Mostlylucid.Avalonia.UITesting` reference.

---

### Task 1: Extract `IHtmlToMarkdownService` interface in lean

Lean exposes its HTML→markdown service as a sync `Convert(string, Uri?)`. FULL needs async. To swap implementations cleanly behind `#if FULL`, introduce a tiny interface in lean now (zero behavioral change) so MainWindow consumes the abstraction instead of the concrete type.

**Files:**
- Create: `MarkdownViewer/Services/IHtmlToMarkdownService.cs`
- Modify: `MarkdownViewer/Services/HtmlToMarkdownService.cs` (implement interface, add `ConvertAsync` wrapper)
- Modify: `MarkdownViewer/Views/MainWindow.axaml.cs:29` (field type `IHtmlToMarkdownService`)
- Modify: `MarkdownViewer/Views/MainWindow.FileOperations.cs:310` (use `await _htmlToMarkdownService.ConvertAsync(...)`)
- Test: `MarkdownViewer.Tests/HtmlToMarkdownServiceTests.cs` (new — guard the existing sync `Convert` keeps working through the interface)

**Interfaces:**
- Produces:
  ```csharp
  public interface IHtmlToMarkdownService
  {
      Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default);
      // LooksLikeHtml stays static on HtmlToMarkdownService for the few call sites already using it statically.
  }
  ```

- [ ] **Step 1: Write the failing test**

Create `MarkdownViewer.Tests/HtmlToMarkdownServiceTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class HtmlToMarkdownServiceTests
{
    [Fact]
    public async Task ConvertAsync_PlainHtml_RoundTripsToMarkdown()
    {
        IHtmlToMarkdownService svc = new HtmlToMarkdownService();
        var html = "<html><body><h1>Hello</h1><p>World</p></body></html>";

        var md = await svc.ConvertAsync(html, sourceUri: null, CancellationToken.None);

        Assert.Contains("Hello", md);
        Assert.Contains("World", md);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj --filter "FullyQualifiedName~HtmlToMarkdownServiceTests" -v normal`
Expected: build FAIL — `IHtmlToMarkdownService` not defined.

- [ ] **Step 3: Add the interface + async wrapper**

Create `MarkdownViewer/Services/IHtmlToMarkdownService.cs`:

```csharp
namespace MarkdownViewer.Services;

public interface IHtmlToMarkdownService
{
    Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default);
}
```

Edit `MarkdownViewer/Services/HtmlToMarkdownService.cs` — class declaration line:

```csharp
public sealed class HtmlToMarkdownService : IHtmlToMarkdownService
```

Add (anywhere inside the class, after `Convert`):

```csharp
public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    => Task.FromResult(Convert(html, sourceUri));
```

- [ ] **Step 4: Re-route MainWindow through the interface**

Edit `MarkdownViewer/Views/MainWindow.axaml.cs` line 29:

```csharp
private readonly IHtmlToMarkdownService _htmlToMarkdownService = new HtmlToMarkdownService();
```

Edit `MarkdownViewer/Views/MainWindow.FileOperations.cs` line 310 (inside `LoadWebPage`):

```csharp
StatusText.Text = "Converting page...";
content = await _htmlToMarkdownService.ConvertAsync(body, uri);
```

(The method is already `async Task`, so `await` is in scope.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj --filter "FullyQualifiedName~HtmlToMarkdownServiceTests" -v normal`
Expected: 1 passed.

- [ ] **Step 6: Verify lean still builds end-to-end**

Run: `dotnet build MarkdownViewer/MarkdownViewer.csproj -c Release`
Expected: build SUCCEEDED, no warnings introduced.

- [ ] **Step 7: Commit**

```bash
git add MarkdownViewer/Services/IHtmlToMarkdownService.cs \
        MarkdownViewer/Services/HtmlToMarkdownService.cs \
        MarkdownViewer/Views/MainWindow.axaml.cs \
        MarkdownViewer/Views/MainWindow.FileOperations.cs \
        MarkdownViewer.Tests/HtmlToMarkdownServiceTests.cs
git commit -m "refactor: extract IHtmlToMarkdownService for FULL swap"
```

---

### Task 2: Skeleton `MarkdownViewer.Full` project

A sibling exe that file-links every lean `.cs` and `.axaml` except `HtmlToMarkdownService.cs`, titled `lucidVIEW-FULL`, builds and runs identically to lean. Defines `FULL` constant. No new packages yet — proves the file-link approach works before adding preview packages.

**Files:**
- Create: `MarkdownViewer.Full/MarkdownViewer.Full.csproj`
- Create: `MarkdownViewer.Full/Program.cs`
- Create: `MarkdownViewer.Full/AppPaths.cs` (per-platform local-state dir)
- Modify: `lucid.viewer/lucid.viewer.sln` (via `dotnet sln add`)

**Interfaces:**
- Produces:
  ```csharp
  internal static class AppPaths
  {
      public static string LocalState { get; }   // creates dir on first access
      public static string ModelCacheDir { get; } // LocalState/models
      public static string TemplateStorePath { get; } // LocalState/styloextract-templates.db
      public static string SettingsFilePath { get; }  // LocalState/settings.json
  }
  ```

- [ ] **Step 1: Create the csproj**

Create `MarkdownViewer.Full/MarkdownViewer.Full.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationManifest>..\MarkdownViewer\app.manifest</ApplicationManifest>

    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>

    <AssemblyName>lucidVIEW-FULL</AssemblyName>
    <RootNamespace>MarkdownViewer</RootNamespace>
    <Version>0.1.0</Version>
    <Product>lucidVIEW-FULL</Product>
    <Description>Dogfood build of lucidVIEW with the preview StyloExtract LLM + Playwright stack</Description>
    <ApplicationIcon>..\MarkdownViewer\favicon.ico</ApplicationIcon>

    <DefineConstants>$(DefineConstants);FULL</DefineConstants>

    <PublishSingleFile>false</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <PublishReadyToRun>false</PublishReadyToRun>
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>

  <!-- File-linked lean source: every .cs except lean's Program.cs (FULL provides
       its own entry). HtmlToMarkdownService.cs stays linked in — FULL adds its own
       HtmlToMarkdownServiceFull class alongside it; MainWindow chooses via #if FULL
       (see Task 4). bin/obj excluded recursively. -->
  <ItemGroup>
    <Compile Include="..\MarkdownViewer\**\*.cs"
             Exclude="..\MarkdownViewer\bin\**;..\MarkdownViewer\obj\**;..\MarkdownViewer\Program.cs"
             Link="%(RecursiveDir)%(Filename)%(Extension)" />
    <AvaloniaXaml Include="..\MarkdownViewer\**\*.axaml"
                  Exclude="..\MarkdownViewer\bin\**;..\MarkdownViewer\obj\**"
                  Link="%(RecursiveDir)%(Filename)%(Extension)" />
    <AvaloniaResource Include="..\MarkdownViewer\Assets\*.svg"
                      Link="Assets\%(Filename)%(Extension)" />
    <AvaloniaResource Include="..\MarkdownViewer\Assets\Raleway-*.ttf"
                      Link="Assets\%(Filename)%(Extension)" />
    <Content Include="..\MarkdownViewer\Assets\manual\user-manual.md"
             Link="manual\user-manual.md"
             CopyToOutputDirectory="PreserveNewest" />
    <Content Include="..\MarkdownViewer\Assets\manual\screenshots\*.png"
             Link="manual\screenshots\%(Filename)%(Extension)"
             CopyToOutputDirectory="PreserveNewest" />
    <Content Include="..\README.md" Link="README.md" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="..\MarkdownViewer\default-settings.json"
             Link="default-settings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <!-- Same package set as lean. We add preview StyloExtract / LlamaSharp / Playwright in later tasks. -->
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
    <PackageReference Include="FluentAvaloniaUI" Version="2.5.0" />
    <PackageReference Include="FluentIcons.Avalonia.Fluent" Version="2.0.321" />
    <PackageReference Include="AnimatedImage.Avalonia" Version="2.1.4" />
    <PackageReference Include="LiveMarkdown.Avalonia" Version="1.9.2" />
    <PackageReference Include="QuestPDF" Version="2026.2.1" />
    <PackageReference Include="QuestPDF.Markdown" Version="1.47.0" />
    <PackageReference Include="SkiaSharp" Version="3.119.2" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="3.119.2" />
    <PackageReference Include="SkiaSharp.NativeAssets.macOS" Version="3.119.2" />
    <PackageReference Include="HarfBuzzSharp.NativeAssets.Linux" Version="8.3.1.3" />
    <PackageReference Include="HarfBuzzSharp.NativeAssets.macOS" Version="8.3.1.3" />
    <PackageReference Include="Tmds.DBus.Protocol" Version="0.92.0" />

    <!-- Lean's StyloExtract refs stay; we'll add the FULL preview refs in Task 4. -->
    <PackageReference Include="Mostlylucid.StyloExtract.Abstractions" Version="1.7.1" />
    <PackageReference Include="Mostlylucid.StyloExtract.Html" Version="1.7.1" />
    <PackageReference Include="Mostlylucid.StyloExtract.Heuristics" Version="1.7.1" />
    <PackageReference Include="Mostlylucid.StyloExtract.Markdown" Version="1.7.1" />

    <PackageReference Include="Avalonia.Diagnostics" Version="11.3.12" Condition="'$(Configuration)' == 'Debug'" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Naiad\src\Naiad\Naiad.csproj" />
    <ProjectReference Include="..\Mostlylucid.ImageSharp.Svg\Mostlylucid.ImageSharp.Svg.csproj" />
  </ItemGroup>

  <!-- Same Debug-only UI testing harness as lean. -->
  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <PackageReference Include="Mostlylucid.Avalonia.UITesting" Version="1.4.2" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create AppPaths helper**

Create `MarkdownViewer.Full/AppPaths.cs`:

```csharp
using System.Runtime.InteropServices;

namespace MarkdownViewer;

internal static class AppPaths
{
    private const string AppFolder = "lucidVIEW-FULL";
    private const string XdgFolder = "lucidview-full";

    public static string LocalState { get; } = EnsureDir(ResolveLocalState());
    public static string ModelCacheDir { get; } = EnsureDir(
        Environment.GetEnvironmentVariable("LUCIDVIEW_MODEL_CACHE")
        ?? Path.Combine(LocalState, "models"));
    public static string TemplateStorePath { get; } = Path.Combine(LocalState, "styloextract-templates.db");
    public static string SettingsFilePath { get; } = Path.Combine(LocalState, "settings.json");

    private static string ResolveLocalState()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolder);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", AppFolder);

        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var baseDir = string.IsNullOrEmpty(xdgState)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "state")
            : xdgState;
        return Path.Combine(baseDir, XdgFolder);
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [ ] **Step 3: Create FULL Program.cs**

Create `MarkdownViewer.Full/Program.cs`:

```csharp
using Avalonia;
using Avalonia.Data.Core.Plugins;
#if DEBUG
using Mostlylucid.Avalonia.UITesting;
#endif

namespace MarkdownViewer;

internal static class FullProgram
{
    private static readonly string CrashLogPath = Path.Combine(AppPaths.LocalState, "crash.log");

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogCrash("UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        // CLI verbs land here in later tasks (--download-model, --install-browsers,
        // --doctor). For now no verbs — start the UI.

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash("Main", ex);
            throw;
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        BindingPlugins.DataValidators.Clear();

        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

#if DEBUG
        builder = builder.UseUITesting(opts =>
        {
            opts.DefaultScreenshotDir = "ux-screenshots";
            opts.Log = Console.WriteLine;
            opts.EnableCrossWindowTracking = true;
            opts.CaptureScreenshotsByDefault = false;
        });
#endif

        return builder.AfterSetup(_ => BindingPlugins.DataValidators.Clear());
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:O}] {source}: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { }
    }
}
```

The file-linked lean `Program.cs` defines `class Program`. FULL provides its own `FullProgram` and sets `StartupObject` to disambiguate. Lean's `Program.cs` is the only file excluded from the link set (already configured in the csproj in Step 1). Add to the `<PropertyGroup>` in `MarkdownViewer.Full.csproj`:

```xml
<StartupObject>MarkdownViewer.FullProgram</StartupObject>
```

- [ ] **Step 4: Add a stub `HtmlToMarkdownServiceFull` so FULL compiles**

Lean's `HtmlToMarkdownService.cs` is link-shared into FULL unchanged. FULL only needs its own *additional* class `HtmlToMarkdownServiceFull` so the `#if FULL` swap in Task 4 has a target type. For Task 2, that swap doesn't exist yet — `MainWindow.axaml.cs:29` still constructs `new HtmlToMarkdownService()` in both lean and FULL — so FULL compiles already. We're creating the FULL-side class now as a placeholder; it remains unreferenced until Task 4 introduces the `#if FULL` field initialiser. Create `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs`:

```csharp
namespace MarkdownViewer.Services;

// Placeholder for Task 4. Delegates to lean's HtmlToMarkdownService verbatim so
// behaviour is identical until the StyloExtract Core pipeline lands.
public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    private readonly HtmlToMarkdownService _inner = new();

    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
        => _inner.ConvertAsync(html, sourceUri, ct);
}
```

No csproj edits needed in this step — `HtmlToMarkdownService.cs` is already link-shared from lean, and FULL-side `.cs` files under `MarkdownViewer.Full/` are picked up by the default SDK `Compile` glob.

- [ ] **Step 5: Build the skeleton**

Run: `dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug`
Expected: build SUCCEEDED.

If you see "duplicate Main" errors, double-check `StartupObject` is set in the FULL csproj.

- [ ] **Step 6: Run the skeleton**

Run: `dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj`
Expected: an Avalonia window opens with the same UI as lean. The window title still says "lucidVIEW" (the lean default from `MainWindow.axaml` line 8) — that's expected; Task 8 will add the FULL status-bar identification surface. The exe name on disk is `lucidVIEW-FULL` (`bin/Debug/net10.0/lucidVIEW-FULL` or `.exe` on Windows).

Close the window.

- [ ] **Step 7: Add FULL to the solution**

Run from `/Users/scottgalloway/RiderProjects/lucidview/`:

```bash
dotnet sln lucid.viewer/lucid.viewer.sln add MarkdownViewer.Full/MarkdownViewer.Full.csproj
```

Expected: "Project ... was added".

- [ ] **Step 8: Commit**

```bash
git add MarkdownViewer.Full/ lucid.viewer/lucid.viewer.sln
git commit -m "feat: scaffold MarkdownViewer.Full sibling project"
```

---

### Task 3: `MarkdownViewer.Full.Tests` skeleton

xUnit test project that references `MarkdownViewer.Full` directly (no file-link gymnastics needed for tests). One passing smoke test that exercises `HtmlToMarkdownServiceFull` against a plain-HTML fixture so any regression in the FULL pipeline shows up on CI.

**Files:**
- Create: `MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj`
- Create: `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs`
- Modify: `lucid.viewer/lucid.viewer.sln`

**Interfaces:**
- Consumes: `HtmlToMarkdownServiceFull` (Task 2 stub)
- Produces: CI-runnable test suite for FULL-side services

- [ ] **Step 1: Create the test csproj**

Create `MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>MarkdownViewer.Full.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MarkdownViewer.Full\MarkdownViewer.Full.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write a failing smoke test**

Create `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class HtmlToMarkdownServiceFullTests
{
    [Fact]
    public async Task ConvertAsync_PlainHtml_ProducesNonEmptyMarkdown()
    {
        IHtmlToMarkdownService svc = new HtmlToMarkdownServiceFull();
        var html = "<html><body><h1>Title</h1><p>Body paragraph.</p></body></html>";

        var md = await svc.ConvertAsync(html, sourceUri: null, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(md), "Markdown should not be empty.");
        Assert.Contains("Title", md);
        Assert.Contains("Body paragraph", md);
    }
}
```

- [ ] **Step 3: Run the test to confirm it passes**

Run: `dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj -v normal`
Expected: 1 passed (it passes immediately because the Task 2 stub already delegates to a working implementation — confirms wiring is correct end-to-end).

- [ ] **Step 4: Add to solution**

```bash
dotnet sln lucid.viewer/lucid.viewer.sln add MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add MarkdownViewer.Full.Tests/ lucid.viewer/lucid.viewer.sln
git commit -m "test: scaffold MarkdownViewer.Full.Tests project"
```

---

### Task 4: `HtmlToMarkdownServiceFull` using StyloExtract Core + SQLite template store

Replace the Task 2 delegating stub with a real implementation using `Mostlylucid.StyloExtract.Core`'s `ILayoutExtractor` against an on-disk SQLite template store. No Playwright, no LLM yet — this isolates the "swap in the full pipeline" change so any breakage is attributable.

**Files:**
- Modify: `MarkdownViewer.Full/MarkdownViewer.Full.csproj` (add preview packages, `Microsoft.Extensions.DependencyInjection`)
- Modify: `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs` (real impl)
- Create: `MarkdownViewer.Full/Services/FullServices.cs` (static DI bootstrap so MainWindow doesn't have to know about IServiceProvider)
- Modify: `MarkdownViewer/Views/MainWindow.axaml.cs:29` (route through `#if FULL` factory)
- Test: `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs` (extend with template-store assertion)

**Interfaces:**
- Consumes: `IHtmlToMarkdownService` (Task 1), `AppPaths.TemplateStorePath` (Task 2)
- Produces:
  ```csharp
  public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService { /* uses ILayoutExtractor */ }
  internal static class FullServices
  {
      public static IServiceProvider Provider { get; }  // lazy-built once
      public static T Get<T>() where T : notnull;
  }
  ```

- [ ] **Step 1: Look up preview package versions**

Read `/Users/scottgalloway/RiderProjects/stylobot-extract/Directory.Packages.props`. Note the version strings for:
- `Mostlylucid.StyloExtract.Core`
- `Mostlylucid.StyloExtract.Templates`

Record them in your shell:

```bash
STYLOEXTRACT_VERSION=$(grep -oP 'StyloExtract.Core" Version="\K[^"]+' \
  /Users/scottgalloway/RiderProjects/stylobot-extract/Directory.Packages.props | head -1)
echo "Using StyloExtract preview $STYLOEXTRACT_VERSION"
```

- [ ] **Step 2: Add packages to FULL csproj**

Edit `MarkdownViewer.Full/MarkdownViewer.Full.csproj`. Drop the four `1.7.1` `StyloExtract.*` package refs (they come transitively via `Core`). Add:

```xml
<PackageReference Include="Mostlylucid.StyloExtract.Core" Version="<preview>" />
<PackageReference Include="Mostlylucid.StyloExtract.Templates" Version="<preview>" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.0" />
```

Replace `<preview>` with the version captured in Step 1.

- [ ] **Step 3: Restore packages**

Run: `dotnet restore MarkdownViewer.Full/MarkdownViewer.Full.csproj`
Expected: restore SUCCEEDED. If a version conflict appears between FULL's preview StyloExtract and lean's 1.7.1, the preview wins via `Update` directives — the FULL csproj is the build root.

- [ ] **Step 4: Write the failing extended test**

Replace `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs`:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class HtmlToMarkdownServiceFullTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"lvfull-{Guid.NewGuid():N}");

    public HtmlToMarkdownServiceFullTests()
    {
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("LUCIDVIEW_STATE_DIR", _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUCIDVIEW_STATE_DIR", null);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ConvertAsync_PlainHtml_ProducesNonEmptyMarkdown()
    {
        var svc = FullServices.Get<IHtmlToMarkdownService>();
        var html = "<html><body><h1>Title</h1><p>Body paragraph.</p></body></html>";

        var md = await svc.ConvertAsync(html, new Uri("https://example.com/page"), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(md));
        Assert.Contains("Title", md);
    }

    [Fact]
    public async Task ConvertAsync_PopulatesTemplateStore()
    {
        var storePath = Path.Combine(_tempDir, "styloextract-templates.db");
        var svc = FullServices.Get<IHtmlToMarkdownService>();

        await svc.ConvertAsync(
            "<html><body><article><h1>A</h1><p>B</p></article></body></html>",
            new Uri("https://example.com/a"),
            CancellationToken.None);

        Assert.True(File.Exists(storePath), $"Template store should exist at {storePath}");
        Assert.True(new FileInfo(storePath).Length > 0, "Template store should not be empty after first extraction");
    }
}
```

Note: `LUCIDVIEW_STATE_DIR` is a test-only override. Add support in `AppPaths`.

- [ ] **Step 5: Add LUCIDVIEW_STATE_DIR override to AppPaths**

Edit `MarkdownViewer.Full/AppPaths.cs` — change `ResolveLocalState`:

```csharp
private static string ResolveLocalState()
{
    var overrideDir = Environment.GetEnvironmentVariable("LUCIDVIEW_STATE_DIR");
    if (!string.IsNullOrEmpty(overrideDir))
        return overrideDir;

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolder);
    // ... rest unchanged
```

- [ ] **Step 6: Implement FullServices DI bootstrap**

Create `MarkdownViewer.Full/Services/FullServices.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StyloExtract.Abstractions;
// Namespaces below resolved from the preview Core + Templates packages.
// If the exact namespace differs, check the package README in
// stylobot-extract/src/StyloExtract.Core/.
using StyloExtract.Core;
using StyloExtract.Templates;

namespace MarkdownViewer.Services;

internal static class FullServices
{
    private static readonly Lazy<IServiceProvider> _lazy = new(Build);

    public static IServiceProvider Provider => _lazy.Value;
    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    private static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

        services.AddStyloExtract(o =>
        {
            o.StorePath = AppPaths.TemplateStorePath;
            o.DefaultProfile = ExtractionProfile.RagFull;
        });

        services.AddSingleton<IHtmlToMarkdownService, HtmlToMarkdownServiceFull>();

        return services.BuildServiceProvider();
    }
}
```

If `AddStyloExtract`'s exact options API differs, consult `stylobot-extract/src/StyloExtract.Core/Extensions/` and adjust. The intent is: SQLite template store at `AppPaths.TemplateStorePath`, default profile `RagFull`.

- [ ] **Step 7: Replace the stub `HtmlToMarkdownServiceFull` with the real impl**

Replace `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs`:

```csharp
using StyloExtract.Abstractions;
using StyloExtract.Core;

namespace MarkdownViewer.Services;

public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    private readonly ILayoutExtractor _extractor;

    public HtmlToMarkdownServiceFull(ILayoutExtractor extractor)
    {
        _extractor = extractor;
    }

    public async Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        var pre = PreProcess(html);
        var result = await _extractor.ExtractAsync(pre, sourceUri, ct);
        return result.Markdown;
    }

    // Same pre-process steps lean's HtmlToMarkdownService runs:
    //   PromoteHtmxLinks, TagMermaidPres. Lift them out of the lean class
    //   into a static helper that lean and FULL can both call without
    //   duplicating logic.
    private static string PreProcess(string html)
        => HtmlPreProcessor.Apply(html);
}
```

Create `MarkdownViewer/Services/HtmlPreProcessor.cs` (in lean — extract the pre-process so FULL doesn't duplicate it):

```csharp
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using StyloExtract.Html;

namespace MarkdownViewer.Services;

public static class HtmlPreProcessor
{
    private static readonly IHtmlDomParser Parser = new AngleSharpHtmlDomParser();

    public static string Apply(string html)
    {
        var doc = Parser.Parse(html, null);
        PromoteHtmxLinks(doc);
        TagMermaidPres(doc);
        return doc.DocumentElement.OuterHtml;
    }

    public static void PromoteHtmxLinks(IDocument doc)
    {
        foreach (var a in doc.QuerySelectorAll("a"))
        {
            if (!string.IsNullOrEmpty(a.GetAttribute("href"))) continue;
            var url = a.GetAttribute("hx-get") ?? a.GetAttribute("hx-post");
            if (string.IsNullOrEmpty(url)) continue;
            a.SetAttribute("href", url);
        }
    }

    public static void TagMermaidPres(IDocument doc)
    {
        foreach (var pre in doc.QuerySelectorAll("pre.mermaid"))
        {
            if (pre.QuerySelector("code") is not null) continue;
            var source = pre.TextContent;
            var code = doc.CreateElement("code");
            code.SetAttribute("class", "language-mermaid");
            code.TextContent = source;
            pre.InnerHtml = string.Empty;
            pre.AppendChild(code);
        }
    }
}
```

Update lean's `HtmlToMarkdownService.cs` to call the shared helper instead of defining `PromoteHtmxLinks` / `TagMermaidPres` locally. Replace the two private methods with `HtmlPreProcessor.PromoteHtmxLinks(doc); HtmlPreProcessor.TagMermaidPres(doc);` in `Convert`.

- [ ] **Step 8: Wire MainWindow through the FULL service**

Edit `MarkdownViewer/Views/MainWindow.axaml.cs` line 29 (file is link-shared, so the same edit applies to both lean and FULL builds):

```csharp
private readonly IHtmlToMarkdownService _htmlToMarkdownService =
#if FULL
    MarkdownViewer.Services.FullServices.Get<IHtmlToMarkdownService>();
#else
    new MarkdownViewer.Services.HtmlToMarkdownService();
#endif
```

- [ ] **Step 9: Build both editions**

```bash
dotnet build MarkdownViewer/MarkdownViewer.csproj -c Release
dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug
```

Expected: both succeed. Lean has no idea FULL exists; FULL pulls preview StyloExtract.

- [ ] **Step 10: Run the tests**

```bash
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj -v normal
dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj -v normal
```

Expected: lean unchanged (1+ passed), FULL 2 passed.

- [ ] **Step 11: Manual smoke — open a real web page**

```bash
dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj
```

In the running app: Ctrl+Shift+W → enter `https://en.wikipedia.org/wiki/Avalonia_(framework)`. Expected: renders as markdown. Check `AppPaths.LocalState`:

```bash
ls -la "$HOME/Library/Application Support/lucidVIEW-FULL/"
```

Expected: `styloextract-templates.db` exists and is non-empty (SQLite store populated).

- [ ] **Step 12: Commit**

```bash
git add MarkdownViewer/Services/HtmlPreProcessor.cs \
        MarkdownViewer/Services/HtmlToMarkdownService.cs \
        MarkdownViewer/Views/MainWindow.axaml.cs \
        MarkdownViewer.Full/MarkdownViewer.Full.csproj \
        MarkdownViewer.Full/AppPaths.cs \
        MarkdownViewer.Full/Services/FullServices.cs \
        MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs \
        MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs
git commit -m "feat(full): wire StyloExtract Core + SQLite template store"
```

---

### Task 5: Playwright fetcher with auto-retry + `--install-browsers` CLI verb

When the plain `HttpClient` fetch returns empty / SPA-marker HTML / fewer than 3 extracted blocks, automatically retry via `PlaywrightHtmlFetcher`. Browser binaries auto-install on first use with status-bar progress. CLI verb `--install-browsers` for explicit pre-bootstrap.

**Files:**
- Modify: `MarkdownViewer.Full/MarkdownViewer.Full.csproj` (add preview Playwright package + `Microsoft.Playwright`)
- Modify: `MarkdownViewer.Full/Services/FullServices.cs` (register `IRenderedHtmlFetcher`)
- Create: `MarkdownViewer.Full/Services/RenderedFetchPolicy.cs` (decides when to retry)
- Modify: `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs` (use the policy)
- Modify: `MarkdownViewer.Full/Program.cs` (`--install-browsers` CLI verb)
- Test: `MarkdownViewer.Full.Tests/RenderedFetchPolicyTests.cs`

**Interfaces:**
- Consumes: `IHtmlToMarkdownService` (Task 1), `FullServices.Provider` (Task 4)
- Produces:
  ```csharp
  public interface IRenderedFetcher  // wraps PlaywrightHtmlFetcher
  {
      Task<(string Html, Uri FinalUri)> FetchAsync(Uri url, CancellationToken ct);
  }
  public static class RenderedFetchPolicy
  {
      public static bool ShouldRetry(string firstPassHtml, string firstPassMarkdown);
  }
  ```

- [ ] **Step 1: Add packages**

Edit `MarkdownViewer.Full/MarkdownViewer.Full.csproj`. Add:

```xml
<PackageReference Include="Mostlylucid.StyloExtract.Playwright" Version="<preview>" />
<PackageReference Include="Microsoft.Playwright" Version="1.49.0" />
```

Run: `dotnet restore MarkdownViewer.Full/MarkdownViewer.Full.csproj`
Expected: succeeds.

- [ ] **Step 2: Write the failing policy test**

Create `MarkdownViewer.Full.Tests/RenderedFetchPolicyTests.cs`:

```csharp
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class RenderedFetchPolicyTests
{
    [Fact]
    public void ShouldRetry_True_WhenMarkdownIsTiny()
    {
        Assert.True(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body>blank</body></html>",
            firstPassMarkdown: ""));
    }

    [Fact]
    public void ShouldRetry_True_WhenSpaMarkerPresent()
    {
        Assert.True(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body><div id=\"__next\">{}</div><script>window.__NEXT_DATA__={}</script></body></html>",
            firstPassMarkdown: "# H\n\nsome content here"));
    }

    [Fact]
    public void ShouldRetry_False_OnFullArticle()
    {
        var md = string.Concat(Enumerable.Repeat("Paragraph of content. ", 50));
        Assert.False(RenderedFetchPolicy.ShouldRetry(
            firstPassHtml: "<html><body><article>...</article></body></html>",
            firstPassMarkdown: md));
    }
}
```

- [ ] **Step 3: Implement the policy**

Create `MarkdownViewer.Full/Services/RenderedFetchPolicy.cs`:

```csharp
namespace MarkdownViewer.Services;

internal static class RenderedFetchPolicy
{
    // Minimum markdown length we consider "extraction found real content".
    // Below this we assume the first-pass extraction failed.
    private const int MinMarkdownLength = 200;

    public static bool ShouldRetry(string firstPassHtml, string firstPassMarkdown)
    {
        if (string.IsNullOrWhiteSpace(firstPassMarkdown))
            return true;
        if (firstPassMarkdown.Length < MinMarkdownLength)
            return true;
        if (SpaDetection.LooksLikeSpa(firstPassHtml))
            return true;
        return false;
    }
}
```

- [ ] **Step 4: Run policy tests**

```bash
dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj --filter "FullyQualifiedName~RenderedFetchPolicy" -v normal
```

Expected: 3 passed.

- [ ] **Step 5: Wire the Playwright fetcher into FullServices**

Edit `MarkdownViewer.Full/Services/FullServices.cs`. Add:

```csharp
using StyloExtract.Playwright;

// inside Build(), after AddStyloExtract:
services.AddSingleton<IRenderedHtmlFetcher>(_ => new PlaywrightHtmlFetcher());
```

- [ ] **Step 6: Move the LoadWebPage fetch through HtmlToMarkdownServiceFull**

The cleanest cut: add an overload `ConvertAsync` on FULL that takes a URL and does the fetch itself. But that changes the lean interface, which we don't want. Instead, expose a FULL-only helper alongside the interface implementation. Edit `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs`:

```csharp
using StyloExtract.Abstractions;
using StyloExtract.Core;
using StyloExtract.Playwright;

namespace MarkdownViewer.Services;

public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    private readonly ILayoutExtractor _extractor;
    private readonly IRenderedHtmlFetcher _renderedFetcher;

    public HtmlToMarkdownServiceFull(ILayoutExtractor extractor, IRenderedHtmlFetcher renderedFetcher)
    {
        _extractor = extractor;
        _renderedFetcher = renderedFetcher;
    }

    public async Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        var pre = HtmlPreProcessor.Apply(html);
        var result = await _extractor.ExtractAsync(pre, sourceUri, ct);
        var md = result.Markdown;

        // Auto-retry via Playwright when first-pass extraction looks empty.
        if (sourceUri is not null && RenderedFetchPolicy.ShouldRetry(html, md))
        {
            await PlaywrightInstaller.EnsureBrowsersInstalledAsync("chromium", ct);
            var rendered = await _renderedFetcher.FetchAsync(sourceUri, new RenderOptions(), ct);
            var renderedPre = HtmlPreProcessor.Apply(rendered.Html);
            var renderedResult = await _extractor.ExtractAsync(renderedPre, rendered.FinalUri, ct);
            md = renderedResult.Markdown;
        }

        return md;
    }
}
```

If `PlaywrightInstaller.EnsureBrowsersInstalledAsync` is a sync method in the preview package (`EnsureBrowsersInstalled`), wrap it in `Task.Run(() => ...)`.

- [ ] **Step 7: Add `--install-browsers` CLI verb**

Edit `MarkdownViewer.Full/Program.cs` — insert before `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`:

```csharp
if (args.Contains("--install-browsers"))
{
    Console.WriteLine("Installing Playwright Chromium...");
    StyloExtract.Playwright.PlaywrightInstaller.EnsureBrowsersInstalled("chromium");
    Console.WriteLine("Done.");
    return 0;
}
```

- [ ] **Step 8: Run the install verb**

```bash
dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj -- --install-browsers
```

Expected: prints "Installing Playwright Chromium..." then "Done." Exit code 0. Browsers land in the platform default Playwright cache (or `PLAYWRIGHT_BROWSERS_PATH` if set). No UI window appears.

- [ ] **Step 9: Manual smoke against a known SPA**

```bash
dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj
```

In the app: Ctrl+Shift+W → enter `https://nextjs.org/`. Expected: first-pass extraction returns mostly empty (it's a Next.js SPA), policy decides to retry, Playwright fetches the rendered DOM, second extraction returns real content. Status bar should show "Converting page..." briefly twice. **Verify content rendered isn't the SPA stub** — that proves the rendered path fired.

- [ ] **Step 10: Add a Playwright integration test (CI-skipped)**

Append to `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs`:

```csharp
[Fact]
[Trait("Category", "RequiresPlaywright")]
public async Task ConvertAsync_SpaPage_RetriesViaPlaywright()
{
    var svc = FullServices.Get<IHtmlToMarkdownService>();
    var spaHtml = """
        <html><head><title>SPA</title></head>
        <body><div id="__next"></div>
        <script>window.__NEXT_DATA__={};</script></body></html>
        """;
    // Force the policy to trigger — empty first pass.
    var md = await svc.ConvertAsync(spaHtml, new Uri("https://example.com/"), CancellationToken.None);
    Assert.NotNull(md);  // No assertion on content — Playwright result is host-dependent.
}
```

Run with: `dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj --filter "Category=RequiresPlaywright" -v normal`. Expected: passes locally if browsers installed.

CI command stays: `dotnet test --filter "Category!=RequiresLlm&Category!=RequiresPlaywright"`.

- [ ] **Step 11: Commit**

```bash
git add MarkdownViewer.Full/MarkdownViewer.Full.csproj \
        MarkdownViewer.Full/Program.cs \
        MarkdownViewer.Full/Services/FullServices.cs \
        MarkdownViewer.Full/Services/RenderedFetchPolicy.cs \
        MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs \
        MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs \
        MarkdownViewer.Full.Tests/RenderedFetchPolicyTests.cs
git commit -m "feat(full): Playwright fetcher with auto-retry + --install-browsers verb"
```

---

### Task 6: LlamaSharp provider with lazy auto-download + `--download-model` CLI verb

Wire `LlamaSharpTextProvider` as the `ILlmTextProvider` for the StyloExtract LLM template inducer. Stylobot pattern: lazy auto-download on first call, single `SemaphoreSlim`-serialised init + inference. Default model `Qwen/Qwen2.5-0.5B-Instruct-GGUF/qwen2.5-0.5b-instruct-q4_k_m.gguf`.

**Files:**
- Modify: `MarkdownViewer.Full/MarkdownViewer.Full.csproj` (add preview Llm.LlamaSharp + LLamaSharp + backends)
- Create: `MarkdownViewer.Full/Models/AppSettings.Full.cs`
- Modify: `MarkdownViewer.Full/Services/FullServices.cs` (register LlamaSharpTextProvider + LlmInducer)
- Modify: `MarkdownViewer.Full/Program.cs` (`--download-model` CLI verb)
- Test: `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs` (add LLM-inducer trait test)

**Interfaces:**
- Consumes: `AppPaths.ModelCacheDir` (Task 2), preview Llm.LlamaSharp APIs
- Produces:
  ```csharp
  public sealed class AppSettingsFull
  {
      public string LlmModelPath { get; set; } = "Qwen/Qwen2.5-0.5B-Instruct-GGUF/qwen2.5-0.5b-instruct-q4_k_m.gguf";
      public bool LlmEnabled { get; set; } = true;
      public bool PlaywrightEnabled { get; set; } = true;
      public int LlmContextSize { get; set; } = 512;
      public int LlmThreads { get; set; } = Environment.ProcessorCount;
      public int LlmGpuLayerCount { get; set; } = -1;
      public bool HasRunBefore { get; set; }
      public static AppSettingsFull Load();
      public void Save();
  }
  ```

- [ ] **Step 1: Add packages**

Edit `MarkdownViewer.Full/MarkdownViewer.Full.csproj`. Add:

```xml
<PackageReference Include="Mostlylucid.StyloExtract.Llm.LlamaSharp" Version="<preview>" />
<PackageReference Include="LLamaSharp" Version="0.20.0" />
<PackageReference Include="LLamaSharp.Backend.Cpu" Version="0.20.0" />
```

(Confirm latest stable LLamaSharp version on NuGet at implementation time; 0.20.0 is illustrative.)

Run: `dotnet restore MarkdownViewer.Full/MarkdownViewer.Full.csproj`
Expected: succeeds.

- [ ] **Step 2: Create AppSettings.Full**

Create `MarkdownViewer.Full/Models/AppSettings.Full.cs`:

```csharp
using System.Text.Json;

namespace MarkdownViewer.Models;

public sealed class AppSettingsFull
{
    public string LlmModelPath { get; set; } =
        "Qwen/Qwen2.5-0.5B-Instruct-GGUF/qwen2.5-0.5b-instruct-q4_k_m.gguf";
    public bool LlmEnabled { get; set; } = true;
    public bool PlaywrightEnabled { get; set; } = true;
    public int LlmContextSize { get; set; } = 512;
    public int LlmThreads { get; set; } = Environment.ProcessorCount;
    public int LlmGpuLayerCount { get; set; } = -1;
    public bool HasRunBefore { get; set; }

    public static AppSettingsFull Load()
    {
        if (!File.Exists(AppPaths.SettingsFilePath))
            return new AppSettingsFull();
        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettingsFull>(json) ?? new AppSettingsFull();
        }
        catch
        {
            return new AppSettingsFull();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsFilePath, json);
    }
}
```

- [ ] **Step 3: Register LlamaSharp provider in FullServices**

Edit `MarkdownViewer.Full/Services/FullServices.cs`. Inside `Build()`, after `services.AddSingleton<IRenderedHtmlFetcher>(...)`:

```csharp
using StyloExtract.Llm.LlamaSharp;
using MarkdownViewer.Models;

var settings = AppSettingsFull.Load();

if (settings.LlmEnabled)
{
    services.AddStyloExtractLlamaSharp(o =>
    {
        o.ModelPath = settings.LlmModelPath;
        o.ModelCacheDir = AppPaths.ModelCacheDir;
        o.ContextSize = settings.LlmContextSize;
        o.Threads = settings.LlmThreads;
        o.GpuLayerCount = settings.LlmGpuLayerCount;
    });
    services.AddStyloExtractLlmInducer(
        Path.Combine(AppPaths.LocalState, "templates"));
}
```

If `AddStyloExtractLlamaSharp` or `AddStyloExtractLlmInducer` aren't the exact extension method names in the preview package, check `stylobot-extract/src/StyloExtract.Llm.LlamaSharp/LlamaSharpServiceCollectionExtensions.cs` and `StyloExtract.Core/Extensions/` for the correct names.

- [ ] **Step 4: Add `--download-model` CLI verb**

Edit `MarkdownViewer.Full/Program.cs`. Add inside `Main`, alongside the existing `--install-browsers` block:

```csharp
if (args.Length > 0 && args[0] == "--download-model")
{
    var hfId = args.Length > 1
        ? args[1]
        : MarkdownViewer.Models.AppSettingsFull.Load().LlmModelPath;
    Console.WriteLine($"Pre-downloading model: {hfId}");
    Console.WriteLine($"Cache dir: {AppPaths.ModelCacheDir}");

    var provider = FullServices.Get<StyloExtract.Llm.LlamaSharp.LlamaSharpTextProvider>();
    await provider.EnsureLoadedAsync();
    Console.WriteLine("Model ready.");
    return 0;
}
```

`Main` signature needs to become `async Task<int>`:

```csharp
public static async Task<int> Main(string[] args)
```

If the provider type isn't directly resolvable from DI, fetch it via the `ILlmTextProvider` interface and cast. Confirm via the preview package source.

- [ ] **Step 5: Smoke the model download**

```bash
dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj -- --download-model
```

Expected: prints "Pre-downloading model: Qwen/...", downloads ~400 MB into `AppPaths.ModelCacheDir`, prints "Model ready.", exits 0. Confirm:

```bash
ls -lh "$HOME/Library/Application Support/lucidVIEW-FULL/models/"
```

Expected: a `.gguf` file present.

- [ ] **Step 6: Add LLM-inducer integration test (CI-skipped)**

Append to `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs`:

```csharp
[Fact]
[Trait("Category", "RequiresLlm")]
public async Task ConvertAsync_NovelLayout_InvokesLlmInducer()
{
    var svc = FullServices.Get<IHtmlToMarkdownService>();
    var novelHtml = """
        <html><body>
          <div class="custom-layout">
            <header><h1>Novel Site</h1></header>
            <main class="weird-wrapper"><article>
              <p>Unusual structure that the heuristic classifier hasn't seen.</p>
            </article></main>
          </div>
        </body></html>
        """;
    var md = await svc.ConvertAsync(novelHtml, new Uri("https://novel-test.invalid/"), CancellationToken.None);
    Assert.Contains("Novel Site", md);
    Assert.Contains("Unusual structure", md);
}
```

Run: `dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj --filter "Category=RequiresLlm" -v normal`.
Expected: passes if model downloaded; slow (5-30 s for cold init).

- [ ] **Step 7: Commit**

```bash
git add MarkdownViewer.Full/MarkdownViewer.Full.csproj \
        MarkdownViewer.Full/Program.cs \
        MarkdownViewer.Full/Models/AppSettings.Full.cs \
        MarkdownViewer.Full/Services/FullServices.cs \
        MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs
git commit -m "feat(full): LlamaSharp template inducer with lazy auto-download + --download-model verb"
```

---

### Task 7: `--doctor` verb, first-run bootstrap dialog, `Help → Diagnostics` UI

User-facing bootstrap. Doctor verb prints status without opening UI. First-run dialog appears once and offers to pre-fetch both model + browsers. Help menu adds a Diagnostics submenu that re-invokes the same code paths.

**Files:**
- Create: `MarkdownViewer.Full/Services/ModelBootstrap.cs` (shared between doctor/dialog/menu)
- Modify: `MarkdownViewer.Full/Program.cs` (`--doctor` verb)
- Create: `MarkdownViewer.Full/Views/FirstRunBootstrapDialog.axaml(.cs)`
- Modify: `MarkdownViewer/Views/MainWindow.axaml` (Help menu `#if FULL` block — wrapped via XAML conditional resource or by putting the FULL menu in a separate UserControl)
- Modify: `MarkdownViewer/Views/MainWindow.axaml.cs` (show dialog on first run via `#if FULL`)

**Interfaces:**
- Consumes: `AppSettingsFull.HasRunBefore` (Task 6), `LlamaSharpTextProvider.EnsureLoadedAsync` (Task 6), `PlaywrightInstaller.EnsureBrowsersInstalled` (Task 5)
- Produces:
  ```csharp
  internal static class ModelBootstrap
  {
      public static DoctorReport Doctor();
      public static Task EnsureModelAsync(IProgress<double>? progress, CancellationToken ct);
      public static Task EnsureBrowsersAsync(IProgress<string>? progress, CancellationToken ct);
  }
  public sealed record DoctorReport(
      string ModelPath, bool ModelPresent, long ModelSizeBytes,
      string BrowsersPath, bool BrowsersPresent,
      bool Ready);
  ```

- [ ] **Step 1: Write the failing doctor test**

Append to `MarkdownViewer.Full.Tests/HtmlToMarkdownServiceFullTests.cs` (or new file `ModelBootstrapTests.cs`):

```csharp
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class ModelBootstrapTests
{
    [Fact]
    public void Doctor_ReportsModelAndBrowserStatus()
    {
        var report = ModelBootstrap.Doctor();

        Assert.False(string.IsNullOrEmpty(report.ModelPath));
        Assert.False(string.IsNullOrEmpty(report.BrowsersPath));
        // Ready = ModelPresent && BrowsersPresent. Don't assert true; depends on host.
    }
}
```

Run: expects build to fail (`ModelBootstrap` undefined).

- [ ] **Step 2: Implement ModelBootstrap**

Create `MarkdownViewer.Full/Services/ModelBootstrap.cs`:

```csharp
using MarkdownViewer.Models;
using StyloExtract.Llm.LlamaSharp;
using StyloExtract.Playwright;

namespace MarkdownViewer.Services;

internal sealed record DoctorReport(
    string ModelPath,
    bool ModelPresent,
    long ModelSizeBytes,
    string BrowsersPath,
    bool BrowsersPresent,
    bool Ready);

internal static class ModelBootstrap
{
    public static DoctorReport Doctor()
    {
        var settings = AppSettingsFull.Load();
        var modelPath = ResolveModelDiskPath(settings.LlmModelPath);
        var modelInfo = File.Exists(modelPath) ? new FileInfo(modelPath) : null;

        var browsersPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH")
                          ?? PlaywrightDefaultPath();
        var browsersPresent = Directory.Exists(browsersPath)
                              && Directory.EnumerateDirectories(browsersPath, "chromium*").Any();

        return new DoctorReport(
            ModelPath: modelPath,
            ModelPresent: modelInfo is not null,
            ModelSizeBytes: modelInfo?.Length ?? 0,
            BrowsersPath: browsersPath,
            BrowsersPresent: browsersPresent,
            Ready: modelInfo is not null && browsersPresent);
    }

    public static async Task EnsureModelAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var provider = FullServices.Get<LlamaSharpTextProvider>();
        // The preview provider's EnsureLoadedAsync handles HF download + load.
        // Progress hookup depends on the preview API; pass-through if available.
        await provider.EnsureLoadedAsync(ct);
    }

    public static Task EnsureBrowsersAsync(IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Installing Chromium...");
        // PlaywrightInstaller is sync; offload.
        return Task.Run(() => PlaywrightInstaller.EnsureBrowsersInstalled("chromium"), ct);
    }

    private static string ResolveModelDiskPath(string hfIdOrLocal)
    {
        if (File.Exists(hfIdOrLocal)) return hfIdOrLocal;
        // HF-id form maps to a file inside the cache dir under a sanitized name.
        // Mirror the stylobot provider's resolution; if the preview package exposes
        // a helper, prefer it.
        var filename = hfIdOrLocal.Replace('/', '_') + ".gguf";
        return Path.Combine(AppPaths.ModelCacheDir, filename);
    }

    private static string PlaywrightDefaultPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ms-playwright");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "ms-playwright");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "ms-playwright");
    }
}
```

- [ ] **Step 3: Run the doctor test**

```bash
dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj --filter "FullyQualifiedName~ModelBootstrap" -v normal
```

Expected: 1 passed.

- [ ] **Step 4: Add `--doctor` CLI verb**

Edit `MarkdownViewer.Full/Program.cs`. Add inside `Main`:

```csharp
if (args.Contains("--doctor"))
{
    var report = ModelBootstrap.Doctor();
    Console.WriteLine($"Model path:     {report.ModelPath}");
    Console.WriteLine($"Model present:  {report.ModelPresent} ({report.ModelSizeBytes / 1024 / 1024} MB)");
    Console.WriteLine($"Browsers path:  {report.BrowsersPath}");
    Console.WriteLine($"Browsers present: {report.BrowsersPresent}");
    Console.WriteLine();
    Console.WriteLine(report.Ready
        ? "Ready to extract."
        : "Not ready — run --download-model and/or --install-browsers.");
    return report.Ready ? 0 : 1;
}
```

Run `dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj -- --doctor`. Expected: prints the report; exit code reflects readiness.

- [ ] **Step 5: Build the first-run dialog (XAML)**

Create `MarkdownViewer.Full/Views/FirstRunBootstrapDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MarkdownViewer.Views.FirstRunBootstrapDialog"
        Title="lucidVIEW-FULL — first run"
        Width="520" Height="320"
        WindowStartupLocation="CenterOwner"
        CanResize="False">
  <StackPanel Margin="20" Spacing="14">
    <TextBlock FontSize="16" FontWeight="Bold" Text="One-time setup" />
    <TextBlock TextWrapping="Wrap">
      lucidVIEW-FULL uses an embedded language model and a headless browser to
      exercise the preview StyloExtract stack on real-world pages.
    </TextBlock>
    <StackPanel Spacing="6">
      <TextBlock Text="• Language model: ~400 MB, downloaded from Hugging Face." />
      <TextBlock Text="• Playwright Chromium: ~150 MB, installed via Microsoft's installer." />
      <TextBlock Text="Both are independently skippable. Without them, FULL falls back to lean behaviour." />
    </StackPanel>
    <TextBlock Name="StatusText" Foreground="Gray" Text="" />
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button Name="SkipBtn" Content="Skip (heuristic only)" Click="OnSkip" />
      <Button Name="DeferBtn" Content="Defer (fetch on first use)" Click="OnDefer" />
      <Button Name="DownloadBtn" Content="Download both" Classes="accent" Click="OnDownload" />
    </StackPanel>
  </StackPanel>
</Window>
```

Create `MarkdownViewer.Full/Views/FirstRunBootstrapDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MarkdownViewer.Models;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

public partial class FirstRunBootstrapDialog : Window
{
    public FirstRunBootstrapDialog() { AvaloniaXamlLoader.Load(this); }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        MarkAsRun();
        Close();
    }

    private void OnDefer(object? sender, RoutedEventArgs e)
    {
        MarkAsRun();
        Close();
    }

    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        DownloadBtn.IsEnabled = false;
        SkipBtn.IsEnabled = false;
        DeferBtn.IsEnabled = false;
        try
        {
            StatusText.Text = "Installing Chromium…";
            await ModelBootstrap.EnsureBrowsersAsync(
                new Progress<string>(s => Dispatcher.UIThread.Post(() => StatusText.Text = s)),
                CancellationToken.None);

            StatusText.Text = "Downloading model (~400 MB)…";
            await ModelBootstrap.EnsureModelAsync(progress: null, CancellationToken.None);

            StatusText.Text = "Ready.";
            MarkAsRun();
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
            DownloadBtn.IsEnabled = true;
            SkipBtn.IsEnabled = true;
            DeferBtn.IsEnabled = true;
        }
    }

    private static void MarkAsRun()
    {
        var settings = AppSettingsFull.Load();
        settings.HasRunBefore = true;
        settings.Save();
    }
}
```

- [ ] **Step 6: Show dialog on first run from MainWindow**

This needs a `#if FULL` join point. Edit `MarkdownViewer/Views/MainWindow.axaml.cs` — locate the constructor or `OnAttachedToVisualTree` (whichever runs once after the window is up). Add:

```csharp
#if FULL
    Dispatcher.UIThread.Post(async () =>
    {
        var settings = MarkdownViewer.Models.AppSettingsFull.Load();
        if (!settings.HasRunBefore)
            await new MarkdownViewer.Views.FirstRunBootstrapDialog().ShowDialog(this);
    }, DispatcherPriority.Background);
#endif
```

Place it near the end of the MainWindow constructor, after the existing init runs.

- [ ] **Step 7: Run the FULL app from a fresh state**

```bash
rm -rf "$HOME/Library/Application Support/lucidVIEW-FULL"
dotnet run --project MarkdownViewer.Full/MarkdownViewer.Full.csproj
```

Expected: window opens, then within ~1s the bootstrap dialog appears over it. Click "Skip" to dismiss. Re-run — dialog does **not** reappear (HasRunBefore now true).

- [ ] **Step 8: Add Help menu items (Diagnostics → Re-download model / Reinstall browsers / Show doctor report)**

Locate the existing Help menu in `MarkdownViewer/Views/MainWindow.axaml`. Add a `#if FULL`-equivalent. XAML doesn't support `#if`, so put a named slot in lean XAML and populate it from code-behind under `#if FULL`. Add to `MainWindow.axaml`:

```xml
<MenuItem Header="Help">
  <!-- existing items -->
  <Separator Name="FullDiagnosticsSeparator" IsVisible="False" />
  <MenuItem Name="FullDiagnosticsMenu" Header="Diagnostics" IsVisible="False">
    <MenuItem Name="ReDownloadModelMenuItem" Header="Re-download model" Click="OnReDownloadModel" />
    <MenuItem Name="ReinstallBrowsersMenuItem" Header="Reinstall Playwright browsers" Click="OnReinstallBrowsers" />
    <MenuItem Name="ShowDoctorMenuItem" Header="Show doctor report" Click="OnShowDoctor" />
  </MenuItem>
</MenuItem>
```

In `MainWindow.axaml.cs` constructor, after init:

```csharp
#if FULL
    FullDiagnosticsSeparator.IsVisible = true;
    FullDiagnosticsMenu.IsVisible = true;
#endif
```

Add the click handlers, gated:

```csharp
#if FULL
private async void OnReDownloadModel(object? sender, RoutedEventArgs e)
{
    StatusText.Text = "Downloading model…";
    await MarkdownViewer.Services.ModelBootstrap.EnsureModelAsync(null, CancellationToken.None);
    StatusText.Text = "Model ready.";
}

private async void OnReinstallBrowsers(object? sender, RoutedEventArgs e)
{
    StatusText.Text = "Installing browsers…";
    await MarkdownViewer.Services.ModelBootstrap.EnsureBrowsersAsync(null, CancellationToken.None);
    StatusText.Text = "Browsers ready.";
}

private async void OnShowDoctor(object? sender, RoutedEventArgs e)
{
    var report = MarkdownViewer.Services.ModelBootstrap.Doctor();
    var content = $"""
        Model: {report.ModelPath}
        Present: {report.ModelPresent} ({report.ModelSizeBytes / 1024 / 1024} MB)

        Browsers: {report.BrowsersPath}
        Present: {report.BrowsersPresent}

        Ready: {report.Ready}
        """;
    await new MarkdownViewer.Views.InputDialog
        { Title = "lucidVIEW-FULL — doctor", Description = content }
        .ShowDialog(this);
}
#else
private void OnReDownloadModel(object? sender, RoutedEventArgs e) { }
private void OnReinstallBrowsers(object? sender, RoutedEventArgs e) { }
private void OnShowDoctor(object? sender, RoutedEventArgs e) { }
#endif
```

The lean stubs exist so XAML's `Click="..."` references resolve in lean builds (the menu items are `IsVisible=False` in lean and never fire).

- [ ] **Step 9: Build both editions to confirm the menu stubs don't break lean**

```bash
dotnet build MarkdownViewer/MarkdownViewer.csproj -c Release
dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug
```

Expected: both succeed.

- [ ] **Step 10: Smoke the Help → Diagnostics menu in FULL**

Run FULL, click `Help → Diagnostics → Show doctor report`. Expected: a dialog appears with the report. Re-download / Reinstall should each update the status bar then settle.

- [ ] **Step 11: Commit**

```bash
git add MarkdownViewer.Full/Services/ModelBootstrap.cs \
        MarkdownViewer.Full/Program.cs \
        MarkdownViewer.Full/Views/FirstRunBootstrapDialog.axaml \
        MarkdownViewer.Full/Views/FirstRunBootstrapDialog.axaml.cs \
        MarkdownViewer/Views/MainWindow.axaml \
        MarkdownViewer/Views/MainWindow.axaml.cs \
        MarkdownViewer.Full.Tests/ModelBootstrapTests.cs
git commit -m "feat(full): --doctor verb, first-run dialog, Help → Diagnostics menu"
```

---

### Task 8: Extraction telemetry surface (status bar + F2 details panel + NDJSON export)

Make the dogfood signal visible: every extraction's match status, template, fetcher, durations, and block count surfaces in a status-bar line and a details panel. F2 opens the panel. NDJSON export for handing data to the StyloExtract repo.

**Files:**
- Create: `MarkdownViewer.Full/Services/ExtractionTelemetry.cs`
- Modify: `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs` (record telemetry per call)
- Create: `MarkdownViewer.Full/Views/ExtractionDetailsPanel.axaml(.cs)`
- Modify: `MarkdownViewer/Views/MainWindow.axaml` (status-bar slot + F2 keybind, IsVisible=False outside FULL)
- Modify: `MarkdownViewer/Views/MainWindow.axaml.cs` (wire status-bar updates + F2 handler under `#if FULL`)
- Test: `MarkdownViewer.Full.Tests/ExtractionTelemetryTests.cs`

**Interfaces:**
- Consumes: `HtmlToMarkdownServiceFull` (Task 4), `IRenderedFetcher` (Task 5), `ILlmTextProvider` (Task 6)
- Produces:
  ```csharp
  public sealed record LastExtractionInfo(
      DateTime When, Uri? Source, string MatchStatus, Guid TemplateId,
      int TemplateVersion, string Fetcher, TimeSpan FetchDuration,
      bool LlmInductionFired, TimeSpan LlmDuration, int BlockCount,
      int OutputCharacterCount);

  public sealed class ExtractionTelemetry
  {
      public LastExtractionInfo? Last { get; }
      public IReadOnlyList<LastExtractionInfo> Recent { get; }  // last 50, circular
      public event Action<LastExtractionInfo>? Recorded;
      public void Record(LastExtractionInfo info);
      public string ExportNdjson();
      public void Clear();
  }
  ```

- [ ] **Step 1: Write the failing telemetry test**

Create `MarkdownViewer.Full.Tests/ExtractionTelemetryTests.cs`:

```csharp
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Full.Tests;

public class ExtractionTelemetryTests
{
    [Fact]
    public void Record_StoresLast()
    {
        var t = new ExtractionTelemetry();
        var info = new LastExtractionInfo(
            DateTime.UtcNow, new Uri("https://example.com/"), "Novel",
            Guid.NewGuid(), 1, "Http", TimeSpan.FromMilliseconds(120),
            LlmInductionFired: false, TimeSpan.Zero, BlockCount: 12, OutputCharacterCount: 800);

        t.Record(info);

        Assert.NotNull(t.Last);
        Assert.Equal(info, t.Last);
        Assert.Single(t.Recent);
    }

    [Fact]
    public void Record_CircularBufferCapsAt50()
    {
        var t = new ExtractionTelemetry();
        for (var i = 0; i < 60; i++)
            t.Record(Stub(i));
        Assert.Equal(50, t.Recent.Count);
        Assert.Equal(59, ((LastExtractionInfo)t.Last!).BlockCount);
    }

    [Fact]
    public void ExportNdjson_ProducesOneLinePerRecord()
    {
        var t = new ExtractionTelemetry();
        t.Record(Stub(1));
        t.Record(Stub(2));

        var ndjson = t.ExportNdjson();
        var lines = ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("{", l));
    }

    private static LastExtractionInfo Stub(int i) => new(
        DateTime.UtcNow, new Uri($"https://e/{i}"), "FastPathHit",
        Guid.NewGuid(), 1, "Http", TimeSpan.FromMilliseconds(10),
        false, TimeSpan.Zero, i, i * 100);
}
```

Run: build FAIL — `ExtractionTelemetry` undefined.

- [ ] **Step 2: Implement ExtractionTelemetry**

Create `MarkdownViewer.Full/Services/ExtractionTelemetry.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace MarkdownViewer.Services;

public sealed record LastExtractionInfo(
    DateTime When,
    Uri? Source,
    string MatchStatus,
    Guid TemplateId,
    int TemplateVersion,
    string Fetcher,
    TimeSpan FetchDuration,
    bool LlmInductionFired,
    TimeSpan LlmDuration,
    int BlockCount,
    int OutputCharacterCount);

public sealed class ExtractionTelemetry
{
    private const int Capacity = 50;
    private readonly object _lock = new();
    private readonly LinkedList<LastExtractionInfo> _ring = new();

    public LastExtractionInfo? Last
    {
        get { lock (_lock) return _ring.Last?.Value; }
    }

    public IReadOnlyList<LastExtractionInfo> Recent
    {
        get { lock (_lock) return _ring.ToList(); }
    }

    public event Action<LastExtractionInfo>? Recorded;

    public void Record(LastExtractionInfo info)
    {
        lock (_lock)
        {
            _ring.AddLast(info);
            while (_ring.Count > Capacity)
                _ring.RemoveFirst();
        }
        Recorded?.Invoke(info);
    }

    public void Clear()
    {
        lock (_lock) _ring.Clear();
    }

    public string ExportNdjson()
    {
        var sb = new StringBuilder();
        foreach (var info in Recent)
            sb.AppendLine(JsonSerializer.Serialize(info));
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Run telemetry tests**

```bash
dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj --filter "FullyQualifiedName~ExtractionTelemetry" -v normal
```

Expected: 3 passed.

- [ ] **Step 4: Record telemetry from HtmlToMarkdownServiceFull**

Edit `MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs`. Add `ExtractionTelemetry` constructor dep, record per call:

```csharp
public sealed class HtmlToMarkdownServiceFull : IHtmlToMarkdownService
{
    private readonly ILayoutExtractor _extractor;
    private readonly IRenderedHtmlFetcher _renderedFetcher;
    private readonly ExtractionTelemetry _telemetry;

    public HtmlToMarkdownServiceFull(
        ILayoutExtractor extractor,
        IRenderedHtmlFetcher renderedFetcher,
        ExtractionTelemetry telemetry)
    {
        _extractor = extractor;
        _renderedFetcher = renderedFetcher;
        _telemetry = telemetry;
    }

    public async Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fetcher = "Http";
        var llmDuration = TimeSpan.Zero;
        var llmFired = false;

        var pre = HtmlPreProcessor.Apply(html);
        var result = await _extractor.ExtractAsync(pre, sourceUri, ct);
        var md = result.Markdown;

        if (sourceUri is not null && RenderedFetchPolicy.ShouldRetry(html, md))
        {
            await PlaywrightInstaller.EnsureBrowsersInstalledAsync("chromium", ct);
            var rsw = System.Diagnostics.Stopwatch.StartNew();
            var rendered = await _renderedFetcher.FetchAsync(sourceUri, new RenderOptions(), ct);
            var renderedPre = HtmlPreProcessor.Apply(rendered.Html);
            var lsw = System.Diagnostics.Stopwatch.StartNew();
            var renderedResult = await _extractor.ExtractAsync(renderedPre, rendered.FinalUri, ct);
            llmDuration = lsw.Elapsed;
            llmFired = renderedResult.LlmInductionFired;
            md = renderedResult.Markdown;
            fetcher = "Playwright";
            result = renderedResult;
        }

        sw.Stop();
        _telemetry.Record(new LastExtractionInfo(
            When: DateTime.UtcNow,
            Source: sourceUri,
            MatchStatus: result.Match?.Status.ToString() ?? "Unknown",
            TemplateId: result.Match?.TemplateId ?? Guid.Empty,
            TemplateVersion: result.Match?.TemplateVersion ?? 0,
            Fetcher: fetcher,
            FetchDuration: sw.Elapsed,
            LlmInductionFired: llmFired,
            LlmDuration: llmDuration,
            BlockCount: result.BlockCount,  // confirm property name from preview API
            OutputCharacterCount: md.Length));

        return md;
    }
}
```

Read `LlmInductionFired` directly off `ExtractionResult` as a typed property. If the preview API doesn't expose it as a public typed property, **cut a new alpha** of `Mostlylucid.StyloExtract.Core` that does (per Global Constraints — don't reflect into internals from FULL). The lambda above is illustrative; the real call is:

```csharp
llmFired = renderedResult.LlmInductionFired;
```

Update `FullServices.Build()` — add `services.AddSingleton<ExtractionTelemetry>();`.

- [ ] **Step 5: Add the status-bar slot in MainWindow.axaml**

Locate the status bar in `MarkdownViewer/Views/MainWindow.axaml` (search for `StatusText`). Add adjacent to it:

```xml
<TextBlock Name="ExtractionStatusText"
           IsVisible="False"
           Margin="12,0,0,0"
           VerticalAlignment="Center"
           Foreground="{DynamicResource ThemeAccentBrush}"
           Cursor="Hand"
           PointerPressed="OnExtractionStatusClicked" />
```

In `MainWindow.axaml.cs`, in the constructor:

```csharp
#if FULL
    ExtractionStatusText.IsVisible = true;
    var telemetry = MarkdownViewer.Services.FullServices.Get<MarkdownViewer.Services.ExtractionTelemetry>();
    telemetry.Recorded += info => Dispatcher.UIThread.Post(() =>
        ExtractionStatusText.Text = Format(info));
    KeyDown += (s, e) =>
    {
        if (e.Key == Avalonia.Input.Key.F2) ShowExtractionDetails();
    };
#endif
```

```csharp
#if FULL
private static string Format(MarkdownViewer.Services.LastExtractionInfo info)
{
    var host = info.Source?.Host ?? "(local)";
    var llmPart = info.LlmInductionFired ? $" · LLM {info.LlmDuration.TotalMilliseconds:F0} ms" : "";
    return $"{host} {info.MatchStatus} v{info.TemplateVersion} · {info.Fetcher} · {info.FetchDuration.TotalMilliseconds:F0} ms · {info.BlockCount} blocks{llmPart}";
}

private void OnExtractionStatusClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    => ShowExtractionDetails();

private void ShowExtractionDetails()
    => _ = new MarkdownViewer.Views.ExtractionDetailsPanel().ShowDialog(this);
#else
private void OnExtractionStatusClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e) { }
#endif
```

- [ ] **Step 6: Build the details panel**

Create `MarkdownViewer.Full/Views/ExtractionDetailsPanel.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MarkdownViewer.Views.ExtractionDetailsPanel"
        Title="Extraction Details"
        Width="900" Height="640"
        WindowStartupLocation="CenterOwner">
  <DockPanel Margin="12">
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,8">
      <Button Name="ExportBtn" Content="Export NDJSON…" Click="OnExport" />
      <Button Name="ClearBtn" Content="Clear" Click="OnClear" />
    </StackPanel>
    <TextBlock DockPanel.Dock="Top" Name="LastDetail" FontFamily="Consolas, Menlo, monospace"
               TextWrapping="Wrap" Margin="0,0,0,8" />
    <DataGrid Name="HistoryGrid" AutoGenerateColumns="False" IsReadOnly="True">
      <DataGrid.Columns>
        <DataGridTextColumn Header="When" Binding="{Binding When}" Width="180" />
        <DataGridTextColumn Header="Host" Binding="{Binding Source.Host}" Width="180" />
        <DataGridTextColumn Header="Status" Binding="{Binding MatchStatus}" Width="120" />
        <DataGridTextColumn Header="Fetcher" Binding="{Binding Fetcher}" Width="100" />
        <DataGridTextColumn Header="LLM ms" Binding="{Binding LlmDuration.TotalMilliseconds}" Width="80" />
        <DataGridTextColumn Header="Blocks" Binding="{Binding BlockCount}" Width="80" />
      </DataGrid.Columns>
    </DataGrid>
  </DockPanel>
</Window>
```

Create `MarkdownViewer.Full/Views/ExtractionDetailsPanel.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

public partial class ExtractionDetailsPanel : Window
{
    private readonly ExtractionTelemetry _telemetry = FullServices.Get<ExtractionTelemetry>();

    public ExtractionDetailsPanel()
    {
        AvaloniaXamlLoader.Load(this);
        Populate();
    }

    private void Populate()
    {
        LastDetail.Text = _telemetry.Last is { } info
            ? $"""
                Source: {info.Source}
                Match: {info.MatchStatus} (template {info.TemplateId} v{info.TemplateVersion})
                Fetcher: {info.Fetcher} · {info.FetchDuration.TotalMilliseconds:F0} ms
                LLM induction: {info.LlmInductionFired} ({info.LlmDuration.TotalMilliseconds:F0} ms)
                Blocks: {info.BlockCount} · Output: {info.OutputCharacterCount} chars
                """
            : "(no extractions yet)";
        HistoryGrid.ItemsSource = _telemetry.Recent.Reverse().ToList();
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export extraction telemetry",
            DefaultExtension = "ndjson",
            SuggestedFileName = $"extractions-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ndjson",
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_telemetry.ExportNdjson());
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        _telemetry.Clear();
        Populate();
    }
}
```

- [ ] **Step 7: Build both editions**

```bash
dotnet build MarkdownViewer/MarkdownViewer.csproj -c Release
dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug
```

Expected: both succeed.

- [ ] **Step 8: Manual smoke**

Run FULL. Open a few web pages (Wikipedia, a docs site, an SPA). Each time:
- Status bar shows the extraction line (host, status, fetcher, durations).
- Press F2 — panel opens with the last extraction detail + history grid.
- Click "Export NDJSON…" — pick a path, verify the file contains one JSON object per line.

- [ ] **Step 9: Commit**

```bash
git add MarkdownViewer.Full/Services/ExtractionTelemetry.cs \
        MarkdownViewer.Full/Services/HtmlToMarkdownServiceFull.cs \
        MarkdownViewer.Full/Services/FullServices.cs \
        MarkdownViewer.Full/Views/ExtractionDetailsPanel.axaml \
        MarkdownViewer.Full/Views/ExtractionDetailsPanel.axaml.cs \
        MarkdownViewer/Views/MainWindow.axaml \
        MarkdownViewer/Views/MainWindow.axaml.cs \
        MarkdownViewer.Full.Tests/ExtractionTelemetryTests.cs
git commit -m "feat(full): extraction telemetry, status bar, F2 details panel, NDJSON export"
```

---

### Task 9: `publish.ps1 -Edition` + CI matrix

Add an `-Edition Lean|Full|All` flag to the existing publish script (lean default stays unchanged) and extend `.github/workflows/ci.yml` to build + smoke-test FULL on Windows/Ubuntu/macOS Debug-only.

**Files:**
- Modify: `publish.ps1` (add `-Edition` param + FULL publish block)
- Modify: `.github/workflows/ci.yml` (add FULL build job)

**Interfaces:**
- Consumes: FULL csproj (Task 2)
- Produces: per-RID `publish/full/<rid>/` directories containing `lucidVIEW-FULL` + Playwright browsers

- [ ] **Step 1: Add `-Edition` param + dispatch**

Edit `publish.ps1`. Change the `param` block:

```powershell
param(
    [ValidateSet('all', 'win', 'linux', 'osx', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [ValidateSet('Lean', 'Full', 'All')]
    [string]$Edition = 'Lean',
    [switch]$Clean
)
```

After the existing per-RID lean loop, before any `exit`, add:

```powershell
function Publish-Full {
    param([string]$Rid)
    $fullProject = Join-Path $PSScriptRoot 'MarkdownViewer.Full/MarkdownViewer.Full.csproj'
    $fullOutput  = Join-Path $outputBase "full/$Rid"
    Write-Host "Publishing FULL for $Rid -> $fullOutput"

    $fullArgs = @(
        '--configuration', 'Release'
        '--runtime', $Rid
        '--self-contained', 'true'
        '-p:PublishSingleFile=false'
        '-p:PublishReadyToRun=false'
        '-p:PublishTrimmed=false'
        '--output', $fullOutput
    )

    dotnet publish $fullProject @fullArgs
    if ($LASTEXITCODE -ne 0) { throw "FULL publish failed for $Rid" }

    # Bake Playwright browsers into the published output so the bundle is
    # self-contained. PowerShell-side env override:
    $env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $fullOutput '.playwright'
    $exe = if ($Rid -like 'win-*') { 'lucidVIEW-FULL.exe' } else { 'lucidVIEW-FULL' }
    & (Join-Path $fullOutput $exe) '--install-browsers'
    Remove-Item Env:PLAYWRIGHT_BROWSERS_PATH
}

if ($Edition -in @('Full', 'All')) {
    foreach ($key in $runtimes.Keys) {
        if ($Platform -ne 'all' -and $Platform -notlike "$key*") { continue }
        Publish-Full -Rid $runtimes[$key]
    }
}

if ($Edition -eq 'Full') { exit 0 }
```

Wrap the existing lean per-RID loop in an `if ($Edition -in @('Lean', 'All')) { ... }` guard so `-Edition Full` skips it.

- [ ] **Step 2: Smoke the publish locally**

```bash
pwsh ./publish.ps1 -Platform osx-arm64 -Edition Full
```

Expected: `publish/full/osx-arm64/` exists with `lucidVIEW-FULL` plus dependencies plus a `.playwright/` subdirectory.

- [ ] **Step 3: Verify the published exe runs**

```bash
publish/full/osx-arm64/lucidVIEW-FULL --doctor
```

Expected: prints a doctor report. Browsers should report present (we just installed them). Model probably absent unless previously downloaded.

- [ ] **Step 4: Extend CI matrix**

Edit `.github/workflows/ci.yml`. Locate the existing matrix job for lean. Add a sibling job (don't merge into the lean matrix — FULL has different filters):

```yaml
  build-full:
    name: Build FULL (${{ matrix.os }})
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Build FULL
        run: dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj -c Debug
      - name: Test FULL (skip heavy)
        run: |
          dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj \
            -c Debug \
            --filter "Category!=RequiresLlm&Category!=RequiresPlaywright"
```

- [ ] **Step 5: Commit**

```bash
git add publish.ps1 .github/workflows/ci.yml
git commit -m "build(full): publish.ps1 -Edition flag + CI build matrix"
```

---

## Self-Review

**Spec coverage check** — every spec section maps to a task:

| Spec section | Task(s) |
|---|---|
| Project layout (sibling project, file-link) | 2 |
| #if FULL join points | 4 (DI in MainWindow ctor field), 7 (Help menu + first-run), 8 (status bar + F2) |
| AppPaths per-platform | 2 |
| AppSettings.Full | 6 |
| First-run dialog | 7 |
| CLI verbs (`--download-model`, `--install-browsers`, `--doctor`) | 5 (`--install-browsers`), 6 (`--download-model`), 7 (`--doctor`) |
| Lazy auto-download | 6 |
| HtmlToMarkdownServiceFull | 4 (core), 5 (Playwright), 6 (LLM init via DI), 8 (telemetry) |
| Fetch path with Playwright auto-retry | 5 |
| Extract path with StyloExtract Core + LLM inducer | 4 (core), 6 (LLM) |
| Template store | 4 |
| Telemetry surface (LastExtractionInfo, status bar, F2 panel, NDJSON) | 8 |
| publish.ps1 -Edition | 9 |
| CI matrix | 9 |
| Tests (with `RequiresLlm` / `RequiresPlaywright` traits) | 3, 4, 5, 6, 7, 8 |
| Interface extraction (prep) | 1 |

**Placeholder scan** — searched for "TBD", "TODO", "implement later", "add appropriate error handling", "write tests for the above". The only intentional placeholder is `<preview>` for StyloExtract preview package versions, handled in Task 4 Step 1.

**Type consistency** —
- `IHtmlToMarkdownService.ConvertAsync(string, Uri?, CancellationToken)` defined Task 1, consumed Tasks 2–4 ✓
- `AppPaths.{LocalState, ModelCacheDir, TemplateStorePath, SettingsFilePath}` defined Task 2, consumed Tasks 4–8 ✓
- `FullServices.Get<T>()` defined Task 4, consumed Tasks 5–8 ✓
- `HtmlToMarkdownServiceFull(ILayoutExtractor, IRenderedHtmlFetcher, ExtractionTelemetry)` final signature Task 8; constructed via DI throughout — implementer must add deps in DI registration when each is introduced (Task 4: extractor; Task 5: fetcher; Task 8: telemetry) ✓
- `ModelBootstrap.{Doctor, EnsureModelAsync, EnsureBrowsersAsync}` defined Task 7, consumed Task 7 ✓
- `ExtractionTelemetry`, `LastExtractionInfo` defined Task 8, consumed Task 8 ✓
- `RenderedFetchPolicy.ShouldRetry(string, string)` defined Task 5, consumed Task 5 ✓

**Scope check** — focused on a single deliverable (sibling FULL exe). No decomposition needed.

**Ambiguity check** — the preview StyloExtract API surface (exact method/property names for `ILayoutExtractor.ExtractAsync`, `ExtractionResult.Match`, `AddStyloExtract` options) is the largest ambiguity. Tasks 4, 6, 8 explicitly say "confirm via `stylobot-extract/src/...`" at the point each is consumed.

No issues to fix inline beyond what's already called out.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-25-lucidview-full.md`. Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch with checkpoints for review.

Which approach?

# lucidREADER Core Implementation Plan (Plan 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract lucidVIEW's reusable services into shared libraries, then build `LucidReader.Core`: a headless, fully tested RSS/Atom feed engine that subscribes, fetches, parses, stores, downloads articles for offline reading, and prunes old content.

**Architecture:** Two new shared libraries carved out of `MarkdownViewer` so both apps consume them. `LucidReader.Core` has no UI dependency: SQLite storage behind `Mostlylucid.Ephemeral.Sqlite.SingleWriter`, two `EphemeralWorkCoordinator` instances for feed refresh and offline download, and a feed parser tested against a corpus of real-world feed fixtures.

**Tech Stack:** .NET 10, C#, xunit 2.9.3, Microsoft.Data.Sqlite, Mostlylucid.Ephemeral 3.0.0, AngleSharp, System.ServiceModel.Syndication.

**Spec:** `docs/superpowers/specs/2026-08-28-lucidreader-design.md`

**Scope:** This plan covers spec build-order items 1 to 5. The Avalonia app is Plan 2; packaging and the FULL build are Plan 3.

## Global Constraints

- Target framework `net10.0`. Nullable enabled, implicit usings enabled.
- `LucidReader.Core` must have **no** Avalonia dependency. It is a plain class library.
- Mostlylucid.Ephemeral packages are version **3.0.0** from NuGet, not the local checkout in `../mostlylucid.atoms` (which is on 2.9.0).
- lucidVIEW's existing test suites must pass unchanged after Tasks 1 and 2. That is the acceptance criterion for both refactors.
- The shared libraries must not acquire dependencies that break lucidVIEW lean's small, AOT-capable publish profile. `LucidReader.Core` is exempt: lucidREADER publishes R2R, not AOT.
- No test touches the network. HTTP goes through an injected `HttpMessageHandler` stub.
- All time-dependent code takes `TimeProvider` by constructor injection so tests use `FakeTimeProvider`, never `Task.Delay`.
- All database writes go through `SqliteSingleWriter`. Reads use `QueryAsync`.
- `EphemeralOptions.MaxTrackedOperations` is the bounded channel capacity and defaults to 200. `EnqueueAsync` blocks when the channel is full. Any coordinator that can receive a burst larger than 200 must raise it explicitly.
- Prose in code comments and user-facing strings: no emdashes.

---

## File Structure

**New shared library `Mostlylucid.LucidView.Content/`**: HTML to markdown conversion, consumed by lucidVIEW and lucidREADER.
- `IHtmlToMarkdownService.cs`: the interface, moved verbatim.
- `HtmlToMarkdownService.cs`: AngleSharp implementation, moved verbatim.
- `HtmlPreProcessor.cs`: moved verbatim.
- `UserAgent.cs`: moved verbatim.

**New shared library `Mostlylucid.LucidView.Shell/`**: app-shell services shared by both apps.
- `ThemeService.cs`, `AppTheme.cs`, `ThemeDefinition.cs`: moved from MarkdownViewer.
- `PdfExportService.cs`, `PrintService.cs`: moved from MarkdownViewer.

**New `LucidReader.Core/`**: the feed engine. One responsibility per file.
- `Model/Folder.cs`, `Model/Feed.cs`, `Model/FeedItem.cs`, `Model/Enums.cs`: domain records.
- `Model/ReaderSettings.cs`: global settings and the per-feed override resolution.
- `Storage/SchemaMigrator.cs`: forward-only migration runner.
- `Storage/ReaderDatabase.cs`: owns the `SqliteSingleWriter`, connection string, WAL and FTS5 probe.
- `Storage/FolderRepository.cs`, `Storage/FeedRepository.cs`, `Storage/ItemRepository.cs`: CRUD, one aggregate each.
- `Storage/SearchRepository.cs`: FTS5 queries.
- `Feeds/ParsedFeed.cs`, `Feeds/ParsedItem.cs`: parser output, distinct from storage records.
- `Feeds/FeedParser.cs`: RSS 2.0, RDF and Atom parsing with per-item error recovery.
- `Feeds/FeedDateParser.cs`: tolerant feed date parsing.
- `Feeds/FeedFetcher.cs`: conditional HTTP only, no parsing, no storage.
- `Sync/FeedRefreshService.cs`: the refresh coordinator.
- `Sync/RefreshScheduler.cs`: the due-feed tick.
- `Sync/BackoffPolicy.cs`: pure next-due calculation.
- `Offline/StubDetector.cs`: pure heuristic.
- `Offline/OfflineDownloader.cs`: the download coordinator.
- `Maintenance/RetentionService.cs`: pruning.

**New `LucidReader.Core.Tests/`**: mirrors the above, plus `Fixtures/Feeds/*.xml`.

---

## Task 1: Extract Mostlylucid.LucidView.Content

**Files:**
- Create: `Mostlylucid.LucidView.Content/Mostlylucid.LucidView.Content.csproj`
- Move: `MarkdownViewer/Services/IHtmlToMarkdownService.cs` → `Mostlylucid.LucidView.Content/IHtmlToMarkdownService.cs`
- Move: `MarkdownViewer/Services/HtmlToMarkdownService.cs` → `Mostlylucid.LucidView.Content/HtmlToMarkdownService.cs`
- Move: `MarkdownViewer/Services/HtmlPreProcessor.cs` → `Mostlylucid.LucidView.Content/HtmlPreProcessor.cs`
- Move: `MarkdownViewer/Services/UserAgent.cs` → `Mostlylucid.LucidView.Content/UserAgent.cs`
- Modify: `MarkdownViewer/MarkdownViewer.csproj` (add ProjectReference)
- Modify: `MarkdownViewer.Full/MarkdownViewer.Full.csproj` (add same ProjectReference)

**Interfaces:**
- Consumes: nothing.
- Produces: namespace `MarkdownViewer.Services` is **kept unchanged** on all four moved files. This is deliberate: it means zero `using` changes across lucidVIEW and its test suites, so the refactor's blast radius is the two csproj files. Public surface after this task:
  - `interface IHtmlToMarkdownService { Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default); }`
  - `class HtmlToMarkdownService : IHtmlToMarkdownService`

- [ ] **Step 1: Record the current test baseline**

Before touching anything, capture what passing looks like. Run:

```bash
cd /Users/scottgalloway/RiderProjects/lucidview
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj 2>&1 | tail -5
```

Write the passed/failed/skipped counts down. This task is complete only when these numbers are identical afterwards. If the suite is already red before you start, stop and report that rather than proceeding.

- [ ] **Step 2: Create the library project**

Create `Mostlylucid.LucidView.Content/Mostlylucid.LucidView.Content.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <PackageId>Mostlylucid.LucidView.Content</PackageId>
    <Version>1.0.0</Version>
    <Authors>Scott Galloway</Authors>
    <Description>lucidVIEW's HTML to markdown conversion pipeline as a reusable library.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <IsPackable>true</IsPackable>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AngleSharp" Version="1.5.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Move the four files with git mv**

Use `git mv` so history follows the files:

```bash
cd /Users/scottgalloway/RiderProjects/lucidview
git mv MarkdownViewer/Services/IHtmlToMarkdownService.cs Mostlylucid.LucidView.Content/IHtmlToMarkdownService.cs
git mv MarkdownViewer/Services/HtmlToMarkdownService.cs Mostlylucid.LucidView.Content/HtmlToMarkdownService.cs
git mv MarkdownViewer/Services/HtmlPreProcessor.cs Mostlylucid.LucidView.Content/HtmlPreProcessor.cs
git mv MarkdownViewer/Services/UserAgent.cs Mostlylucid.LucidView.Content/UserAgent.cs
```

Do **not** change the `namespace MarkdownViewer.Services;` declaration in any of them.

- [ ] **Step 4: Wire the ProjectReference into both apps**

In `MarkdownViewer/MarkdownViewer.csproj`, inside the `ItemGroup` that already holds `ProjectReference` entries, add:

```xml
<ProjectReference Include="..\Mostlylucid.LucidView.Content\Mostlylucid.LucidView.Content.csproj" />
```

Add the identical line to `MarkdownViewer.Full/MarkdownViewer.Full.csproj`. This is required, not optional: FULL compiles lean's sources via the `<Compile Include="..\MarkdownViewer\**\*.cs" />` glob, so the four files just vanished from FULL's compilation too.

- [ ] **Step 5: Build both apps and confirm they compile**

Run:

```bash
dotnet build MarkdownViewer/MarkdownViewer.csproj 2>&1 | tail -5
dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj 2>&1 | tail -5
```

Expected: both succeed with 0 errors.

The likely failure is a duplicate-type error in FULL, if the glob still picks the files up from a stale `bin`/`obj`. If that happens, `dotnet clean` both projects and rebuild before investigating further.

- [ ] **Step 6: Confirm the test baseline is unchanged**

Run:

```bash
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj 2>&1 | tail -5
dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj 2>&1 | tail -5
```

Expected: identical passed/failed/skipped counts to Step 1. Any change means the refactor altered behaviour and must be investigated, not accepted.

- [ ] **Step 7: Commit**

```bash
git add Mostlylucid.LucidView.Content MarkdownViewer/MarkdownViewer.csproj MarkdownViewer.Full/MarkdownViewer.Full.csproj
git commit -m "refactor: extract HTML-to-markdown pipeline into Mostlylucid.LucidView.Content"
```

---

## Task 2: Extract Mostlylucid.LucidView.Shell

**Files:**
- Create: `Mostlylucid.LucidView.Shell/Mostlylucid.LucidView.Shell.csproj`
- Move: `MarkdownViewer/Services/ThemeService.cs` → `Mostlylucid.LucidView.Shell/ThemeService.cs`
- Move: `MarkdownViewer/Services/PdfExportService.cs` → `Mostlylucid.LucidView.Shell/PdfExportService.cs`
- Move: `MarkdownViewer/Services/PrintService.cs` → `Mostlylucid.LucidView.Shell/PrintService.cs`
- Move: the `AppTheme` enum and `ThemeDefinition` type from wherever they currently live in `MarkdownViewer/Models/` into `Mostlylucid.LucidView.Shell/`
- Modify: `MarkdownViewer/MarkdownViewer.csproj`, `MarkdownViewer.Full/MarkdownViewer.Full.csproj`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces, with namespaces again left unchanged so callers need no edits:
  - `class ThemeService { ThemeService(Application app); AppTheme RequestedTheme { get; } AppTheme CurrentEffectiveTheme { get; } ThemeDefinition? CustomTheme { get; set; } AppTheme ApplyTheme(AppTheme theme); AppTheme RefreshAutoTheme(); }`
  - `PdfExportService` and `PrintService` keep their existing public signatures verbatim.

- [ ] **Step 1: Locate the theme model types**

`ThemeService` references `AppTheme` and `ThemeDefinition`. Find where they are declared before moving anything, because they must travel with the service:

```bash
cd /Users/scottgalloway/RiderProjects/lucidview
grep -rn "enum AppTheme" MarkdownViewer/
grep -rn "class ThemeDefinition\|record ThemeDefinition" MarkdownViewer/
```

Note the file paths. If `ThemeDefinition` is declared in a file alongside unrelated settings types, move only the type, not the whole file, and leave the rest in place.

- [ ] **Step 2: Check what PdfExportService and PrintService depend on**

These two are the risk in this task, because they may reach back into rendering or view types that are staying behind in `MarkdownViewer`. Find out before moving:

```bash
grep -n "^using" MarkdownViewer/Services/PdfExportService.cs MarkdownViewer/Services/PrintService.cs
grep -n "MarkdownViewer\." MarkdownViewer/Services/PdfExportService.cs MarkdownViewer/Services/PrintService.cs | head -20
```

If either references a type that lives in `MarkdownViewer/Views/` or `MarkdownViewer/Controls/`, do **not** force the move. Stop and report: the honest outcome may be that only `ThemeService` moves in this task, and export/print get a narrow interface extracted in Plan 2 when the reader's export needs are concrete. Moving a service by dragging half the view layer with it would defeat the purpose.

- [ ] **Step 3: Create the library project**

Create `Mostlylucid.LucidView.Shell/Mostlylucid.LucidView.Shell.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <PackageId>Mostlylucid.LucidView.Shell</PackageId>
    <Version>1.0.0</Version>
    <Authors>Scott Galloway</Authors>
    <Description>lucidVIEW's theme, PDF export and print services as a reusable library.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <IsPackable>true</IsPackable>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="QuestPDF" Version="2026.2.1" />
    <PackageReference Include="QuestPDF.Markdown" Version="1.47.0" />
  </ItemGroup>
</Project>
```

If Step 2 concluded that only `ThemeService` moves, drop the two QuestPDF references.

- [ ] **Step 4: Move the files with git mv**

```bash
git mv MarkdownViewer/Services/ThemeService.cs Mostlylucid.LucidView.Shell/ThemeService.cs
git mv MarkdownViewer/Services/PdfExportService.cs Mostlylucid.LucidView.Shell/PdfExportService.cs
git mv MarkdownViewer/Services/PrintService.cs Mostlylucid.LucidView.Shell/PrintService.cs
```

Then move the `AppTheme` and `ThemeDefinition` declarations found in Step 1 into `Mostlylucid.LucidView.Shell/AppTheme.cs` and `Mostlylucid.LucidView.Shell/ThemeDefinition.cs`, keeping their original namespaces.

- [ ] **Step 5: Wire the ProjectReference into both apps**

Add to both `MarkdownViewer/MarkdownViewer.csproj` and `MarkdownViewer.Full/MarkdownViewer.Full.csproj`:

```xml
<ProjectReference Include="..\Mostlylucid.LucidView.Shell\Mostlylucid.LucidView.Shell.csproj" />
```

- [ ] **Step 6: Build both apps**

```bash
dotnet build MarkdownViewer/MarkdownViewer.csproj 2>&1 | tail -5
dotnet build MarkdownViewer.Full/MarkdownViewer.Full.csproj 2>&1 | tail -5
```

Expected: both succeed with 0 errors.

- [ ] **Step 7: Confirm the test baseline is unchanged**

```bash
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj 2>&1 | tail -5
dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj 2>&1 | tail -5
```

Expected: identical counts to Task 1 Step 1.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: extract theme, PDF export and print into Mostlylucid.LucidView.Shell"
```

---

## Task 3: Scaffold LucidReader.Core and the domain model

**Files:**
- Create: `LucidReader.Core/LucidReader.Core.csproj`
- Create: `LucidReader.Core/Model/Enums.cs`
- Create: `LucidReader.Core/Model/Folder.cs`
- Create: `LucidReader.Core/Model/Feed.cs`
- Create: `LucidReader.Core/Model/FeedItem.cs`
- Create: `LucidReader.Core.Tests/LucidReader.Core.Tests.csproj`
- Test: `LucidReader.Core.Tests/Model/FeedTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces the domain records every later task uses. Exact shapes are in Step 3; later tasks reference these names verbatim.

- [ ] **Step 1: Create the Core project**

Create `LucidReader.Core/LucidReader.Core.csproj`. Note there is no Avalonia reference and no UI dependency; that is a hard constraint.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <RootNamespace>LucidReader.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
    <PackageReference Include="Mostlylucid.Ephemeral" Version="3.0.0" />
    <PackageReference Include="Mostlylucid.Ephemeral.Sqlite.SingleWriter" Version="3.0.0" />
    <PackageReference Include="Mostlylucid.Ephemeral.Atoms.Retry" Version="3.0.0" />
    <PackageReference Include="System.ServiceModel.Syndication" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Mostlylucid.LucidView.Content\Mostlylucid.LucidView.Content.csproj" />
    <ProjectReference Include="..\Mostlylucid.LucidView.Markdown\Mostlylucid.LucidView.Markdown.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="LucidReader.Core.Tests" />
  </ItemGroup>
</Project>
```

If any package version above does not resolve, run `dotnet add package <name>` to take the current stable version and note what you used. Do not silently pin an older major.

- [ ] **Step 2: Create the test project**

Create `LucidReader.Core.Tests/LucidReader.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\LucidReader.Core\LucidReader.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`Microsoft.Extensions.TimeProvider.Testing` supplies `FakeTimeProvider`, which every time-dependent test in this plan uses.

- [ ] **Step 3: Write the domain model**

Create `LucidReader.Core/Model/Enums.cs`:

```csharp
namespace LucidReader.Core.Model;

public enum ContentSource
{
    Feed = 0,
    Extracted = 1
}

public enum OfflineState
{
    None = 0,
    Pending = 1,
    Downloaded = 2,
    Failed = 3
}
```

Create `LucidReader.Core/Model/Folder.cs`:

```csharp
namespace LucidReader.Core.Model;

public sealed record Folder
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public int SortOrder { get; init; }
    public long? ParentId { get; init; }
}
```

Create `LucidReader.Core/Model/Feed.cs`. The four nullable override properties are the heart of the settings model: null means "inherit the global value", so changing a global default moves every non-overridden feed with it.

```csharp
namespace LucidReader.Core.Model;

public sealed record Feed
{
    public long Id { get; init; }
    public long? FolderId { get; init; }
    public required string FeedUrl { get; init; }
    public string? SiteUrl { get; init; }
    public string? Title { get; init; }
    public string? TitleOverride { get; init; }
    public string? IconPath { get; init; }
    public bool IsEnabled { get; init; } = true;

    public DateTimeOffset? LastFetchedUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? NextDueUtc { get; init; }

    public int? RefreshIntervalMinutes { get; init; }
    public bool? AutoDownload { get; init; }
    public bool? FetchFullText { get; init; }
    public int? RetentionDays { get; init; }

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(TitleOverride) ? TitleOverride
        : !string.IsNullOrWhiteSpace(Title) ? Title
        : FeedUrl;
}
```

Create `LucidReader.Core/Model/FeedItem.cs`:

```csharp
namespace LucidReader.Core.Model;

public sealed record FeedItem
{
    public long Id { get; init; }
    public long FeedId { get; init; }
    public required string Guid { get; init; }
    public string? Link { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public string? Summary { get; init; }
    public string? ContentMarkdown { get; init; }
    public ContentSource ContentSource { get; init; }
    public bool IsRead { get; init; }
    public bool IsStarred { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public OfflineState OfflineState { get; init; }
    public string? OfflineError { get; init; }
}
```

- [ ] **Step 4: Write the failing test for DisplayTitle**

`DisplayTitle` is the one piece of logic in the model, and it is worth pinning because the UI depends on the fallback order. Create `LucidReader.Core.Tests/Model/FeedTests.cs`:

```csharp
using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Model;

public class FeedTests
{
    [Fact]
    public void DisplayTitle_prefers_the_user_override()
    {
        var feed = new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Feed's own title",
            TitleOverride = "My name for it"
        };

        Assert.Equal("My name for it", feed.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_falls_back_to_the_feed_title_when_no_override()
    {
        var feed = new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Feed's own title"
        };

        Assert.Equal("Feed's own title", feed.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_falls_back_to_the_url_when_the_feed_has_no_title()
    {
        var feed = new Feed { FeedUrl = "https://example.com/feed.xml" };

        Assert.Equal("https://example.com/feed.xml", feed.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_treats_a_whitespace_override_as_absent()
    {
        var feed = new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            Title = "Feed's own title",
            TitleOverride = "   "
        };

        Assert.Equal("Feed's own title", feed.DisplayTitle);
    }
}
```

- [ ] **Step 5: Add both projects to the solution and run the tests**

```bash
cd /Users/scottgalloway/RiderProjects/lucidview
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj 2>&1 | tail -5
```

Expected: 4 passed. The implementation was written in Step 3, so this is the confirmation, not a red-then-green cycle. Every task from here on writes the test first.

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core LucidReader.Core.Tests
git commit -m "feat(reader): scaffold LucidReader.Core and the domain model"
```

---

## Task 4: Schema and the migration runner

**Files:**
- Create: `LucidReader.Core/Storage/SchemaMigrator.cs`
- Create: `LucidReader.Core/Storage/Migrations.cs`
- Test: `LucidReader.Core.Tests/Storage/SchemaMigratorTests.cs`
- Test: `LucidReader.Core.Tests/Storage/TempDatabase.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `static class Migrations { static IReadOnlyList<string> All { get; } }` where index 0 is migration version 1.
  - `static class SchemaMigrator { static Task<int> MigrateAsync(SqliteConnection connection, CancellationToken ct = default); }` returning the schema version after migrating. Throws `InvalidOperationException` when the database is newer than the app.

- [ ] **Step 1: Write the temp-database test helper**

Every storage test needs a real database file, because SQLite's in-memory mode behaves differently around WAL and FTS5 and would hide exactly the bugs we care about. Create `LucidReader.Core.Tests/Storage/TempDatabase.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// A real SQLite file in a temp directory, deleted on dispose. Not in-memory:
/// WAL and FTS5 behaviour differ there, and those differences are what the
/// storage tests exist to catch.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    public string Path { get; }
    public string ConnectionString { get; }

    public TempDatabase()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lucidreader-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "reader.db");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // A held file handle on Windows is not worth failing a test over.
        }
    }
}
```

- [ ] **Step 2: Write the failing migration tests**

Create `LucidReader.Core.Tests/Storage/SchemaMigratorTests.cs`:

```csharp
using LucidReader.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class SchemaMigratorTests
{
    [Fact]
    public async Task Migrating_a_fresh_database_creates_every_table()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        await SchemaMigrator.MigrateAsync(connection);

        var tables = await ReadTableNamesAsync(connection);
        Assert.Contains("folders", tables);
        Assert.Contains("feeds", tables);
        Assert.Contains("items", tables);
        Assert.Contains("tags", tables);
        Assert.Contains("item_tags", tables);
        Assert.Contains("items_fts", tables);
    }

    [Fact]
    public async Task Migrating_sets_the_schema_version()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        var version = await SchemaMigrator.MigrateAsync(connection);

        Assert.Equal(Migrations.All.Count, version);
    }

    [Fact]
    public async Task Migrating_twice_is_a_no_op()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        var first = await SchemaMigrator.MigrateAsync(connection);
        var second = await SchemaMigrator.MigrateAsync(connection);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_database_newer_than_the_app_is_refused()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();
        await SchemaMigrator.MigrateAsync(connection);

        // Simulate a database written by a future version of the app.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA user_version = {Migrations.All.Count + 5};";
            await command.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SchemaMigrator.MigrateAsync(connection));
        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fts5_is_available_in_the_native_sqlite_build()
    {
        using var db = new TempDatabase();
        await using var connection = db.Open();

        await SchemaMigrator.MigrateAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM items_fts WHERE items_fts MATCH 'anything';";
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(0L, Convert.ToInt64(result));
    }

    private static async Task<List<string>> ReadTableNamesAsync(SqliteConnection connection)
    {
        var names = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
```

The last test is the FTS5 availability check from spec section 7.1, running in CI on every platform. If the native `e_sqlite3` were ever built without FTS5, this is what tells us, instead of a user discovering it at first search.

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SchemaMigratorTests 2>&1 | tail -10
```

Expected: compilation failure, `SchemaMigrator` and `Migrations` do not exist.

- [ ] **Step 4: Write the schema**

Create `LucidReader.Core/Storage/Migrations.cs`. Each list entry is one schema version, applied in order, and entries are **append-only** once shipped: never edit a released migration, add a new one.

```csharp
namespace LucidReader.Core.Storage;

/// <summary>
/// Forward-only schema migrations. Append only. Once a migration has shipped
/// it is frozen; corrections go in a new entry.
/// </summary>
public static class Migrations
{
    public static IReadOnlyList<string> All { get; } = new[] { V1 };

    private const string V1 = """
        CREATE TABLE folders (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            name        TEXT    NOT NULL,
            sort_order  INTEGER NOT NULL DEFAULT 0,
            parent_id   INTEGER NULL REFERENCES folders(id) ON DELETE SET NULL
        );

        CREATE TABLE feeds (
            id                        INTEGER PRIMARY KEY AUTOINCREMENT,
            folder_id                 INTEGER NULL REFERENCES folders(id) ON DELETE SET NULL,
            feed_url                  TEXT    NOT NULL,
            site_url                  TEXT    NULL,
            title                     TEXT    NULL,
            title_override            TEXT    NULL,
            icon_path                 TEXT    NULL,
            is_enabled                INTEGER NOT NULL DEFAULT 1,
            last_fetched_utc          TEXT    NULL,
            last_success_utc          TEXT    NULL,
            etag                      TEXT    NULL,
            last_modified             TEXT    NULL,
            consecutive_failures      INTEGER NOT NULL DEFAULT 0,
            last_error                TEXT    NULL,
            next_due_utc              TEXT    NULL,
            refresh_interval_minutes  INTEGER NULL,
            auto_download             INTEGER NULL,
            fetch_full_text           INTEGER NULL,
            retention_days            INTEGER NULL
        );

        CREATE UNIQUE INDEX ix_feeds_url ON feeds(feed_url);
        CREATE INDEX ix_feeds_next_due ON feeds(next_due_utc) WHERE is_enabled = 1;

        CREATE TABLE items (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            feed_id           INTEGER NOT NULL REFERENCES feeds(id) ON DELETE CASCADE,
            guid              TEXT    NOT NULL,
            link              TEXT    NULL,
            title             TEXT    NULL,
            author            TEXT    NULL,
            published_utc     TEXT    NULL,
            updated_utc       TEXT    NULL,
            summary           TEXT    NULL,
            content_markdown  TEXT    NULL,
            content_source    INTEGER NOT NULL DEFAULT 0,
            is_read           INTEGER NOT NULL DEFAULT 0,
            is_starred        INTEGER NOT NULL DEFAULT 0,
            first_seen_utc    TEXT    NOT NULL,
            offline_state     INTEGER NOT NULL DEFAULT 0,
            offline_error     TEXT    NULL
        );

        CREATE UNIQUE INDEX ix_items_feed_guid ON items(feed_id, guid);
        CREATE INDEX ix_items_feed_published ON items(feed_id, published_utc DESC);
        CREATE INDEX ix_items_unread ON items(is_read, published_utc DESC);
        CREATE INDEX ix_items_starred ON items(is_starred) WHERE is_starred = 1;
        CREATE INDEX ix_items_offline_pending ON items(offline_state) WHERE offline_state = 1;

        CREATE TABLE tags (
            id    INTEGER PRIMARY KEY AUTOINCREMENT,
            name  TEXT    NOT NULL
        );

        CREATE UNIQUE INDEX ix_tags_name ON tags(name);

        CREATE TABLE item_tags (
            item_id  INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            tag_id   INTEGER NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
            PRIMARY KEY (item_id, tag_id)
        );

        CREATE VIRTUAL TABLE items_fts USING fts5(
            title,
            content_markdown,
            content='items',
            content_rowid='id',
            tokenize='unicode61'
        );

        CREATE TRIGGER items_fts_insert AFTER INSERT ON items BEGIN
            INSERT INTO items_fts(rowid, title, content_markdown)
            VALUES (new.id, new.title, new.content_markdown);
        END;

        CREATE TRIGGER items_fts_delete AFTER DELETE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, content_markdown)
            VALUES ('delete', old.id, old.title, old.content_markdown);
        END;

        CREATE TRIGGER items_fts_update AFTER UPDATE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, content_markdown)
            VALUES ('delete', old.id, old.title, old.content_markdown);
            INSERT INTO items_fts(rowid, title, content_markdown)
            VALUES (new.id, new.title, new.content_markdown);
        END;
        """;
}
```

Two notes for whoever maintains this. The FTS5 table is an external-content table (`content='items'`), so it stores no duplicate copy of the article text; the three triggers are what keep it in sync, and the delete trigger's odd-looking `INSERT ... VALUES ('delete', ...)` form is FTS5's required syntax for removing a row from an external-content index. Second, `ix_feeds_next_due` is a partial index over enabled feeds only, because the scheduler's due query filters on exactly that.

- [ ] **Step 5: Write the migration runner**

Create `LucidReader.Core/Storage/SchemaMigrator.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public static class SchemaMigrator
{
    /// <summary>
    /// Applies any migrations the database has not yet seen and returns the
    /// resulting schema version. Refuses to touch a database written by a
    /// newer version of the app rather than guessing at its shape.
    /// </summary>
    public static async Task<int> MigrateAsync(
        SqliteConnection connection,
        CancellationToken ct = default)
    {
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", ct);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", ct);

        var current = await ReadUserVersionAsync(connection, ct);
        var target = Migrations.All.Count;

        if (current > target)
            throw new InvalidOperationException(
                $"This database was written by a newer version of lucidREADER " +
                $"(schema {current}, this build understands {target}). " +
                $"Upgrade lucidREADER to open it.");

        if (current == target)
            return current;

        await using var transaction = await connection.BeginTransactionAsync(ct);
        for (var version = current; version < target; version++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Migrations.All[version];
            await command.ExecuteNonQueryAsync(ct);
        }

        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = (SqliteTransaction)transaction;
            // PRAGMA does not accept parameters, and target is an int we control.
            versionCommand.CommandText = $"PRAGMA user_version = {target};";
            await versionCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return target;
    }

    private static async Task<int> ReadUserVersionAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SchemaMigratorTests 2>&1 | tail -5
```

Expected: 5 passed.

If `Fts5_is_available_in_the_native_sqlite_build` fails with "no such module: fts5", the native SQLite build lacks FTS5 and this is the spec section 7.1 fallback territory. Stop and report; do not work around it by dropping the FTS table.

- [ ] **Step 7: Commit**

```bash
git add LucidReader.Core/Storage LucidReader.Core.Tests/Storage
git commit -m "feat(reader): schema and forward-only migration runner"
```

---

## Task 5: ReaderDatabase, the single-writer gateway

**Files:**
- Create: `LucidReader.Core/Storage/ReaderDatabase.cs`
- Create: `LucidReader.Core/Storage/ReaderPaths.cs`
- Test: `LucidReader.Core.Tests/Storage/ReaderDatabaseTests.cs`

**Interfaces:**
- Consumes: `SchemaMigrator.MigrateAsync` from Task 4.
- Produces the object every repository takes in its constructor:
  - `sealed class ReaderDatabase : IAsyncDisposable`
  - `static Task<ReaderDatabase> OpenAsync(string databasePath, CancellationToken ct = default)`
  - `SqliteSingleWriter Writer { get; }`
  - `Task<T> QueryAsync<T>(Func<SqliteConnection, Task<T>> reader, CancellationToken ct = default)`
  - `Task<int> WriteAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)`
  - `Task<long> WriteReturningIdAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)`
- `static class ReaderPaths { static string DefaultDatabasePath { get; } static string AppDataDirectory { get; } }`

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Storage/ReaderDatabaseTests.cs`:

```csharp
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class ReaderDatabaseTests
{
    [Fact]
    public async Task Opening_creates_and_migrates_the_database_file()
    {
        using var temp = new TempDatabase();

        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        Assert.True(File.Exists(temp.Path));
        var version = await database.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        });
        Assert.Equal(Migrations.All.Count, version);
    }

    [Fact]
    public async Task Opening_creates_missing_parent_directories()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lucidreader-tests", Guid.NewGuid().ToString("N"), "nested");
        var path = Path.Combine(dir, "reader.db");
        try
        {
            await using var database = await ReaderDatabase.OpenAsync(path);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteReturningIdAsync_gives_back_the_inserted_row_id()
    {
        using var temp = new TempDatabase();
        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        var id = await database.WriteReturningIdAsync(
            "INSERT INTO folders (name, sort_order) VALUES ($name, $sort);",
            new Dictionary<string, object?> { ["$name"] = "News", ["$sort"] = 0 });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task Concurrent_writes_all_land_without_a_busy_error()
    {
        using var temp = new TempDatabase();
        await using var database = await ReaderDatabase.OpenAsync(temp.Path);

        var writes = Enumerable.Range(0, 50).Select(i =>
            database.WriteAsync(
                "INSERT INTO folders (name, sort_order) VALUES ($name, $sort);",
                new Dictionary<string, object?> { ["$name"] = $"Folder {i}", ["$sort"] = i }));

        await Task.WhenAll(writes);

        var count = await database.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM folders;";
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        });
        Assert.Equal(50L, count);
    }
}
```

The last test is the reason `SqliteSingleWriter` is in the design at all: fifty concurrent writers against a raw connection is how you produce `SQLITE_BUSY` in production. It should pass by construction here, and it is a regression guard for anyone who later "simplifies" the writer away.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ReaderDatabaseTests 2>&1 | tail -10
```

Expected: compilation failure, `ReaderDatabase` does not exist.

- [ ] **Step 3: Write ReaderPaths**

Create `LucidReader.Core/Storage/ReaderPaths.cs`:

```csharp
namespace LucidReader.Core.Storage;

/// <summary>
/// Where lucidREADER keeps its data. The database sits beside settings.json
/// so the two travel together when a user copies their profile.
/// </summary>
public static class ReaderPaths
{
    public const string AppFolderName = "lucidREADER";

    public static string AppDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create),
            AppFolderName);

    public static string DefaultDatabasePath =>
        Path.Combine(AppDataDirectory, "reader.db");

    public static string DefaultSettingsPath =>
        Path.Combine(AppDataDirectory, "settings.json");
}
```

On macOS `SpecialFolder.ApplicationData` resolves to `~/.config` under .NET, not `~/Library/Application Support`. That is the documented .NET behaviour and it is consistent across platforms, so we take it rather than hand-rolling per-platform paths. The spec's example path is illustrative; this is the actual location. Update the spec's section 3 wording when this task lands.

- [ ] **Step 4: Write ReaderDatabase**

Create `LucidReader.Core/Storage/ReaderDatabase.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Mostlylucid.Ephemeral.Sqlite.SingleWriter;

namespace LucidReader.Core.Storage;

/// <summary>
/// Owns the connection string and the single writer. Every write in the app
/// goes through here, which is what keeps SQLite's writer lock uncontended
/// while two coordinators are running.
/// </summary>
public sealed class ReaderDatabase : IAsyncDisposable
{
    private readonly SqliteSingleWriter _writer;

    private ReaderDatabase(string connectionString, SqliteSingleWriter writer)
    {
        ConnectionString = connectionString;
        _writer = writer;
    }

    public string ConnectionString { get; }

    public SqliteSingleWriter Writer => _writer;

    public static async Task<ReaderDatabase> OpenAsync(
        string databasePath,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(ct);
            await SchemaMigrator.MigrateAsync(connection, ct);
        }

        var writer = SqliteSingleWriter.GetOrCreate(connectionString);
        return new ReaderDatabase(connectionString, writer);
    }

    public Task<T> QueryAsync<T>(
        Func<SqliteConnection, Task<T>> reader,
        CancellationToken ct = default) =>
        _writer.QueryAsync(reader, ct);

    public Task<int> WriteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default) =>
        _writer.WriteAsync(sql, parameters, ct);

    /// <summary>
    /// Inserts and returns the new row id. The SELECT runs on the writer's own
    /// connection inside the same call, so last_insert_rowid() is guaranteed to
    /// be this statement's, not another writer's.
    /// </summary>
    public async Task<long> WriteReturningIdAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        return await _writer.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (key, value) in parameters)
                    command.Parameters.AddWithValue(key, value ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(ct);
            }

            await using var idCommand = connection.CreateCommand();
            idCommand.Transaction = transaction;
            idCommand.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt64(await idCommand.ExecuteScalarAsync(ct));
        }, ct);
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}
```

The `ExecuteInTransactionAsync` delegate signature and namespace must be confirmed against the installed 3.0.0 package. Check with:

```bash
grep -n "public async Task<T> ExecuteInTransactionAsync" ~/.nuget/packages/mostlylucid.ephemeral.sqlite.singlewriter/3.0.0/lib/net*/*.xml 2>/dev/null
```

or by inspecting the assembly in your IDE. If the delegate takes only a connection, adjust the lambda accordingly. Do not guess: this is the one API in the plan taken from a 2.9.0 source checkout rather than the 3.0.0 package.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ReaderDatabaseTests 2>&1 | tail -5
```

Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core/Storage LucidReader.Core.Tests/Storage
git commit -m "feat(reader): ReaderDatabase single-writer gateway and app data paths"
```

---

## Task 6: Folder and feed repositories

**Files:**
- Create: `LucidReader.Core/Storage/FolderRepository.cs`
- Create: `LucidReader.Core/Storage/FeedRepository.cs`
- Create: `LucidReader.Core/Storage/RowMappers.cs`
- Test: `LucidReader.Core.Tests/Storage/FeedRepositoryTests.cs`

**Interfaces:**
- Consumes: `ReaderDatabase` (Task 5), `Folder` and `Feed` (Task 3).
- Produces:
  - `sealed class FolderRepository(ReaderDatabase db)` with `Task<long> AddAsync(string name, long? parentId = null, CancellationToken ct = default)`, `Task<IReadOnlyList<Folder>> GetAllAsync(CancellationToken ct = default)`, `Task RenameAsync(long id, string name, CancellationToken ct = default)`, `Task DeleteAsync(long id, CancellationToken ct = default)`.
  - `sealed class FeedRepository(ReaderDatabase db)` with `Task<long> AddAsync(Feed feed, CancellationToken ct = default)`, `Task<Feed?> GetAsync(long id, CancellationToken ct = default)`, `Task<Feed?> GetByUrlAsync(string feedUrl, CancellationToken ct = default)`, `Task<IReadOnlyList<Feed>> GetAllAsync(CancellationToken ct = default)`, `Task<IReadOnlyList<Feed>> GetDueAsync(DateTimeOffset nowUtc, int limit, CancellationToken ct = default)`, `Task UpdateAsync(Feed feed, CancellationToken ct = default)`, `Task RecordSuccessAsync(long feedId, string? etag, string? lastModified, DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default)`, `Task RecordFailureAsync(long feedId, string error, DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default)`, `Task DeleteAsync(long id, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Storage/FeedRepositoryTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class FeedRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private FolderRepository _folders = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _folders = new FolderRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private static Feed NewFeed(string url = "https://example.com/feed.xml") =>
        new() { FeedUrl = url, Title = "Example", SiteUrl = "https://example.com" };

    [Fact]
    public async Task Adding_a_feed_round_trips_every_field()
    {
        var id = await _feeds.AddAsync(NewFeed() with
        {
            RefreshIntervalMinutes = 15,
            AutoDownload = false,
            FetchFullText = true,
            RetentionDays = 30
        });

        var loaded = await _feeds.GetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("https://example.com/feed.xml", loaded!.FeedUrl);
        Assert.Equal("Example", loaded.Title);
        Assert.Equal(15, loaded.RefreshIntervalMinutes);
        Assert.False(loaded.AutoDownload);
        Assert.True(loaded.FetchFullText);
        Assert.Equal(30, loaded.RetentionDays);
        Assert.True(loaded.IsEnabled);
    }

    [Fact]
    public async Task Unset_overrides_round_trip_as_null_not_as_a_default()
    {
        var id = await _feeds.AddAsync(NewFeed());

        var loaded = await _feeds.GetAsync(id);

        Assert.Null(loaded!.RefreshIntervalMinutes);
        Assert.Null(loaded.AutoDownload);
        Assert.Null(loaded.FetchFullText);
        Assert.Null(loaded.RetentionDays);
    }

    [Fact]
    public async Task Adding_the_same_url_twice_is_rejected()
    {
        await _feeds.AddAsync(NewFeed());

        await Assert.ThrowsAnyAsync<Exception>(() => _feeds.AddAsync(NewFeed()));
    }

    [Fact]
    public async Task GetByUrl_finds_an_existing_subscription()
    {
        await _feeds.AddAsync(NewFeed());

        var found = await _feeds.GetByUrlAsync("https://example.com/feed.xml");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task GetDue_returns_only_feeds_whose_next_due_has_passed()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var dueId = await _feeds.AddAsync(NewFeed("https://a.example/feed.xml") with
        {
            NextDueUtc = now.AddMinutes(-1)
        });
        await _feeds.AddAsync(NewFeed("https://b.example/feed.xml") with
        {
            NextDueUtc = now.AddMinutes(30)
        });

        var due = await _feeds.GetDueAsync(now, limit: 10);

        Assert.Single(due);
        Assert.Equal(dueId, due[0].Id);
    }

    [Fact]
    public async Task GetDue_treats_a_never_fetched_feed_as_due()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.AddAsync(NewFeed());

        var due = await _feeds.GetDueAsync(now, limit: 10);

        Assert.Single(due);
    }

    [Fact]
    public async Task GetDue_skips_disabled_feeds()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        await _feeds.AddAsync(NewFeed() with { IsEnabled = false });

        var due = await _feeds.GetDueAsync(now, limit: 10);

        Assert.Empty(due);
    }

    [Fact]
    public async Task Recording_a_success_clears_the_failure_count_and_error()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var id = await _feeds.AddAsync(NewFeed());
        await _feeds.RecordFailureAsync(id, "connection refused", now, now.AddMinutes(5));

        await _feeds.RecordSuccessAsync(id, "\"abc123\"", "Wed, 27 Aug 2026 10:00:00 GMT", now, now.AddMinutes(30));

        var loaded = await _feeds.GetAsync(id);
        Assert.Equal(0, loaded!.ConsecutiveFailures);
        Assert.Null(loaded.LastError);
        Assert.Equal("\"abc123\"", loaded.ETag);
        Assert.Equal("Wed, 27 Aug 2026 10:00:00 GMT", loaded.LastModified);
        Assert.Equal(now, loaded.LastSuccessUtc);
    }

    [Fact]
    public async Task Recording_failures_increments_the_count_each_time()
    {
        var now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var id = await _feeds.AddAsync(NewFeed());

        await _feeds.RecordFailureAsync(id, "timeout", now, now.AddMinutes(5));
        await _feeds.RecordFailureAsync(id, "timeout", now, now.AddMinutes(10));
        await _feeds.RecordFailureAsync(id, "500 Server Error", now, now.AddMinutes(20));

        var loaded = await _feeds.GetAsync(id);
        Assert.Equal(3, loaded!.ConsecutiveFailures);
        Assert.Equal("500 Server Error", loaded.LastError);
    }

    [Fact]
    public async Task Deleting_a_folder_orphans_its_feeds_rather_than_deleting_them()
    {
        var folderId = await _folders.AddAsync("News");
        var feedId = await _feeds.AddAsync(NewFeed() with { FolderId = folderId });

        await _folders.DeleteAsync(folderId);

        var loaded = await _feeds.GetAsync(feedId);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.FolderId);
    }
}
```

That last test pins a real product decision: deleting a folder must never silently unsubscribe the user from ten feeds. The schema's `ON DELETE SET NULL` is what implements it, and this test is what stops someone changing it to `CASCADE` later.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedRepositoryTests 2>&1 | tail -10
```

Expected: compilation failure, the repositories do not exist.

- [ ] **Step 3: Write the row mappers**

Create `LucidReader.Core/Storage/RowMappers.cs`. Centralising this avoids the classic bug where two hand-written readers disagree about column order.

```csharp
using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

internal static class RowMappers
{
    public static string? GetNullableString(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int? GetNullableInt(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static bool? GetNullableBool(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) != 0;
    }

    public static bool GetBool(this SqliteDataReader reader, string column) =>
        reader.GetInt32(reader.GetOrdinal(column)) != 0;

    /// <summary>
    /// Dates are stored as ISO-8601 round-trip strings ("o"), which sort
    /// lexicographically in the same order they sort chronologically. That is
    /// what lets the scheduler's due query and the item ordering use plain
    /// string comparison in SQL.
    /// </summary>
    public static DateTimeOffset? GetNullableDate(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;
        return DateTimeOffset.Parse(
            reader.GetString(ordinal),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
    }

    public static DateTimeOffset GetDate(this SqliteDataReader reader, string column) =>
        GetNullableDate(reader, column)
        ?? throw new InvalidOperationException($"Column {column} was unexpectedly null.");

    public static string? ToDbString(this DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("o");

    public static string ToDbString(this DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o");

    public static Feed ReadFeed(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        FolderId = reader.IsDBNull(reader.GetOrdinal("folder_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("folder_id")),
        FeedUrl = reader.GetString(reader.GetOrdinal("feed_url")),
        SiteUrl = reader.GetNullableString("site_url"),
        Title = reader.GetNullableString("title"),
        TitleOverride = reader.GetNullableString("title_override"),
        IconPath = reader.GetNullableString("icon_path"),
        IsEnabled = reader.GetBool("is_enabled"),
        LastFetchedUtc = reader.GetNullableDate("last_fetched_utc"),
        LastSuccessUtc = reader.GetNullableDate("last_success_utc"),
        ETag = reader.GetNullableString("etag"),
        LastModified = reader.GetNullableString("last_modified"),
        ConsecutiveFailures = reader.GetInt32(reader.GetOrdinal("consecutive_failures")),
        LastError = reader.GetNullableString("last_error"),
        NextDueUtc = reader.GetNullableDate("next_due_utc"),
        RefreshIntervalMinutes = reader.GetNullableInt("refresh_interval_minutes"),
        AutoDownload = reader.GetNullableBool("auto_download"),
        FetchFullText = reader.GetNullableBool("fetch_full_text"),
        RetentionDays = reader.GetNullableInt("retention_days")
    };

    public static Folder ReadFolder(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
        ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("parent_id"))
    };
}
```

- [ ] **Step 4: Write FolderRepository**

Create `LucidReader.Core/Storage/FolderRepository.cs`:

```csharp
using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class FolderRepository(ReaderDatabase db)
{
    public Task<long> AddAsync(string name, long? parentId = null, CancellationToken ct = default) =>
        db.WriteReturningIdAsync(
            "INSERT INTO folders (name, sort_order, parent_id) " +
            "VALUES ($name, (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM folders), $parent);",
            new Dictionary<string, object?> { ["$name"] = name, ["$parent"] = parentId },
            ct);

    public Task<IReadOnlyList<Folder>> GetAllAsync(CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<Folder>>(async connection =>
        {
            var results = new List<Folder>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM folders ORDER BY sort_order, name;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadFolder((SqliteDataReader)reader));
            return results;
        }, ct);

    public Task RenameAsync(long id, string name, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE folders SET name = $name WHERE id = $id;",
            new Dictionary<string, object?> { ["$name"] = name, ["$id"] = id },
            ct);

    /// <summary>
    /// Feeds in the folder are moved to the top level, never deleted. Removing a
    /// folder must not silently unsubscribe the user from everything in it.
    /// </summary>
    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        db.WriteAsync(
            "DELETE FROM folders WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id },
            ct);
}
```

- [ ] **Step 5: Write FeedRepository**

Create `LucidReader.Core/Storage/FeedRepository.cs`:

```csharp
using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class FeedRepository(ReaderDatabase db)
{
    public Task<long> AddAsync(Feed feed, CancellationToken ct = default) =>
        db.WriteReturningIdAsync(
            """
            INSERT INTO feeds (
                folder_id, feed_url, site_url, title, title_override, icon_path,
                is_enabled, next_due_utc, refresh_interval_minutes, auto_download,
                fetch_full_text, retention_days)
            VALUES (
                $folder, $url, $site, $title, $titleOverride, $icon,
                $enabled, $nextDue, $interval, $autoDownload,
                $fullText, $retention);
            """,
            new Dictionary<string, object?>
            {
                ["$folder"] = feed.FolderId,
                ["$url"] = feed.FeedUrl,
                ["$site"] = feed.SiteUrl,
                ["$title"] = feed.Title,
                ["$titleOverride"] = feed.TitleOverride,
                ["$icon"] = feed.IconPath,
                ["$enabled"] = feed.IsEnabled ? 1 : 0,
                ["$nextDue"] = feed.NextDueUtc.ToDbString(),
                ["$interval"] = feed.RefreshIntervalMinutes,
                ["$autoDownload"] = feed.AutoDownload switch { true => 1, false => 0, null => (object?)null },
                ["$fullText"] = feed.FetchFullText switch { true => 1, false => 0, null => (object?)null },
                ["$retention"] = feed.RetentionDays
            },
            ct);

    public Task<Feed?> GetAsync(long id, CancellationToken ct = default) =>
        QuerySingleAsync("SELECT * FROM feeds WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id }, ct);

    public Task<Feed?> GetByUrlAsync(string feedUrl, CancellationToken ct = default) =>
        QuerySingleAsync("SELECT * FROM feeds WHERE feed_url = $url;",
            new Dictionary<string, object?> { ["$url"] = feedUrl }, ct);

    public Task<IReadOnlyList<Feed>> GetAllAsync(CancellationToken ct = default) =>
        QueryManyAsync("SELECT * FROM feeds ORDER BY title, feed_url;",
            new Dictionary<string, object?>(), ct);

    /// <summary>
    /// Feeds whose next_due_utc has passed, plus feeds that have never been
    /// fetched (null next_due). Disabled feeds are excluded, matching the
    /// partial index ix_feeds_next_due.
    /// </summary>
    public Task<IReadOnlyList<Feed>> GetDueAsync(
        DateTimeOffset nowUtc, int limit, CancellationToken ct = default) =>
        QueryManyAsync(
            """
            SELECT * FROM feeds
            WHERE is_enabled = 1
              AND (next_due_utc IS NULL OR next_due_utc <= $now)
            ORDER BY next_due_utc IS NOT NULL, next_due_utc
            LIMIT $limit;
            """,
            new Dictionary<string, object?>
            {
                ["$now"] = nowUtc.ToDbString(),
                ["$limit"] = limit
            }, ct);

    public Task UpdateAsync(Feed feed, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE feeds SET
                folder_id = $folder, site_url = $site, title = $title,
                title_override = $titleOverride, icon_path = $icon,
                is_enabled = $enabled,
                refresh_interval_minutes = $interval, auto_download = $autoDownload,
                fetch_full_text = $fullText, retention_days = $retention
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = feed.Id,
                ["$folder"] = feed.FolderId,
                ["$site"] = feed.SiteUrl,
                ["$title"] = feed.Title,
                ["$titleOverride"] = feed.TitleOverride,
                ["$icon"] = feed.IconPath,
                ["$enabled"] = feed.IsEnabled ? 1 : 0,
                ["$interval"] = feed.RefreshIntervalMinutes,
                ["$autoDownload"] = feed.AutoDownload switch { true => 1, false => 0, null => (object?)null },
                ["$fullText"] = feed.FetchFullText switch { true => 1, false => 0, null => (object?)null },
                ["$retention"] = feed.RetentionDays
            },
            ct);

    public Task RecordSuccessAsync(
        long feedId, string? etag, string? lastModified,
        DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE feeds SET
                last_fetched_utc = $now, last_success_utc = $now,
                etag = $etag, last_modified = $lastModified,
                consecutive_failures = 0, last_error = NULL,
                next_due_utc = $nextDue
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$now"] = nowUtc.ToDbString(),
                ["$etag"] = etag,
                ["$lastModified"] = lastModified,
                ["$nextDue"] = nextDueUtc.ToDbString()
            },
            ct);

    public Task RecordFailureAsync(
        long feedId, string error,
        DateTimeOffset nowUtc, DateTimeOffset nextDueUtc, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE feeds SET
                last_fetched_utc = $now,
                consecutive_failures = consecutive_failures + 1,
                last_error = $error,
                next_due_utc = $nextDue
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = feedId,
                ["$now"] = nowUtc.ToDbString(),
                ["$error"] = error,
                ["$nextDue"] = nextDueUtc.ToDbString()
            },
            ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        db.WriteAsync("DELETE FROM feeds WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id }, ct);

    private Task<Feed?> QuerySingleAsync(
        string sql, Dictionary<string, object?> parameters, CancellationToken ct) =>
        db.QueryAsync<Feed?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (key, value) in parameters)
                command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct)
                ? RowMappers.ReadFeed((SqliteDataReader)reader)
                : null;
        }, ct);

    private Task<IReadOnlyList<Feed>> QueryManyAsync(
        string sql, Dictionary<string, object?> parameters, CancellationToken ct) =>
        db.QueryAsync<IReadOnlyList<Feed>>(async connection =>
        {
            var results = new List<Feed>();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (key, value) in parameters)
                command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(RowMappers.ReadFeed((SqliteDataReader)reader));
            return results;
        }, ct);
}
```

Note the `switch` expressions on the nullable bools. A plain `feed.AutoDownload` boxed into the parameter would write `0` for null, silently converting "inherit the global value" into "explicitly off". That is the single most likely bug in this file, which is why Step 1 has a test for it.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedRepositoryTests 2>&1 | tail -5
```

Expected: 10 passed.

- [ ] **Step 7: Commit**

```bash
git add LucidReader.Core/Storage LucidReader.Core.Tests/Storage
git commit -m "feat(reader): folder and feed repositories"
```

---

## Task 7: Item repository, dedupe, and read/star state

**Files:**
- Create: `LucidReader.Core/Storage/ItemRepository.cs`
- Create: `LucidReader.Core/Storage/ItemQuery.cs`
- Test: `LucidReader.Core.Tests/Storage/ItemRepositoryTests.cs`

**Interfaces:**
- Consumes: `ReaderDatabase` (Task 5), `FeedItem`, `ContentSource`, `OfflineState` (Task 3).
- Produces:
  - `enum ItemFilter { All, Unread, Starred }`
  - `readonly record struct ItemQuery(long? FeedId, long? FolderId, ItemFilter Filter, int Limit, int Offset)`
  - `sealed class ItemRepository(ReaderDatabase db)` with:
    - `Task<long> UpsertAsync(FeedItem item, CancellationToken ct = default)` returning the row id, inserting or updating on `(feed_id, guid)`.
    - `Task<int> UpsertManyAsync(IReadOnlyList<FeedItem> items, CancellationToken ct = default)` returning the count of rows that were newly inserted.
    - `Task<FeedItem?> GetAsync(long id, CancellationToken ct = default)`
    - `Task<IReadOnlyList<FeedItem>> QueryAsync(ItemQuery query, CancellationToken ct = default)`
    - `Task<IReadOnlyList<FeedItem>> GetPendingOfflineAsync(int limit, CancellationToken ct = default)`
    - `Task SetReadAsync(long id, bool isRead, CancellationToken ct = default)`
    - `Task SetStarredAsync(long id, bool isStarred, CancellationToken ct = default)`
    - `Task MarkFeedReadAsync(long feedId, CancellationToken ct = default)`
    - `Task SetContentAsync(long id, string markdown, ContentSource source, CancellationToken ct = default)`
    - `Task SetOfflineFailedAsync(long id, string error, CancellationToken ct = default)`
    - `Task<int> GetUnreadCountAsync(long feedId, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Storage/ItemRepositoryTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class ItemRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml", Title = "Example" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private FeedItem NewItem(string guid = "guid-1", string title = "Hello") => new()
    {
        FeedId = _feedId,
        Guid = guid,
        Title = title,
        Link = $"https://example.com/{guid}",
        Summary = "A summary.",
        PublishedUtc = DateTimeOffset.Parse("2026-08-28T09:00:00Z"),
        FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
    };

    [Fact]
    public async Task Upserting_a_new_item_inserts_it()
    {
        var id = await _items.UpsertAsync(NewItem());

        var loaded = await _items.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Hello", loaded!.Title);
        Assert.Equal(OfflineState.None, loaded.OfflineState);
    }

    [Fact]
    public async Task Upserting_the_same_guid_updates_in_place_and_does_not_duplicate()
    {
        var first = await _items.UpsertAsync(NewItem(title: "Original title"));
        var second = await _items.UpsertAsync(NewItem(title: "Corrected title"));

        Assert.Equal(first, second);
        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Single(all);
        Assert.Equal("Corrected title", all[0].Title);
    }

    [Fact]
    public async Task Re_upserting_preserves_read_and_starred_state()
    {
        var id = await _items.UpsertAsync(NewItem());
        await _items.SetReadAsync(id, true);
        await _items.SetStarredAsync(id, true);

        await _items.UpsertAsync(NewItem(title: "Republished with an edit"));

        var loaded = await _items.GetAsync(id);
        Assert.True(loaded!.IsRead);
        Assert.True(loaded.IsStarred);
        Assert.Equal("Republished with an edit", loaded.Title);
    }

    [Fact]
    public async Task Re_upserting_preserves_downloaded_content()
    {
        var id = await _items.UpsertAsync(NewItem());
        await _items.SetContentAsync(id, "# The full article", ContentSource.Extracted);

        await _items.UpsertAsync(NewItem(title: "Title fixed upstream"));

        var loaded = await _items.GetAsync(id);
        Assert.Equal("# The full article", loaded!.ContentMarkdown);
        Assert.Equal(ContentSource.Extracted, loaded.ContentSource);
    }

    [Fact]
    public async Task UpsertMany_reports_only_the_newly_inserted_count()
    {
        await _items.UpsertAsync(NewItem("guid-1"));

        var inserted = await _items.UpsertManyAsync(new[]
        {
            NewItem("guid-1"),
            NewItem("guid-2"),
            NewItem("guid-3")
        });

        Assert.Equal(2, inserted);
    }

    [Fact]
    public async Task The_same_guid_in_two_different_feeds_is_two_items()
    {
        var otherFeed = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://other.example/feed.xml" });

        await _items.UpsertAsync(NewItem("shared-guid"));
        await _items.UpsertAsync(NewItem("shared-guid") with { FeedId = otherFeed });

        var mine = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        var theirs = await _items.QueryAsync(new ItemQuery(otherFeed, null, ItemFilter.All, 100, 0));
        Assert.Single(mine);
        Assert.Single(theirs);
    }

    [Fact]
    public async Task Unread_filter_returns_only_unread_items()
    {
        var readId = await _items.UpsertAsync(NewItem("guid-1"));
        await _items.UpsertAsync(NewItem("guid-2"));
        await _items.SetReadAsync(readId, true);

        var unread = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.Unread, 100, 0));

        Assert.Single(unread);
        Assert.Equal("guid-2", unread[0].Guid);
    }

    [Fact]
    public async Task Starred_filter_crosses_feeds()
    {
        var otherFeed = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://other.example/feed.xml" });
        var a = await _items.UpsertAsync(NewItem("guid-1"));
        var b = await _items.UpsertAsync(NewItem("guid-2") with { FeedId = otherFeed });
        await _items.SetStarredAsync(a, true);
        await _items.SetStarredAsync(b, true);

        var starred = await _items.QueryAsync(new ItemQuery(null, null, ItemFilter.Starred, 100, 0));

        Assert.Equal(2, starred.Count);
    }

    [Fact]
    public async Task Items_come_back_newest_first()
    {
        await _items.UpsertAsync(NewItem("old") with
        {
            PublishedUtc = DateTimeOffset.Parse("2026-08-01T09:00:00Z")
        });
        await _items.UpsertAsync(NewItem("new") with
        {
            PublishedUtc = DateTimeOffset.Parse("2026-08-27T09:00:00Z")
        });

        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));

        Assert.Equal("new", all[0].Guid);
        Assert.Equal("old", all[1].Guid);
    }

    [Fact]
    public async Task An_item_with_no_published_date_sorts_by_when_we_first_saw_it()
    {
        await _items.UpsertAsync(NewItem("dated") with
        {
            PublishedUtc = DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-01T09:00:00Z")
        });
        await _items.UpsertAsync(NewItem("undated") with
        {
            PublishedUtc = null,
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
        });

        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));

        Assert.Equal("undated", all[0].Guid);
    }

    [Fact]
    public async Task Marking_a_whole_feed_read_clears_its_unread_count()
    {
        await _items.UpsertAsync(NewItem("guid-1"));
        await _items.UpsertAsync(NewItem("guid-2"));

        await _items.MarkFeedReadAsync(_feedId);

        Assert.Equal(0, await _items.GetUnreadCountAsync(_feedId));
    }

    [Fact]
    public async Task Pending_offline_items_are_returned_for_download()
    {
        var id = await _items.UpsertAsync(NewItem() with { OfflineState = OfflineState.Pending });
        await _items.UpsertAsync(NewItem("guid-2"));

        var pending = await _items.GetPendingOfflineAsync(limit: 10);

        Assert.Single(pending);
        Assert.Equal(id, pending[0].Id);
    }

    [Fact]
    public async Task A_failed_download_records_the_error_and_keeps_the_summary()
    {
        var id = await _items.UpsertAsync(NewItem() with { OfflineState = OfflineState.Pending });

        await _items.SetOfflineFailedAsync(id, "404 Not Found");

        var loaded = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, loaded!.OfflineState);
        Assert.Equal("404 Not Found", loaded.OfflineError);
        Assert.Equal("A summary.", loaded.Summary);
    }

    [Fact]
    public async Task Deleting_a_feed_deletes_its_items()
    {
        await _items.UpsertAsync(NewItem());

        await new FeedRepository(_db).DeleteAsync(_feedId);

        var all = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Empty(all);
    }
}
```

The three preservation tests are the important ones. A feed that republishes an item with a corrected title must not resurrect it as unread, un-star it, or throw away an article we already spent a network round trip extracting. Getting this wrong is the classic RSS reader bug where a publisher's typo fix marks fifty items unread.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ItemRepositoryTests 2>&1 | tail -10
```

Expected: compilation failure, `ItemRepository` does not exist.

- [ ] **Step 3: Write ItemQuery**

Create `LucidReader.Core/Storage/ItemQuery.cs`:

```csharp
namespace LucidReader.Core.Storage;

public enum ItemFilter
{
    All = 0,
    Unread = 1,
    Starred = 2
}

/// <summary>
/// A single item-list query. FeedId and FolderId are both optional: null for
/// both means "across every feed", which is what the All items, Unread and
/// Starred smart rows use.
/// </summary>
public readonly record struct ItemQuery(
    long? FeedId,
    long? FolderId,
    ItemFilter Filter,
    int Limit,
    int Offset);
```

- [ ] **Step 4: Write ItemRepository**

Create `LucidReader.Core/Storage/ItemRepository.cs`:

```csharp
using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class ItemRepository(ReaderDatabase db)
{
    /// <summary>
    /// Inserts, or updates the publisher-owned fields when we have seen this
    /// (feed_id, guid) before. Reader-owned state (read, starred, content we
    /// downloaded, offline state) is deliberately never touched by an upsert:
    /// a publisher fixing a typo must not mark fifty items unread.
    /// </summary>
    private const string UpsertSql =
        """
        INSERT INTO items (
            feed_id, guid, link, title, author, published_utc, updated_utc,
            summary, content_markdown, content_source, is_read, is_starred,
            first_seen_utc, offline_state, offline_error)
        VALUES (
            $feedId, $guid, $link, $title, $author, $published, $updated,
            $summary, $content, $contentSource, $isRead, $isStarred,
            $firstSeen, $offlineState, $offlineError)
        ON CONFLICT(feed_id, guid) DO UPDATE SET
            link = excluded.link,
            title = excluded.title,
            author = excluded.author,
            published_utc = excluded.published_utc,
            updated_utc = excluded.updated_utc,
            summary = excluded.summary;
        """;

    public async Task<long> UpsertAsync(FeedItem item, CancellationToken ct = default)
    {
        await db.WriteAsync(UpsertSql, BuildParameters(item), ct);
        return await db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM items WHERE feed_id = $feedId AND guid = $guid;";
            command.Parameters.AddWithValue("$feedId", item.FeedId);
            command.Parameters.AddWithValue("$guid", item.Guid);
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }, ct);
    }

    /// <summary>
    /// Returns how many rows were newly inserted, which is what the caller
    /// needs in order to queue only genuinely new items for offline download.
    /// </summary>
    public async Task<int> UpsertManyAsync(
        IReadOnlyList<FeedItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return 0;

        var before = await CountAsync(items[0].FeedId, ct);
        var commands = items.Select(item => (UpsertSql, (object?)BuildParameters(item)));
        await db.Writer.WriteBatchAsync(commands, ct);
        var after = await CountAsync(items[0].FeedId, ct);
        return after - before;
    }

    public Task<FeedItem?> GetAsync(long id, CancellationToken ct = default) =>
        db.QueryAsync<FeedItem?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadItem((SqliteDataReader)reader) : null;
        }, ct);

    public Task<IReadOnlyList<FeedItem>> QueryAsync(
        ItemQuery query,
        CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            var where = new List<string>();
            await using var command = connection.CreateCommand();

            if (query.FeedId is { } feedId)
            {
                where.Add("i.feed_id = $feedId");
                command.Parameters.AddWithValue("$feedId", feedId);
            }

            if (query.FolderId is { } folderId)
            {
                where.Add("f.folder_id = $folderId");
                command.Parameters.AddWithValue("$folderId", folderId);
            }

            switch (query.Filter)
            {
                case ItemFilter.Unread:
                    where.Add("i.is_read = 0");
                    break;
                case ItemFilter.Starred:
                    where.Add("i.is_starred = 1");
                    break;
            }

            var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            // COALESCE so an item with no published date sorts by when we first
            // saw it, rather than sinking to the bottom of the list forever.
            command.CommandText =
                $"""
                 SELECT i.* FROM items i
                 JOIN feeds f ON f.id = i.feed_id
                 {whereClause}
                 ORDER BY COALESCE(i.published_utc, i.first_seen_utc) DESC, i.id DESC
                 LIMIT $limit OFFSET $offset;
                 """;
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);

            var results = new List<FeedItem>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(ReadItem((SqliteDataReader)reader));
            return results;
        }, ct);

    public Task<IReadOnlyList<FeedItem>> GetPendingOfflineAsync(
        int limit,
        CancellationToken ct = default) =>
        db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM items
                WHERE offline_state = 1
                ORDER BY COALESCE(published_utc, first_seen_utc) DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);

            var results = new List<FeedItem>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(ReadItem((SqliteDataReader)reader));
            return results;
        }, ct);

    public Task SetReadAsync(long id, bool isRead, CancellationToken ct = default) =>
        db.WriteAsync("UPDATE items SET is_read = $value WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id, ["$value"] = isRead ? 1 : 0 }, ct);

    public Task SetStarredAsync(long id, bool isStarred, CancellationToken ct = default) =>
        db.WriteAsync("UPDATE items SET is_starred = $value WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id, ["$value"] = isStarred ? 1 : 0 }, ct);

    public Task MarkFeedReadAsync(long feedId, CancellationToken ct = default) =>
        db.WriteAsync("UPDATE items SET is_read = 1 WHERE feed_id = $feedId AND is_read = 0;",
            new Dictionary<string, object?> { ["$feedId"] = feedId }, ct);

    public Task SetContentAsync(
        long id, string markdown, ContentSource source, CancellationToken ct = default) =>
        db.WriteAsync(
            """
            UPDATE items SET
                content_markdown = $content,
                content_source = $source,
                offline_state = 2,
                offline_error = NULL
            WHERE id = $id;
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = id,
                ["$content"] = markdown,
                ["$source"] = (int)source
            }, ct);

    public Task SetOfflineFailedAsync(long id, string error, CancellationToken ct = default) =>
        db.WriteAsync(
            "UPDATE items SET offline_state = 3, offline_error = $error WHERE id = $id;",
            new Dictionary<string, object?> { ["$id"] = id, ["$error"] = error }, ct);

    public Task<int> GetUnreadCountAsync(long feedId, CancellationToken ct = default) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM items WHERE feed_id = $feedId AND is_read = 0;";
            command.Parameters.AddWithValue("$feedId", feedId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }, ct);

    private Task<int> CountAsync(long feedId, CancellationToken ct) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM items WHERE feed_id = $feedId;";
            command.Parameters.AddWithValue("$feedId", feedId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }, ct);

    private static Dictionary<string, object?> BuildParameters(FeedItem item) => new()
    {
        ["$feedId"] = item.FeedId,
        ["$guid"] = item.Guid,
        ["$link"] = item.Link,
        ["$title"] = item.Title,
        ["$author"] = item.Author,
        ["$published"] = item.PublishedUtc.ToDbString(),
        ["$updated"] = item.UpdatedUtc.ToDbString(),
        ["$summary"] = item.Summary,
        ["$content"] = item.ContentMarkdown,
        ["$contentSource"] = (int)item.ContentSource,
        ["$isRead"] = item.IsRead ? 1 : 0,
        ["$isStarred"] = item.IsStarred ? 1 : 0,
        ["$firstSeen"] = item.FirstSeenUtc.ToDbString(),
        ["$offlineState"] = (int)item.OfflineState,
        ["$offlineError"] = item.OfflineError
    };

    private static FeedItem ReadItem(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        FeedId = reader.GetInt64(reader.GetOrdinal("feed_id")),
        Guid = reader.GetString(reader.GetOrdinal("guid")),
        Link = reader.GetNullableString("link"),
        Title = reader.GetNullableString("title"),
        Author = reader.GetNullableString("author"),
        PublishedUtc = reader.GetNullableDate("published_utc"),
        UpdatedUtc = reader.GetNullableDate("updated_utc"),
        Summary = reader.GetNullableString("summary"),
        ContentMarkdown = reader.GetNullableString("content_markdown"),
        ContentSource = (ContentSource)reader.GetInt32(reader.GetOrdinal("content_source")),
        IsRead = reader.GetBool("is_read"),
        IsStarred = reader.GetBool("is_starred"),
        FirstSeenUtc = reader.GetDate("first_seen_utc"),
        OfflineState = (OfflineState)reader.GetInt32(reader.GetOrdinal("offline_state")),
        OfflineError = reader.GetNullableString("offline_error")
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter ItemRepositoryTests 2>&1 | tail -5
```

Expected: 14 passed.

If `UpsertManyAsync` fails, check the `WriteBatchAsync` signature against the installed 3.0.0 package. Its parameter list is `IEnumerable<(string Sql, object? Parameters)>`, and whether it accepts a dictionary as `Parameters` or reflects over a POCO matters here. If it only reflects, use the dictionary overload of `WriteAsync` in a loop inside `ExecuteInTransactionAsync` instead, and keep the return-count behaviour identical.

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core/Storage LucidReader.Core.Tests/Storage
git commit -m "feat(reader): item repository with guid dedupe and preserved reader state"
```

---

## Task 8: Full-text search

**Files:**
- Create: `LucidReader.Core/Storage/SearchRepository.cs`
- Test: `LucidReader.Core.Tests/Storage/SearchRepositoryTests.cs`

**Interfaces:**
- Consumes: `ReaderDatabase` (Task 5), `ItemRepository` (Task 7), `FeedItem` (Task 3).
- Produces: `sealed class SearchRepository(ReaderDatabase db)` with `Task<IReadOnlyList<FeedItem>> SearchAsync(string query, int limit, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Storage/SearchRepositoryTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class SearchRepositoryTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private SearchRepository _search = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _search = new SearchRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private async Task<long> AddAsync(string guid, string title, string? content = null)
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = guid,
            Title = title,
            FirstSeenUtc = DateTimeOffset.Parse("2026-08-28T10:00:00Z")
        });
        if (content is not null)
            await _items.SetContentAsync(id, content, ContentSource.Feed);
        return id;
    }

    [Fact]
    public async Task Searching_matches_on_title()
    {
        await AddAsync("a", "Avalonia rendering internals");
        await AddAsync("b", "Something unrelated");

        var results = await _search.SearchAsync("Avalonia", 50);

        Assert.Single(results);
        Assert.Equal("a", results[0].Guid);
    }

    [Fact]
    public async Task Searching_matches_on_article_body()
    {
        await AddAsync("a", "A title with nothing useful in it",
            "The body mentions SQLite and its writer lock.");
        await AddAsync("b", "Another title");

        var results = await _search.SearchAsync("writer lock", 50);

        Assert.Single(results);
        Assert.Equal("a", results[0].Guid);
    }

    [Fact]
    public async Task Content_added_after_insert_becomes_searchable()
    {
        var id = await AddAsync("a", "Placeholder title");

        await _items.SetContentAsync(id, "Now containing the word marmalade.", ContentSource.Extracted);

        var results = await _search.SearchAsync("marmalade", 50);
        Assert.Single(results);
    }

    [Fact]
    public async Task Deleting_an_item_removes_it_from_the_index()
    {
        await AddAsync("a", "Ephemeral coordinators");
        await new FeedRepository(_db).DeleteAsync(_feedId);

        var results = await _search.SearchAsync("Ephemeral", 50);

        Assert.Empty(results);
    }

    [Fact]
    public async Task A_query_with_fts_syntax_characters_does_not_throw()
    {
        await AddAsync("a", "Perfectly normal article");

        var results = await _search.SearchAsync("\"unbalanced quote AND (", 50);

        Assert.Empty(results);
    }

    [Fact]
    public async Task An_empty_query_returns_nothing_rather_than_everything()
    {
        await AddAsync("a", "Perfectly normal article");

        var results = await _search.SearchAsync("   ", 50);

        Assert.Empty(results);
    }
}
```

The last two matter more than they look. FTS5 `MATCH` treats its argument as a query language, so a user typing a stray quote or parenthesis into the search box produces a `SqliteException`, not zero results. Escaping is the fix, and these tests are what prove it.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SearchRepositoryTests 2>&1 | tail -10
```

Expected: compilation failure, `SearchRepository` does not exist.

- [ ] **Step 3: Write SearchRepository**

Create `LucidReader.Core/Storage/SearchRepository.cs`:

```csharp
using LucidReader.Core.Model;
using Microsoft.Data.Sqlite;

namespace LucidReader.Core.Storage;

public sealed class SearchRepository(ReaderDatabase db)
{
    public Task<IReadOnlyList<FeedItem>> SearchAsync(
        string query,
        int limit,
        CancellationToken ct = default)
    {
        var ftsQuery = ToFtsQuery(query);
        if (ftsQuery is null)
            return Task.FromResult<IReadOnlyList<FeedItem>>(Array.Empty<FeedItem>());

        return db.QueryAsync<IReadOnlyList<FeedItem>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT i.* FROM items_fts
                JOIN items i ON i.id = items_fts.rowid
                WHERE items_fts MATCH $query
                ORDER BY rank
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$query", ftsQuery);
            command.Parameters.AddWithValue("$limit", limit);

            var results = new List<FeedItem>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(ReadItem((SqliteDataReader)reader));
            return results;
        }, ct);
    }

    /// <summary>
    /// Turns whatever the user typed into a safe FTS5 query. Every term is
    /// wrapped in double quotes as a phrase literal, with inner quotes doubled,
    /// so a stray quote or parenthesis is searched for rather than parsed as
    /// FTS5 syntax and thrown back as an exception. Returns null for a query
    /// with no usable terms.
    /// </summary>
    private static string? ToFtsQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var terms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim('"', '(', ')', '*', ':', '^'))
            .Where(term => term.Length > 0)
            .Select(term => "\"" + term.Replace("\"", "\"\"") + "\"")
            .ToList();

        return terms.Count == 0 ? null : string.Join(" ", terms);
    }

    private static FeedItem ReadItem(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        FeedId = reader.GetInt64(reader.GetOrdinal("feed_id")),
        Guid = reader.GetString(reader.GetOrdinal("guid")),
        Link = reader.GetNullableString("link"),
        Title = reader.GetNullableString("title"),
        Author = reader.GetNullableString("author"),
        PublishedUtc = reader.GetNullableDate("published_utc"),
        UpdatedUtc = reader.GetNullableDate("updated_utc"),
        Summary = reader.GetNullableString("summary"),
        ContentMarkdown = reader.GetNullableString("content_markdown"),
        ContentSource = (ContentSource)reader.GetInt32(reader.GetOrdinal("content_source")),
        IsRead = reader.GetBool("is_read"),
        IsStarred = reader.GetBool("is_starred"),
        FirstSeenUtc = reader.GetDate("first_seen_utc"),
        OfflineState = (OfflineState)reader.GetInt32(reader.GetOrdinal("offline_state")),
        OfflineError = reader.GetNullableString("offline_error")
    };
}
```

Multi-word queries join as space-separated phrases, which FTS5 treats as an implicit AND. "writer lock" therefore finds documents containing both words, which is what a user expects from a search box.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter SearchRepositoryTests 2>&1 | tail -5
```

Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add LucidReader.Core/Storage LucidReader.Core.Tests/Storage
git commit -m "feat(reader): FTS5 full-text search with query escaping"
```

---

## Task 9: Settings and per-feed override resolution

**Files:**
- Create: `LucidReader.Core/Model/ReaderSettings.cs`
- Create: `LucidReader.Core/Model/EffectiveFeedSettings.cs`
- Create: `LucidReader.Core/Storage/SettingsStore.cs`
- Test: `LucidReader.Core.Tests/Model/EffectiveFeedSettingsTests.cs`
- Test: `LucidReader.Core.Tests/Storage/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `Feed` (Task 3).
- Produces:
  - `sealed record ReaderSettings` with the global defaults, plus `static ReaderSettings Defaults { get; }`.
  - `readonly record struct EffectiveFeedSettings(TimeSpan RefreshInterval, bool AutoDownload, bool FetchFullText, int? RetentionDays)` and `static EffectiveFeedSettings Resolve(Feed feed, ReaderSettings globals)`.
  - `sealed class SettingsStore` with `static Task<ReaderSettings> LoadAsync(string path, CancellationToken ct = default)` and `static Task SaveAsync(string path, ReaderSettings settings, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing resolution tests**

This is the rule the whole settings UI depends on, so it gets pinned before anything renders it. Create `LucidReader.Core.Tests/Model/EffectiveFeedSettingsTests.cs`:

```csharp
using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Model;

public class EffectiveFeedSettingsTests
{
    private static readonly ReaderSettings Globals = ReaderSettings.Defaults with
    {
        DefaultRefreshIntervalMinutes = 30,
        AutoDownloadArticles = true,
        FetchFullText = true,
        KeepReadArticlesDays = 30
    };

    private static Feed Feed() => new() { FeedUrl = "https://example.com/feed.xml" };

    [Fact]
    public void A_feed_with_no_overrides_inherits_every_global()
    {
        var effective = EffectiveFeedSettings.Resolve(Feed(), Globals);

        Assert.Equal(TimeSpan.FromMinutes(30), effective.RefreshInterval);
        Assert.True(effective.AutoDownload);
        Assert.True(effective.FetchFullText);
        Assert.Equal(30, effective.RetentionDays);
    }

    [Fact]
    public void An_override_wins_over_the_global()
    {
        var feed = Feed() with { RefreshIntervalMinutes = 5 };

        var effective = EffectiveFeedSettings.Resolve(feed, Globals);

        Assert.Equal(TimeSpan.FromMinutes(5), effective.RefreshInterval);
    }

    [Fact]
    public void A_false_override_is_respected_and_not_mistaken_for_unset()
    {
        var feed = Feed() with { AutoDownload = false };

        var effective = EffectiveFeedSettings.Resolve(feed, Globals);

        Assert.False(effective.AutoDownload);
    }

    [Fact]
    public void Changing_a_global_moves_every_non_overridden_feed()
    {
        var feed = Feed();

        var before = EffectiveFeedSettings.Resolve(feed, Globals);
        var after = EffectiveFeedSettings.Resolve(
            feed, Globals with { DefaultRefreshIntervalMinutes = 120 });

        Assert.Equal(TimeSpan.FromMinutes(30), before.RefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(120), after.RefreshInterval);
    }

    [Fact]
    public void Changing_a_global_leaves_an_overridden_feed_alone()
    {
        var feed = Feed() with { RefreshIntervalMinutes = 5 };

        var after = EffectiveFeedSettings.Resolve(
            feed, Globals with { DefaultRefreshIntervalMinutes = 120 });

        Assert.Equal(TimeSpan.FromMinutes(5), after.RefreshInterval);
    }

    [Fact]
    public void Keeping_unread_forever_resolves_to_no_retention_limit()
    {
        var globals = Globals with { KeepUnreadForever = true };

        var effective = EffectiveFeedSettings.Resolve(Feed(), globals);

        Assert.Equal(30, effective.RetentionDays);
        Assert.True(globals.KeepUnreadForever);
    }

    [Fact]
    public void A_refresh_interval_below_the_floor_is_clamped()
    {
        var feed = Feed() with { RefreshIntervalMinutes = 0 };

        var effective = EffectiveFeedSettings.Resolve(feed, Globals);

        Assert.Equal(ReaderSettings.MinimumRefreshInterval, effective.RefreshInterval);
    }
}
```

The clamp in the last test exists so no combination of settings can produce a hot loop against someone's server.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter EffectiveFeedSettingsTests 2>&1 | tail -10
```

Expected: compilation failure, the types do not exist.

- [ ] **Step 3: Write ReaderSettings**

Create `LucidReader.Core/Model/ReaderSettings.cs`:

```csharp
namespace LucidReader.Core.Model;

/// <summary>
/// Global defaults. Every field here has a matching nullable override on Feed;
/// null on the feed means "use the value from here".
/// </summary>
public sealed record ReaderSettings
{
    /// <summary>
    /// No setting combination may poll a server faster than this. It is a floor
    /// on the whole app, not a default.
    /// </summary>
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(5);

    // Updates
    public int DefaultRefreshIntervalMinutes { get; init; } = 30;
    public bool RefreshOnStartup { get; init; } = true;
    public bool PauseWhenOffline { get; init; } = true;
    public int MaxConcurrentFetches { get; init; } = 4;

    // Offline
    public bool AutoDownloadArticles { get; init; } = true;
    public bool FetchFullText { get; init; } = true;
    public bool CacheImages { get; init; } = true;
    public int MaxImageBytes { get; init; } = 5 * 1024 * 1024;
    public int MaxConcurrentDownloads { get; init; } = 2;

    // Retention
    public int KeepReadArticlesDays { get; init; } = 30;
    public bool KeepUnreadForever { get; init; } = true;
    public int KeepUnreadDays { get; init; } = 180;
    public int MaxArticlesPerFeed { get; init; } = 500;
    public bool NeverDeleteStarred { get; init; } = true;

    // Reading
    public string Theme { get; init; } = "Auto";
    public double FontSize { get; init; } = 15;
    public double ColumnWidth { get; init; } = 760;
    public int MarkReadDwellMilliseconds { get; init; } = 800;
    public bool OpenLinksExternally { get; init; } = true;

    public static ReaderSettings Defaults { get; } = new();
}
```

- [ ] **Step 4: Write EffectiveFeedSettings**

Create `LucidReader.Core/Model/EffectiveFeedSettings.cs`:

```csharp
namespace LucidReader.Core.Model;

/// <summary>
/// A feed's settings after its overrides have been layered over the globals.
/// Everything downstream of the settings UI works with this, never with the
/// raw nullable fields, so the inherit-versus-override rule lives in exactly
/// one place.
/// </summary>
public readonly record struct EffectiveFeedSettings(
    TimeSpan RefreshInterval,
    bool AutoDownload,
    bool FetchFullText,
    int? RetentionDays)
{
    public static EffectiveFeedSettings Resolve(Feed feed, ReaderSettings globals)
    {
        var minutes = feed.RefreshIntervalMinutes ?? globals.DefaultRefreshIntervalMinutes;
        var interval = TimeSpan.FromMinutes(minutes);
        if (interval < ReaderSettings.MinimumRefreshInterval)
            interval = ReaderSettings.MinimumRefreshInterval;

        return new EffectiveFeedSettings(
            interval,
            feed.AutoDownload ?? globals.AutoDownloadArticles,
            feed.FetchFullText ?? globals.FetchFullText,
            feed.RetentionDays ?? globals.KeepReadArticlesDays);
    }
}
```

- [ ] **Step 5: Write the settings store and its tests**

Create `LucidReader.Core/Storage/SettingsStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using LucidReader.Core.Model;

namespace LucidReader.Core.Storage;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Returns the defaults when the file is missing or unreadable. A corrupt
    /// settings file must not stop the app opening; the user's feeds and
    /// articles are in the database, and those are what matter.
    /// </summary>
    public static async Task<ReaderSettings> LoadAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path)) return ReaderSettings.Defaults;

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<ReaderSettings>(stream, Options, ct);
            return loaded ?? ReaderSettings.Defaults;
        }
        catch (JsonException)
        {
            return ReaderSettings.Defaults;
        }
        catch (IOException)
        {
            return ReaderSettings.Defaults;
        }
    }

    /// <summary>
    /// Writes to a temp file and moves it into place, so an interrupted save
    /// cannot leave a half-written settings file behind.
    /// </summary>
    public static async Task SaveAsync(
        string path,
        ReaderSettings settings,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, ct);
        }

        File.Move(temp, path, overwrite: true);
    }
}
```

Create `LucidReader.Core.Tests/Storage/SettingsStoreTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lucidreader-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task A_missing_file_yields_the_defaults()
    {
        var settings = await SettingsStore.LoadAsync(Path_);

        Assert.Equal(ReaderSettings.Defaults, settings);
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        var original = ReaderSettings.Defaults with
        {
            DefaultRefreshIntervalMinutes = 90,
            AutoDownloadArticles = false,
            Theme = "Dark"
        };

        await SettingsStore.SaveAsync(Path_, original);
        var loaded = await SettingsStore.LoadAsync(Path_);

        Assert.Equal(original, loaded);
    }

    [Fact]
    public async Task A_corrupt_file_falls_back_to_the_defaults_rather_than_throwing()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path_, "{ this is not json");

        var settings = await SettingsStore.LoadAsync(Path_);

        Assert.Equal(ReaderSettings.Defaults, settings);
    }

    [Fact]
    public async Task Saving_leaves_no_temp_file_behind()
    {
        await SettingsStore.SaveAsync(Path_, ReaderSettings.Defaults);

        Assert.False(File.Exists(Path_ + ".tmp"));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter "EffectiveFeedSettingsTests|SettingsStoreTests" 2>&1 | tail -5
```

Expected: 11 passed.

- [ ] **Step 7: Commit**

```bash
git add LucidReader.Core/Model LucidReader.Core/Storage LucidReader.Core.Tests
git commit -m "feat(reader): global settings and per-feed override resolution"
```

---

## Task 10: The feed fixture corpus and parser contract

**Files:**
- Create: `LucidReader.Core/Feeds/ParsedFeed.cs`
- Create: `LucidReader.Core/Feeds/ParsedItem.cs`
- Create: `LucidReader.Core/Feeds/IFeedParser.cs`
- Create: `LucidReader.Core.Tests/Fixtures/Feeds/*.xml` (nine files, contents below)
- Test: `LucidReader.Core.Tests/Feeds/FixtureCorpusTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `sealed record ParsedFeed(string? Title, string? SiteUrl, IReadOnlyList<ParsedItem> Items, int SkippedItemCount)`
  - `sealed record ParsedItem` with `Guid`, `Link`, `Title`, `Author`, `PublishedUtc`, `UpdatedUtc`, `Summary`, `ContentHtml`
  - `interface IFeedParser { bool CanParse(string content); ParsedFeed Parse(string content, Uri sourceUri); }`

- [ ] **Step 1: Write the parser output types**

Create `LucidReader.Core/Feeds/ParsedItem.cs`:

```csharp
namespace LucidReader.Core.Feeds;

/// <summary>
/// One item as the feed published it. Deliberately separate from FeedItem:
/// this is what a remote server said, not what we have decided to store.
/// Guid is nullable here because plenty of feeds omit it; the storage layer
/// is what fills in a link-hash fallback.
/// </summary>
public sealed record ParsedItem
{
    public string? Guid { get; init; }
    public string? Link { get; init; }
    public string? Title { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public string? Summary { get; init; }

    /// <summary>
    /// The richest content the feed offered: content:encoded for RSS, or an
    /// Atom content element, falling back to the description or summary.
    /// Still HTML at this point; conversion to markdown happens later.
    /// </summary>
    public string? ContentHtml { get; init; }
}
```

Create `LucidReader.Core/Feeds/ParsedFeed.cs`:

```csharp
namespace LucidReader.Core.Feeds;

/// <summary>
/// SkippedItemCount records items the parser could not read at all. Partial
/// success is the normal case with real feeds: eighteen good items out of
/// twenty is a successful fetch, not a failure, but we surface the two.
/// </summary>
public sealed record ParsedFeed(
    string? Title,
    string? SiteUrl,
    IReadOnlyList<ParsedItem> Items,
    int SkippedItemCount);
```

Create `LucidReader.Core/Feeds/IFeedParser.cs`:

```csharp
namespace LucidReader.Core.Feeds;

public interface IFeedParser
{
    /// <summary>
    /// A cheap look at the document to decide whether this parser should try.
    /// Never throws: a false return means "not mine", not "malformed".
    /// </summary>
    bool CanParse(string content);

    /// <summary>
    /// Parses, or throws FeedParseException when the document is unreadable.
    /// sourceUri resolves relative links.
    /// </summary>
    ParsedFeed Parse(string content, Uri sourceUri);
}

public sealed class FeedParseException(string message, Exception? inner = null)
    : Exception(message, inner);
```

- [ ] **Step 2: Create the fixture corpus**

These files are the specification for Tasks 11 and 12. Create the directory and each file exactly as given. Every one encodes a real-world behaviour that broke a reader at some point.

```bash
mkdir -p LucidReader.Core.Tests/Fixtures/Feeds
```

`LucidReader.Core.Tests/Fixtures/Feeds/rss2-simple.xml`: the happy path.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Example Blog</title>
    <link>https://example.com/</link>
    <description>An example.</description>
    <item>
      <title>First post</title>
      <link>https://example.com/first</link>
      <guid isPermaLink="false">tag:example.com,2026:1</guid>
      <pubDate>Wed, 26 Aug 2026 09:00:00 GMT</pubDate>
      <description>A short summary of the first post.</description>
    </item>
    <item>
      <title>Second post</title>
      <link>https://example.com/second</link>
      <guid isPermaLink="false">tag:example.com,2026:2</guid>
      <pubDate>Thu, 27 Aug 2026 09:00:00 GMT</pubDate>
      <description>A short summary of the second post.</description>
    </item>
  </channel>
</rss>
```

`LucidReader.Core.Tests/Fixtures/Feeds/rss2-content-encoded.xml`: full text in `content:encoded`, which `SyndicationFeed` does not surface as content. This is the single most common reason a reader shows a stub when the full article was right there.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/"
     xmlns:dc="http://purl.org/dc/elements/1.1/">
  <channel>
    <title>Full Text Blog</title>
    <link>https://fulltext.example/</link>
    <item>
      <title>An article with its whole body in the feed</title>
      <link>https://fulltext.example/article</link>
      <guid>https://fulltext.example/article</guid>
      <dc:creator>Jo Bloggs</dc:creator>
      <dc:date>2026-08-27T09:00:00Z</dc:date>
      <description>Just the first sentence.</description>
      <content:encoded><![CDATA[<p>The first paragraph.</p><p>The second paragraph, which the description does not contain.</p>]]></content:encoded>
    </item>
  </channel>
</rss>
```

`LucidReader.Core.Tests/Fixtures/Feeds/atom-simple.xml`: Atom, with `updated` differing from `published`.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<feed xmlns="http://www.w3.org/2005/Atom">
  <title>Atom Example</title>
  <link href="https://atom.example/"/>
  <updated>2026-08-27T10:00:00Z</updated>
  <entry>
    <title>An Atom entry</title>
    <link href="https://atom.example/entry-1"/>
    <id>urn:uuid:1225c695-cfb8-4ebb-aaaa-80da344efa6a</id>
    <published>2026-08-26T09:00:00Z</published>
    <updated>2026-08-27T11:30:00Z</updated>
    <author><name>Sam Reader</name></author>
    <summary>A summary.</summary>
    <content type="html">&lt;p&gt;The full entry body.&lt;/p&gt;</content>
  </entry>
</feed>
```

`LucidReader.Core.Tests/Fixtures/Feeds/rdf-rss1.xml`: RSS 1.0/RDF, which is a different document shape entirely.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
         xmlns="http://purl.org/rss/1.0/"
         xmlns:dc="http://purl.org/dc/elements/1.1/">
  <channel rdf:about="https://rdf.example/">
    <title>RDF Example</title>
    <link>https://rdf.example/</link>
    <description>An RSS 1.0 feed.</description>
  </channel>
  <item rdf:about="https://rdf.example/item-1">
    <title>An RDF item</title>
    <link>https://rdf.example/item-1</link>
    <description>The description.</description>
    <dc:date>2026-08-27T09:00:00Z</dc:date>
    <dc:creator>Pat Writer</dc:creator>
  </item>
</rdf:RDF>
```

`LucidReader.Core.Tests/Fixtures/Feeds/rss2-bad-dates.xml`: three date formats that are all technically wrong and all common in the wild.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Bad Dates</title>
    <link>https://baddates.example/</link>
    <item>
      <title>Missing the day name</title>
      <link>https://baddates.example/1</link>
      <guid>https://baddates.example/1</guid>
      <pubDate>26 Aug 2026 09:00:00 GMT</pubDate>
    </item>
    <item>
      <title>ISO date in a pubDate element</title>
      <link>https://baddates.example/2</link>
      <guid>https://baddates.example/2</guid>
      <pubDate>2026-08-27T09:00:00Z</pubDate>
    </item>
    <item>
      <title>Complete nonsense</title>
      <link>https://baddates.example/3</link>
      <guid>https://baddates.example/3</guid>
      <pubDate>last Tuesday-ish</pubDate>
    </item>
  </channel>
</rss>
```

`LucidReader.Core.Tests/Fixtures/Feeds/rss2-no-guid.xml`: no guid anywhere, so dedupe has to fall back to the link.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>No Guids</title>
    <link>https://noguid.example/</link>
    <item>
      <title>An item with no guid</title>
      <link>https://noguid.example/article-1</link>
      <pubDate>Wed, 26 Aug 2026 09:00:00 GMT</pubDate>
      <description>A summary.</description>
    </item>
  </channel>
</rss>
```

`LucidReader.Core.Tests/Fixtures/Feeds/rss2-relative-links.xml`: relative hrefs that must be resolved against the feed URL.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Relative Links</title>
    <link>/</link>
    <item>
      <title>An item with a relative link</title>
      <link>/posts/article-1</link>
      <guid isPermaLink="false">relative-1</guid>
      <description>A summary.</description>
    </item>
  </channel>
</rss>
```

`LucidReader.Core.Tests/Fixtures/Feeds/rss2-undeclared-entity.xml`: a raw `&nbsp;`, which is undeclared in XML and makes a strict parser throw on the whole document. One bad character must not cost the user the other items.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Entity Trouble</title>
    <link>https://entity.example/</link>
    <item>
      <title>An item with a raw&nbsp;entity</title>
      <link>https://entity.example/1</link>
      <guid>https://entity.example/1</guid>
      <description>A summary.</description>
    </item>
    <item>
      <title>A perfectly fine item</title>
      <link>https://entity.example/2</link>
      <guid>https://entity.example/2</guid>
      <description>Another summary.</description>
    </item>
  </channel>
</rss>
```

`LucidReader.Core.Tests/Fixtures/Feeds/not-a-feed.html`: an HTML page served where a feed was expected, usually a login wall or a soft 404.

```html
<!DOCTYPE html>
<html><head><title>Sign in</title></head>
<body><h1>Please sign in to continue</h1></body></html>
```

- [ ] **Step 3: Write the corpus guard test**

This one test stops the corpus rotting: fixtures that stop being loaded, or a fixture added without a test, are both caught here. Create `LucidReader.Core.Tests/Feeds/FixtureCorpusTests.cs`:

```csharp
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public static class Fixtures
{
    public static string Feed(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Feeds", name));

    public static IReadOnlyList<string> AllFeedFiles() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Feeds"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name)
            .ToList();
}

public class FixtureCorpusTests
{
    [Fact]
    public void Every_fixture_is_copied_to_the_test_output()
    {
        var files = Fixtures.AllFeedFiles();

        Assert.Contains("rss2-simple.xml", files);
        Assert.Contains("rss2-content-encoded.xml", files);
        Assert.Contains("atom-simple.xml", files);
        Assert.Contains("rdf-rss1.xml", files);
        Assert.Contains("rss2-bad-dates.xml", files);
        Assert.Contains("rss2-no-guid.xml", files);
        Assert.Contains("rss2-relative-links.xml", files);
        Assert.Contains("rss2-undeclared-entity.xml", files);
        Assert.Contains("not-a-feed.html", files);
    }

    [Fact]
    public void Every_fixture_has_content()
    {
        foreach (var name in Fixtures.AllFeedFiles())
            Assert.False(string.IsNullOrWhiteSpace(Fixtures.Feed(name)), $"{name} is empty.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FixtureCorpusTests 2>&1 | tail -5
```

Expected: 2 passed.

A failure here almost certainly means the `<None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />` item from Task 3 Step 2 is missing or the path separator is wrong on this platform. Fix the csproj rather than the test.

- [ ] **Step 5: Commit**

```bash
git add LucidReader.Core/Feeds LucidReader.Core.Tests/Fixtures LucidReader.Core.Tests/Feeds
git commit -m "test(reader): feed fixture corpus and parser contract types"
```

---

## Task 11: The feed parser

> **Deviation from the spec, approved before implementation.** Spec section 4.1
> names `System.ServiceModel.Syndication` as the primary parser. It is not used.
> It throws on a malformed `pubDate` and loses the whole document, it does not
> surface `content:encoded`, and it does not read RSS 1.0/RDF. Three of the nine
> fixtures in Task 10 fail against it. A LINQ-to-XML parser handles all three
> formats uniformly and degrades per item rather than per document. The
> `System.ServiceModel.Syndication` PackageReference added in Task 3 should be
> removed.

**Files:**
- Create: `LucidReader.Core/Feeds/FeedDateParser.cs`
- Create: `LucidReader.Core/Feeds/FeedParser.cs`
- Modify: `LucidReader.Core/LucidReader.Core.csproj` (remove the syndication package)
- Test: `LucidReader.Core.Tests/Feeds/FeedDateParserTests.cs`
- Test: `LucidReader.Core.Tests/Feeds/FeedParserTests.cs`

**Interfaces:**
- Consumes: `ParsedFeed`, `ParsedItem`, `IFeedParser`, `FeedParseException` (Task 10).
- Produces:
  - `static class FeedDateParser { static DateTimeOffset? TryParse(string? value); }`
  - `sealed class FeedParser : IFeedParser`

- [ ] **Step 1: Write the failing date parser tests**

Dates get their own type and their own tests because they are where feeds are most casually broken. Create `LucidReader.Core.Tests/Feeds/FeedDateParserTests.cs`:

```csharp
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedDateParserTests
{
    [Theory]
    [InlineData("Wed, 26 Aug 2026 09:00:00 GMT", "2026-08-26T09:00:00+00:00")]
    [InlineData("26 Aug 2026 09:00:00 GMT", "2026-08-26T09:00:00+00:00")]
    [InlineData("Wed, 26 Aug 2026 09:00:00 +0100", "2026-08-26T09:00:00+01:00")]
    [InlineData("2026-08-27T09:00:00Z", "2026-08-27T09:00:00+00:00")]
    [InlineData("2026-08-27T09:00:00+02:00", "2026-08-27T09:00:00+02:00")]
    [InlineData("2026-08-27", "2026-08-27T00:00:00+00:00")]
    public void Recognised_formats_parse(string input, string expected)
    {
        var parsed = FeedDateParser.TryParse(input);

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeOffset.Parse(expected), parsed!.Value);
    }

    [Theory]
    [InlineData("last Tuesday-ish")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0000-00-00")]
    public void Unrecognised_input_returns_null_rather_than_throwing(string? input)
    {
        Assert.Null(FeedDateParser.TryParse(input));
    }

    [Fact]
    public void Surrounding_whitespace_is_tolerated()
    {
        Assert.NotNull(FeedDateParser.TryParse("  Wed, 26 Aug 2026 09:00:00 GMT \n"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedDateParserTests 2>&1 | tail -10
```

Expected: compilation failure, `FeedDateParser` does not exist.

- [ ] **Step 3: Write FeedDateParser**

Create `LucidReader.Core/Feeds/FeedDateParser.cs`:

```csharp
using System.Globalization;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Feed dates are unreliable. A date we cannot read is null, never an
/// exception: an unparseable timestamp costs the item its sort position, and
/// nothing more. The item itself is still worth showing.
/// </summary>
public static class FeedDateParser
{
    private static readonly string[] Formats =
    [
        // RFC 822 as RSS specifies it, and the common variants that omit the
        // day name or use a two-digit year.
        "ddd, dd MMM yyyy HH:mm:ss zzz",
        "ddd, dd MMM yyyy HH:mm:ss K",
        "ddd, dd MMM yyyy HH:mm zzz",
        "ddd, dd MMM yyyy HH:mm K",
        "dd MMM yyyy HH:mm:ss zzz",
        "dd MMM yyyy HH:mm:ss K",
        "dd MMM yyyy HH:mm zzz",
        // ISO 8601, which plenty of RSS feeds use in a pubDate regardless of
        // what the specification says.
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.fffK",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd"
    ];

    public static DateTimeOffset? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();

        // "GMT" is not a zone designator DateTimeOffset understands, but it is
        // what most RSS feeds emit.
        var normalised = trimmed.EndsWith(" GMT", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(trimmed.AsSpan(0, trimmed.Length - 4), " +0000")
            : trimmed;

        if (DateTimeOffset.TryParseExact(
                normalised, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var exact))
            return exact;

        if (DateTimeOffset.TryParse(
                normalised, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var loose))
            return loose;

        return null;
    }
}
```

Note the RFC 822 formats use `zzz` with a normalised `+0000` rather than trying to teach `DateTimeOffset` the string "GMT". Attempting the latter is the usual source of a parser that works in London and fails everywhere else.

- [ ] **Step 4: Run the date tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedDateParserTests 2>&1 | tail -5
```

Expected: 12 passed.

- [ ] **Step 5: Write the failing parser tests**

Create `LucidReader.Core.Tests/Feeds/FeedParserTests.cs`:

```csharp
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedParserTests
{
    private static readonly Uri Source = new("https://example.com/feed.xml");
    private readonly FeedParser _parser = new();

    private ParsedFeed Parse(string fixture) =>
        _parser.Parse(Fixtures.Feed(fixture), Source);

    [Fact]
    public void Rss2_yields_the_channel_title_and_every_item()
    {
        var feed = Parse("rss2-simple.xml");

        Assert.Equal("Example Blog", feed.Title);
        Assert.Equal("https://example.com/", feed.SiteUrl);
        Assert.Equal(2, feed.Items.Count);
        Assert.Equal("First post", feed.Items[0].Title);
        Assert.Equal("tag:example.com,2026:1", feed.Items[0].Guid);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-26T09:00:00Z"),
            feed.Items[0].PublishedUtc);
    }

    [Fact]
    public void Content_encoded_beats_the_description_for_article_content()
    {
        var feed = Parse("rss2-content-encoded.xml");

        var item = Assert.Single(feed.Items);
        Assert.Contains("second paragraph", item.ContentHtml);
        Assert.Equal("Just the first sentence.", item.Summary);
    }

    [Fact]
    public void Dublin_core_supplies_the_author_and_date_when_rss_does_not()
    {
        var feed = Parse("rss2-content-encoded.xml");

        var item = Assert.Single(feed.Items);
        Assert.Equal("Jo Bloggs", item.Author);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T09:00:00Z"), item.PublishedUtc);
    }

    [Fact]
    public void Atom_is_parsed_including_the_distinct_updated_date()
    {
        var feed = Parse("atom-simple.xml");

        Assert.Equal("Atom Example", feed.Title);
        var entry = Assert.Single(feed.Items);
        Assert.Equal("urn:uuid:1225c695-cfb8-4ebb-aaaa-80da344efa6a", entry.Guid);
        Assert.Equal("https://atom.example/entry-1", entry.Link);
        Assert.Equal("Sam Reader", entry.Author);
        Assert.Equal(DateTimeOffset.Parse("2026-08-26T09:00:00Z"), entry.PublishedUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T11:30:00Z"), entry.UpdatedUtc);
        Assert.Contains("full entry body", entry.ContentHtml);
    }

    [Fact]
    public void Rss1_rdf_is_parsed_despite_its_different_document_shape()
    {
        var feed = Parse("rdf-rss1.xml");

        Assert.Equal("RDF Example", feed.Title);
        var item = Assert.Single(feed.Items);
        Assert.Equal("An RDF item", item.Title);
        Assert.Equal("Pat Writer", item.Author);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T09:00:00Z"), item.PublishedUtc);
    }

    [Fact]
    public void An_unparseable_date_costs_the_item_its_date_and_nothing_else()
    {
        var feed = Parse("rss2-bad-dates.xml");

        Assert.Equal(3, feed.Items.Count);
        Assert.NotNull(feed.Items[0].PublishedUtc);
        Assert.NotNull(feed.Items[1].PublishedUtc);
        Assert.Null(feed.Items[2].PublishedUtc);
        Assert.Equal("Complete nonsense", feed.Items[2].Title);
    }

    [Fact]
    public void An_item_with_no_guid_is_returned_with_a_null_guid()
    {
        var feed = Parse("rss2-no-guid.xml");

        var item = Assert.Single(feed.Items);
        Assert.Null(item.Guid);
        Assert.Equal("https://noguid.example/article-1", item.Link);
    }

    [Fact]
    public void Relative_links_are_resolved_against_the_feed_url()
    {
        var feed = Parse("rss2-relative-links.xml");

        var item = Assert.Single(feed.Items);
        Assert.Equal("https://example.com/posts/article-1", item.Link);
    }

    [Fact]
    public void An_undeclared_entity_does_not_cost_us_the_other_items()
    {
        var feed = Parse("rss2-undeclared-entity.xml");

        Assert.Equal(2, feed.Items.Count);
        Assert.Contains(feed.Items, item => item.Title == "A perfectly fine item");
    }

    [Fact]
    public void CanParse_rejects_an_html_page_served_instead_of_a_feed()
    {
        Assert.False(_parser.CanParse(Fixtures.Feed("not-a-feed.html")));
    }

    [Fact]
    public void Parsing_an_html_page_throws_a_feed_parse_exception()
    {
        Assert.Throws<FeedParseException>(
            () => _parser.Parse(Fixtures.Feed("not-a-feed.html"), Source));
    }

    [Theory]
    [InlineData("rss2-simple.xml")]
    [InlineData("rss2-content-encoded.xml")]
    [InlineData("atom-simple.xml")]
    [InlineData("rdf-rss1.xml")]
    [InlineData("rss2-bad-dates.xml")]
    [InlineData("rss2-no-guid.xml")]
    [InlineData("rss2-relative-links.xml")]
    [InlineData("rss2-undeclared-entity.xml")]
    public void CanParse_accepts_every_real_feed_in_the_corpus(string fixture)
    {
        Assert.True(_parser.CanParse(Fixtures.Feed(fixture)));
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedParserTests 2>&1 | tail -10
```

Expected: compilation failure, `FeedParser` does not exist.

- [ ] **Step 7: Write FeedParser**

Create `LucidReader.Core/Feeds/FeedParser.cs`:

```csharp
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Reads RSS 2.0, RSS 1.0/RDF and Atom with one LINQ-to-XML pass.
///
/// System.ServiceModel.Syndication is deliberately not used: it throws on a
/// malformed pubDate and loses the entire document, it does not surface
/// content:encoded, and it does not read RDF. Failing per item instead of per
/// document is the whole point of this class.
/// </summary>
public sealed partial class FeedParser : IFeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Rss1 = "http://purl.org/rss/1.0/";

    public bool CanParse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        var document = TryLoad(content);
        if (document?.Root is null) return false;

        var root = document.Root.Name;
        return root.LocalName is "rss" or "RDF"
               || (root.LocalName == "feed" && root.Namespace == Atom);
    }

    public ParsedFeed Parse(string content, Uri sourceUri)
    {
        var document = TryLoad(content)
            ?? throw new FeedParseException("The response is not well-formed XML.");

        var root = document.Root
            ?? throw new FeedParseException("The response has no root element.");

        return root.Name.LocalName switch
        {
            "rss" => ParseRss2(root, sourceUri),
            "RDF" => ParseRdf(root, sourceUri),
            "feed" when root.Name.Namespace == Atom => ParseAtom(root, sourceUri),
            _ => throw new FeedParseException(
                $"Unrecognised feed root element <{root.Name.LocalName}>.")
        };
    }

    /// <summary>
    /// Loads strictly, then retries once with undeclared HTML entities replaced
    /// by their numeric equivalents. A single stray &amp;nbsp; is not a reason
    /// to throw away a user's whole feed.
    /// </summary>
    private static XDocument? TryLoad(string content)
    {
        try
        {
            return XDocument.Parse(content, LoadOptions.None);
        }
        catch (XmlException)
        {
            try
            {
                return XDocument.Parse(ReplaceUndeclaredEntities(content), LoadOptions.None);
            }
            catch (XmlException)
            {
                return null;
            }
        }
    }

    private static string ReplaceUndeclaredEntities(string content) =>
        UndeclaredEntityPattern().Replace(content, match => match.Value switch
        {
            "&nbsp;" => "&#160;",
            "&copy;" => "&#169;",
            "&mdash;" => "&#8212;",
            "&ndash;" => "&#8211;",
            "&hellip;" => "&#8230;",
            "&rsquo;" => "&#8217;",
            "&lsquo;" => "&#8216;",
            "&ldquo;" => "&#8220;",
            "&rdquo;" => "&#8221;",
            "&trade;" => "&#8482;",
            "&pound;" => "&#163;",
            "&euro;" => "&#8364;",
            // Anything else undeclared becomes a literal ampersand, which is
            // always valid and never loses the surrounding text.
            _ => "&amp;" + match.Value[1..]
        });

    /// <summary>
    /// Named entities other than the five XML predefines. Numeric references
    /// are already legal and are left alone.
    /// </summary>
    [GeneratedRegex(@"&(?!(?:amp|lt|gt|quot|apos);|#\d+;|#x[0-9a-fA-F]+;)[a-zA-Z][a-zA-Z0-9]*;")]
    private static partial Regex UndeclaredEntityPattern();

    private static ParsedFeed ParseRss2(XElement root, Uri sourceUri)
    {
        var channel = root.Element("channel")
            ?? throw new FeedParseException("RSS feed has no <channel> element.");

        var (items, skipped) = ParseItems(
            channel.Elements("item"), element => ParseRssItem(element, sourceUri));

        return new ParsedFeed(
            Trimmed(channel.Element("title")?.Value),
            ResolveLink(channel.Element("link")?.Value, sourceUri),
            items,
            skipped);
    }

    private static ParsedFeed ParseRdf(XElement root, Uri sourceUri)
    {
        var channel = root.Element(Rss1 + "channel") ?? root.Element("channel");

        // RDF items are siblings of <channel>, not children of it.
        var itemElements = root.Elements(Rss1 + "item").Concat(root.Elements("item"));
        var (items, skipped) = ParseItems(
            itemElements, element => ParseRssItem(element, sourceUri));

        return new ParsedFeed(
            Trimmed(channel?.Element(Rss1 + "title")?.Value ?? channel?.Element("title")?.Value),
            ResolveLink(
                channel?.Element(Rss1 + "link")?.Value ?? channel?.Element("link")?.Value,
                sourceUri),
            items,
            skipped);
    }

    private static ParsedFeed ParseAtom(XElement root, Uri sourceUri)
    {
        var (items, skipped) = ParseItems(
            root.Elements(Atom + "entry"), element => ParseAtomEntry(element, sourceUri));

        return new ParsedFeed(
            Trimmed(root.Element(Atom + "title")?.Value),
            ResolveLink(AtomLink(root), sourceUri),
            items,
            skipped);
    }

    /// <summary>
    /// Parses each item independently. One malformed item is skipped and
    /// counted; it never costs the caller the rest of the feed.
    /// </summary>
    private static (IReadOnlyList<ParsedItem> Items, int Skipped) ParseItems(
        IEnumerable<XElement> elements,
        Func<XElement, ParsedItem> parse)
    {
        var items = new List<ParsedItem>();
        var skipped = 0;

        foreach (var element in elements)
        {
            try
            {
                items.Add(parse(element));
            }
            catch (Exception)
            {
                skipped++;
            }
        }

        return (items, skipped);
    }

    private static ParsedItem ParseRssItem(XElement element, Uri sourceUri)
    {
        // RSS 1.0 puts its children in the RSS 1.0 namespace; RSS 2.0 uses none.
        string? Child(string name) =>
            element.Element(name)?.Value ?? element.Element(Rss1 + name)?.Value;

        var description = Trimmed(Child("description"));
        var encoded = Trimmed(element.Element(Content + "encoded")?.Value);

        return new ParsedItem
        {
            Guid = Trimmed(Child("guid")),
            Link = ResolveLink(Child("link"), sourceUri),
            Title = Trimmed(Child("title")),
            Author = Trimmed(element.Element(Dc + "creator")?.Value ?? Child("author")),
            PublishedUtc = FeedDateParser.TryParse(
                Child("pubDate") ?? element.Element(Dc + "date")?.Value),
            UpdatedUtc = FeedDateParser.TryParse(element.Element(Dc + "date")?.Value),
            Summary = description,
            // content:encoded is the full article when a publisher offers one.
            ContentHtml = encoded ?? description
        };
    }

    private static ParsedItem ParseAtomEntry(XElement element, Uri sourceUri)
    {
        var summary = Trimmed(element.Element(Atom + "summary")?.Value);
        var content = Trimmed(element.Element(Atom + "content")?.Value);

        return new ParsedItem
        {
            Guid = Trimmed(element.Element(Atom + "id")?.Value),
            Link = ResolveLink(AtomLink(element), sourceUri),
            Title = Trimmed(element.Element(Atom + "title")?.Value),
            Author = Trimmed(element.Element(Atom + "author")?.Element(Atom + "name")?.Value),
            PublishedUtc = FeedDateParser.TryParse(element.Element(Atom + "published")?.Value)
                           ?? FeedDateParser.TryParse(element.Element(Atom + "updated")?.Value),
            UpdatedUtc = FeedDateParser.TryParse(element.Element(Atom + "updated")?.Value),
            Summary = summary,
            ContentHtml = content ?? summary
        };
    }

    /// <summary>
    /// The alternate link, or the first link with no rel, which is what most
    /// Atom feeds actually emit.
    /// </summary>
    private static string? AtomLink(XElement element)
    {
        var links = element.Elements(Atom + "link").ToList();
        var alternate = links.FirstOrDefault(link =>
            (string?)link.Attribute("rel") == "alternate");
        var bare = links.FirstOrDefault(link => link.Attribute("rel") is null);
        return (string?)(alternate ?? bare)?.Attribute("href");
    }

    private static string? ResolveLink(string? value, Uri sourceUri)
    {
        var trimmed = Trimmed(value);
        if (trimmed is null) return null;

        return Uri.TryCreate(sourceUri, trimmed, out var absolute)
            ? absolute.ToString()
            : trimmed;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 8: Remove the syndication package**

In `LucidReader.Core/LucidReader.Core.csproj`, delete:

```xml
<PackageReference Include="System.ServiceModel.Syndication" Version="10.0.0" />
```

- [ ] **Step 9: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedParserTests 2>&1 | tail -5
```

Expected: 19 passed.

- [ ] **Step 10: Commit**

```bash
git add LucidReader.Core LucidReader.Core.Tests/Feeds
git commit -m "feat(reader): RSS 2.0, RDF and Atom parser with per-item error recovery"
```

---

## Task 12: Conditional HTTP fetch

**Files:**
- Create: `LucidReader.Core/Feeds/FeedFetchResult.cs`
- Create: `LucidReader.Core/Feeds/FeedFetcher.cs`
- Test: `LucidReader.Core.Tests/Feeds/StubHttpHandler.cs`
- Test: `LucidReader.Core.Tests/Feeds/FeedFetcherTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `abstract record FeedFetchResult` with cases `Fetched(string Content, string? ETag, string? LastModified)`, `NotModified`, `Failed(string Error, bool IsTransient)`.
  - `sealed class FeedFetcher(HttpClient http)` with `Task<FeedFetchResult> FetchAsync(string feedUrl, string? etag, string? lastModified, CancellationToken ct = default)`.

- [ ] **Step 1: Write the stub HTTP handler**

Create `LucidReader.Core.Tests/Feeds/StubHttpHandler.cs`:

```csharp
using System.Net;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Serves canned responses and records the requests it saw, so tests can
/// assert on conditional headers without touching the network.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    public static StubHttpHandler Returning(
        HttpStatusCode status,
        string? body = null,
        string? etag = null,
        string? lastModified = null) =>
        new(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (body is not null) response.Content = new StringContent(body);
            if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag);
            if (lastModified is not null)
                response.Content?.Headers.TryAddWithoutValidation("Last-Modified", lastModified);
            return response;
        });

    public static StubHttpHandler Throwing(Exception exception) =>
        new(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_respond(request));
    }

    public HttpClient CreateClient() => new(this);
}
```

- [ ] **Step 2: Write the failing fetcher tests**

Create `LucidReader.Core.Tests/Feeds/FeedFetcherTests.cs`:

```csharp
using System.Net;
using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

public class FeedFetcherTests
{
    private const string Url = "https://example.com/feed.xml";

    [Fact]
    public async Task A_200_returns_the_body_and_the_validators()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<rss/>", etag: "\"abc\"",
            lastModified: "Wed, 27 Aug 2026 10:00:00 GMT");
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var fetched = Assert.IsType<FeedFetchResult.Fetched>(result);
        Assert.Equal("<rss/>", fetched.Content);
        Assert.Equal("\"abc\"", fetched.ETag);
        Assert.Equal("Wed, 27 Aug 2026 10:00:00 GMT", fetched.LastModified);
    }

    [Fact]
    public async Task A_stored_etag_is_sent_as_if_none_match()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        var fetcher = new FeedFetcher(handler.CreateClient());

        await fetcher.FetchAsync(Url, "\"abc\"", null);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("\"abc\"", request.Headers.IfNoneMatch.ToString());
    }

    [Fact]
    public async Task A_stored_last_modified_is_sent_as_if_modified_since()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        var fetcher = new FeedFetcher(handler.CreateClient());

        await fetcher.FetchAsync(Url, null, "Wed, 27 Aug 2026 10:00:00 GMT");

        var request = Assert.Single(handler.Requests);
        Assert.NotNull(request.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task A_304_returns_NotModified_and_no_body()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, "\"abc\"", null);

        Assert.IsType<FeedFetchResult.NotModified>(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Gone, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Error_statuses_are_classified_as_transient_or_permanent(
        HttpStatusCode status, bool expectedTransient)
    {
        var handler = StubHttpHandler.Returning(status);
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.Equal(expectedTransient, failed.IsTransient);
    }

    [Fact]
    public async Task A_network_exception_is_a_transient_failure_not_a_throw()
    {
        var handler = StubHttpHandler.Throwing(new HttpRequestException("connection refused"));
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.True(failed.IsTransient);
        Assert.Contains("connection refused", failed.Error);
    }

    [Fact]
    public async Task A_timeout_is_a_transient_failure()
    {
        var handler = StubHttpHandler.Throwing(new TaskCanceledException("timed out"));
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync(Url, null, null, CancellationToken.None);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.True(failed.IsTransient);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_being_swallowed()
    {
        var handler = StubHttpHandler.Throwing(new TaskCanceledException("cancelled"));
        var fetcher = new FeedFetcher(handler.CreateClient());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetcher.FetchAsync(Url, null, null, cts.Token));
    }

    [Fact]
    public async Task A_malformed_url_fails_permanently_rather_than_throwing()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<rss/>");
        var fetcher = new FeedFetcher(handler.CreateClient());

        var result = await fetcher.FetchAsync("not a url", null, null);

        var failed = Assert.IsType<FeedFetchResult.Failed>(result);
        Assert.False(failed.IsTransient);
    }

    [Fact]
    public async Task The_request_identifies_lucidREADER()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<rss/>");
        var fetcher = new FeedFetcher(handler.CreateClient());

        await fetcher.FetchAsync(Url, null, null);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("lucidREADER", request.Headers.UserAgent.ToString());
    }
}
```

The transient-versus-permanent split matters downstream: a 503 should back off and retry, while a 404 or 410 should climb toward the auto-pause threshold, because retrying a deleted feed forever is what makes a reader an unwelcome client.

The cancellation test is the subtle one. `HttpClient` surfaces both a timeout and a caller cancellation as `TaskCanceledException`, so distinguishing them requires checking the caller's token. Conflating them means a user closing the app records a fake failure on every in-flight feed.

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedFetcherTests 2>&1 | tail -10
```

Expected: compilation failure, `FeedFetcher` does not exist.

- [ ] **Step 4: Write FeedFetchResult**

Create `LucidReader.Core/Feeds/FeedFetchResult.cs`:

```csharp
namespace LucidReader.Core.Feeds;

public abstract record FeedFetchResult
{
    private FeedFetchResult() { }

    public sealed record Fetched(string Content, string? ETag, string? LastModified)
        : FeedFetchResult;

    public sealed record NotModified : FeedFetchResult;

    /// <summary>
    /// IsTransient separates "try again later" (timeouts, 5xx, 429) from
    /// "this feed is broken" (404, 410, 401, 403). Only the latter should
    /// push a feed toward being auto-paused.
    /// </summary>
    public sealed record Failed(string Error, bool IsTransient) : FeedFetchResult;
}
```

- [ ] **Step 5: Write FeedFetcher**

Create `LucidReader.Core/Feeds/FeedFetcher.cs`:

```csharp
using System.Globalization;
using System.Net;

namespace LucidReader.Core.Feeds;

/// <summary>
/// One conditional GET. Does not parse and does not write to the database:
/// this class knows about HTTP and nothing else.
/// </summary>
public sealed class FeedFetcher(HttpClient http)
{
    public const string UserAgentString =
        "lucidREADER/1.0 (+https://www.mostlylucid.net)";

    public async Task<FeedFetchResult> FetchAsync(
        string feedUrl,
        string? etag,
        string? lastModified,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new FeedFetchResult.Failed($"Not a usable feed URL: {feedUrl}", false);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgentString);
        request.Headers.TryAddWithoutValidation(
            "Accept", "application/atom+xml, application/rss+xml, application/xml;q=0.9, */*;q=0.8");

        if (!string.IsNullOrWhiteSpace(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        if (!string.IsNullOrWhiteSpace(lastModified)
            && DateTimeOffset.TryParse(
                lastModified, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var since))
            request.Headers.IfModifiedSince = since;

        try
        {
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return new FeedFetchResult.NotModified();

            if (!response.IsSuccessStatusCode)
                return new FeedFetchResult.Failed(
                    $"{(int)response.StatusCode} {response.ReasonPhrase}",
                    IsTransient(response.StatusCode));

            var content = await response.Content.ReadAsStringAsync(ct);
            return new FeedFetchResult.Fetched(
                content,
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified?.ToString("r"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked us to stop. That is not a feed failure, so it
            // must not be recorded as one.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation too.
            return new FeedFetchResult.Failed("The request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            return new FeedFetchResult.Failed(ex.Message, true);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedFetcherTests 2>&1 | tail -5
```

Expected: 19 passed.

- [ ] **Step 7: Commit**

```bash
git add LucidReader.Core/Feeds LucidReader.Core.Tests/Feeds
git commit -m "feat(reader): conditional feed fetch with transient failure classification"
```

---

## Task 13: Backoff policy

**Files:**
- Create: `LucidReader.Core/Sync/BackoffPolicy.cs`
- Test: `LucidReader.Core.Tests/Sync/BackoffPolicyTests.cs`

**Interfaces:**
- Consumes: `EffectiveFeedSettings` (Task 9).
- Produces: `sealed class BackoffPolicy(Random? random = null)` with:
  - `DateTimeOffset NextDueAfterSuccess(DateTimeOffset nowUtc, EffectiveFeedSettings settings)`
  - `DateTimeOffset NextDueAfterFailure(DateTimeOffset nowUtc, int consecutiveFailures, EffectiveFeedSettings settings)`
  - `static bool ShouldAutoPause(int consecutiveFailures)`
  - `const int AutoPauseThreshold = 20`
  - `static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6)`

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Sync/BackoffPolicyTests.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Sync;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

public class BackoffPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-28T12:00:00Z");

    private static EffectiveFeedSettings Settings(int minutes = 30) =>
        new(TimeSpan.FromMinutes(minutes), true, true, 30);

    // A fixed seed makes the jitter deterministic, so these tests never flake.
    private static BackoffPolicy Policy() => new(new Random(12345));

    [Fact]
    public void Success_schedules_the_next_fetch_one_interval_away()
    {
        var next = Policy().NextDueAfterSuccess(Now, Settings(30));

        Assert.Equal(Now.AddMinutes(30), next);
    }

    [Fact]
    public void The_first_failure_waits_longer_than_zero_but_less_than_the_interval()
    {
        var next = Policy().NextDueAfterFailure(Now, 1, Settings(30));

        Assert.True(next > Now);
        Assert.True(next <= Now.AddMinutes(30));
    }

    [Fact]
    public void Each_further_failure_waits_longer_than_the_one_before()
    {
        var policy = Policy();

        var first = policy.NextDueAfterFailure(Now, 1, Settings());
        var second = policy.NextDueAfterFailure(Now, 2, Settings());
        var third = policy.NextDueAfterFailure(Now, 3, Settings());
        var fourth = policy.NextDueAfterFailure(Now, 4, Settings());

        Assert.True(second > first);
        Assert.True(third > second);
        Assert.True(fourth > third);
    }

    [Fact]
    public void Backoff_is_capped_so_a_dead_feed_is_still_retried_occasionally()
    {
        var next = Policy().NextDueAfterFailure(Now, 50, Settings());

        Assert.True(next <= Now.Add(BackoffPolicy.MaxBackoff));
    }

    [Fact]
    public void Backoff_never_schedules_a_fetch_in_the_past()
    {
        var policy = Policy();

        for (var failures = 1; failures <= 30; failures++)
            Assert.True(policy.NextDueAfterFailure(Now, failures, Settings()) > Now);
    }

    [Fact]
    public void Jitter_spreads_two_feeds_failing_at_the_same_moment()
    {
        var policy = Policy();

        var a = policy.NextDueAfterFailure(Now, 5, Settings());
        var b = policy.NextDueAfterFailure(Now, 5, Settings());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void A_feed_is_auto_paused_only_after_the_threshold()
    {
        Assert.False(BackoffPolicy.ShouldAutoPause(19));
        Assert.True(BackoffPolicy.ShouldAutoPause(20));
        Assert.True(BackoffPolicy.ShouldAutoPause(21));
    }
}
```

The jitter test is the reason `Random` is injectable. Without jitter, a laptop that wakes from sleep with fifty failed feeds retries all fifty at the same instant, forever.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter BackoffPolicyTests 2>&1 | tail -10
```

Expected: compilation failure, `BackoffPolicy` does not exist.

- [ ] **Step 3: Write BackoffPolicy**

Create `LucidReader.Core/Sync/BackoffPolicy.cs`:

```csharp
using LucidReader.Core.Model;
using Mostlylucid.Ephemeral.Atoms.Retry;

namespace LucidReader.Core.Sync;

/// <summary>
/// Decides when a feed is next due.
///
/// Uses BackoffStrategies from the Ephemeral retry atom for the curve, but not
/// RetryAtom itself: that holds its queue in memory, and our retry state has to
/// survive the app closing. The schedule lives in feeds.next_due_utc instead.
/// </summary>
public sealed class BackoffPolicy(Random? random = null)
{
    /// <summary>
    /// After this many consecutive failures a feed is paused and the user is
    /// asked, rather than hammering a host that is plainly not coming back.
    /// </summary>
    public const int AutoPauseThreshold = 20;

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(2);

    private readonly Func<int, TimeSpan> _backoff =
        BackoffStrategies.ExponentialWithJitter(
            BaseDelay,
            factor: 2.0,
            jitterRatio: 0.2,
            random: random ?? Random.Shared);

    public DateTimeOffset NextDueAfterSuccess(
        DateTimeOffset nowUtc,
        EffectiveFeedSettings settings) =>
        nowUtc.Add(settings.RefreshInterval);

    public DateTimeOffset NextDueAfterFailure(
        DateTimeOffset nowUtc,
        int consecutiveFailures,
        EffectiveFeedSettings settings)
    {
        var attempt = Math.Max(1, consecutiveFailures);
        var delay = _backoff(attempt);

        if (delay > MaxBackoff) delay = MaxBackoff;

        // Jitter is symmetric, so a small delay can come back at or below zero.
        // A next-due in the past would make the scheduler spin.
        if (delay < TimeSpan.FromSeconds(30)) delay = TimeSpan.FromSeconds(30);

        return nowUtc.Add(delay);
    }

    public static bool ShouldAutoPause(int consecutiveFailures) =>
        consecutiveFailures >= AutoPauseThreshold;
}
```

Watch the exponent: `ExponentialWithJitter` computes `baseDelay * factor^(attempt-1)`, so attempt 30 would be `2 minutes * 2^29`, which overflows into an absurd `TimeSpan` before the cap is applied. The cap is applied after, so the result is still correct, but if you change the base or factor, confirm the intermediate does not overflow `TimeSpan.MaxValue`. The `NextDueAfterFailure` loop test up to 30 failures is what catches it.

- [ ] **Step 4: Verify the retry atom namespace**

The `using Mostlylucid.Ephemeral.Atoms.Retry;` line is taken from the 2.9.0 source checkout. Confirm it against the installed 3.0.0 package before assuming it compiles. If the namespace differs, fix the using; if `BackoffStrategies` is not public in 3.0.0, implement the same curve inline (`baseMs * pow(factor, attempt-1)` plus symmetric jitter) and drop the package reference, keeping every test unchanged.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter BackoffPolicyTests 2>&1 | tail -5
```

Expected: 7 passed.

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core/Sync LucidReader.Core.Tests/Sync
git commit -m "feat(reader): persisted exponential backoff with jitter and auto-pause threshold"
```

---

## Task 14: FeedRefreshService, the refresh coordinator

**Files:**
- Create: `LucidReader.Core/Sync/FeedRefreshRequest.cs`
- Create: `LucidReader.Core/Sync/FeedRefreshService.cs`
- Test: `LucidReader.Core.Tests/Sync/FeedRefreshServiceTests.cs`

**Interfaces:**
- Consumes: `FeedRepository`, `ItemRepository` (Tasks 6, 7), `FeedFetcher`, `FeedParser` (Tasks 11, 12), `BackoffPolicy` (Task 13), `ReaderSettings`, `EffectiveFeedSettings` (Task 9).
- Produces:
  - `readonly record struct FeedRefreshRequest(long FeedId, bool IsManual)`
  - `sealed class FeedRefreshService : IAsyncDisposable` with:
    - constructor `(FeedRepository feeds, ItemRepository items, FeedFetcher fetcher, IFeedParser parser, BackoffPolicy backoff, Func<ReaderSettings> settings, TimeProvider timeProvider, int maxConcurrency = 4)`
    - `bool TryQueue(long feedId, bool isManual = false)` returning false when the feed is already queued or running
    - `Task QueueAsync(long feedId, bool isManual = false, CancellationToken ct = default)`
    - `Task<FeedRefreshOutcome> RefreshNowAsync(long feedId, CancellationToken ct = default)` for tests and for the synchronous "refresh this feed" path
    - `int PendingCount { get; }`, `int ActiveCount { get; }`, `int TotalFailed { get; }`
    - `void Pause()`, `void Resume()`
    - `event Action<FeedRefreshOutcome>? Completed`
  - `readonly record struct FeedRefreshOutcome(long FeedId, bool Success, int NewItemCount, bool NotModified, string? Error)`
  - `const TimeSpan MaxFeedFetchDuration` of 60 seconds.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Sync/FeedRefreshServiceTests.cs`:

```csharp
using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

public class FeedRefreshServiceTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private ItemRepository _items = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        _items = new ItemRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private FeedRefreshService CreateService(StubHttpHandler handler) =>
        new(_feeds, _items,
            new FeedFetcher(handler.CreateClient()),
            new FeedParser(),
            new BackoffPolicy(new Random(999)),
            () => ReaderSettings.Defaults,
            _time);

    private Task<long> AddFeedAsync(string url = "https://example.com/feed.xml") =>
        _feeds.AddAsync(new Feed { FeedUrl = url });

    [Fact]
    public async Task A_successful_refresh_stores_the_items()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.True(outcome.Success);
        Assert.Equal(2, outcome.NewItemCount);
        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task A_successful_refresh_adopts_the_feeds_own_title()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);

        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal("Example Blog", feed!.Title);
        Assert.Equal("https://example.com/", feed.SiteUrl);
    }

    [Fact]
    public async Task A_second_refresh_of_unchanged_content_adds_nothing()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);
        var second = await service.RefreshNowAsync(feedId);

        Assert.Equal(0, second.NewItemCount);
        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task An_item_with_no_guid_is_stored_under_a_stable_link_hash()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-no-guid.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);
        var afterFirst = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        await service.RefreshNowAsync(feedId);
        var afterSecond = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));

        Assert.Single(afterFirst);
        Assert.Single(afterSecond);
        Assert.Equal(afterFirst[0].Guid, afterSecond[0].Guid);
    }

    [Fact]
    public async Task A_304_is_a_success_that_stores_nothing()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotModified);
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.True(outcome.Success);
        Assert.True(outcome.NotModified);
        Assert.Equal(0, outcome.NewItemCount);
    }

    [Fact]
    public async Task A_successful_refresh_records_the_validators_and_next_due()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"), etag: "\"v1\"");
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);

        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal("\"v1\"", feed!.ETag);
        Assert.Equal(0, feed.ConsecutiveFailures);
        Assert.Equal(_time.GetUtcNow().AddMinutes(30), feed.NextDueUtc);
    }

    [Fact]
    public async Task A_failed_refresh_records_the_error_and_backs_off()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.ServiceUnavailable);
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var outcome = await service.RefreshNowAsync(feedId);

        Assert.False(outcome.Success);
        var feed = await _feeds.GetAsync(feedId);
        Assert.Equal(1, feed!.ConsecutiveFailures);
        Assert.Contains("503", feed.LastError);
        Assert.True(feed.NextDueUtc > _time.GetUtcNow());
    }

    [Fact]
    public async Task An_unparseable_response_is_a_failure_that_keeps_existing_items()
    {
        var okHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using (var service = CreateService(okHandler))
        {
            var seeded = await AddFeedAsync();
            await service.RefreshNowAsync(seeded);
        }

        var badHandler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("not-a-feed.html"));
        await using var second = CreateService(badHandler);
        var feedId = (await _feeds.GetByUrlAsync("https://example.com/feed.xml"))!.Id;

        var outcome = await second.RefreshNowAsync(feedId);

        Assert.False(outcome.Success);
        var stored = await _items.QueryAsync(new ItemQuery(feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Reaching_the_auto_pause_threshold_disables_the_feed()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotFound);
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        for (var i = 0; i < BackoffPolicy.AutoPauseThreshold; i++)
            await service.RefreshNowAsync(feedId);

        var feed = await _feeds.GetAsync(feedId);
        Assert.False(feed!.IsEnabled);
    }

    [Fact]
    public async Task Queueing_a_feed_that_is_already_queued_is_refused()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();
        service.Pause();

        var first = service.TryQueue(feedId);
        var second = service.TryQueue(feedId);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task A_queued_feed_can_be_queued_again_once_it_has_finished()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        var completed = new TaskCompletionSource();
        service.Completed += _ => completed.TrySetResult();
        Assert.True(service.TryQueue(feedId));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(service.TryQueue(feedId));
    }

    [Fact]
    public async Task Completion_is_reported_for_every_queued_feed()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedIds = new List<long>();
        for (var i = 0; i < 5; i++)
            feedIds.Add(await AddFeedAsync($"https://example{i}.com/feed.xml"));

        var outcomes = new List<FeedRefreshOutcome>();
        var done = new TaskCompletionSource();
        service.Completed += outcome =>
        {
            lock (outcomes)
            {
                outcomes.Add(outcome);
                if (outcomes.Count == 5) done.TrySetResult();
            }
        };

        foreach (var id in feedIds) service.TryQueue(id);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(5, outcomes.Count);
    }

    [Fact]
    public async Task New_items_are_marked_pending_when_auto_download_is_on()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await AddFeedAsync();

        await service.RefreshNowAsync(feedId);

        var pending = await _items.GetPendingOfflineAsync(100);
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task New_items_are_not_marked_pending_when_the_feed_opts_out()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        await using var service = CreateService(handler);
        var feedId = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://example.com/feed.xml",
            AutoDownload = false
        });

        await service.RefreshNowAsync(feedId);

        var pending = await _items.GetPendingOfflineAsync(100);
        Assert.Empty(pending);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedRefreshServiceTests 2>&1 | tail -10
```

Expected: compilation failure, `FeedRefreshService` does not exist.

- [ ] **Step 3: Write FeedRefreshRequest**

Create `LucidReader.Core/Sync/FeedRefreshRequest.cs`:

```csharp
namespace LucidReader.Core.Sync;

public readonly record struct FeedRefreshRequest(long FeedId, bool IsManual);

public readonly record struct FeedRefreshOutcome(
    long FeedId,
    bool Success,
    int NewItemCount,
    bool NotModified,
    string? Error);
```

- [ ] **Step 4: Write FeedRefreshService**

Create `LucidReader.Core/Sync/FeedRefreshService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Mostlylucid.Ephemeral;

namespace LucidReader.Core.Sync;

/// <summary>
/// Owns feed refreshing. Work goes through an EphemeralWorkCoordinator so
/// concurrency is bounded and progress is observable, and an in-flight set
/// coalesces a manual refresh with an already-queued automatic one.
/// </summary>
public sealed class FeedRefreshService : IAsyncDisposable
{
    /// <summary>
    /// The coordinator requires an explicit bound, and rightly so: a server
    /// that accepts a connection and then stalls would otherwise hold its
    /// concurrency slot until the app closes.
    /// </summary>
    public static readonly TimeSpan MaxFeedFetchDuration = TimeSpan.FromSeconds(60);

    private readonly FeedRepository _feeds;
    private readonly ItemRepository _items;
    private readonly FeedFetcher _fetcher;
    private readonly IFeedParser _parser;
    private readonly BackoffPolicy _backoff;
    private readonly Func<ReaderSettings> _settings;
    private readonly TimeProvider _time;
    private readonly EphemeralWorkCoordinator<FeedRefreshRequest> _coordinator;
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    public FeedRefreshService(
        FeedRepository feeds,
        ItemRepository items,
        FeedFetcher fetcher,
        IFeedParser parser,
        BackoffPolicy backoff,
        Func<ReaderSettings> settings,
        TimeProvider timeProvider,
        int maxConcurrency = 4)
    {
        _feeds = feeds;
        _items = items;
        _fetcher = fetcher;
        _parser = parser;
        _backoff = backoff;
        _settings = settings;
        _time = timeProvider;

        _coordinator = new EphemeralWorkCoordinator<FeedRefreshRequest>(
            RunAsync,
            MaxFeedFetchDuration,
            new EphemeralOptions
            {
                MaxConcurrency = maxConcurrency,
                // The default of 200 is the bounded channel's capacity, and
                // EnqueueAsync blocks once it is full. A user with more than
                // 200 subscriptions hitting Refresh All would stall on that.
                MaxTrackedOperations = 4096
            },
            timeProvider);
    }

    public int PendingCount => _coordinator.PendingCount;
    public int ActiveCount => _coordinator.ActiveCount;
    public int TotalFailed => _coordinator.TotalFailed;

    public event Action<FeedRefreshOutcome>? Completed;

    /// <summary>
    /// Queues a refresh, or returns false if this feed is already queued or
    /// running. That is the coalescing rule: pressing Refresh twice does not
    /// fetch twice.
    /// </summary>
    public bool TryQueue(long feedId, bool isManual = false)
    {
        if (!_inFlight.TryAdd(feedId, 0)) return false;

        if (_coordinator.TryEnqueue(new FeedRefreshRequest(feedId, isManual)))
            return true;

        _inFlight.TryRemove(feedId, out _);
        return false;
    }

    public async Task QueueAsync(long feedId, bool isManual = false, CancellationToken ct = default)
    {
        if (!_inFlight.TryAdd(feedId, 0)) return;

        try
        {
            await _coordinator.EnqueueAsync(new FeedRefreshRequest(feedId, isManual), ct);
        }
        catch
        {
            _inFlight.TryRemove(feedId, out _);
            throw;
        }
    }

    public void Pause() => _coordinator.Pause();
    public void Resume() => _coordinator.Resume();

    private async Task RunAsync(FeedRefreshRequest request, CancellationToken ct)
    {
        try
        {
            var outcome = await RefreshCoreAsync(request.FeedId, ct);
            Completed?.Invoke(outcome);
        }
        finally
        {
            _inFlight.TryRemove(request.FeedId, out _);
        }
    }

    /// <summary>
    /// Refreshes one feed inline, bypassing the queue. Used by the synchronous
    /// refresh path and by tests.
    /// </summary>
    public Task<FeedRefreshOutcome> RefreshNowAsync(long feedId, CancellationToken ct = default) =>
        RefreshCoreAsync(feedId, ct);

    private async Task<FeedRefreshOutcome> RefreshCoreAsync(long feedId, CancellationToken ct)
    {
        var feed = await _feeds.GetAsync(feedId, ct);
        if (feed is null)
            return new FeedRefreshOutcome(feedId, false, 0, false, "The feed no longer exists.");

        var settings = EffectiveFeedSettings.Resolve(feed, _settings());
        var now = _time.GetUtcNow();

        var result = await _fetcher.FetchAsync(feed.FeedUrl, feed.ETag, feed.LastModified, ct);

        switch (result)
        {
            case FeedFetchResult.NotModified:
                await _feeds.RecordSuccessAsync(
                    feedId, feed.ETag, feed.LastModified, now,
                    _backoff.NextDueAfterSuccess(now, settings), ct);
                return new FeedRefreshOutcome(feedId, true, 0, true, null);

            case FeedFetchResult.Failed failed:
                await RecordFailureAsync(feed, failed.Error, now, settings, ct);
                return new FeedRefreshOutcome(feedId, false, 0, false, failed.Error);

            case FeedFetchResult.Fetched fetched:
                return await StoreAsync(feed, fetched, settings, now, ct);

            default:
                throw new InvalidOperationException("Unreachable fetch result.");
        }
    }

    private async Task<FeedRefreshOutcome> StoreAsync(
        Feed feed,
        FeedFetchResult.Fetched fetched,
        EffectiveFeedSettings settings,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ParsedFeed parsed;
        try
        {
            parsed = _parser.Parse(fetched.Content, new Uri(feed.FeedUrl));
        }
        catch (Exception ex)
        {
            // A parse failure is a feed problem, not a crash, and it must not
            // touch the items we already have stored.
            await RecordFailureAsync(feed, ex.Message, now, settings, ct);
            return new FeedRefreshOutcome(feed.Id, false, 0, false, ex.Message);
        }

        var items = parsed.Items
            .Select(item => new FeedItem
            {
                FeedId = feed.Id,
                Guid = StableGuid(item),
                Link = item.Link,
                Title = item.Title,
                Author = item.Author,
                PublishedUtc = item.PublishedUtc,
                UpdatedUtc = item.UpdatedUtc,
                Summary = item.Summary,
                ContentMarkdown = null,
                ContentSource = ContentSource.Feed,
                FirstSeenUtc = now,
                OfflineState = settings.AutoDownload ? OfflineState.Pending : OfflineState.None
            })
            .ToList();

        var newCount = await _items.UpsertManyAsync(items, ct);

        // Adopt the feed's own title and site link, but never overwrite a title
        // the user set for themselves.
        if (parsed.Title is not null || parsed.SiteUrl is not null)
        {
            await _feeds.UpdateAsync(feed with
            {
                Title = parsed.Title ?? feed.Title,
                SiteUrl = parsed.SiteUrl ?? feed.SiteUrl
            }, ct);
        }

        await _feeds.RecordSuccessAsync(
            feed.Id, fetched.ETag, fetched.LastModified, now,
            _backoff.NextDueAfterSuccess(now, settings), ct);

        return new FeedRefreshOutcome(feed.Id, true, newCount, false, null);
    }

    private async Task RecordFailureAsync(
        Feed feed,
        string error,
        DateTimeOffset now,
        EffectiveFeedSettings settings,
        CancellationToken ct)
    {
        var failures = feed.ConsecutiveFailures + 1;
        await _feeds.RecordFailureAsync(
            feed.Id, error, now,
            _backoff.NextDueAfterFailure(now, failures, settings), ct);

        if (BackoffPolicy.ShouldAutoPause(failures) && feed.IsEnabled)
            await _feeds.UpdateAsync(feed with { IsEnabled = false }, ct);
    }

    /// <summary>
    /// The feed's own guid when it has one, otherwise a hash of the link. The
    /// hash has to be stable across refreshes, or every fetch would look like
    /// a fresh batch of items and the user's list would fill with duplicates.
    /// </summary>
    private static string StableGuid(ParsedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Guid)) return item.Guid;

        var basis = item.Link ?? item.Title ?? string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return "sha256:" + Convert.ToHexString(hash)[..32];
    }

    public async ValueTask DisposeAsync()
    {
        _coordinator.Complete();
        await _coordinator.DisposeAsync();
    }
}
```

Two details worth understanding before changing this file. `_inFlight` is removed in a `finally`, so a body that throws still releases the feed for re-queueing; forgetting that is how a feed becomes permanently unrefreshable. And `RecordFailureAsync` reads `feed.ConsecutiveFailures + 1` rather than re-reading the row, which is safe only because a feed can be in flight once at a time, which is exactly what `_inFlight` guarantees.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter FeedRefreshServiceTests 2>&1 | tail -5
```

Expected: 14 passed.

If `EphemeralWorkCoordinator` has no `DisposeAsync`, use `Complete()` followed by whatever the 3.0.0 package offers for graceful shutdown, and adjust `DisposeAsync` accordingly.

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core/Sync LucidReader.Core.Tests/Sync
git commit -m "feat(reader): feed refresh coordinator with coalescing and auto-pause"
```

---

## Task 15: RefreshScheduler, the due-feed tick

**Files:**
- Create: `LucidReader.Core/Sync/RefreshScheduler.cs`
- Test: `LucidReader.Core.Tests/Sync/RefreshSchedulerTests.cs`

**Interfaces:**
- Consumes: `FeedRepository` (Task 6), `FeedRefreshService` (Task 14).
- Produces: `sealed class RefreshScheduler : IAsyncDisposable` with constructor `(FeedRepository feeds, FeedRefreshService refresh, TimeProvider timeProvider, TimeSpan? tickInterval = null)`, plus `void Start()`, `Task StopAsync()`, `Task<int> TickAsync(CancellationToken ct = default)` returning how many feeds were queued, `bool IsRunning { get; }`.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Sync/RefreshSchedulerTests.cs`:

```csharp
using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Sync;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

public class RefreshSchedulerTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private FeedRepository _feeds = null!;
    private FeedRefreshService _refresh = null!;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _feeds = new FeedRepository(_db);
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, Fixtures.Feed("rss2-simple.xml"));
        _refresh = new FeedRefreshService(
            _feeds, new ItemRepository(_db),
            new FeedFetcher(handler.CreateClient()), new FeedParser(),
            new BackoffPolicy(new Random(7)), () => ReaderSettings.Defaults, _time);
        // Paused so TickAsync's queueing can be observed without the work
        // racing to completion and clearing the in-flight set.
        _refresh.Pause();
    }

    public async Task DisposeAsync()
    {
        await _refresh.DisposeAsync();
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private RefreshScheduler CreateScheduler() =>
        new(_feeds, _refresh, _time, TimeSpan.FromMinutes(1));

    [Fact]
    public async Task A_tick_queues_every_due_feed()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(-1)
        });
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://b.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(-5)
        });
        await using var scheduler = CreateScheduler();

        var queued = await scheduler.TickAsync();

        Assert.Equal(2, queued);
    }

    [Fact]
    public async Task A_tick_leaves_feeds_that_are_not_due_alone()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(30)
        });
        await using var scheduler = CreateScheduler();

        Assert.Equal(0, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_never_fetched_feed_is_due_immediately()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = CreateScheduler();

        Assert.Equal(1, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_disabled_feed_is_never_queued()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            IsEnabled = false,
            NextDueUtc = _time.GetUtcNow().AddMinutes(-10)
        });
        await using var scheduler = CreateScheduler();

        Assert.Equal(0, await scheduler.TickAsync());
    }

    [Fact]
    public async Task A_second_tick_does_not_re_queue_a_feed_that_is_still_in_flight()
    {
        await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://a.example/feed.xml",
            NextDueUtc = _time.GetUtcNow().AddMinutes(-1)
        });
        await using var scheduler = CreateScheduler();

        var first = await scheduler.TickAsync();
        var second = await scheduler.TickAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task Advancing_the_clock_past_the_interval_fires_a_tick()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = CreateScheduler();
        scheduler.Start();

        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForAsync(() => _refresh.PendingCount > 0);

        Assert.True(_refresh.PendingCount > 0);
    }

    [Fact]
    public async Task Stopping_prevents_any_further_ticks()
    {
        await _feeds.AddAsync(new Feed { FeedUrl = "https://a.example/feed.xml" });
        await using var scheduler = CreateScheduler();
        scheduler.Start();
        await scheduler.StopAsync();

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(scheduler.IsRunning);
        Assert.Equal(0, _refresh.PendingCount);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter RefreshSchedulerTests 2>&1 | tail -10
```

Expected: compilation failure, `RefreshScheduler` does not exist.

- [ ] **Step 3: Write RefreshScheduler**

Create `LucidReader.Core/Sync/RefreshScheduler.cs`:

```csharp
using LucidReader.Core.Storage;

namespace LucidReader.Core.Sync;

/// <summary>
/// A plain timer over one SQL query. Ephemeral's ScheduledTasks atom is not
/// used here on purpose: the whole scheduling rule is "next_due_utc has
/// passed", which the database answers better than a scheduler would.
/// </summary>
public sealed class RefreshScheduler(
    FeedRepository feeds,
    FeedRefreshService refresh,
    TimeProvider timeProvider,
    TimeSpan? tickInterval = null) : IAsyncDisposable
{
    private const int MaxFeedsPerTick = 200;

    private readonly TimeSpan _interval = tickInterval ?? TimeSpan.FromMinutes(1);
    private readonly CancellationTokenSource _stopping = new();
    private ITimer? _timer;

    public bool IsRunning => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;

        _timer = timeProvider.CreateTimer(
            _ => _ = TickSafelyAsync(),
            null,
            _interval,
            _interval);
    }

    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }
    }

    /// <summary>
    /// Queues every feed whose next_due_utc has passed. Returns how many were
    /// actually queued, which is fewer than were due when some are already in
    /// flight from a manual refresh.
    /// </summary>
    public async Task<int> TickAsync(CancellationToken ct = default)
    {
        var due = await feeds.GetDueAsync(timeProvider.GetUtcNow(), MaxFeedsPerTick, ct);

        var queued = 0;
        foreach (var feed in due)
            if (refresh.TryQueue(feed.Id))
                queued++;

        return queued;
    }

    private async Task TickSafelyAsync()
    {
        try
        {
            await TickAsync(_stopping.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception)
        {
            // A tick that throws must not kill the timer, or refreshing stops
            // silently for the rest of the session.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stopping.Dispose();
    }
}
```

`MaxFeedsPerTick` is a real cap, so it gets said out loud rather than hidden: with more than 200 feeds due at once, the remainder are picked up on the next tick a minute later. That is deliberate. Queueing 2000 feeds in one go would make the first refresh after a long offline period feel like a hang.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter RefreshSchedulerTests 2>&1 | tail -5
```

Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add LucidReader.Core/Sync LucidReader.Core.Tests/Sync
git commit -m "feat(reader): due-feed scheduler tick"
```

---

## Task 16: Stub detection

**Files:**
- Create: `LucidReader.Core/Offline/StubDetector.cs`
- Test: `LucidReader.Core.Tests/Offline/StubDetectorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class StubDetector { static bool IsStub(string? contentHtml); const int FullArticleThreshold = 1500; }`

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Offline/StubDetectorTests.cs`:

```csharp
using LucidReader.Core.Offline;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

public class StubDetectorTests
{
    private static string Words(int count) =>
        "<p>" + string.Join(" ", Enumerable.Repeat("lorem ipsum dolor", count)) + "</p>";

    [Fact]
    public void Null_content_is_a_stub()
    {
        Assert.True(StubDetector.IsStub(null));
    }

    [Fact]
    public void Empty_content_is_a_stub()
    {
        Assert.True(StubDetector.IsStub("   "));
    }

    [Fact]
    public void A_short_summary_is_a_stub()
    {
        Assert.True(StubDetector.IsStub("<p>Just the opening sentence of the piece.</p>"));
    }

    [Fact]
    public void A_long_body_is_not_a_stub()
    {
        Assert.False(StubDetector.IsStub(Words(200)));
    }

    [Theory]
    [InlineData("<p>An opening line.</p><p><a href=\"https://x.example/1\">Read more</a></p>")]
    [InlineData("<p>An opening line.</p><a href=\"https://x.example/1\">Continue reading</a>")]
    [InlineData("<p>An opening line.</p><a href=\"https://x.example/1\">Read the full article</a>")]
    [InlineData("<p>An opening line.</p><a href=\"https://x.example/1\">[...]</a>")]
    public void A_trailing_read_more_link_marks_a_stub(string html)
    {
        Assert.True(StubDetector.IsStub(html));
    }

    [Fact]
    public void A_read_more_phrase_in_the_middle_of_a_long_article_is_not_a_stub()
    {
        var html = Words(150) + "<p>As we said, read more about this elsewhere.</p>" + Words(150);

        Assert.False(StubDetector.IsStub(html));
    }

    [Fact]
    public void Markup_does_not_count_toward_the_length()
    {
        // Long enough in raw characters, but almost no actual text.
        var html = "<div class=\"wrapper-with-a-very-long-class-name-indeed\">"
                   + string.Concat(Enumerable.Repeat("<span style=\"color:#ffffff\"></span>", 60))
                   + "<p>Two words.</p></div>";

        Assert.True(StubDetector.IsStub(html));
    }
}
```

The last two tests are the ones that stop this heuristic being useless. Counting raw HTML length means a heavily-marked-up two-sentence summary reads as a full article, and a naive "contains read more" check misclassifies any long article that happens to use the phrase.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter StubDetectorTests 2>&1 | tail -10
```

Expected: compilation failure, `StubDetector` does not exist.

- [ ] **Step 3: Write StubDetector**

Create `LucidReader.Core/Offline/StubDetector.cs`:

```csharp
using System.Text.RegularExpressions;

namespace LucidReader.Core.Offline;

/// <summary>
/// Decides whether feed-supplied content is the whole article or a teaser.
/// A heuristic, and wrong sometimes: being wrong costs one unnecessary page
/// fetch, or one article read as a summary with a retry button. Neither is
/// worth a heavier mechanism.
/// </summary>
public static partial class StubDetector
{
    /// <summary>
    /// Visible characters at or above which content is treated as a full
    /// article regardless of how it ends.
    /// </summary>
    public const int FullArticleThreshold = 1500;

    /// <summary>
    /// Below this, content is a stub whatever else it looks like.
    /// </summary>
    private const int ObviousStubThreshold = 400;

    public static bool IsStub(string? contentHtml)
    {
        if (string.IsNullOrWhiteSpace(contentHtml)) return true;

        var text = VisibleText(contentHtml);
        if (text.Length < ObviousStubThreshold) return true;
        if (text.Length >= FullArticleThreshold) return false;

        // In the middle band, the deciding factor is how it ends. A teaser
        // finishes by pointing somewhere else.
        var tail = text[^Math.Min(120, text.Length)..];
        return ReadMorePattern().IsMatch(tail);
    }

    private static string VisibleText(string html)
    {
        var withoutTags = TagPattern().Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespacePattern().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(
        @"(read\s+(the\s+)?(more|full|rest)|continue\s+reading|view\s+(the\s+)?(full\s+)?(article|post)|\[\s*\.\.\.\s*\]|\.\.\.\s*$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReadMorePattern();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter StubDetectorTests 2>&1 | tail -5
```

Expected: 11 passed.

- [ ] **Step 5: Commit**

```bash
git add LucidReader.Core/Offline LucidReader.Core.Tests/Offline
git commit -m "feat(reader): stub detection heuristic for truncated feed content"
```

---

## Task 17: OfflineDownloader

**Files:**
- Create: `LucidReader.Core/Offline/ArticleFetcher.cs`
- Create: `LucidReader.Core/Offline/OfflineDownloader.cs`
- Test: `LucidReader.Core.Tests/Offline/OfflineDownloaderTests.cs`

**Interfaces:**
- Consumes: `ItemRepository`, `FeedRepository` (Tasks 6, 7), `IHtmlToMarkdownService` (Task 1, namespace `MarkdownViewer.Services`), `StubDetector` (Task 16), `EffectiveFeedSettings` (Task 9).
- Produces:
  - `sealed class ArticleFetcher(HttpClient http)` with `Task<string?> FetchHtmlAsync(string url, CancellationToken ct = default)` returning null on any failure.
  - `sealed class OfflineDownloader : IAsyncDisposable` with constructor `(ItemRepository items, FeedRepository feeds, ArticleFetcher articles, IHtmlToMarkdownService converter, Func<ReaderSettings> settings, TimeProvider timeProvider, int maxConcurrency = 2)`, plus `bool TryQueue(long itemId)`, `Task<int> QueuePendingAsync(int limit = 200, CancellationToken ct = default)`, `Task DownloadNowAsync(long itemId, CancellationToken ct = default)`, `int PendingCount { get; }`, `int ActiveCount { get; }`, `event Action<long>? Completed`.
  - `static readonly TimeSpan MaxArticleFetchDuration` of 180 seconds.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Offline/OfflineDownloaderTests.cs`:

```csharp
using System.Net;
using LucidReader.Core.Model;
using LucidReader.Core.Offline;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Feeds;
using LucidReader.Core.Tests.Storage;
using MarkdownViewer.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Offline;

/// <summary>
/// A converter that records what it was given, so tests can assert which HTML
/// reached it without depending on AngleSharp's exact markdown output.
/// </summary>
internal sealed class RecordingConverter : IHtmlToMarkdownService
{
    public List<string> Converted { get; } = [];

    public Task<string> ConvertAsync(string html, Uri? sourceUri, CancellationToken ct = default)
    {
        Converted.Add(html);
        return Task.FromResult("# Converted\n\n" + html.Length + " characters of HTML.");
    }
}

public class OfflineDownloaderTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));
    private readonly RecordingConverter _converter = new();

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private FeedRepository _feeds = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feeds = new FeedRepository(_db);
        _feedId = await _feeds.AddAsync(new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private OfflineDownloader CreateDownloader(StubHttpHandler handler) =>
        new(_items, _feeds, new ArticleFetcher(handler.CreateClient()),
            _converter, () => ReaderSettings.Defaults, _time);

    private Task<long> AddItemAsync(string? summary, OfflineState state = OfflineState.Pending) =>
        _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = Guid.NewGuid().ToString(),
            Title = "An article",
            Link = "https://example.com/article",
            Summary = summary,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = state
        });

    private static string LongArticle() =>
        "<p>" + string.Join(" ", Enumerable.Repeat("substantive prose here", 200)) + "</p>";

    [Fact]
    public async Task Feed_content_that_is_already_complete_is_converted_without_a_page_fetch()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>full page</html>");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync(LongArticle());

        await downloader.DownloadNowAsync(id);

        Assert.Empty(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Feed, item.ContentSource);
        Assert.NotNull(item.ContentMarkdown);
    }

    [Fact]
    public async Task A_stub_triggers_a_page_fetch_and_is_stored_as_extracted()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.OK, "<html><body>" + LongArticle() + "</body></html>");
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>Short teaser.</p>");

        await downloader.DownloadNowAsync(id);

        Assert.Single(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Extracted, item.ContentSource);
    }

    [Fact]
    public async Task A_failed_page_fetch_leaves_the_summary_readable()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotFound);
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync("<p>Short teaser.</p>");

        await downloader.DownloadNowAsync(id);

        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Failed, item!.OfflineState);
        Assert.NotNull(item.OfflineError);
        Assert.Equal("<p>Short teaser.</p>", item.Summary);
    }

    [Fact]
    public async Task A_stub_with_no_link_falls_back_to_converting_the_summary()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>");
        await using var downloader = CreateDownloader(handler);
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = "no-link",
            Summary = "<p>Short teaser.</p>",
            Link = null,
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        await downloader.DownloadNowAsync(id);

        Assert.Empty(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(OfflineState.Downloaded, item!.OfflineState);
        Assert.Equal(ContentSource.Feed, item.ContentSource);
    }

    [Fact]
    public async Task Full_text_fetch_disabled_on_the_feed_skips_the_page_fetch()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "<html>page</html>");
        var feedId = await _feeds.AddAsync(new Feed
        {
            FeedUrl = "https://nofulltext.example/feed.xml",
            FetchFullText = false
        });
        await using var downloader = CreateDownloader(handler);
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = feedId,
            Guid = "x",
            Summary = "<p>Short teaser.</p>",
            Link = "https://nofulltext.example/article",
            FirstSeenUtc = _time.GetUtcNow(),
            OfflineState = OfflineState.Pending
        });

        await downloader.DownloadNowAsync(id);

        Assert.Empty(handler.Requests);
        var item = await _items.GetAsync(id);
        Assert.Equal(ContentSource.Feed, item!.ContentSource);
    }

    [Fact]
    public async Task Queueing_pending_work_picks_up_everything_marked_pending()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        for (var i = 0; i < 3; i++) await AddItemAsync(LongArticle());

        var queued = await downloader.QueuePendingAsync();

        Assert.Equal(3, queued);
    }

    [Fact]
    public async Task An_item_already_queued_is_not_queued_twice()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync(LongArticle());

        var first = downloader.TryQueue(id);
        var second = downloader.TryQueue(id);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task Every_queued_item_reaches_completion()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        var ids = new List<long>();
        for (var i = 0; i < 10; i++) ids.Add(await AddItemAsync(LongArticle()));

        var completed = new List<long>();
        var done = new TaskCompletionSource();
        downloader.Completed += id =>
        {
            lock (completed)
            {
                completed.Add(id);
                if (completed.Count == 10) done.TrySetResult();
            }
        };

        foreach (var id in ids) downloader.TryQueue(id);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(10, completed.Count);
    }

    [Fact]
    public async Task Downloaded_content_becomes_searchable()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, LongArticle());
        await using var downloader = CreateDownloader(handler);
        var id = await AddItemAsync(LongArticle());

        await downloader.DownloadNowAsync(id);

        var results = await new SearchRepository(_db).SearchAsync("Converted", 10);
        Assert.Single(results);
        Assert.Equal(id, results[0].Id);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter OfflineDownloaderTests 2>&1 | tail -10
```

Expected: compilation failure, `OfflineDownloader` does not exist.

- [ ] **Step 3: Write ArticleFetcher**

Create `LucidReader.Core/Offline/ArticleFetcher.cs`:

```csharp
using LucidReader.Core.Feeds;

namespace LucidReader.Core.Offline;

/// <summary>
/// Fetches an article page as HTML. Returns null rather than throwing on any
/// failure: a page we cannot get is a normal outcome, and the caller already
/// has the feed summary to fall back on.
/// </summary>
public sealed class ArticleFetcher(HttpClient http)
{
    private const int MaxArticleBytes = 8 * 1024 * 1024;

    public async Task<string?> FetchHtmlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation(
                "Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode) return null;

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Content.Headers.ContentLength > MaxArticleBytes) return null;

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Write OfflineDownloader**

Create `LucidReader.Core/Offline/OfflineDownloader.cs`:

```csharp
using System.Collections.Concurrent;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using MarkdownViewer.Services;
using Mostlylucid.Ephemeral;

namespace LucidReader.Core.Offline;

/// <summary>
/// Converts feed items into stored markdown, fetching the original page when
/// the feed only gave a teaser.
///
/// This runs on its own coordinator rather than sharing the refresh one: page
/// fetches take much longer than feed fetches, and a burst of new items must
/// not starve feed refreshing.
/// </summary>
public sealed class OfflineDownloader : IAsyncDisposable
{
    public static readonly TimeSpan MaxArticleFetchDuration = TimeSpan.FromSeconds(180);

    private readonly ItemRepository _items;
    private readonly FeedRepository _feeds;
    private readonly ArticleFetcher _articles;
    private readonly IHtmlToMarkdownService _converter;
    private readonly Func<ReaderSettings> _settings;
    private readonly EphemeralWorkCoordinator<long> _coordinator;
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    public OfflineDownloader(
        ItemRepository items,
        FeedRepository feeds,
        ArticleFetcher articles,
        IHtmlToMarkdownService converter,
        Func<ReaderSettings> settings,
        TimeProvider timeProvider,
        int maxConcurrency = 2)
    {
        _items = items;
        _feeds = feeds;
        _articles = articles;
        _converter = converter;
        _settings = settings;

        _coordinator = new EphemeralWorkCoordinator<long>(
            RunAsync,
            MaxArticleFetchDuration,
            new EphemeralOptions
            {
                MaxConcurrency = maxConcurrency,
                // A first sync of a large OPML import can produce thousands of
                // pending items at once, and the 200 default would block the
                // enqueueing caller.
                MaxTrackedOperations = 8192
            },
            timeProvider);
    }

    public int PendingCount => _coordinator.PendingCount;
    public int ActiveCount => _coordinator.ActiveCount;

    public event Action<long>? Completed;

    public bool TryQueue(long itemId)
    {
        if (!_inFlight.TryAdd(itemId, 0)) return false;

        if (_coordinator.TryEnqueue(itemId)) return true;

        _inFlight.TryRemove(itemId, out _);
        return false;
    }

    public async Task<int> QueuePendingAsync(int limit = 200, CancellationToken ct = default)
    {
        var pending = await _items.GetPendingOfflineAsync(limit, ct);
        return pending.Count(item => TryQueue(item.Id));
    }

    private async Task RunAsync(long itemId, CancellationToken ct)
    {
        try
        {
            await DownloadNowAsync(itemId, ct);
        }
        finally
        {
            _inFlight.TryRemove(itemId, out _);
            Completed?.Invoke(itemId);
        }
    }

    public async Task DownloadNowAsync(long itemId, CancellationToken ct = default)
    {
        var item = await _items.GetAsync(itemId, ct);
        if (item is null) return;

        var feed = await _feeds.GetAsync(item.FeedId, ct);
        if (feed is null) return;

        var settings = EffectiveFeedSettings.Resolve(feed, _settings());
        var feedContent = item.Summary;

        // The feed already gave us the whole thing, or we are not allowed to
        // go looking for more. Either way, convert what we have.
        if (!StubDetector.IsStub(feedContent)
            || !settings.FetchFullText
            || string.IsNullOrWhiteSpace(item.Link))
        {
            await StoreAsync(itemId, feedContent, item.Link, ContentSource.Feed, ct);
            return;
        }

        var html = await _articles.FetchHtmlAsync(item.Link, ct);
        if (html is null)
        {
            await _items.SetOfflineFailedAsync(
                itemId, $"Could not fetch {item.Link}", ct);
            return;
        }

        try
        {
            var markdown = await _converter.ConvertAsync(html, new Uri(item.Link), ct);
            await _items.SetContentAsync(itemId, markdown, ContentSource.Extracted, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _items.SetOfflineFailedAsync(itemId, ex.Message, ct);
        }
    }

    private async Task StoreAsync(
        long itemId,
        string? html,
        string? link,
        ContentSource source,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            await _items.SetOfflineFailedAsync(itemId, "The feed supplied no content.", ct);
            return;
        }

        try
        {
            var uri = Uri.TryCreate(link, UriKind.Absolute, out var parsed) ? parsed : null;
            var markdown = await _converter.ConvertAsync(html, uri, ct);
            await _items.SetContentAsync(itemId, markdown, source, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _items.SetOfflineFailedAsync(itemId, ex.Message, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _coordinator.Complete();
        await _coordinator.DisposeAsync();
    }
}
```

**Not implemented here, deliberately:** spec section 4.3 step 4 says referenced images are pulled into `ImageCacheService` and rewritten to local paths. That is left out of this task because `ImageCacheService` is an Avalonia-adjacent service in `Mostlylucid.LucidView.Markdown`, and wiring it here would give `LucidReader.Core` the UI dependency the global constraints forbid. Image caching belongs in Plan 2, where the app composes the downloader with the cache. Note it in Plan 2's scope when writing it.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter OfflineDownloaderTests 2>&1 | tail -5
```

Expected: 9 passed.

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core/Offline LucidReader.Core.Tests/Offline
git commit -m "feat(reader): offline article downloader with full-text fallback"
```

---

## Task 18: Retention and cleanup

**Files:**
- Create: `LucidReader.Core/Maintenance/RetentionService.cs`
- Test: `LucidReader.Core.Tests/Maintenance/RetentionServiceTests.cs`

**Interfaces:**
- Consumes: `ReaderDatabase` (Task 5), `ReaderSettings` (Task 9).
- Produces: `sealed class RetentionService(ReaderDatabase db, Func<ReaderSettings> settings, TimeProvider timeProvider)` with `Task<int> PruneAsync(CancellationToken ct = default)` returning the number of items deleted, and `Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

Create `LucidReader.Core.Tests/Maintenance/RetentionServiceTests.cs`:

```csharp
using LucidReader.Core.Maintenance;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using LucidReader.Core.Tests.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace LucidReader.Core.Tests.Maintenance;

public class RetentionServiceTests : IAsyncLifetime
{
    private readonly TempDatabase _temp = new();
    private readonly FakeTimeProvider _time =
        new(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));

    private ReaderDatabase _db = null!;
    private ItemRepository _items = null!;
    private long _feedId;

    public async Task InitializeAsync()
    {
        _db = await ReaderDatabase.OpenAsync(_temp.Path);
        _items = new ItemRepository(_db);
        _feedId = await new FeedRepository(_db).AddAsync(
            new Feed { FeedUrl = "https://example.com/feed.xml" });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _temp.Dispose();
    }

    private async Task<long> AddAsync(
        string guid, int ageDays, bool isRead, bool isStarred = false)
    {
        var id = await _items.UpsertAsync(new FeedItem
        {
            FeedId = _feedId,
            Guid = guid,
            Title = guid,
            PublishedUtc = _time.GetUtcNow().AddDays(-ageDays),
            FirstSeenUtc = _time.GetUtcNow().AddDays(-ageDays)
        });
        if (isRead) await _items.SetReadAsync(id, true);
        if (isStarred) await _items.SetStarredAsync(id, true);
        return id;
    }

    private RetentionService Service(ReaderSettings settings) =>
        new(_db, () => settings, _time);

    private async Task<int> CountAsync() =>
        (await _items.QueryAsync(new ItemQuery(null, null, ItemFilter.All, 1000, 0))).Count;

    [Fact]
    public async Task Read_items_older_than_the_window_are_deleted()
    {
        await AddAsync("old-read", 40, isRead: true);
        await AddAsync("recent-read", 5, isRead: true);
        var service = Service(ReaderSettings.Defaults with { KeepReadArticlesDays = 30 });

        var deleted = await service.PruneAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Unread_items_survive_when_keeping_unread_forever()
    {
        await AddAsync("old-unread", 400, isRead: false);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepReadArticlesDays = 30,
            KeepUnreadForever = true
        });

        var deleted = await service.PruneAsync();

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task Unread_items_are_pruned_when_a_window_is_configured()
    {
        await AddAsync("old-unread", 400, isRead: false);
        await AddAsync("recent-unread", 10, isRead: false);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepUnreadForever = false,
            KeepUnreadDays = 180
        });

        var deleted = await service.PruneAsync();

        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task Starred_items_are_never_deleted_by_age()
    {
        await AddAsync("old-starred", 900, isRead: true, isStarred: true);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepReadArticlesDays = 1,
            NeverDeleteStarred = true
        });

        var deleted = await service.PruneAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task The_per_feed_cap_keeps_the_newest_items()
    {
        for (var i = 0; i < 10; i++)
            await AddAsync($"item-{i:D2}", ageDays: i, isRead: false);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepUnreadForever = true,
            MaxArticlesPerFeed = 5
        });

        await service.PruneAsync();

        var remaining = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Equal(5, remaining.Count);
        Assert.Contains(remaining, item => item.Guid == "item-00");
        Assert.DoesNotContain(remaining, item => item.Guid == "item-09");
    }

    [Fact]
    public async Task The_per_feed_cap_still_spares_starred_items()
    {
        for (var i = 0; i < 10; i++)
            await AddAsync($"item-{i:D2}", ageDays: i, isRead: true, isStarred: i == 9);
        var service = Service(ReaderSettings.Defaults with
        {
            KeepReadArticlesDays = 365,
            MaxArticlesPerFeed = 5,
            NeverDeleteStarred = true
        });

        await service.PruneAsync();

        var remaining = await _items.QueryAsync(new ItemQuery(_feedId, null, ItemFilter.All, 100, 0));
        Assert.Contains(remaining, item => item.Guid == "item-09");
    }

    [Fact]
    public async Task Pruning_removes_the_items_from_the_search_index_too()
    {
        var id = await AddAsync("old-read", 40, isRead: true);
        await _items.SetContentAsync(id, "distinctive haystack term", ContentSource.Feed);
        var service = Service(ReaderSettings.Defaults with { KeepReadArticlesDays = 30 });

        await service.PruneAsync();

        var results = await new SearchRepository(_db).SearchAsync("haystack", 10);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Pruning_an_empty_database_deletes_nothing_and_does_not_throw()
    {
        var service = Service(ReaderSettings.Defaults);

        Assert.Equal(0, await service.PruneAsync());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter RetentionServiceTests 2>&1 | tail -10
```

Expected: compilation failure, `RetentionService` does not exist.

- [ ] **Step 3: Write RetentionService**

Create `LucidReader.Core/Maintenance/RetentionService.cs`:

```csharp
using LucidReader.Core.Model;
using LucidReader.Core.Storage;

namespace LucidReader.Core.Maintenance;

/// <summary>
/// Deletes old items according to the retention settings. Starred items are
/// exempt from every rule when NeverDeleteStarred is on: a star is the user
/// saying "keep this", and no automatic policy should override that.
/// </summary>
public sealed class RetentionService(
    ReaderDatabase db,
    Func<ReaderSettings> settings,
    TimeProvider timeProvider)
{
    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var current = settings();
        var now = timeProvider.GetUtcNow();
        var starredClause = current.NeverDeleteStarred ? "AND is_starred = 0" : "";
        var deleted = 0;

        // Read items past their window.
        if (current.KeepReadArticlesDays > 0)
        {
            deleted += await db.WriteAsync(
                $"""
                 DELETE FROM items
                 WHERE is_read = 1
                   AND COALESCE(published_utc, first_seen_utc) < $cutoff
                   {starredClause};
                 """,
                new Dictionary<string, object?>
                {
                    ["$cutoff"] = now.AddDays(-current.KeepReadArticlesDays).ToDbString()
                }, ct);
        }

        // Unread items, only when the user has asked for a window.
        if (!current.KeepUnreadForever && current.KeepUnreadDays > 0)
        {
            deleted += await db.WriteAsync(
                $"""
                 DELETE FROM items
                 WHERE is_read = 0
                   AND COALESCE(published_utc, first_seen_utc) < $cutoff
                   {starredClause};
                 """,
                new Dictionary<string, object?>
                {
                    ["$cutoff"] = now.AddDays(-current.KeepUnreadDays).ToDbString()
                }, ct);
        }

        // Per-feed cap: keep the newest N in each feed, drop the rest.
        if (current.MaxArticlesPerFeed > 0)
        {
            deleted += await db.WriteAsync(
                $"""
                 DELETE FROM items
                 WHERE id IN (
                     SELECT id FROM (
                         SELECT id,
                                ROW_NUMBER() OVER (
                                    PARTITION BY feed_id
                                    ORDER BY COALESCE(published_utc, first_seen_utc) DESC
                                ) AS row_number
                         FROM items
                         WHERE 1 = 1 {starredClause}
                     )
                     WHERE row_number > $max
                 );
                 """,
                new Dictionary<string, object?> { ["$max"] = current.MaxArticlesPerFeed },
                ct);
        }

        return deleted;
    }

    public Task<long> GetDatabaseSizeBytesAsync(CancellationToken ct = default) =>
        db.QueryAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        }, ct);
}
```

The FTS index needs no explicit cleanup: the `items_fts_delete` trigger from Task 4 removes each deleted row from the index, which is what the search test in Step 1 confirms.

Note the `starredClause` is interpolated rather than parameterised. That is safe because it is one of two compile-time constants chosen by a bool, never user input. If anyone extends this to interpolate anything else, it must become a parameter.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj --filter RetentionServiceTests 2>&1 | tail -5
```

Expected: 8 passed.

- [ ] **Step 5: Run the whole suite**

Everything in this plan should now be green together, and lucidVIEW should still be untouched:

```bash
cd /Users/scottgalloway/RiderProjects/lucidview
dotnet test LucidReader.Core.Tests/LucidReader.Core.Tests.csproj 2>&1 | tail -5
dotnet test MarkdownViewer.Tests/MarkdownViewer.Tests.csproj 2>&1 | tail -5
dotnet test MarkdownViewer.Full.Tests/MarkdownViewer.Full.Tests.csproj 2>&1 | tail -5
```

Expected: all three green, with the lucidVIEW counts matching the Task 1 Step 1 baseline exactly.

- [ ] **Step 6: Commit**

```bash
git add LucidReader.Core/Maintenance LucidReader.Core.Tests/Maintenance
git commit -m "feat(reader): retention pruning with starred-item exemption"
```

---

## Done

At this point `LucidReader.Core` is a complete headless feed engine: it subscribes, schedules, fetches conditionally, parses three feed formats, dedupes, stores, downloads articles for offline reading, searches, and prunes. There is no UI, and every behaviour above is covered by a test that does not touch the network.

Plan 2 builds the Avalonia app on top of it, including the image caching left out of Task 17. Plan 3 covers packaging, the macOS signed native SQLite library, and the FULL StyloExtract binding.

## Deferred to Plan 2, deliberately

Recorded here so none of it is quietly lost between plans. Each item is spec
scope that this plan does not deliver.

- **Image caching.** Spec 4.3 step 4. `ImageCacheService` lives in
  `Mostlylucid.LucidView.Markdown` and wiring it into `LucidReader.Core` would
  give the Core project the UI dependency the global constraints forbid. The app
  composes the downloader with the cache in Plan 2.
- **Wiring the concurrency settings.** `ReaderSettings.MaxConcurrentFetches` and
  `MaxConcurrentDownloads` exist and round-trip, but Tasks 14 and 17 take
  concurrency as a constructor argument defaulting to 4 and 2. Plan 2 passes the
  settings values in at composition time. Note that both coordinators fix their
  concurrency at construction, so changing the setting requires rebuilding them:
  Plan 2 must either rebuild on change or make the setting take effect at next
  launch, and say which in its UI.
- **Tags.** The `tags` and `item_tags` tables are created in Task 4 and have no
  repository yet, because nothing headless reads them. Plan 2 adds
  `TagRepository` alongside the item actions that use it.
- **OPML import and export.** Spec 6.3. Pure file handling against
  `FolderRepository` and `FeedRepository`, both of which exist after Task 6.
- **Feed autodiscovery from a site URL.** Spec 6.3.
- **Startup FTS5 probe in the running app.** Task 4 proves FTS5 works in the
  test suite on every platform. Spec 7.1 also wants the shipped app to fail
  loudly at startup rather than at first search; that check belongs in the app's
  startup path, in Plan 3 with the rest of the packaging work.
- **Pausing refresh when the machine is offline.** `ReaderSettings.PauseWhenOffline`
  round-trips, and `FeedRefreshService` exposes `Pause()` and `Resume()`, but
  nothing observes network availability yet. That observation is platform code
  and belongs with the app.

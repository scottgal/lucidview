# mylo (source)

This directory is the mylo desktop application. mylo is an RSS and Atom reader
that runs on your machine, fetches feeds directly from the sites that publish
them, and keeps everything in one SQLite file.

- **Using mylo?** Start with [the user manual](Assets/manual/user-manual.md).
  It ships inside the app too, under Help, mylo User Manual, or press F1.
- **Installing it?** See [README-mylo.md](../README-mylo.md) at the repository
  root, and the [releases page](https://github.com/scottgal/lucidview/releases).

The rest of this page is for working on the code.

## Why the folder is called LucidReader

The product is `mylo`. The projects, folders and namespaces still say
`LucidReader`, which was the working name. That split is deliberate: renaming
namespaces across the tree is churn that changes nothing a user sees. The
assembly, the window title, the data directory and the User-Agent all say
`mylo`.

## Layout

| Project | What lives there |
|---|---|
| `LucidReader/` | The Avalonia app: views, view logic, styles, assets, the composition root (`ReaderServices.cs`). |
| `LucidReader.Core/` | Everything with no UI dependency: `Feeds/` (fetching, parsing, autodiscovery, the URL policy), `Storage/` (SQLite, migrations, repositories, search), `Offline/` (article download and conversion), `Opml/`, `Sync/` (the refresh service and scheduler), `Maintenance/` (retention), `Net/`, `Notifications/`. |
| `LucidReader.Core.Tests/` | Tests for both. |
| `ux-scripts/` | The UI driving scripts. |

Anything that has to be right lives in a plain, testable class rather than in a
view. A `Window` cannot be constructed in a unit test here, so view code stays
thin and the decisions sit in types like `ReadingColumnMetrics`,
`FeedUpdateSummary`, `ReaderLayout`, `FeedUrlPolicy` and `ArticleListDetector`.

## Building and running

```bash
dotnet build LucidReader/LucidReader.csproj
dotnet run   --project LucidReader/LucidReader.csproj
dotnet test  LucidReader.Core.Tests/LucidReader.Core.Tests.csproj
```

A macOS `.app` bundle, which is what releases ship:

```bash
LucidReader/macos/make-bundle.sh
```

The bundle matters beyond packaging. `IncludeNativeLibrariesForSelfExtract` is
off for the hardened runtime, so a publish leaves five dylibs beside the
executable; copying only the binary produces an app that starts and then dies
at the first database access. The bundle script fails the build if any of them
is missing.

## Driving the UI

The scripts under `ux-scripts/` drive the real application through
`Mostlylucid.Avalonia.UITesting`, which is a Debug-only reference.

```bash
ux-scripts/run-reader-smoke.sh          # any of the 22 runners
ux-scripts/capture-reader-manual.sh     # regenerate every manual screenshot
```

They run **headless** by default: the app renders into a bitmap, so a run puts
no window on screen and takes no keyboard focus. Set `MYLO_UX_MODE=` (empty) to
watch one happen.

Each run seeds a throwaway profile via `MYLO_DATA_DIR` and removes it from an
`EXIT`/`INT`/`TERM` trap, so the scripts are repeatable from any starting state
and leave your real profile alone. See `ux-scripts/reader-harness.sh`.

## Things that will bite you

These are not style preferences. Each one has cost real debugging time here.

**Bindings fail silently.** `AvaloniaUseCompiledBindingsByDefault` is `false`,
so `{Binding Foo}` naming a property that does not exist does nothing at all,
with no error anywhere. Add the property first and match the name exactly.

**Never hand-write `InitializeComponent`.** A
`private void InitializeComponent() => AvaloniaXamlLoader.Load(this);` does not
override the generated method, it shadows it. The XAML still loads but every
`x:Name` field stays null. This crashed a dialog on open, and `MainWindow`
carried it for weeks behind a `FindControl` workaround.

**`Process.Start` belongs in one place.** `Services/SafeLinkOpener.cs` is the
only site, and it allows http and https only. Anything that opens a URL goes
through it, and a handler that consults it must mark the event handled so
nothing downstream gets a second attempt at the URL.

**Remote content is hostile.** Feed items, OPML files, article HTML and image
references all come from somewhere else. Every URL derived from them goes
through `Feeds/FeedUrlPolicy` before a request, and redirects are re-checked
per hop in `Feeds/PolicyHttpHandler`.

**Migrations are forward-only.** The schema is at version 8. Never edit an
existing migration; add the next one, and verify it against a database that
already holds data.

**Keyboard shortcuts are not `Window.KeyBindings`.** Avalonia evaluates key
bindings before the routed `KeyDown`, so bare-letter shortcuts fired while the
user typed in the search box. They go through `Services/ReaderShortcuts.cs`
behind a text-entry focus guard instead.

**The harness cannot see every control.** It cannot click or screenshot a
`ContextMenu`, a `NativeMenu` or a tray menu. Anything reachable only from one
of those cannot be verified, which is why several actions also have a named
button.

## Where mylo keeps your data

`~/Library/Application Support/mylo/` on macOS: `reader.db` with its `-wal` and
`-shm`, `settings.json`, and the image cache. Delete the folder for a clean
slate.

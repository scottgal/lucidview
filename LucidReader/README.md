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

## Packaging

Two scripts, one per shape, and releases run exactly these:

```bash
LucidReader/macos/make-bundle.sh osx-arm64        # or osx-x64
LucidReader/packaging/make-archive.sh win-x64     # win-arm64, linux-x64, linux-arm64
```

`make-bundle.sh` produces `mylo.app`: an Info.plist, an `.icns` and an ad-hoc
signature around the publish output. `make-archive.sh` produces a folder
archived as a `.zip` for Windows and a `.tar.gz` for Linux, with an
`install.txt` and, on Linux, a `mylo.desktop` and an icon.

Both fail the build if the executable, any native library or the bundled
manual is missing from the payload, and that check is the point of having
scripts at all. `IncludeNativeLibrariesForSelfExtract` is off on **every** RID,
so no platform's publish is one file: it is the executable plus its native
libraries (five dylibs on macOS, five DLLs on Windows, four `.so` files on
Linux) plus `manual/`. Copy out only the binary, as any naive packaging step
would, and you ship something that opens a window and then dies at the first
database access.

macOS has to have it off, because self-extracting the SQLite library into a
temp directory makes the hardened runtime refuse to load it. Windows and Linux
do not have to, and it is still off there on purpose; the reasoning is in
`LucidReader.csproj` next to the property.

The Homebrew cask for the macOS builds is in
[`packaging/homebrew`](../packaging/homebrew) at the repository root, with a
README covering the tap and Gatekeeper.

### Window chrome is not the same on every platform

macOS extends the client area under the title bar so the toolbar and the
traffic lights share a band, which is why the toolbar carries an 80px left
margin. Windows and Linux put their system buttons on the **right**, so that
layout gives them a dead gutter on the left and system buttons drawn over the
Settings button on the right. `ConfigurePlatformChrome` in
`MainWindow.Layout.cs` switches the extended client area back off on those
platforms, restores the ordinary system title bar and evens the margin up. The
toolbar's drag and double-click-to-zoom handlers are macOS-only for the same
reason: on Windows and Linux the real title bar is still there and already
does both.

## Driving the UI

The scripts under `ux-scripts/` drive the real application through
`Mostlylucid.Avalonia.UITesting`, which is a Debug-only reference.

```bash
ux-scripts/run-reader-smoke.sh          # any of the 24 runners
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

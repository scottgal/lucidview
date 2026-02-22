# ***lucid*VIEW**

An Avalonia Markdown Viewer with code highlighting and Mermaid support using Naiad Time-boxed weekend project. Fork it,
fix it, ship it. Public domain.

**by [***mostly*lucid**](https://www.mostlylucid.net)**

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![License](https://img.shields.io/badge/license-UnLicense-green)

---

## Why?

Every markdown viewer either:

- Wraps a Chromium browser (100MB+ bloat)
- Looks like it's from 2005
- Doesn't support mermaid diagrams
- Can't handle local images

This one doesn't. Single exe. Cross-platform. Fast.

*Time-boxed weekend project. Fork it, fix it, ship it. Public domain.*

---

## Diagram Examples

All diagrams rendered natively by Naiad -- no browser, no JavaScript, no external services.

<p align="center">
  <img src="docs/images/example-flowchart.svg" alt="Flowchart" width="500">
</p>

<p align="center">
  <img src="docs/images/example-sequence.svg" alt="Sequence Diagram" width="400">
  <img src="docs/images/example-pie.svg" alt="Pie Chart" width="250">
</p>

---

## Features

**Rendering**

- Real-time markdown with [LiveMarkdown.Avalonia](https://github.com/DearVa/LiveMarkdown.Avalonia) - syntax highlighting
  built-in
- Mermaid diagrams via [Naiad](https://github.com/SimonCropp/Naiad) -- 20+ diagram types rendered natively in .NET
- Mermaid-compatible Naiad directives via comments (`%% naiad: ...`) for per-file theme/skin configuration
- Local & remote images that actually work

**UI**

- 4 themes: Light, Dark, VS Code, GitHub
- TOC navigation panel
- Search (Ctrl+F)
- Zoom slider
- Preview/Raw toggle

**Deployment**

- Single file executable
- No dependencies
- ~50MB

**Extensibility**

- Pluginized native diagram replacement pipeline (`MarkdownViewer/Plugins`)
- Shape skin packs from folder or archive (`Naiad/skins`)

---

## Install

Grab from [Releases](../../releases):

| Platform    | Download                  |
|-------------|---------------------------|
| Windows     | `lucidVIEW-win-x64.zip`   |
| macOS Intel | `lucidVIEW-osx-x64.zip`   |
| macOS ARM   | `lucidVIEW-osx-arm64.zip` |
| Linux       | `lucidVIEW-linux-x64.zip` |

Extract. Run. Done.

---

## Usage

```bash
lucidVIEW document.md
```

Or drag & drop. Or Ctrl+O. Or paste a URL (Ctrl+Shift+O).

### Naiad Per-File Options (Mermaid-Compatible)

```mermaid
%% naiad: skinPack=daisyui, theme=dark
flowchart LR
  A[Start] --> B[Done]
```

See:

- `Naiad/skins/README.md`
- `MarkdownViewer/Plugins/README.md`

### Shortcuts

| Key            | Action     |
|----------------|------------|
| `Ctrl+O`       | Open file  |
| `Ctrl+Shift+O` | Open URL   |
| `Ctrl+F`       | Search     |
| `Ctrl+B`       | Menu panel |
| `Ctrl++/-`     | Zoom       |
| `F11`          | Fullscreen |
| `F1`           | Help       |

---

## Build

```bash
git clone https://github.com/scottgal/markdown.viewer.git
cd markdown.viewer
dotnet run --project MarkdownViewer/MarkdownViewer.csproj
```

Publish:

```bash
dotnet publish MarkdownViewer/MarkdownViewer.csproj -c Release -r win-x64
```

---

## Stack

| Package                                                                  | Version | Purpose                                  |
|--------------------------------------------------------------------------|---------|------------------------------------------|
| [Avalonia](https://avaloniaui.net/)                                      | 11.3.10 | Cross-platform UI                        |
| [LiveMarkdown.Avalonia](https://github.com/DearVa/LiveMarkdown.Avalonia) | 1.7.0   | Markdown rendering + syntax highlighting |
| [Naiad](https://github.com/SimonCropp/Naiad)                             | 0.1.1   | Mermaid diagrams                         |
| [SkiaSharp](https://github.com/mono/SkiaSharp)                           | 3.119.1 | Graphics                                 |
| [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia)                    | 3.4.1   | SVG support                              |

---

## License

[The Unlicense](https://unlicense.org/) - Do whatever you want.

---

*View this file in lucidVIEW: F1 or Help menu*

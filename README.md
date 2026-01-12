# ***lucid*VIEW**

A [***mostly*lucid**](https://www.mostlylucid.net) 'time boxed tool' to deliver modern, lightweight cross-platform markdown viewer built with [Avalonia UI](https://avaloniaui.net/).

This is the result of being annoyed by existing ones and wondering why a .NET 'native' one didn't exist.

Well the answer is *because it's a PITA otherwise* using WebViews really is the best way to do this but here's what happens if you don't. 

Anyway here's the tool and code. Feel free to fork and fix it's public domain. Any problems open an issue but no promises. As I say 'time boxed' and this is what I did today 🤓


**by [***mostly*lucid**](https://www.mostlylucid.net)**

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![License](https://img.shields.io/badge/license-UnLicense-green)

## Features

- **Modern, clean UI** with slide-out menu panel
- **GitHub-style markdown rendering** with full CommonMark support
- **Syntax highlighting** for code blocks with automatic language detection
- **4 themes**: Light, Dark, VS Code, and GitHub
- **Font size controls** (A/A buttons in header)
- **Navigation panel** with automatic heading detection
- **Search functionality** (Ctrl+F) to find text within documents
- **Preview and Raw modes** to view rendered or source markdown
- **Drag and drop** support for opening files
- **URL loading** for viewing remote markdown files
- **Print support** (cross-platform via browser)
- **Command line support** for file associations
- **Single file deployment** - one executable, no dependencies

---

## Installation

### Download

Download the latest release for your platform from the [Releases](../../releases) page:

- **Windows**: `lucidVIEW-win-x64.zip`
- **macOS Intel**: `lucidVIEW-osx-x64.zip`
- **macOS Apple Silicon**: `lucidVIEW-osx-arm64.zip`
- **Linux**: `lucidVIEW-linux-x64.zip`

### Setup

1. Extract the archive to a folder of your choice
2. Run `lucidVIEW` (or `lucidVIEW.exe` on Windows)

### Set as Default Markdown Viewer

#### Windows
1. Right-click any `.md` file
2. Select "Open with" > "Choose another app"
3. Browse to `lucidVIEW.exe`
4. Check "Always use this app to open .md files"
5. Click OK

#### macOS
1. Right-click any `.md` file
2. Select "Get Info"
3. Under "Open with", select lucidVIEW
4. Click "Change All..."

#### Linux
Create a `.desktop` file in `~/.local/share/applications/`:

```ini
[Desktop Entry]
Name=lucidVIEW
Exec=/path/to/lucidVIEW %f
Type=Application
MimeType=text/markdown;text/x-markdown;
Icon=lucidview
```

---

## Usage

### Opening Files

- **Menu**: Click the hamburger menu > Open file...
- **Keyboard**: Press `Ctrl+O`
- **Drag and Drop**: Drag a markdown file onto the application window
- **Command Line**: `lucidVIEW path/to/file.md`
- **URL**: Menu > Open URL... or `Ctrl+Shift+O`

### Supported File Types

- `.md` - Markdown
- `.markdown` - Markdown
- `.mdown` - Markdown
- `.mkd` - Markdown
- `.txt` - Plain text (rendered as markdown)

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Open file |
| `Ctrl+Shift+O` | Open URL |
| `Ctrl+F` | Toggle search |
| `Escape` | Close menu/search |
| `Ctrl+B` | Toggle menu panel |
| `F11` | Toggle full screen |
| `Ctrl++` | Increase font size |
| `Ctrl+-` | Decrease font size |
| `Ctrl+P` | Print |
| `F1` | Open help |

---

## Themes

lucidVIEW includes four built-in themes:

| Theme | Description |
|-------|-------------|
| **Light** | Clean, bright theme for well-lit environments |
| **Dark** | Dark theme with muted colors for low-light conditions |
| **VS Code** | Visual Studio Code's default dark theme |
| **GitHub** | GitHub's markdown rendering style |

To change themes, open the menu and select from the theme cards.

---

## Print

Click the menu > Print... to open the document in your browser with print-ready formatting. Use `Ctrl+P` in the browser to print.

The print view:
- Uses clean formatting optimized for paper
- Includes document title and timestamp
- Automatically opens the browser's print dialog

---

## Building from Source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
# Clone the repository
git clone https://github.com/scottgal/markdown.viewer.git
cd markdown.viewer

# Build
dotnet build MarkdownViewer/MarkdownViewer.csproj -c Release

# Run
dotnet run --project MarkdownViewer/MarkdownViewer.csproj
```

### Publish

```bash
# Windows
dotnet publish MarkdownViewer/MarkdownViewer.csproj -c Release -r win-x64 -o publish/win-x64

# macOS
dotnet publish MarkdownViewer/MarkdownViewer.csproj -c Release -r osx-arm64 -o publish/osx-arm64

# Linux
dotnet publish MarkdownViewer/MarkdownViewer.csproj -c Release -r linux-x64 -o publish/linux-x64
```

---

## About

**lucidVIEW** is built with:

- [Avalonia UI](https://avaloniaui.net/) - Cross-platform .NET UI framework
- [Markdown.Avalonia](https://github.com/whistyun/Markdown.Avalonia) - Markdown rendering
- [Naiad](https://github.com/SimonCropp/Naiad) - A *pre-release* Mermaid to SVG library. 

### Version
1.0.0

### Author
[***mostly*lucid**](https://www.mostlylucid.net)

### License
[The Unlicense](https://unlicense.org/) - Public Domain

---

*This documentation can be viewed in lucidVIEW itself! Press F1 or open the Help menu.*

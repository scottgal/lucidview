# Markdown Viewer

A lightweight, cross-platform markdown viewer built with [Avalonia UI](https://avaloniaui.net/).

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **GitHub-style markdown rendering** with full CommonMark support
- **Syntax highlighting** for code blocks with automatic language detection
- **Multiple themes**: Light, Dark, VS Code, and GitHub
- **Navigation panel** with automatic heading detection
- **Search functionality** (Ctrl+F) to find text within documents
- **Preview and Raw modes** to view rendered or source markdown
- **Drag and drop** support for opening files
- **URL loading** for viewing remote markdown files
- **Relative image resolution** for both local files and URLs
- **Recent files** for quick access to previously opened documents
- **Zoom controls** to adjust text size
- **Full screen mode** for distraction-free reading
- **Native AOT compilation** for fast startup and small binary size

---

## Getting Started

### Opening Files

There are several ways to open markdown files:

1. **File Menu**: Click `File > Open File...` or press `Ctrl+O`
2. **Drag and Drop**: Drag a markdown file onto the application window
3. **Command Line**: Run `MarkdownViewer.exe yourfile.md`
4. **URL**: Click `File > Open URL...` or press `Ctrl+Shift+O` to load from the web

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
| `Escape` | Close search |
| `Ctrl+B` | Toggle navigation panel |
| `F11` | Toggle full screen |
| `Ctrl++` | Zoom in |
| `Ctrl+-` | Zoom out |
| `Ctrl+0` | Reset zoom |
| `F1` | Open user manual |

### Search Shortcuts

When the search panel is open:

| Shortcut | Action |
|----------|--------|
| `Enter` | Find next match |
| `Shift+Enter` | Find previous match |
| `Escape` | Close search |

---

## Themes

Markdown Viewer includes four built-in themes:

### Light
A clean, bright theme with excellent readability. Best for well-lit environments.

### Dark
A dark theme with muted colors that reduces eye strain in low-light conditions.

### VS Code
Inspired by Visual Studio Code's default dark theme with familiar syntax highlighting colors.

### GitHub
Matches GitHub's markdown rendering style for a consistent reading experience.

**To change themes:**
- Use the dropdown in the toolbar, or
- Navigate to `Theme` menu and select your preference

Your theme preference is saved automatically.

---

## Navigation Panel

The navigation panel provides a table of contents based on markdown headings.

**To show/hide the navigation panel:**
- Click the grid icon in the toolbar, or
- Press `Ctrl+B`, or
- Navigate to `View > Navigation Panel`

The panel automatically extracts headings (H1-H6) from your document and displays them hierarchically. Click any heading to jump to that section.

---

## Search

Press `Ctrl+F` to open the search panel. Type your search term and press `Enter` to find matches.

- **Next match**: Press `Enter` or click "Next"
- **Previous match**: Press `Shift+Enter` or click "Prev"
- **Close search**: Press `Escape` or click "X"

The search displays the current match position (e.g., "3 of 15") and automatically scrolls to show matching text in the raw view.

---

## Preview vs Raw Mode

Toggle between two viewing modes using the tabs below the toolbar:

### Preview Mode
Shows the rendered markdown with formatting, syntax highlighting, images, and links.

### Raw Mode
Shows the original markdown source code in a monospace font for easy editing reference.

---

## Images

Markdown Viewer supports images from multiple sources:

### Local Images
```markdown
![Alt text](./images/screenshot.png)
![Alt text](../assets/logo.png)
```
Images are resolved relative to the markdown file's location.

### Remote Images
```markdown
![Alt text](https://example.com/image.png)
```
Remote images are loaded automatically.

### Base64 Embedded Images
```markdown
![Alt text](data:image/png;base64,iVBORw0KGgo...)
```

---

## Code Blocks

Code blocks are rendered with syntax highlighting. Specify the language after the opening fence:

~~~markdown
```javascript
function hello() {
    console.log("Hello, World!");
}
```
~~~

Supported languages include: JavaScript, TypeScript, Python, C#, Java, Go, Rust, HTML, CSS, JSON, YAML, Bash, SQL, and many more.

---

## Settings

Access settings via `Settings > Preferences...`

### Available Settings

- **Theme**: Choose your preferred color scheme
- **Font Size**: Adjust the base font size for rendered content
- **Show Navigation Panel**: Toggle default navigation panel visibility

Settings are saved automatically and persist between sessions.

---

## Recent Files

The application remembers your recently opened files.

**To access recent files:**
1. Navigate to `File > Recent Files`
2. Click on any file to open it

**To clear recent files:**
1. Navigate to `File > Recent Files`
2. Click "Clear Recent Files"

---

## Loading Remote Files

You can view markdown files hosted on the web:

1. Press `Ctrl+Shift+O` or navigate to `File > Open URL...`
2. Enter the full URL of the markdown file
3. Click OK

**Examples:**
- `https://raw.githubusercontent.com/user/repo/main/README.md`
- `https://example.com/docs/guide.md`

Images in remote files are resolved relative to the file's URL.

---

## Command Line Usage

Open a file directly from the command line:

```bash
# Windows
MarkdownViewer.exe path/to/file.md

# macOS/Linux
./MarkdownViewer path/to/file.md
```

---

## Zoom

Adjust the zoom level for better readability:

- **Zoom In**: `Ctrl++` or `View > Zoom In`
- **Zoom Out**: `Ctrl+-` or `View > Zoom Out`
- **Reset**: `Ctrl+0` or `View > Reset Zoom`

The current zoom level is displayed in the status bar (e.g., "125%").

---

## Full Screen Mode

Press `F11` or navigate to `View > Full Screen` to enter full screen mode.

Press `F11` again or `Escape` to exit full screen.

---

## File Association

### Windows

To set Markdown Viewer as the default application for .md files:

1. Right-click any `.md` file
2. Select "Open with" > "Choose another app"
3. Browse to `MarkdownViewer.exe`
4. Check "Always use this app to open .md files"
5. Click OK

### macOS

1. Right-click any `.md` file
2. Select "Get Info"
3. Under "Open with", select Markdown Viewer
4. Click "Change All..."

### Linux

Create a `.desktop` file in `~/.local/share/applications/`:

```ini
[Desktop Entry]
Name=Markdown Viewer
Exec=/path/to/MarkdownViewer %f
Type=Application
MimeType=text/markdown;text/x-markdown;
```

---

## Troubleshooting

### Images not loading
- Ensure the image path is correct relative to the markdown file
- Check that the image file exists
- For remote images, verify the URL is accessible

### Slow rendering for large files
- Try disabling syntax highlighting in very large code blocks
- Consider splitting very large documents

### Theme not applying
- Restart the application
- Check that settings are being saved correctly

---

## About

**Markdown Viewer** is a lightweight, cross-platform markdown viewer built with:

- [Avalonia UI](https://avaloniaui.net/) - Cross-platform .NET UI framework
- [Markdown.Avalonia](https://github.com/whistyun/Markdown.Avalonia) - Markdown rendering
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) - Syntax highlighting

### Version
1.0.0

### License
MIT License

---

## Support

For issues, feature requests, or contributions, please visit the project repository.

---

*This documentation was written in markdown and can be viewed in Markdown Viewer itself!*

# MarkdownViewer.Browser

Avalonia browser spike that references `Naiad` and renders Mermaid input to SVG text.

This is a proof project to validate the `*.Browser` host shape and runtime compatibility before splitting the existing desktop app into shared + desktop + browser projects.

## Build

```bash
dotnet build MarkdownViewer.Browser/MarkdownViewer.Browser.csproj
```

## Publish

```bash
dotnet publish MarkdownViewer.Browser/MarkdownViewer.Browser.csproj -c Release
```

Serve:

```bash
python -m http.server 8081 --directory MarkdownViewer.Browser/bin/Release/net10.0-browser/publish/wwwroot
```

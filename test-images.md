# Image Loading Test

This document tests various image formats and sources.

## Remote Images (HTTP/HTTPS)

### PNG Badges (shields.io)
![Build Status](https://img.shields.io/badge/build-passing-brightgreen)
![License](https://img.shields.io/badge/license-MIT-blue)
![Version](https://img.shields.io/badge/version-1.0.0-orange)

### GitHub Avatars
![GitHub](https://github.githubassets.com/images/modules/logos_page/GitHub-Mark.png)

### SVG Images
![SVG Test](https://upload.wikimedia.org/wikipedia/commons/0/02/SVG_logo.svg)

### Animated GIF
![Loading Animation](https://upload.wikimedia.org/wikipedia/commons/b/b1/Loading_icon.gif)

### WebP Image
![WebP Test](https://www.gstatic.com/webp/gallery/1.webp)

## HTML img Tags

<img src="https://img.shields.io/badge/HTML-img_tag-red" alt="HTML Test">

<img src="https://github.githubassets.com/images/modules/logos_page/Octocat.png" width="100" alt="Octocat">

## Relative Paths (should work when loaded locally)

These would resolve relative to the markdown file location:
- `./images/local.png`
- `../assets/image.jpg`

## Data URIs

![Tiny](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==)

## Mermaid Diagrams

```mermaid
graph TD
    A[Start] --> B{Is it working?}
    B -->|Yes| C[Great!]
    B -->|No| D[Debug]
    D --> B
```

```mermaid
sequenceDiagram
    participant User
    participant App
    participant Cache
    User->>App: Load markdown
    App->>Cache: Check for images
    Cache-->>App: Return cached/download
    App-->>User: Display content
```

## Code Blocks

```csharp
public class ImageCacheService
{
    public async Task<string> CacheRemoteImageAsync(string url)
    {
        // Download and cache the image
        return localPath;
    }
}
```

```javascript
// JavaScript example
const loadImage = async (url) => {
    const response = await fetch(url);
    return response.blob();
};
```

## Tables

| Format | Supported | Notes |
|--------|-----------|-------|
| PNG | Yes | Standard format |
| JPG | Yes | Standard format |
| GIF | Yes | Including animated |
| WebP | Yes | Modern format |
| SVG | Yes | Converted to PNG |

## Lists

1. First item
2. Second item
   - Nested bullet
   - Another nested
3. Third item

- Bullet list
- Another bullet
  1. Nested numbered
  2. Another nested

## Blockquotes

> This is a blockquote.
> It can span multiple lines.

> **Note:** Important information here.

## Links

- [mostlylucid.net](https://mostlylucid.net)
- [GitHub](https://github.com)
- **[Bold Link](https://example.com)**

---

*End of test document*

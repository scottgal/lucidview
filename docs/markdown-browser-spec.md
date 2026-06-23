# Markdown Browser for lucidVIEW

**Status:** design proposal.
**Premise:** lucidVIEW already opens URLs and converts HTML→markdown via
StyloExtract. With richer inline structure landing in StyloExtract 1.7.0
(headings, inline links, images — see [styloextract-markdown-spec.md](styloextract-markdown-spec.md)),
every HTTP link becomes a navigable destination. lucidVIEW stops being a
viewer and becomes a **calm, native, markdown-first browser** — no Chromium,
no JavaScript, no tracking, no ads, instant launch.

## What lucidVIEW becomes

A focused reader that treats the web like a library of markdown documents. You
type or paste a URL, you get a clean rendered page, you click a link, you get
the next clean page. Back/forward like a browser. History like a browser. But
the renderer is the same one that's already shipped — native Avalonia,
Mermaid offline, 6 themes, Word-style ruler, real print, PDF export.

This is **not** a browser-as-app-platform. It can't run a SPA. It can't fill a
form. It can't watch a video. For any of those the user falls back to their
real browser via "Open in Browser" (`Ctrl+Shift+E`).

## Core UX

### Navigation chrome

Add a slim **address bar strip** between the title bar and the document
column. Always visible when a URL is loaded; hidden for local-file mode (no
ambiguity needed). Layout left → right:

```
[ ← ][ → ][ ↻ ]  https://example.com/path/page    [ Open in Browser ↗ ][ ⋮ ]
```

- `←` / `→` — back / forward in history (`Alt+Left` / `Alt+Right`)
- `↻` — reload (`F5` / `Ctrl+R`)
- URL textbox — type/paste a URL or markdown file path, Enter loads it
- "Open in Browser ↗" — explicit handoff to system default
- `⋮` — menu: Save as `.md`, View source HTML, Reader settings for this domain

### Link routing

Every `http(s)` link in rendered markdown changes behaviour. Today they open
in the system browser. The spec routes them through lucidVIEW unless the user
explicitly asks otherwise.

Routing rules:

| Link target | Behaviour |
|---|---|
| Same-document anchor (`#section`) | Scroll within doc (unchanged) |
| Local relative path (`./foo.md`) | Load via `LoadFile` (unchanged) |
| `http(s)` to a recognised-text type | **Load via `LoadWebPage`** (new) |
| `http(s)` to a binary / unsupported MIME | Open in system browser (with status-bar hint) |
| `Ctrl+Click` on any `http(s)` link | Force open in system browser |
| `Shift+Click` on any `http(s)` link | Force load in lucidVIEW |

The "recognised text type" check is a fast `HEAD` (or `GET` with
`Range: bytes=0-0` fallback) for `Content-Type` starting with `text/`, plus
known extensions (`.md`, `.markdown`, `.html`, `.htm`). PDFs, images, zips
etc. go to the system browser.

A subtle but important point: a user reading a long article shouldn't accidentally
launch fifteen browser windows by clicking footnote links. In-app navigation is
the safer default.

### History

- In-memory stack of (URL, scroll position, title) triples per session
- `Alt+Left` / `Alt+Right` walk the stack
- Side panel "History" section shows the last ~50 visited URLs across the
  session, click-to-jump
- No persistent cross-session history (privacy-by-default; opt-in setting if
  the user wants it)

### Reading state per-page

- Scroll position restored when navigating back to a previously-visited URL
- Theme and font settings are global, not per-page
- Mermaid diagrams cache per-URL (already works for files; extend to URLs)

### Source-toggle for HTML pages

The existing Preview / Raw tabs already let you flip to the markdown source.
For URLs that came in as HTML, add a third tab: **HTML**, showing the original
HTML response, syntax-highlighted. Useful for debugging StyloExtract output.

### Save-as-markdown

A "Save as `.md`" menu item that writes the rendered markdown (including the
StyloExtract output) to disk. The user can build a local archive of read
articles just by saving them. Pair with frontmatter that records the source
URL, fetch date, and StyloExtract version so the file is self-describing.

```yaml
---
source: https://en.wikipedia.org/wiki/Markdown
fetched: 2026-06-23T14:22:00Z
converter: Mostlylucid.StyloExtract 1.7.0 / ReaderGrade
title: "Markdown — Wikipedia"
---

# Markdown

…
```

## Architecture changes

### Models

```csharp
public sealed record NavigationEntry(
    string Url,
    string Title,
    double ScrollPosition,
    DateTime VisitedAtUtc);

public sealed class SessionHistory
{
    public IReadOnlyList<NavigationEntry> Stack { get; }
    public int CurrentIndex { get; }
    public bool CanGoBack { get; }
    public bool CanGoForward { get; }
    public void Push(NavigationEntry entry);
    public NavigationEntry? Back();
    public NavigationEntry? Forward();
}
```

### Services

- **`SessionHistoryService`** — in-memory back/forward stack, max 200 entries
- **`ContentTypeProbeService`** — fast `HEAD` to decide route-in-app vs
  open-in-browser; cache results by host+path for 5 min
- **`HtmlToMarkdownService`** (existing) — switches to `ExtractionProfile.ReaderGrade`
  once StyloExtract 1.7.0 lands

### Views

- New `AddressBar.axaml` user control — drops into `MainWindow.axaml` above
  the document
- New `HtmlSourceTab.axaml.cs` — third tab next to Preview / Raw, only shown
  for URL-loaded docs
- `MainWindow.FileOperations.cs` — `LoadWebPage` accepts an optional
  `pushToHistory: bool` parameter; back/forward call it with `false` to avoid
  re-pushing

### Keybindings (added)

| Shortcut | Action |
|---|---|
| `Ctrl+L` | Focus the URL bar |
| `Alt+Left` | Back |
| `Alt+Right` | Forward |
| `F5` / `Ctrl+R` | Reload |
| `Ctrl+Shift+E` | Open current URL in system browser |
| `Ctrl+Shift+S` | Save current page as `.md` |

`Ctrl+L` is well-established muscle memory from every real browser; worth
matching.

## Phased rollout

### Phase 1 — MVP (1-2 commits)

- Address bar visible only when a URL is loaded
- In-app navigation on `http(s)` link clicks (no Content-Type probe yet — just
  always try in-app, fall back to browser on failure)
- Back / Forward / Reload buttons + keybindings
- In-session history stack only

Outcome: clicking a Wikipedia link opens that page inside lucidVIEW. That's
"a markdown browser" by the kitchen-table definition.

### Phase 2 — polish

- `ContentTypeProbeService` for smarter routing
- HTML source tab
- Save-as-markdown with frontmatter
- History panel in side panel (next to Recent Files)
- Reload preserves scroll position

### Phase 3 — power features

- Per-domain reader settings (always reader / never reader / specific
  profile)
- Bookmarks (separate from history)
- `Ctrl+F` search across history (titles + URLs)
- Optional persistent history (off by default)
- Multiple tabs (real question whether this fits the "calm" vision — leave
  for last and re-evaluate)

## What we explicitly don't build

- **JavaScript execution.** If a site is SPA-only, surface a clear "this page
  needs a browser" hint and offer "Open in Browser".
- **Form submission.** Search engines, logins, anything POST. Same fallback.
- **Cookies, sessions, accounts.** Stateless reader.
- **Tabs (yet).** One doc at a time. Decision deferred to Phase 3.
- **Multi-window.** Same reason.
- **Web extensions, ad blockers, etc.** The architecture (no JS engine) means
  there's nothing to extend.

## Open questions

1. **Should the address bar be visible for local-file mode too?** Argument
   for: consistent chrome reduces cognitive switching. Argument against: an
   address bar over a local file looks weird ("file:///Users/.../foo.md").
   Lean toward hide-when-local-file.
2. **What's the default for ambiguous text/* responses?** A `text/plain`
   response on `https://api.example.com/data.json` — try-in-app or
   open-in-browser? Lean toward in-app (it's a reader; you can always re-route
   with Ctrl+click).
3. **Cache lifetime for fetched pages?** Today every navigation re-fetches.
   For back/forward we should obviously cache; for "I just opened that URL
   thirty seconds ago" maybe also. Tie to `ImageCacheService`-style HTTP
   freshness handling.
4. **What if StyloExtract returns empty?** Show a friendly "this page didn't
   convert cleanly — open in browser?" message rather than a blank pane (we
   saw this with `example.com`).
5. **Print / PDF export of a remote page** — does this work today? It should
   "just work" because the rendered markdown is the same, but worth a test
   pass.
6. **Drag-drop URL** — drag a URL from another app onto lucidVIEW window.
   Should that load via `LoadWebPage`? Probably yes; cheap to add.

## Why this fits lucidVIEW

- One ~50 MB exe still: no Chromium, no JS engine, no webview
- Single-instance, instant launch
- Same renderer the user already uses for local files
- Plays to the project's stated character (no browser, no internet
  round-trip)
- The "calm reading" framing matches the existing themes / ruler /
  print-PDF feature set
- Differentiates clearly from every other markdown viewer (none of which
  navigate the web)

## What it's not trying to be

Lynx with pictures. Pocket. A research tool. A privacy browser. It's a markdown
reader that happens to be hyperlinked — and that's enough.

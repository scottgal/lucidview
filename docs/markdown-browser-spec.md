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

### Being a smart browser

Browsing the modern web means meeting servers halfway. A few low-cost moves
that make lucidVIEW behave like a polite, identifiable, and persistent
markdown client:

**1. Branded User-Agent.** Send a UA that identifies the app and a project
URL, following the bot-style "+URL" convention so site operators can see
who's hitting them:

```
lucidVIEW/2.6.3 (Markdown Browser; +https://www.mostlylucid.net/lucidview)
```

This already ships. Sites that recognise the UA (mostlylucid.net,
hypothetically others) can return markdown directly.

**2. Markdown-first Accept header.** Send `text/markdown` before `text/html`
in the Accept chain so any server that can produce markdown does. Already
in `DownloadWebPageAsync`. Servers known to honour this:

- `mostlylucid.net` (the user's own blog)
- Cloudflare URL→Markdown rewriting (when configured upstream)
- Jina Reader (`r.jina.ai/<original-url>`) returns markdown for any URL

**3. Smart-fallback chain when extraction is sparse.** A real-world test:
`https://www.bbc.com/news` returns 30 KB of Next.js HTML with `__NEXT_DATA__`
JSON and no static `<main>` content — StyloExtract produces ~1 character.
A BBC article URL (`/news/articles/<id>`) returns full `<main>`/`<article>`
markup and works fine. The lesson: when the heuristic comes up empty, try
again before giving up — using only local fetches against the same origin.

Proposed fallback chain (each step only fires when the prior produces
less than ~200 characters of content), in order:

  1. Direct fetch with `Accept: text/markdown` → use if MIME is markdown
  2. Direct fetch with `Accept: text/html` → StyloExtract → use if dense
  3. Check the HTML head for `<link rel="amphtml">` → re-fetch the AMP
     variant against the same origin (cleaner static HTML, no hydration)
  4. Check for a site-known text/lite endpoint pattern derived from the
     hostname (`text.npr.org`, `lite.cnn.com`, etc. — see the
     reader-friendly-sites table) → rewrite the URL and re-fetch
  5. Try parsing `__NEXT_DATA__` / `__INITIAL_STATE__` / `__APOLLO_STATE__`
     JSON blobs from the response — many SSR-with-hydration frameworks
     stash the full article payload there, and we already have the HTML
  6. Synthesize a minimal stub from page metadata (`og:title` +
     `og:description` + canonical link + author + publish date) so the
     user at least sees what the page is about
  7. Show "this page needs JavaScript to render — open in browser?" with
     a one-click handoff

**StyloExtract is the converter, period.** Every step above either feeds
StyloExtract a *different fetch* against the same origin (AMP variant,
text-mirror URL) or parses an embedded payload (SSR JSON, metadata) and
hands the resulting text to StyloExtract for shaping. The chain is about
finding *better input* for our converter, not switching to a different
one. Falling back to Jina/Trafilatura/Outline/Pocket/etc. is explicitly
not on the table: (a) those services see every URL the user visits,
which defeats the privacy framing, and (b) StyloExtract beats them at
the conversion job anyway. If the answer to "this page didn't convert
well" is ever "try a different converter", that's a StyloExtract
improvement waiting to happen, not a routing change here.

**4. Reader-friendly sites menu (future).** A curated short-list of sites
that are markdown-natively friendly, pinned as quick-access bookmarks:

- `text.npr.org` — NPR text-only mirror
- `lite.cnn.com` — CNN text-only
- `68k.news` — Hacker News-style aggregator that links to reader-mode
  versions of articles
- `apnews.com/hub/ap-top-news` — AP wire feed, mostly static
- `news.ycombinator.com` — already pretty plain
- Wikipedia (works great today)
- BBC `/news/articles/<id>` URLs — when they're not Next.js

Source list: <https://greycoder.com/a-list-of-text-only-new-sites/>.

The menu could surface under "Reader-Friendly Sites" in the side panel,
or as a dropdown next to the address bar.

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

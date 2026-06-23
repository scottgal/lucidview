# StyloExtract → Markdown: gaps and proposed extensions

**Audience:** StyloExtract maintainer.
**Source:** lucidVIEW reader use case, observed against Wikipedia and similar
real-world articles.
**StyloExtract version under review:** `Mostlylucid.StyloExtract.*` 1.6.1.

## TL;DR

The current `TypedMarkdownRenderer` + `BlockRoleRenderers` produce **prose-only
markdown** — every anchor, image, list item, emphasis span, code span, and
heading level is collapsed to plain text. For a markdown viewer like lucidVIEW
this looks like a wall of paragraphs with one occasional `# Heading`. To make
the output usable as **reader-grade markdown**, the abstractions need to carry
inline structure and the renderer needs to emit it.

## Concrete observations

### 1. All inline HTML collapses to plain text

`HeuristicBlockClassifier`, `ExtractorApplicator`, and `RepeatedItemDetector`
all populate `ExtractedBlock.Text` from `element.TextContent.Trim()`. That
strips:

- Anchor tags (`<a href>` → bare text, the href is dropped from the block body)
- Images (`<img>` → no text, no record at all on the block)
- Emphasis (`<em>`, `<strong>`, `<b>`, `<i>` → indistinguishable)
- Inline code (`<code>` → bare text)
- Line breaks (`<br>`)
- Sub/sup, mark, kbd, etc.

The block's `Links` field captures `<a>` hrefs but as a **flat array** with no
positional information. The renderer uses `Links` only for `PrimaryNavigation`,
`SecondaryNavigation`, and `Breadcrumb` — never for content blocks.

### 2. Heading levels are erased

The classifier emits `BlockRole.Heading` for any heading-like element but does
not record whether it was `h1`, `h2`, `h3`, etc. `BlockRoleRenderers` always
emits `"# " + text`. A multi-level Wikipedia article gets ten H1s and no
sub-structure.

### 3. No image story

`ExtractedBlock` has no `Images` field. `BlockRole.Image` does not exist. The
segmenter drops `<img>` elements entirely. Even feature articles with hero +
inline images render as captionless prose.

### 4. Lists, tables, code blocks are flattened

- **Lists:** `<ul>`/`<ol>`/`<li>` flatten into one run of `TextContent` —
  bullets and ordering lost.
- **Code blocks:** `BlockRole.CodeBlock` renders `block.Text` directly with no
  triple-backtick fencing or language hint.
- **Tables:** `BlockRole.Table` renders `block.Text` directly with no GFM
  pipes/dashes.

### 5. `ExtractedBlock.Markdown` field is dead

The contract has `required string Markdown { get; init; }` but no producer in
the Heuristics package populates it — every code path passes `""` or omits it
via `Markdown = ""` (e.g. `LayoutExtractor.cs` JSON-LD fallback). The
`BlockRoleRenderers` ignores it. Either lift it out of the contract or wire it
through.

## Proposed extensions

The minimum set to make output reader-grade. Designed to be **additive** —
existing consumers using `.Text` keep working unchanged.

### A. Inline runs on `ExtractedBlock`

Replace flat `Text` with a structured token sequence while keeping `Text`
as the plain-text projection for backwards compat:

```csharp
public sealed record ExtractedBlock {
    // existing fields …
    public IReadOnlyList<InlineRun> Inline { get; init; } = [];
}

public abstract record InlineRun;
public sealed record TextRun(string Text)                                 : InlineRun;
public sealed record EmphasisRun(IReadOnlyList<InlineRun> Children)        : InlineRun;
public sealed record StrongRun(IReadOnlyList<InlineRun> Children)          : InlineRun;
public sealed record CodeRun(string Text)                                 : InlineRun;
public sealed record LinkRun(string Href, IReadOnlyList<InlineRun> Children): InlineRun;
public sealed record ImageRun(string Src, string? Alt, string? Title)      : InlineRun;
public sealed record LineBreakRun                                          : InlineRun;
```

Renderer emits these as `**bold**`, `*em*`, `` `code` ``, `[text](href)`,
`![alt](src)`. Producers walk anchors/em/strong/code/img/br during
classification instead of `TextContent.Trim()`.

`Text` continues to exist; project it from `Inline` via `InlineRun.PlainText()`
so it stays the verbatim text view.

### B. Heading level on the block

```csharp
public sealed record ExtractedBlock {
    // existing fields …
    public int HeadingLevel { get; init; } // 0 = not a heading, 1–6 = h1..h6
}
```

`BlockRoleRenderers` emits `new string('#', Math.Clamp(b.HeadingLevel, 1, 6))`.

### C. Block-level list / table / code structure

Add list and table roles with proper item structure:

```csharp
public enum BlockRole { …existing…, List, ListItem }

public sealed record ExtractedBlock {
    // existing fields …
    public bool Ordered { get; init; }                  // for List role
    public string? CodeLanguage { get; init; }          // for CodeBlock role
    public IReadOnlyList<IReadOnlyList<InlineRun>>? TableHeader { get; init; }
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<InlineRun>>>? TableRows { get; init; }
}
```

Renderers:

- `List` → `string.Join("\n", items.Select(i => $"- {render(i.Inline)}"))`
  (or `1. ` numbered when `Ordered`).
- `CodeBlock` → ```` ``` ```` + language + body + ```` ``` ````.
- `Table` → GFM pipes / dashes from `TableHeader` + `TableRows`.

### D. Block-level Images (orphan / figure)

For images that aren't inline within prose (hero image, `<figure>`,
standalone illustrations):

```csharp
public enum BlockRole { …existing…, Image, Figure }

public sealed record ExtractedBlock {
    // existing fields …
    public string? ImageSrc { get; init; }
    public string? ImageAlt { get; init; }
    public string? ImageCaption { get; init; }
}
```

Renderer emits `![alt](src)` plus a caption paragraph when present.

### E. Make `ExtractedBlock.Markdown` the renderer's input, not output

The `Markdown` field on the contract is misleading today (always empty).
Either:

1. Populate it from producers as a pre-rendered alternative the consumer can
   prefer; or
2. Remove it from the contract and version-bump abstractions.

Option 1 unblocks `TypedMarkdownRenderer` falling back to `block.Markdown` when
non-empty, which would let advanced producers (e.g. a future
`MarkdownitTreeProducer` that walks the DOM with markdown-it-style rules)
emit ready-made markdown without needing the inline-run model. Option 2 is
cleaner — pick one and ship.

## Backwards compatibility

- New fields are optional with sensible defaults; existing consumers keep
  reading `.Text` and `.Links` unchanged.
- `TypedMarkdownRenderer` detects whether `Inline.Count > 0` and prefers the
  structured path; falls back to the current `MarkdownEscaper.Escape(block.Text)`
  when not.
- New roles (`List`, `ListItem`, `Image`, `Figure`) are filtered through the
  existing `ShouldEmit` profile gate — add them to `RagFull` and
  `MainContentOnly` allowlists.
- `HeadingLevel = 0` falls through to current `# text` behaviour.

## Test cases (paste-in fixtures)

1. **Wikipedia article**: `https://en.wikipedia.org/wiki/Markdown`
   - Expected: H1 lead → H2 sections → H3 subsections, with inline `[term](wiki-link)`
     in prose, an infobox table on the right, and a hero image.
   - Current output: ~6 H1s and a wall of plain text. No links. No images. No tables.

2. **GitHub README**: `https://github.com/scottgal/lucidview/blob/main/README.md`
   - Expected: shields preserved as `![alt](url)`, fenced code blocks for the
     install snippets, lists for shortcuts, GFM table for the feature matrix.
   - Current output: shield URLs gone, code blocks unfenced (the inner shell
     turns into one wrapped paragraph), feature table collapses to comma-
     separated text.

3. **Hacker News story page**: `https://news.ycombinator.com/item?id=…`
   - Expected: `RepeatedItem` per comment with the commenter link as
     `[username](profile-url)` inline.
   - Current output: each comment is one big text run, author handle stuck on
     the front with no link.

4. **Documentation site (Microsoft Learn)**: e.g. `https://learn.microsoft.com/en-us/dotnet/…`
   - Expected: sectioned H2/H3, code blocks fenced with `csharp`, inline
     `<code>` for API names, image badges for "Applies to" notes.
   - Current output: heading hierarchy flat, code blocks unfenced, API names
     indistinguishable from prose.

## Out of scope (do not address in this issue)

- Footnotes, definition lists, math, MathML, mermaid blocks — niche, can wait.
- Smart-quote / em-dash normalisation — orthogonal to structure.
- The empty-page case (e.g. `example.com`) — that's a classifier-coverage
  problem, separate.

## Priority order

If you can only do one at a time:

1. **Heading levels** (B) — biggest readability win for one field.
2. **Inline runs** (A) — unlocks links, emphasis, code spans, inline images.
3. **List + code + table block structure** (C) — long tail but high value.
4. **Block-level images** (D) — hero/figure support.
5. **Markdown-field decision** (E) — housekeeping.

## Notes from the lucidVIEW side

- lucidVIEW today uses `ExtractionProfile.RagFull`. If you ship a new
  `ExtractionProfile.ReaderGrade` that turns on the structured paths, lucidVIEW
  will switch to it.
- We feed StyloExtract whole `text/html` responses, no pre-cleaning. Images
  and links inside `<main>`/`<article>` are typical; we don't need MainContent
  rewriting, only structure preservation inside it.
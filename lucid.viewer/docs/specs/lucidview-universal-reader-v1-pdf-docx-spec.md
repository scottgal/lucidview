# lucidVIEW Universal Reader v1 Spec (PDF + DOCX)

**Status:** Draft for implementation  
**Owner:** lucidVIEW engineering  
**Last updated:** February 25, 2026  
**Scope:** Desktop Avalonia app (`net9.0`) in this repository

## 1. Overview

This spec defines v1 of a universal reader for lucidVIEW, with initial support for:
1. PDF
2. DOCX

The goal is a single reading shell with consistent navigation, search, and document state, while allowing format-specific rendering engines behind a shared adapter interface.

## 2. Goals and Non-Goals

### 2.1 Goals

1. Open local PDF and DOCX files.
2. Provide page/location navigation with keyboard and mouse support.
3. Provide zoom controls and fit modes.
4. Provide text search within the active document.
5. Keep UI and state model format-agnostic so EPUB/MOBI can be added later.
6. Handle untrusted files defensively (limits, validation, no external fetches).

### 2.2 Non-Goals (v1)

1. Editing documents.
2. PDF form filling.
3. Annotations/highlighting persistence.
4. DRM-protected content.
5. Pixel-perfect DOCX print-layout fidelity unless commercial engine is approved.

## 3. User Experience Requirements

### 3.1 Primary Flows

1. User opens a file (`.pdf` or `.docx`) from file picker or drag-drop.
2. Reader shell loads document and displays first location.
3. User navigates using:
   - next/previous page
   - page/location jump
   - scroll wheel/touchpad
   - keyboard shortcuts
4. User searches text and moves through matches.
5. User closes document; last position is saved for reopen.

### 3.2 Reader Shell (Common)

Required UI elements:
1. Top toolbar:
   - Open
   - Back/Forward history (optional v1.1)
   - Zoom out / zoom in / reset
   - Fit width / fit page
   - Search box + next/prev match
2. Left panel (collapsible):
   - Thumbnails for PDF
   - TOC/headings when available
3. Main viewport:
   - rendered page/content
4. Bottom status bar:
   - page/location indicator
   - total pages/locations
   - loading/progress messages

### 3.3 Keyboard Shortcuts

1. `Ctrl+O`: Open file
2. `PageDown` / `Space`: Next page/location
3. `PageUp` / `Shift+Space`: Previous page/location
4. `Ctrl+F`: Focus search
5. `Enter` in search: Next match
6. `Shift+Enter`: Previous match
7. `Ctrl++` / `Ctrl+-` / `Ctrl+0`: Zoom in/out/reset
8. `Ctrl+L`: Jump to page/location

## 4. Format Behavior

### 4.1 PDF (Fixed Layout)

PDF is page-native.

Required behavior:
1. Report exact `PageCount`.
2. Render per-page bitmap tiles at requested zoom.
3. Support single-page and continuous-page modes.
4. Jump to exact page number.
5. Text search across full document with match navigation.
6. Optional text selection overlay (v1.1 if extraction quality is stable).

### 4.2 DOCX (Reflow First, Paged UI)

DOCX is not page-native without a full layout engine.  
v1 will support a reading-oriented model:

1. Convert DOCX to sanitized HTML/content blocks.
2. Render in reflow mode in the main viewer.
3. Expose virtual pagination for UX consistency:
   - location units generated from viewport slices
   - page count updates when viewport width/font scale changes
4. Search against extracted text with hit navigation.
5. TOC/headings from DOCX styles where available.

Note on fidelity:
1. v1 prioritizes readable content over print-layout parity.
2. If print-layout parity is required, adopt a commercial DOCX layout engine in v1.2+.

## 5. Functional Requirements

### 5.1 File Open and Detection

1. Supported extensions in v1:
   - `.pdf`
   - `.docx`
2. Detect format by both extension and file signature.
3. Reject unsupported/corrupt files with actionable error messages.

### 5.2 Navigation

1. Next/previous navigation must complete in under 100 ms when page is cached.
2. Jump-to-page/location:
   - PDF: absolute page number
   - DOCX: virtual page/location index
3. Scroll sync updates status bar indicator.

### 5.3 Search

1. Case-insensitive search by default.
2. Show total match count and current match index.
3. Navigate next/previous matches.
4. Keep search state while document is open.

### 5.4 State Persistence

Persist per-document:
1. last location
2. zoom setting
3. view mode (single vs continuous, fit mode)
4. last search query (optional)

Storage:
1. Local app data JSON store keyed by stable file identity (`path + size + modifiedUtc` hash).

### 5.5 Error Handling

1. Fail gracefully on malformed files.
2. Never crash UI thread from parser/renderer exceptions.
3. Show non-blocking error panel with retry/open-another actions.

## 6. Non-Functional Requirements

### 6.1 Performance Targets

PDF:
1. First page visible in <= 800 ms for 50 MB test PDF on baseline machine.
2. Next/prev cached page display in <= 100 ms.
3. Memory cap target: <= 600 MB working set on 500-page PDF (with bounded cache).

DOCX:
1. First readable content in <= 1.2 s for 10 MB DOCX.
2. Re-pagination after resize in <= 300 ms for typical docs (< 300 pages equivalent).

### 6.2 Reliability

1. No unhandled exceptions for corpus test set.
2. Renderer timeouts for pathological files.
3. Cancellation support when opening another document mid-load.

### 6.3 Security

1. Disable all external resource fetches from document content.
2. Sanitize DOCX->HTML output:
   - strip script/event attributes
   - allowlist tags/attributes
3. Apply file size/page count limits with override setting:
   - default max PDF pages: 5,000
   - default max file size: 250 MB
4. Never execute embedded macros or active content.
5. Log sanitized diagnostics only (no document content dumps).

### 6.4 Accessibility

1. All toolbar actions keyboard reachable.
2. High contrast compatible controls.
3. Screen-reader labels for primary controls and status indicators.

## 7. Architecture

### 7.1 High-Level Components

1. `ReaderShellView` / `ReaderShellViewModel`
2. `IDocumentAdapterFactory`
3. `IDocumentAdapter` (per format)
4. `PageCacheService`
5. `SearchService`
6. `DocumentStateStore`
7. `SecurityPolicy` (limits + sanitization configuration)

### 7.2 Core Interfaces (Proposed)

```csharp
public interface IDocumentAdapter : IAsyncDisposable
{
    string Format { get; }                     // "pdf", "docx"
    DocumentCapabilities Capabilities { get; }
    ValueTask<DocumentMetadata> LoadAsync(string path, CancellationToken ct);
    ValueTask<int> GetLocationCountAsync(CancellationToken ct);
    ValueTask<PageRenderResult> RenderLocationAsync(int locationIndex, RenderOptions options, CancellationToken ct);
    ValueTask<SearchResultsPage> SearchAsync(string query, SearchCursor? cursor, CancellationToken ct);
    ValueTask<OutlineNode[]> GetOutlineAsync(CancellationToken ct);
    ValueTask<string?> GetAccessibleTextAsync(int locationIndex, CancellationToken ct);
}
```

```csharp
public interface IDocumentAdapterFactory
{
    bool CanOpen(string path, Stream headerStream);
    IDocumentAdapter Create(string path);
}
```

### 7.3 Data Contracts (Proposed)

```csharp
public sealed record DocumentMetadata(
    string Title,
    string Author,
    int? NativePageCount,
    bool IsVirtualPagination,
    string Fingerprint);

public sealed record RenderOptions(
    double Zoom,
    FitMode FitMode,
    int ViewportWidth,
    int ViewportHeight,
    ThemeMode ThemeMode);

public sealed record PageRenderResult(
    int LocationIndex,
    int TotalLocations,
    IImage Bitmap,
    Rect? FocusRect);
```

### 7.4 Rendering Pipeline

PDF:
1. Parse metadata/page tree.
2. Render requested page at target DPI from zoom.
3. Store bitmap in LRU cache keyed by `(docFingerprint, page, zoomBucket, theme)`.
4. Return image to viewport.

DOCX:
1. Parse DOCX package.
2. Convert to safe intermediate HTML/content model.
3. Build virtual location map from viewport metrics.
4. Render visible slice to viewer surface.
5. Recompute location map on resize/font scale changes (debounced).

## 8. Library Strategy

### 8.1 PDF Libraries

Selection criteria:
1. Cross-platform desktop support.
2. Active maintenance.
3. Reliable rendering quality.
4. Text extraction support for search.
5. Clear license for commercial distribution.

Plan:
1. Run a spike comparing two candidates and lock one.
2. Keep adapter boundary so engine swap is low-risk.

### 8.2 DOCX Libraries

v1 default path:
1. OpenXML parser + DOCX->HTML conversion pipeline.
2. Sanitizer and style mapper for readable output.

Optional path for print-fidelity mode:
1. Commercial renderer integration behind same adapter.
2. Feature flag: `DocxPrintLayoutEnabled`.

## 9. Pagination Model

### 9.1 Unified Location Abstraction

Expose a format-agnostic `locationIndex` in UI and state.

1. PDF: `locationIndex == pageIndex` (native pages).
2. DOCX: `locationIndex == virtualPageIndex` (viewport-derived slices).

### 9.2 Virtual Pagination Rules (DOCX)

1. Stable for fixed viewport + zoom.
2. Recomputed when:
   - window width changes beyond threshold
   - zoom/font scale changes
3. Preserve reading position by content anchor (nearest heading/paragraph ID).

## 10. Search Model

### 10.1 PDF

1. Use text extraction layer for indexed page text.
2. Return hit list with `locationIndex` + bounding info when available.

### 10.2 DOCX

1. Search in normalized text stream from content model.
2. Map match offsets to virtual locations and anchors.

## 11. Project Structure Changes

Add:
1. `lucid.viewer/Services/Reader/`
2. `lucid.viewer/Services/Reader/Adapters/Pdf/`
3. `lucid.viewer/Services/Reader/Adapters/Docx/`
4. `lucid.viewer/Models/Reader/`
5. `lucid.viewer/Views/ReaderShell.axaml`
6. `lucid.viewer/ViewModels/ReaderShellViewModel.cs`

Keep current `MainWindow` as host shell and route to `ReaderShell`.

## 12. Telemetry and Diagnostics

Track locally (opt-in if telemetry added later):
1. open time
2. first render time
3. render failures by format
4. search latency
5. cache hit ratio

Logs must include:
1. file fingerprint
2. format
3. operation phase
4. exception type/message (sanitized)

## 13. Test Plan

### 13.1 Unit Tests

1. Adapter selection by file signature/extension.
2. Location model conversions.
3. State persistence load/save.
4. Security sanitization for DOCX HTML.

### 13.2 Integration Tests

1. Open sample PDF corpus (small/large/scanned/text-heavy).
2. Open sample DOCX corpus (headings/tables/images/long docs).
3. Search navigation accuracy.
4. Resize-triggered DOCX repagination.

### 13.3 Manual Smoke Checklist

1. Open file and render first location.
2. Navigate 20+ next/prev operations.
3. Jump to specific location.
4. Run search and cycle matches.
5. Close/reopen and verify resume location.
6. Verify memory does not grow unbounded after 200 page flips.

## 14. Acceptance Criteria

1. User can open both PDF and DOCX from UI and drag-drop.
2. User can navigate with toolbar, keyboard, and scroll.
3. Search works in both formats and navigates all matches.
4. Resume position works across app restarts.
5. No crashes across baseline corpus.
6. Performance targets in section 6 are met on baseline machine.

## 15. Delivery Plan

### Milestone 1: Reader Shell + PDF MVP

1. Build `ReaderShellView` and shared navigation state.
2. Implement `IDocumentAdapter` + PDF adapter.
3. Add PDF pagination, zoom, search, and resume state.
4. Validate against PDF corpus and performance targets.

### Milestone 2: DOCX Reflow MVP

1. Implement DOCX adapter with sanitized content model.
2. Add virtual pagination and anchor-preserving repagination.
3. Add TOC/headings and search mapping.
4. Validate against DOCX corpus and resize scenarios.

### Milestone 3: Hardening

1. Security limit enforcement and timeout behavior.
2. Cache tuning and memory guardrails.
3. Accessibility pass and keyboard audit.
4. Error UX polish.

## 16. Risks and Mitigations

1. **DOCX page fidelity mismatch vs Word**
   - Mitigation: set explicit expectation for reading mode in v1; evaluate commercial print-layout engine separately.
2. **PDF engine licensing constraints**
   - Mitigation: spike and legal/license review before lock-in.
3. **Large document memory pressure**
   - Mitigation: bounded LRU cache, zoom bucketing, proactive bitmap disposal.
4. **Search quality variance by engine**
   - Mitigation: abstraction keeps extraction replaceable.
5. **Virtual pagination instability**
   - Mitigation: anchor-based restore plus debounced recompute rules.

## 17. Open Decisions

1. Final PDF engine package choice after spike.
2. DOCX v1 fidelity target sign-off (reflow vs print-layout).
3. Whether to include text selection in v1 or defer to v1.1.
4. Corpus definition and baseline machine spec for perf gating.

## 18. Out of Scope Formats (Planned Next)

After v1 ships:
1. EPUB adapter (same reflow stack as DOCX, format-specific parser).
2. MOBI strategy (direct parser or conversion pipeline).
3. RTF/ODT via additional adapters.


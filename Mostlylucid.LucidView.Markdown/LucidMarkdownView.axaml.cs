using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;
using Mostlylucid.LucidView.Markdown.Plugins;
using Mostlylucid.LucidView.Markdown.Services;

namespace Mostlylucid.LucidView.Markdown;

/// <summary>
/// A self-contained Avalonia UserControl that renders Markdown (including Mermaid diagrams)
/// using the LiveMarkdown.Avalonia renderer and Naiad diagram pipeline.
/// </summary>
public partial class LucidMarkdownView : UserControl
{
    // ── Styled Properties ───────────────────────────────────────────────────

    /// <summary>Defines the <see cref="Markdown"/> property.</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<LucidMarkdownView, string?>(nameof(Markdown));

    /// <summary>Defines the <see cref="SourcePath"/> property.</summary>
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<LucidMarkdownView, string?>(nameof(SourcePath));

    // ── CLR wrappers ────────────────────────────────────────────────────────

    /// <summary>The Markdown text to render. Changing this triggers a re-render.</summary>
    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>
    /// Base path used to resolve relative image paths inside the Markdown.
    /// Defaults to <see cref="Path.GetTempPath()"/> when not set.
    /// </summary>
    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    // ── Events ──────────────────────────────────────────────────────────────

    /// <summary>Raised when a link inside the rendered Markdown is clicked.</summary>
    public event EventHandler<LinkClickedEventArgs>? LinkClick;

    /// <summary>
    /// Raised when a C4 element in a rendered C4 diagram is clicked, with the element's id — so a host
    /// can treat the architecture diagram as a navigation surface (e.g. focus the owning agent).
    /// </summary>
    public event Action<string>? C4ElementClicked;

    // ── Internals ───────────────────────────────────────────────────────────

    private readonly MarkdownService _markdownService;
    private readonly ImageCacheService _imageCacheService;
    private readonly DiagramRendererPluginHost _pluginHost;
    private int _renderGeneration;

    // Marker replacement watch. See ScheduleDiagramMarkerReplacement.
    private bool _markerWatchRunning;
    private int _markerWatchGeneration;
    private int _markerWatchPasses;
    private int _markerWatchReplaced;
    private int _markerWatchExpected;

    /// <summary>
    /// How many layout passes a marker gets to appear in before the watch gives up. Generous
    /// because it costs one tree walk per pass and only runs while a document still has an
    /// unreplaced diagram in it, and because a host that measures this view inside a scrolling,
    /// centred column takes several passes to settle before LiveMarkdown's text is even built.
    /// </summary>
    private const int MaxMarkerWatchPasses = 60;

    // ── Constructor ─────────────────────────────────────────────────────────

    public LucidMarkdownView()
    {
        InitializeComponent();

        _markdownService = new MarkdownService();
        _imageCacheService = new ImageCacheService();
        _markdownService.SetImageCacheService(_imageCacheService);

        _pluginHost = new DiagramRendererPluginHost([
            new AvaloniaNativeDiagramRendererPlugin(
                _markdownService,
                ResolveDiagramTextBrush,
                SaveDiagramAsNoOp,
                c4ElementClicked: id => C4ElementClicked?.Invoke(id))
        ]);

        MdViewer.LinkClick += (s, e) => LinkClick?.Invoke(s, e);
    }

    // ── Avalonia lifecycle ──────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MdViewer.ImageBasePath = SourcePath ?? Path.GetTempPath();
        _markdownService.SetBasePath(SourcePath);

        // Re-render when attached so late bindings (e.g. DataContext) are reflected.
        if (!string.IsNullOrEmpty(Markdown))
            Dispatcher.UIThread.Post(() => _ = RenderAsync(Markdown), DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Stop rendering when we leave the tree. Cancel any in-flight diagram batch and clear the
        // streaming builder so no async render/layout work outlives the control. Besides being the
        // correct lifetime behaviour for a closed document, this is essential under a *shared*
        // headless test session: a view that keeps re-driving layout after its window closes leaves
        // never-settling work on the single UI thread and eventually wedges a later test.
        StopDiagramMarkerWatch();
        _markdownService.CancelRenderBatch();
        MdViewer.MarkdownBuilder = new ObservableStringBuilder();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty)
            _ = RenderAsync(change.GetNewValue<string?>());

        if (change.Property == SourcePathProperty)
        {
            var sourcePath = change.GetNewValue<string?>();
            MdViewer.ImageBasePath = sourcePath ?? Path.GetTempPath();
            _markdownService.SetBasePath(sourcePath);

            // XAML bindings can set Markdown before SourcePath. Reprocess in
            // that order so relative local images are not permanently parsed
            // against the temporary directory.
            if (Markdown is not null)
                _ = RenderAsync(Markdown);
        }
    }

    // ── Render pipeline ─────────────────────────────────────────────────────

    private async Task RenderAsync(string? markdown)
    {
        var renderGeneration = Interlocked.Increment(ref _renderGeneration);
        // Start a new batch even for plain Markdown. A previous document may
        // still have a PNG fallback rendering in the background.
        var ct = _markdownService.BeginNewRenderBatch();

        if (markdown is null)
        {
            StopDiagramMarkerWatch();
            MdViewer.MarkdownBuilder = new ObservableStringBuilder();
            return;
        }

        var (processed, pendingDiagrams) = _markdownService.ProcessMarkdownFast(markdown);

        var builder = new ObservableStringBuilder();
        builder.Append(processed);
        MdViewer.MarkdownBuilder = builder;

        ScheduleDiagramMarkerReplacement(renderGeneration);

        if (pendingDiagrams.Count == 0) return;

        try
        {
            var replacements = await _markdownService.RenderPendingDiagramsAsync(pendingDiagrams, ct);

            if (renderGeneration != Volatile.Read(ref _renderGeneration))
                return;

            var updated = processed;
            foreach (var (placeholder, replacement) in replacements)
                updated = updated.Replace(placeholder, replacement);

            var diagramBuilder = new ObservableStringBuilder();
            diagramBuilder.Append(updated);
            MdViewer.MarkdownBuilder = diagramBuilder;

            ScheduleDiagramMarkerReplacement(renderGeneration);
        }
        catch (OperationCanceledException)
        {
            // A new render batch was started (e.g. Markdown changed again) — this is expected.
        }
    }

    // ── Diagram marker replacement ──────────────────────────────────────────

    /// <summary>
    /// Keeps swapping diagram markers for their drawn controls until the document's diagrams are
    /// all in place, or a bounded number of layout passes has gone by.
    ///
    /// The pipeline turns a mermaid fence into a marker (FLOWCHART:key and friends), hands the text
    /// to LiveMarkdown, and then walks the visual tree looking for the Run that marker was laid out
    /// as. The walk can only find that Run once LiveMarkdown has built it, which is at least one
    /// layout pass after the builder is swapped, and in a host that measures this view inside a
    /// ScrollViewer with a centred, explicitly sized column, later than that: the pane's own layout
    /// has to settle first. A single deferred pass posted at Loaded priority therefore ran, found
    /// nothing and stopped, and since a flowchart is laid out synchronously it has no later async
    /// step to trigger another pass. The article showed the literal text FLOWCHART:flowchart-0.
    ///
    /// So the pass is driven off LayoutUpdated rather than a fixed delay: it runs exactly when the
    /// tree changes shape, which is precisely when a marker can first become findable, and stops as
    /// soon as every diagram the document declares has been replaced. The pass count is a backstop
    /// for a document whose marker never appears (a key with no marker text left in the source),
    /// so the handler cannot stay subscribed for the life of the window.
    /// </summary>
    private void ScheduleDiagramMarkerReplacement(int renderGeneration)
    {
        _markerWatchGeneration = renderGeneration;
        _markerWatchPasses = 0;
        _markerWatchReplaced = 0;
        _markerWatchExpected =
            _markdownService.FlowchartLayouts.Count +
            _markdownService.DiagramDocuments.Count +
            _markdownService.C4Layouts.Count;

        // A re-render of an already laid out document can succeed straight away.
        _markerWatchReplaced += _pluginHost.ReplaceDiagramMarkers(MdViewer);

        if (_markerWatchExpected == 0 || _markerWatchReplaced >= _markerWatchExpected)
        {
            StopDiagramMarkerWatch();
            return;
        }

        if (_markerWatchRunning) return;
        _markerWatchRunning = true;
        MdViewer.LayoutUpdated += OnDiagramMarkerLayoutUpdated;
    }

    private void OnDiagramMarkerLayoutUpdated(object? sender, EventArgs e)
    {
        // A newer document is rendering; its own schedule owns the watch from here.
        if (_markerWatchGeneration != Volatile.Read(ref _renderGeneration))
        {
            StopDiagramMarkerWatch();
            return;
        }

        _markerWatchPasses++;
        _markerWatchReplaced += _pluginHost.ReplaceDiagramMarkers(MdViewer);

        if (_markerWatchReplaced >= _markerWatchExpected || _markerWatchPasses >= MaxMarkerWatchPasses)
            StopDiagramMarkerWatch();
    }

    private void StopDiagramMarkerWatch()
    {
        if (!_markerWatchRunning) return;
        _markerWatchRunning = false;
        MdViewer.LayoutUpdated -= OnDiagramMarkerLayoutUpdated;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private IBrush? ResolveDiagramTextBrush()
    {
        // Use the control's current foreground, falling back to white for dark themes.
        if (Foreground is { } fg) return fg;
        return Brushes.White;
    }

    private static Task SaveDiagramAsNoOp(string mermaidSource, string format) => Task.CompletedTask;
}

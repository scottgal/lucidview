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
        _markdownService.CancelRenderBatch();
        MdViewer.MarkdownBuilder = new ObservableStringBuilder();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty)
            _ = RenderAsync(change.GetNewValue<string?>());

        if (change.Property == SourcePathProperty)
            MdViewer.ImageBasePath = change.GetNewValue<string?>() ?? Path.GetTempPath();
    }

    // ── Render pipeline ─────────────────────────────────────────────────────

    private async Task RenderAsync(string? markdown)
    {
        if (markdown is null)
        {
            MdViewer.MarkdownBuilder = new ObservableStringBuilder();
            return;
        }

        var (processed, pendingDiagrams) = _markdownService.ProcessMarkdownFast(markdown);

        var builder = new ObservableStringBuilder();
        builder.Append(processed);
        MdViewer.MarkdownBuilder = builder;

        // First pass runs synchronously, but on a fresh render the builder update hasn't been laid out into
        // the visual tree yet, so this walk finds no marker Runs. That's fine for async diagrams (their
        // await + second pass below catches them after layout) but flowcharts have no async step — without a
        // deferred pass their FLOWCHART: marker never gets replaced (it renders as raw text). So always defer
        // one more pass after a layout tick; ReplaceDiagramMarkers is idempotent (a replaced marker is gone).
        _pluginHost.ReplaceDiagramMarkers(MdViewer);
        Dispatcher.UIThread.Post(() => _pluginHost.ReplaceDiagramMarkers(MdViewer), DispatcherPriority.Loaded);

        if (pendingDiagrams.Count == 0) return;

        var ct = _markdownService.BeginNewRenderBatch();
        try
        {
            var replacements = await _markdownService.RenderPendingDiagramsAsync(pendingDiagrams, ct);
            var updated = processed;
            foreach (var (placeholder, replacement) in replacements)
                updated = updated.Replace(placeholder, replacement);

            var diagramBuilder = new ObservableStringBuilder();
            diagramBuilder.Append(updated);
            MdViewer.MarkdownBuilder = diagramBuilder;

            _pluginHost.ReplaceDiagramMarkers(MdViewer);
        }
        catch (OperationCanceledException)
        {
            // A new render batch was started (e.g. Markdown changed again) — this is expected.
        }
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

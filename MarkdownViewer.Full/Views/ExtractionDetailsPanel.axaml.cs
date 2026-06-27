using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

public partial class ExtractionDetailsPanel : Window
{
    private readonly ExtractionTelemetry _telemetry = FullServices.Get<ExtractionTelemetry>();
    // AvaloniaXamlLoader.Load(this) doesn't auto-wire named-element fields the
    // way the source-generated InitializeComponent() does — look them up
    // explicitly via FindControl in the ctor.
    private TextBlock? _lastDetail;
    private DataGrid? _historyGrid;

    public ExtractionDetailsPanel()
    {
        AvaloniaXamlLoader.Load(this);
        _lastDetail = this.FindControl<TextBlock>("LastDetail");
        _historyGrid = this.FindControl<DataGrid>("HistoryGrid");
        Populate();
    }

    private void Populate()
    {
        if (_lastDetail is not null)
        {
            _lastDetail.Text = _telemetry.Last is { } info
                ? $"""
                    Source: {info.Source}
                    Match: {info.MatchStatus} (template {info.TemplateId} v{info.TemplateVersion})
                    Fetcher: {info.Fetcher} · {info.TotalDuration.TotalMilliseconds:F0} ms
                    LLM induction: {info.LlmInductionFired} ({info.LlmDuration.TotalMilliseconds:F0} ms)
                    Blocks: {info.BlockCount} · Output: {info.OutputCharacterCount} chars
                    """
                : "(no extractions yet)";
        }
        if (_historyGrid is not null)
            _historyGrid.ItemsSource = _telemetry.Recent.Reverse().ToList();
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export extraction telemetry",
            DefaultExtension = "ndjson",
            SuggestedFileName = $"extractions-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ndjson",
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_telemetry.ExportNdjson());
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        _telemetry.Clear();
        Populate();
    }
}

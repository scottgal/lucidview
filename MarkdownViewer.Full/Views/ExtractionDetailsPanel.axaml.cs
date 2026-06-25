using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

public partial class ExtractionDetailsPanel : Window
{
    private readonly ExtractionTelemetry _telemetry = FullServices.Get<ExtractionTelemetry>();

    public ExtractionDetailsPanel()
    {
        AvaloniaXamlLoader.Load(this);
        Populate();
    }

    private void Populate()
    {
        LastDetail.Text = _telemetry.Last is { } info
            ? $"""
                Source: {info.Source}
                Match: {info.MatchStatus} (template {info.TemplateId} v{info.TemplateVersion})
                Fetcher: {info.Fetcher} · {info.TotalDuration.TotalMilliseconds:F0} ms
                LLM induction: {info.LlmInductionFired} ({info.LlmDuration.TotalMilliseconds:F0} ms)
                Blocks: {info.BlockCount} · Output: {info.OutputCharacterCount} chars
                """
            : "(no extractions yet)";
        HistoryGrid.ItemsSource = _telemetry.Recent.Reverse().ToList();
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

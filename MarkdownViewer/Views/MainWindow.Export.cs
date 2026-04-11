using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

public partial class MainWindow
{
    private void OnExportPdf(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = ExportPdf();
    }

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = PrintToPrinter();
    }

    private async Task ExportPdf()
    {
        if (string.IsNullOrEmpty(_rawContent))
        {
            StatusText.Text = "No document to export";
            return;
        }
        if (_filePickerOpen) return;
        _filePickerOpen = true;

        try
        {
            var suggestedName = Path.GetFileNameWithoutExtension(_currentFilePath ?? "document");
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PDF",
                SuggestedFileName = $"{suggestedName}.pdf",
                DefaultExtension = "pdf",
                FileTypeChoices =
                [
                    new FilePickerFileType("PDF Document") { Patterns = ["*.pdf"] }
                ]
            });

            if (file is null)
            {
                StatusText.Text = "PDF export canceled";
                return;
            }

            var outputPath = file.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                StatusText.Text = "Unable to resolve output path";
                return;
            }

            StatusText.Text = "Exporting PDF...";

            var exportContext = CreatePdfExportContext();

            var pdfService = new PdfExportService(_markdownService);
            await pdfService.ExportAsync(
                _rawContent,
                outputPath,
                exportContext.Title,
                _fontSize,
                exportContext.BasePath,
                exportContext.AllowLocalFiles);

            StatusText.Text = $"PDF saved: {outputPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"PDF export error: {ex.Message}";
        }
        finally
        {
            _filePickerOpen = false;
        }
    }

    private async Task PrintToPrinter()
    {
        if (string.IsNullOrEmpty(_rawContent))
        {
            StatusText.Text = "No document to print";
            return;
        }

        string? tempPdf = null;
        try
        {
            StatusText.Text = "Generating PDF for printer...";

            var exportContext = CreatePdfExportContext();

            var pdfService = new PdfExportService(_markdownService);
            tempPdf = await pdfService.ExportToTempAsync(
                _rawContent,
                exportContext.Title,
                _fontSize,
                exportContext.BasePath,
                exportContext.AllowLocalFiles);

            StatusText.Text = "Sending to default printer...";
            await PrintService.PrintAsync(tempPdf);

            StatusText.Text = "Sent to default printer";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Print failed: {ex.Message}";
        }
        finally
        {
            if (tempPdf != null)
            {
                var pathToDelete = tempPdf;
                _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
                {
                    try { if (File.Exists(pathToDelete)) File.Delete(pathToDelete); } catch { }
                });
            }
        }
    }

    private (string? BasePath, string Title, bool AllowLocalFiles) CreatePdfExportContext()
    {
        var allowLocalFiles = !string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath);
        var basePath = allowLocalFiles ? Path.GetDirectoryName(_currentFilePath) : null;
        var title = Path.GetFileNameWithoutExtension(_currentFilePath ?? "Document");
        return (basePath, title, allowLocalFiles);
    }
}

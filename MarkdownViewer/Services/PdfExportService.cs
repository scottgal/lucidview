using System.Text.RegularExpressions;
using MarkdownViewer.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Markdown;

namespace MarkdownViewer.Services;

public partial class PdfExportService
{
    private readonly MarkdownService _markdownService;

    public PdfExportService(MarkdownService markdownService)
    {
        _markdownService = markdownService;
    }

    public async Task ExportAsync(
        string rawMarkdown,
        string outputPath,
        string? documentTitle,
        int fontSize,
        string? basePath,
        CancellationToken ct = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var (processedMarkdown, tempFiles) = await PreprocessMermaidAsync(rawMarkdown, ct);

        try
        {
            processedMarkdown = ResolveImagePaths(processedMarkdown, basePath);
            await BuildAndSavePdfAsync(processedMarkdown, outputPath, documentTitle, fontSize);
        }
        finally
        {
            foreach (var tempFile in tempFiles)
                TryDeleteFile(tempFile);
        }
    }

    private async Task<(string Markdown, List<string> TempFiles)> PreprocessMermaidAsync(
        string markdown, CancellationToken ct)
    {
        var tempFiles = new List<string>();
        var result = markdown;

        var blocks = MarkdownService.FindMermaidBlocks(markdown).ToList();
        if (blocks.Count == 0)
            return (result, tempFiles);

        // Render mermaid blocks in parallel
        var tasks = blocks.Select(block => Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var pngBytes = _markdownService.ExportMermaidToPngBytes(block.MermaidCode, scale: 2f);
                var tempPath = Path.Combine(Path.GetTempPath(), $"lucidview_mermaid_{Guid.NewGuid():N}.png");
                File.WriteAllBytes(tempPath, pngBytes);
                return (block.FullMatch, TempPath: tempPath, Success: true);
            }
            catch
            {
                return (block.FullMatch, TempPath: (string?)null, Success: false);
            }
        }, ct)).ToList();

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
        {
            if (r.Success && r.TempPath != null)
            {
                tempFiles.Add(r.TempPath);
                var replacement = $"![Diagram]({new Uri(r.TempPath).AbsoluteUri})";
                result = result.Replace(r.FullMatch, replacement);
            }
            else
            {
                var codeBlock = r.FullMatch.Replace("```mermaid", "```");
                result = result.Replace(r.FullMatch, codeBlock);
            }
        }

        return (result, tempFiles);
    }

    private static string ResolveImagePaths(string markdown, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath))
            return markdown;

        return ImagePathRegex().Replace(markdown, match =>
        {
            var alt = match.Groups[1].Value;
            var path = match.Groups[2].Value;

            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "file" || uri.Scheme == "data"))
                return match.Value;

            var resolved = Path.GetFullPath(Path.Combine(basePath, path));
            if (File.Exists(resolved))
                return $"![{alt}]({new Uri(resolved).AbsoluteUri})";

            return match.Value;
        });
    }

    private static async Task BuildAndSavePdfAsync(
        string processedMarkdown,
        string outputPath,
        string? documentTitle,
        int fontSize)
    {
        var theme = ThemeColors.Light;
        var title = documentTitle ?? "Document";

        // Parse markdown and download images (local file:// and remote http/https)
        var parsed = ParsedMarkdownDocument.FromText(processedMarkdown);
        await parsed.DownloadImages();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(12, Unit.Millimetre);
                page.MarginVertical(12, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(fontSize).FontColor(Colors.Black));

                page.Header()
                    .BorderBottom(1)
                    .BorderColor(theme.Accent)
                    .PaddingBottom(8)
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Text(title)
                            .FontSize(fontSize + 4)
                            .Bold()
                            .FontColor(Colors.Black);

                        row.ConstantItem(80)
                            .AlignRight()
                            .Text("lucidVIEW")
                            .FontSize(9)
                            .FontColor(theme.TextMuted);
                    });

                page.Content()
                    .PaddingVertical(10)
                    .Markdown(parsed, options =>
                    {
                        options.BlockQuoteBorderColor = theme.BlockquoteBorder;
                        options.CodeBlockBackground = theme.CodeBackground;
                        options.LinkTextColor = theme.Link;
                        options.HeadingTextColor = theme.Text;
                        options.CalculateHeadingSize = level => fontSize + 12 - 2 * (level - 1);
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            });
        });

        document.GeneratePdf(outputPath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex ImagePathRegex();
}

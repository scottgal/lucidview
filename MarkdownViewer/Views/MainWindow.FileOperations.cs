using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiveMarkdown.Avalonia;

namespace MarkdownViewer.Views;

public partial class MainWindow
{
    private const int MaxRemoteMarkdownBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly HttpClient RemoteMarkdownClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    // Re-entry guard. macOS sometimes fires Click + IActivatableLifetime in
    // the same flow which used to open two stacked file pickers (the second
    // underneath the first, unclickable). The guard makes the dialog one-at-
    // a-time regardless of which path triggered it.
    private bool _filePickerOpen;
    private int _openFileEnterCount;
    private int _openFileBlockedCount;

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenFile();
    }

    private void OnOpenUrl(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenUrl();
    }

    private void OnMostlyLucidClick(object? sender, RoutedEventArgs e)
    {
        OpenBrowserUrl("https://www.mostlylucid.net");
    }

    private void OnLinkClick(object? sender, LinkClickedEventArgs e)
    {
        var href = e.HRef;
        if (href == null) return;

        var url = href.ToString();
        if (string.IsNullOrWhiteSpace(url)) return;

        // Anchor links: scroll within document
        if (url.StartsWith('#'))
        {
            var anchorText = Uri.UnescapeDataString(url[1..]).Replace("-", " ");
            var heading = _headings
                .SelectMany(h => FlattenHeadings([h]))
                .FirstOrDefault(h => h.Text.Equals(anchorText, StringComparison.OrdinalIgnoreCase)
                    || h.Text.Replace(" ", "-").Equals(url[1..], StringComparison.OrdinalIgnoreCase));
            if (heading != null)
            {
                ScrollToHeading(heading);
            }
            e.Handled = true;
            return;
        }

        // HTTP(S) links: open in default browser
        if (href.IsAbsoluteUri && href.Scheme is "http" or "https")
        {
            OpenBrowserUrl(href.AbsoluteUri);
            e.Handled = true;
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var httpUri) && httpUri.Scheme is "http" or "https")
        {
            OpenBrowserUrl(httpUri.AbsoluteUri);
            e.Handled = true;
            return;
        }

        // Resolve relative paths against current file's directory
        var resolvedPath = TryResolveLocalPath(url);
        if (resolvedPath != null)
        {
            var ext = Path.GetExtension(resolvedPath).ToLowerInvariant();
            if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt")
            {
                _ = LoadFile(resolvedPath);
                e.Handled = true;
                return;
            }

            // Other local files: open with default app
            OpenBrowserUrl(resolvedPath);
            e.Handled = true;
            return;
        }

        // file:// URIs
        if (href.IsAbsoluteUri && href.Scheme == "file")
        {
            var localPath = href.LocalPath;
            var ext = Path.GetExtension(localPath).ToLowerInvariant();
            if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt")
            {
                _ = LoadFile(localPath);
                e.Handled = true;
                return;
            }

            if (File.Exists(localPath))
            {
                OpenBrowserUrl(localPath);
                e.Handled = true;
                return;
            }
        }

        StatusText.Text = "Blocked unsupported link target";
        e.Handled = true;
    }

    private string? TryResolveLocalPath(string relativePath)
    {
        if (_currentFilePath == null) return null;

        var dir = Path.GetDirectoryName(_currentFilePath);
        if (dir == null) return null;

        // Handle relative paths like ./other.md or ../other.md or other.md
        var candidate = Path.GetFullPath(Path.Combine(dir, relativePath));
        return File.Exists(candidate) ? candidate : null;
    }

    private void OpenBrowserUrl(string url)
    {
        try
        {
            if (File.Exists(url))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private async Task OpenFile()
    {
        Interlocked.Increment(ref _openFileEnterCount);
        if (Environment.GetEnvironmentVariable("LUCIDVIEW_FILE_DEBUG") == "1")
            Console.WriteLine($"[file] OpenFile enter#{_openFileEnterCount} pickerOpen={_filePickerOpen}");
        if (_filePickerOpen)
        {
            Interlocked.Increment(ref _openFileBlockedCount);
            if (Environment.GetEnvironmentVariable("LUCIDVIEW_FILE_DEBUG") == "1")
                Console.WriteLine($"[file] OpenFile BLOCKED (re-entry) blocked#{_openFileBlockedCount}");
            return;
        }
        _filePickerOpen = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Markdown File",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Markdown Files")
                        { Patterns = ["*.md", "*.markdown", "*.mdown", "*.mkd", "*.txt"] },
                    new FilePickerFileType("All Files") { Patterns = ["*.*"] }
                ]
            });

            if (files.Count > 0) await LoadFile(files[0].Path.LocalPath);
        }
        finally
        {
            _filePickerOpen = false;
        }
    }

    private async Task LoadFile(string path)
    {
        try
        {
            StatusText.Text = $"Loading {Path.GetFileName(path)}...";

            // Drop the in-memory image cache so dynamic shields (build status,
            // latest version, downloads) re-fetch on every document open.
            // The on-disk file remains as a fallback but new fetches overwrite.
            _imageCacheService.InvalidateInMemoryCache();

            var content = await File.ReadAllTextAsync(path);
            var basePath = Path.GetDirectoryName(path);
            _markdownService.SetBasePath(basePath);

            // Display content immediately for fast response
            await DisplayMarkdown(content);

            _settings.AddRecentFile(path);
            UpdateRecentFiles();

            var fileInfo = new FileInfo(path);
            ApplyLoadedDocumentState(
                sourcePath: path,
                displayTitle: Path.GetFileName(path),
                content: content,
                statusText: path,
                fileDateText: fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                fileInfoText: $"{fileInfo.Length:N0} bytes");

            QueueImageCaching(content);
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        catch
        {
            // Ignore known library errors (e.g., StaticBinding from Markdown.Avalonia)
        }
    }

    private async Task CacheImagesAndRefreshAsync(string content, List<string> imageUrls)
    {
        try
        {
            await _imageCacheService.PreCacheImagesAsync(imageUrls);
            if (!string.Equals(_rawContent, content, StringComparison.Ordinal))
                return;
            await DisplayMarkdown(content);
        }
        catch (Exception ex) when (IsIgnorableError(ex))
        {
        }
        catch
        {
        }
    }

    private async Task OpenUrl()
    {
        var dialog = new InputDialog("Open URL", "Enter the URL of a markdown file:");
        var result = await dialog.ShowDialog<string?>(this);

        if (!string.IsNullOrWhiteSpace(result)) await LoadFromUrl(result);
    }

    private async Task LoadFromUrl(string url)
    {
        try
        {
            StatusText.Text = "Downloading...";

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Only http:// and https:// URLs are supported.");

            var content = await DownloadMarkdownAsync(uri);
            var baseUrl = $"{uri.Scheme}://{uri.Host}{string.Join("", uri.Segments.Take(uri.Segments.Length - 1))}";
            _markdownService.SetBaseUrl(baseUrl);

            await DisplayMarkdown(content);

            ApplyLoadedDocumentState(
                sourcePath: url,
                displayTitle: uri.Segments.LastOrDefault()?.TrimEnd('/') ?? "Remote",
                content: content,
                statusText: url,
                fileDateText: "Remote",
                fileInfoText: $"{content.Length:N0} chars");

            QueueImageCaching(content);
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        catch
        {
        }
    }

    private static async Task<string> DownloadMarkdownAsync(Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("lucidVIEW/2.2");
        request.Headers.Accept.ParseAdd("text/markdown");
        request.Headers.Accept.ParseAdd("text/x-markdown;q=0.95");
        request.Headers.Accept.ParseAdd("text/plain;q=0.9");
        request.Headers.Accept.ParseAdd("text/*;q=0.8");
        request.Headers.Accept.ParseAdd("*/*;q=0.5");

        using var response = await RemoteMarkdownClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxRemoteMarkdownBytes)
            throw new InvalidOperationException("Remote markdown exceeds size limit.");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        if (content.Length > MaxRemoteMarkdownBytes)
            throw new InvalidOperationException("Remote markdown exceeds size limit.");

        return content;
    }

    private void ApplyLoadedDocumentState(
        string sourcePath,
        string displayTitle,
        string content,
        string statusText,
        string fileDateText,
        string fileInfoText)
    {
        _currentFilePath = sourcePath;
        Title = $"{displayTitle} - lucidVIEW";
        EnableDocumentControls(true);

        StatusText.Text = statusText;
        FileDateText.Text = fileDateText;
        WordCountText.Text = $"{CountWords(content):N0} words";
        FileInfoText.Text = fileInfoText;
    }

    private void QueueImageCaching(string content)
    {
        var imageUrls = _markdownService.ExtractImageUrls(content);
        if (imageUrls.Count > 0)
            _ = CacheImagesAndRefreshAsync(content, imageUrls);
    }
}

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

public partial class MainWindow
{
    private const int MaxRemoteMarkdownBytes = 2 * 1024 * 1024;
    private static readonly HttpClient RemoteMarkdownClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    // Re-entry guard for overlapping picker triggers (Click + IActivatableLifetime).
    private bool _filePickerOpen;

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".wsh",
        ".sh", ".bash", ".zsh", ".fish",
        ".app", ".pkg", ".dmg", ".scpt", ".command", ".tool",
        ".scr", ".msi", ".jar"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".txt"
    };

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenFile();
    }

    private void OnOpenWebPage(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = OpenWebPage();
    }

    private void OnSaveAsMarkdown(object? sender, RoutedEventArgs e)
    {
        CloseSidePanel();
        _ = SaveAsMarkdown();
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

        if (url.StartsWith('#'))
        {
            var slug = url[1..];
            var heading = FlattenHeadings(_headings).FirstOrDefault(h =>
                h.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
            if (heading != null)
                ScrollToHeading(heading);
            e.Handled = true;
            return;
        }

        if (href.IsAbsoluteUri && href.Scheme is "http" or "https")
        {
            _ = LoadWebPage(href.AbsoluteUri);
            e.Handled = true;
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var httpUri) && httpUri.Scheme is "http" or "https")
        {
            _ = LoadWebPage(httpUri.AbsoluteUri);
            e.Handled = true;
            return;
        }

        // Source loaded from a URL: resolve relative hrefs (/blog/foo, ../bar)
        // against the current page URL instead of treating them as local paths.
        if (TryGetCurrentRemoteBase(out var remoteBase)
            && Uri.TryCreate(remoteBase, url, out var combined)
            && combined.Scheme is "http" or "https")
        {
            _ = LoadWebPage(combined.AbsoluteUri);
            e.Handled = true;
            return;
        }

        var resolvedPath = TryResolveLocalPath(url);
        if (resolvedPath != null)
        {
            var ext = Path.GetExtension(resolvedPath);
            if (MarkdownExtensions.Contains(ext))
            {
                _ = LoadFile(resolvedPath);
                e.Handled = true;
                return;
            }

            if (ExecutableExtensions.Contains(ext))
            {
                StatusText.Text = "Blocked executable link target";
                e.Handled = true;
                return;
            }

            OpenBrowserUrl(resolvedPath);
            e.Handled = true;
            return;
        }

        if (href.IsAbsoluteUri && href.Scheme == "file")
        {
            var localPath = href.LocalPath;
            var ext = Path.GetExtension(localPath);
            if (MarkdownExtensions.Contains(ext))
            {
                _ = LoadFile(localPath);
                e.Handled = true;
                return;
            }

            if (!ExecutableExtensions.Contains(ext) && File.Exists(localPath))
            {
                OpenBrowserUrl(localPath);
                e.Handled = true;
                return;
            }
        }

        StatusText.Text = "Blocked unsupported link target";
        e.Handled = true;
    }

    private bool TryGetCurrentRemoteBase(out Uri baseUri)
    {
        baseUri = null!;
        if (string.IsNullOrEmpty(_currentFilePath)) return false;
        if (!Uri.TryCreate(_currentFilePath, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not "http" and not "https") return false;
        baseUri = uri;
        return true;
    }

    private string? TryResolveLocalPath(string relativePath)
    {
        if (_currentFilePath == null) return null;

        var dir = Path.GetDirectoryName(_currentFilePath);
        if (string.IsNullOrEmpty(dir)) return null;

        var candidate = Path.GetFullPath(Path.Combine(dir, relativePath));

        // Containment: resolved path MUST stay under the document directory to
        // prevent malicious markdown linking to ../../../usr/bin/open and friends.
        var dirWithSep = dir.EndsWith(Path.DirectorySeparatorChar)
            ? dir
            : dir + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(dirWithSep, StringComparison.OrdinalIgnoreCase))
            return null;

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
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private async Task OpenFile()
    {
        if (_filePickerOpen) return;
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

            _imageCacheService.InvalidateInMemoryCache();

            var content = await File.ReadAllTextAsync(path);
            var basePath = Path.GetDirectoryName(path);
            _markdownService.SetBasePath(basePath);

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

            PushHistory(path, Path.GetFileName(path));
            SetSourceMode(SourceMode.LocalFile);
            QueueImageCaching(content);
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
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
    }

    private async Task OpenWebPage()
    {
        var dialog = new InputDialog(
            "Open URL",
            "Enter a URL — markdown is preferred, HTML pages are converted:",
            watermark: "https://example.com");
        var result = await dialog.ShowDialog<string?>(this);

        if (!string.IsNullOrWhiteSpace(result)) await LoadWebPage(result);
    }

    private async Task LoadWebPage(string url)
    {
        try
        {
            StatusText.Text = "Downloading...";

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Only http:// and https:// URLs are supported.");

            var (body, isHtml) = await DownloadWebPageAsync(uri);
            string content;
            var mode = isHtml ? SourceMode.ConvertedFromHtml : SourceMode.DirectMarkdown;

            if (isHtml)
            {
                StatusText.Text = "Converting page...";
                content = await _htmlToMarkdownService.ConvertAsync(body, uri);

                if (content.Trim().Length < SparseExtractionThreshold && SpaDetection.LooksLikeSpa(body))
                {
                    var framework = SpaDetection.DetectFramework(body);
                    content = SpaDetection.BuildStubMarkdown(url, framework);
                    mode = SourceMode.ClientSideRendered;
                }
            }
            else
            {
                content = body;
            }

            var baseUrl = uri.GetLeftPart(UriPartial.Authority)
                + string.Join("", uri.Segments.Take(uri.Segments.Length - 1));
            _markdownService.SetBaseUrl(baseUrl);

            await DisplayMarkdown(content);

            _settings.AddRecentFile(url, displayName: uri.Host);
            UpdateRecentFiles();

            var title = uri.Segments.LastOrDefault()?.TrimEnd('/') ?? uri.Host;
            var status = mode switch
            {
                SourceMode.ConvertedFromHtml => $"{url} (converted from HTML)",
                SourceMode.ClientSideRendered => $"{url} (client-side rendered — no conversion possible)",
                _ => url
            };
            ApplyLoadedDocumentState(
                sourcePath: url,
                displayTitle: title,
                content: content,
                statusText: status,
                fileDateText: "Remote",
                fileInfoText: $"{content.Length:N0} chars");

            PushHistory(url, title);
            SetSourceMode(mode);
            QueueImageCaching(content);
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void PushHistory(string url, string title)
    {
        if (_suppressHistoryPush) return;
        _sessionHistory.Push(new NavigationEntry(url, title));
        UpdateNavigationButtonState();
    }

    private void UpdateNavigationButtonState()
    {
        BackButton.IsEnabled = _sessionHistory.CanGoBack;
        ForwardButton.IsEnabled = _sessionHistory.CanGoForward;
        ReloadButton.IsEnabled = _sessionHistory.Current is not null;

        if (AddressBar is not null && _sessionHistory.Current is { } entry)
            AddressBar.Text = entry.Url;
    }

    private void OnGoBack(object? sender, RoutedEventArgs e) => _ = GoBack();
    private void OnGoForward(object? sender, RoutedEventArgs e) => _ = GoForward();
    private void OnReload(object? sender, RoutedEventArgs e) => _ = Reload();

    private async Task GoBack()
    {
        var entry = _sessionHistory.Back();
        if (entry is null) return;
        UpdateNavigationButtonState();
        await NavigateToHistoryEntry(entry);
    }

    private async Task GoForward()
    {
        var entry = _sessionHistory.Forward();
        if (entry is null) return;
        UpdateNavigationButtonState();
        await NavigateToHistoryEntry(entry);
    }

    private async Task Reload()
    {
        var entry = _sessionHistory.Current;
        if (entry is null) return;
        await NavigateToHistoryEntry(entry);
    }

    private async Task NavigateToHistoryEntry(NavigationEntry entry)
    {
        _suppressHistoryPush = true;
        try
        {
            if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                await LoadWebPage(entry.Url);
            else if (File.Exists(entry.Url))
                await LoadFile(entry.Url);
        }
        finally
        {
            _suppressHistoryPush = false;
        }
    }

    private void FocusAddressBar()
    {
        AddressBar.Focus();
        AddressBar.SelectAll();
    }

    private void OpenCurrentInExternalBrowser()
    {
        var url = _sessionHistory.Current?.Url ?? _currentFilePath;
        if (string.IsNullOrEmpty(url)) return;
        OpenBrowserUrl(url);
    }

    private void OnAddressBarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;
        e.Handled = true;
        var raw = AddressBar.Text?.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        _ = NavigateFromAddressBar(raw);
    }

    private async Task NavigateFromAddressBar(string raw)
    {
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                await LoadWebPage(raw);
                return;
            }
            if (uri.Scheme == "file" && File.Exists(uri.LocalPath))
            {
                await LoadFile(uri.LocalPath);
                return;
            }
        }

        // Bare host like "wikipedia.org/wiki/Markdown" → treat as https.
        if (!raw.Contains(' ') && raw.Contains('.') && !File.Exists(raw)
            && !raw.StartsWith('/') && !raw.StartsWith('~'))
        {
            await LoadWebPage("https://" + raw);
            return;
        }

        var expanded = raw.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), raw[1..].TrimStart('/', '\\'))
            : raw;

        if (File.Exists(expanded))
        {
            await LoadFile(expanded);
            return;
        }

        StatusText.Text = $"Couldn't resolve: {raw}";
    }

    private async Task SaveAsMarkdown()
    {
        if (string.IsNullOrEmpty(_rawContent))
        {
            StatusText.Text = "Nothing to save";
            return;
        }
        if (_filePickerOpen) return;
        _filePickerOpen = true;
        try
        {
            var isUrl = !string.IsNullOrEmpty(_currentFilePath)
                && Uri.TryCreate(_currentFilePath, UriKind.Absolute, out var sourceUri)
                && sourceUri.Scheme is "http" or "https";

            string suggestedName;
            if (isUrl && Uri.TryCreate(_currentFilePath, UriKind.Absolute, out var uri))
            {
                var lastSegment = uri.Segments.LastOrDefault()?.TrimEnd('/');
                suggestedName = string.IsNullOrEmpty(lastSegment) ? uri.Host : lastSegment;
                if (suggestedName.Contains('.'))
                    suggestedName = Path.GetFileNameWithoutExtension(suggestedName);
            }
            else
            {
                suggestedName = Path.GetFileNameWithoutExtension(_currentFilePath ?? "document");
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save markdown",
                SuggestedFileName = $"{suggestedName}.md",
                DefaultExtension = "md",
                FileTypeChoices =
                [
                    new FilePickerFileType("Markdown") { Patterns = ["*.md", "*.markdown"] },
                    new FilePickerFileType("Plain text") { Patterns = ["*.txt"] }
                ]
            });

            if (file is null)
            {
                StatusText.Text = "Save canceled";
                return;
            }

            var path = file.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText.Text = "Unable to resolve save path";
                return;
            }

            // URL-loaded markdown gets a frontmatter snapshot so the saved
            // file records where it came from and which converter version
            // produced it. Local-file copies stay verbatim — the user
            // already has the source path.
            string output;
            if (isUrl && Uri.TryCreate(_currentFilePath, UriKind.Absolute, out var fmUri))
                output = BuildSavedMarkdown(fmUri, _rawContent);
            else
                output = _rawContent;

            await File.WriteAllTextAsync(path, output);
            StatusText.Text = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (!IsIgnorableError(ex))
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
        finally
        {
            _filePickerOpen = false;
        }
    }

    private static string BuildSavedMarkdown(Uri sourceUri, string content)
    {
        var styloVersion = typeof(StyloExtract.Heuristics.HeuristicBlockClassifier).Assembly
            .GetName().Version?.ToString(3) ?? "unknown";
        var lucidVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        var fetched = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var title = ExtractFirstHeading(content) ?? sourceUri.Host;

        var sb = new System.Text.StringBuilder();
        sb.Append("---\n");
        sb.Append("source: ").Append(sourceUri).Append('\n');
        sb.Append("title: ").Append(YamlEscape(title)).Append('\n');
        sb.Append("fetched: ").Append(fetched).Append('\n');
        sb.Append("converter: Mostlylucid.StyloExtract ").Append(styloVersion).Append('\n');
        sb.Append("client: lucidVIEW ").Append(lucidVersion).Append('\n');
        sb.Append("---\n\n");
        sb.Append(content);
        return sb.ToString();
    }

    private static string? ExtractFirstHeading(string content)
    {
        foreach (var rawLine in content.Split('\n', 30))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();
            if (line.StartsWith("# "))
                return line[2..].Trim();
        }
        return null;
    }

    private static string YamlEscape(string s)
    {
        // Quoted scalar; escape backslash and double-quote.
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private static async Task<(string Body, bool IsHtml)> DownloadWebPageAsync(Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent.Value);
        // Prefer markdown so servers that already produce markdown
        // (mostlylucid.net, Cloudflare URL→markdown, Jina Reader) bypass
        // the StyloExtract conversion entirely.
        request.Headers.Accept.ParseAdd("text/markdown");
        request.Headers.Accept.ParseAdd("text/x-markdown;q=0.95");
        request.Headers.Accept.ParseAdd("text/html;q=0.85");
        request.Headers.Accept.ParseAdd("application/xhtml+xml;q=0.85");
        request.Headers.Accept.ParseAdd("text/plain;q=0.7");
        request.Headers.Accept.ParseAdd("*/*;q=0.5");

#if FULL
        var fetchSw = Stopwatch.StartNew();
        // alpha.17: warm any persisted streaming template for this host into
        // the hot cache BEFORE the response body arrives so ScanByHost on the
        // first chunk-threshold tick (or stream-end) hits the host template
        // instead of bouncing on NoTemplate. Fire-and-forget warm — by the
        // time the body finishes reading the lookup has completed.
        var streamingHost = uri.Host;
        var warmSelector = FullServices.Get<StyloExtract.Streaming.StreamingPathSelector>();
        _ = warmSelector.WarmByHostAsync(streamingHost).AsTask();
#endif

        using var response = await RemoteMarkdownClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxRemoteMarkdownBytes)
            throw new InvalidOperationException("Remote page exceeds size limit.");

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";

#if FULL
        var (bytes, lastVerdict, peakBufferedBytes) = await ReadBodyWithLimitAndScanAsync(
            response.Content, MaxRemoteMarkdownBytes, streamingHost, warmSelector);
#else
        var bytes = await ReadBodyWithLimitAsync(response.Content, MaxRemoteMarkdownBytes);
#endif
        var body = System.Text.Encoding.UTF8.GetString(bytes);

        var isMarkdown = mediaType.Contains("markdown", StringComparison.OrdinalIgnoreCase);
        var isHtml = !isMarkdown
            && (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("xhtml", StringComparison.OrdinalIgnoreCase)
                || HtmlToMarkdownService.LooksLikeHtml(body));

#if FULL
        fetchSw.Stop();
        // alpha.17: chunk-aware streaming-scan flow.
        //   - ReadBodyWithLimitAndScanAsync already invoked ScanByHost on a
        //     64KB-threshold cadence + once on stream-end, so lastVerdict holds
        //     the final verdict the scanner produced over the accumulating buffer.
        //   - When verdict is NoTemplate AND the body looks HTML-shaped, kick
        //     StreamingTemplateInducer.Induce to build + upsert a host-keyed
        //     template so the NEXT visit to this host gets a real verdict.
        //     The induced template persists via SqliteStreamingTemplateStore.
        //   - Status-bar fetch segment shows the verdict the scanner WOULD have
        //     emitted for this fetch: Http+NoTemplate on first visit, then
        //     Http+Captured (or +Bailout) once the inducer has primed the store.
        try
        {
            var verdict = lastVerdict;
            Console.WriteLine($"[streaming] scanned {bytes.Length} bytes from {uri.Host}: verdict={verdict}");

            if (verdict == StyloExtract.Streaming.ScanVerdict.NoTemplate && isHtml)
            {
                var inducer = FullServices.Get<StyloExtract.Streaming.StreamingTemplateInducer>();
                var summary = inducer.Describe(bytes);
                var induced = inducer.Induce(streamingHost, bytes);
                if (induced is not null)
                {
                    var store = FullServices.Get<StyloExtract.Streaming.IStreamingTemplateStore>();
                    await store.UpsertAsync(induced, CancellationToken.None);
                    Console.WriteLine(
                        $"[streaming] induced template for {streamingHost} " +
                        $"(prefix={summary?.PrefixMarker}, content={summary?.ContentStartMarker}, end={summary?.ContentEndMarker})");

                    // Re-use the LLM telemetry channel — same status-bar segment that
                    // lights up for deterministic / LLM template induction. Tagged
                    // (streaming) so the user can tell streaming-induced templates
                    // apart from heuristic / LLM ones.
                    var telemetryInd = FullServices.Get<MarkdownViewer.Services.ExtractionTelemetry>();
                    telemetryInd.EmitStage(
                        MarkdownViewer.Services.ExtractionStage.Llm,
                        started: false,
                        detail: $"{streamingHost} (streaming)");
                }
                else
                {
                    Console.WriteLine($"[streaming] inducer returned null for {streamingHost} — no plausible fences");
                }
            }

            var telemetry = FullServices.Get<MarkdownViewer.Services.ExtractionTelemetry>();
            // alpha.19: surface the sliding-window memory cap as a status-bar
            // metric. peakBufferedBytes is the high-watermark of the tokenizer's
            // in-flight byte buffer across the whole feed — under the alpha.19
            // contract this stays O(longest tag) regardless of response size,
            // so a 200 KB response with peak ~8 KB is the headline proof the
            // streaming scan held bounded memory. Falls back to "peak0B" when
            // we never built a scanner (NoTemplate path).
            var peakDetail = peakBufferedBytes > 0
                ? $"Http+{verdict}+peak{peakBufferedBytes}B/{bytes.Length}B"
                : $"Http+{verdict}";
            telemetry.EmitStage(
                MarkdownViewer.Services.ExtractionStage.Fetch,
                started: false,
                detail: peakDetail,
                duration: fetchSw.Elapsed);
            Console.WriteLine(
                $"[streaming] peak buffered={peakBufferedBytes:N0} B vs response={bytes.Length:N0} B " +
                $"(ratio={(bytes.Length > 0 ? (peakBufferedBytes * 100.0 / bytes.Length).ToString("F2") : "n/a")}%)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[streaming] scan/induction failed: {ex.Message}");
        }
#endif

        return (body, isHtml);
    }

    private static async Task<byte[]> ReadBodyWithLimitAsync(HttpContent content, long maxBytes)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException("Remote page exceeds size limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

#if FULL
    /// <summary>
    /// alpha.18: true chunked streaming-scan body reader. Replaces alpha.17's
    /// 64 KB-threshold whole-buffer re-scan with a per-chunk feed via the new
    /// <see cref="StyloExtract.Streaming.IncrementalFenceScanner"/>. As bytes
    /// arrive from HttpClient we feed them straight into the incremental
    /// tokenizer + scanner, which holds a partial-tag buffer across chunk
    /// boundaries and emits a running verdict as soon as enough tags have
    /// passed for the structural fences to match.
    ///
    /// Behavioural difference vs alpha.17:
    /// <list type="bullet">
    ///   <item><description>Captured can land on chunk 2 or 3 — well before the
    ///   whole body has been buffered — instead of waiting for a 64 KB
    ///   threshold tick.</description></item>
    ///   <item><description>No O(N) re-scan-from-start cost on each threshold
    ///   tick; tokenization is amortised across the bytes seen so far.</description></item>
    ///   <item><description>Same byte-budget contract; same fallback behaviour
    ///   when the host has no warmed template (scanner is null, alpha.17
    ///   auto-induction path still fires from the buffered bytes after
    ///   stream-end).</description></item>
    /// </list>
    /// </summary>
    private static async Task<(byte[] Bytes, StyloExtract.Streaming.ScanVerdict LastVerdict, int PeakBufferedBytes)> ReadBodyWithLimitAndScanAsync(
        HttpContent content,
        long maxBytes,
        string host,
        StyloExtract.Streaming.StreamingPathSelector selector)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long total = 0;

        // Look up the host-keyed template (hot cache only; the caller fires
        // WarmByHostAsync upstream so by now the warm should have landed in
        // the hot tier). Null template -> no incremental scan; we still drain
        // the body so auto-induction can run on full bytes after stream end.
        var store = MarkdownViewer.Services.FullServices.Get<StyloExtract.Streaming.IStreamingTemplateStore>();
        var template = store.TryGetHotByHost(host);
        StyloExtract.Streaming.IncrementalFenceScanner? scanner = template is not null
            ? StyloExtract.Streaming.IncrementalFenceScanner.Create(template)
            : null;

        var lastVerdict = template is null
            ? StyloExtract.Streaming.ScanVerdict.NoTemplate
            : StyloExtract.Streaming.ScanVerdict.Continue;
        int chunkIndex = 0;
        bool earlyTerminal = false;

        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException("Remote page exceeds size limit.");
            buffer.Write(chunk, 0, read);
            chunkIndex++;

            if (scanner is not null && !earlyTerminal)
            {
                var v = scanner.Feed(chunk.AsSpan(0, read));
                Console.WriteLine($"[streaming] chunk {chunkIndex} ({read} bytes): {v}");
                if (v is StyloExtract.Streaming.ScanVerdict.Captured
                    or StyloExtract.Streaming.ScanVerdict.Bailout)
                {
                    lastVerdict = v;
                    earlyTerminal = true;
                    // Keep draining the body — the markdown converter downstream
                    // still needs the full bytes — but the verdict is now latched.
                }
                else
                {
                    lastVerdict = v;
                }
            }
        }

        if (scanner is not null && !earlyTerminal)
            lastVerdict = scanner.Flush();

        // alpha.18: kick the refit orchestrator off-hot-path on captured scans.
        // The orchestrator handles its own gating (cadence + EWMA drift) and
        // fires the version sink when a refit fires.
        if (scanner is not null
            && lastVerdict == StyloExtract.Streaming.ScanVerdict.Captured)
        {
            var bytesCopy = buffer.ToArray();
            try
            {
                var orchestrator = MarkdownViewer.Services.FullServices.Get<StyloExtract.Streaming.StreamingRefitOrchestrator>();
                orchestrator.RecordCaptured(
                    host,
                    scanner.CaptureStartByte,
                    scanner.CaptureEndByte,
                    bytesCopy);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[streaming-refit] record-captured failed: {ex.Message}");
            }
        }

        // alpha.19: scanner exposes the tokenizer's high-watermark; surface it
        // so callers can render the bounded-memory property in telemetry. Null
        // scanner (no template) reports 0 — we never built the partial-tag
        // buffer at all, so "peak 0 B" is the honest answer.
        var peakBuffered = scanner?.PeakBufferedBytes ?? 0;
        return (buffer.ToArray(), lastVerdict, peakBuffered);
    }
#endif

    private enum SourceMode
    {
        LocalFile,
        DirectMarkdown,
        ConvertedFromHtml,
        ClientSideRendered
    }

    private const int SparseExtractionThreshold = 200;

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

    private void SetSourceMode(SourceMode mode)
    {
        SourceModeIcon.IsVisible = true;
        switch (mode)
        {
            case SourceMode.LocalFile:
                SourceModeIcon.Symbol = FluentIcons.Common.Symbol.Document;
                ToolTip.SetTip(SourceModeIcon, "Local markdown file");
                break;
            case SourceMode.DirectMarkdown:
                SourceModeIcon.Symbol = FluentIcons.Common.Symbol.Globe;
                ToolTip.SetTip(SourceModeIcon, "Direct markdown from URL (no conversion)");
                break;
            case SourceMode.ConvertedFromHtml:
                SourceModeIcon.Symbol = FluentIcons.Common.Symbol.ArrowSync;
                ToolTip.SetTip(SourceModeIcon, "Converted from HTML via StyloExtract");
                break;
            case SourceMode.ClientSideRendered:
                SourceModeIcon.Symbol = FluentIcons.Common.Symbol.Warning;
                ToolTip.SetTip(SourceModeIcon, "Client-side rendered — JavaScript required, lucidVIEW can't run it");
                break;
        }
    }

    private void QueueImageCaching(string content)
    {
        var imageUrls = _markdownService.ExtractImageUrls(content);
        if (imageUrls.Count > 0)
            _ = CacheImagesAndRefreshAsync(content, imageUrls);
    }
}

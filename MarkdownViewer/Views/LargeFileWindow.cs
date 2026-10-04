using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Mostlylucid.LucidView.Markdown;
using MarkdownViewer.Services;

namespace MarkdownViewer.Views;

/// <summary>A file-backed reader with only the current page in the visual tree.</summary>
public sealed class LargeFileWindow : Window
{
    private const int PageLines = 120;
    private readonly IndexedTextFile _file;
    private readonly string _path;
    private readonly bool _markdown;
    private readonly CancellationTokenSource _lifetime = new();
    private MarkdownFileIndex? _markdownIndex;
    private bool _showSource;
    private bool _closed;
    private readonly ScrollBar _position = new()
    {
        Name = "FilePosition", Orientation = Orientation.Vertical,
        Width = 18, SmallChange = 1, LargeChange = PageLines
    };
    private readonly TextBox _source = new()
    {
        Name = "SourceText", IsReadOnly = true, AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 14,
        TextWrapping = TextWrapping.NoWrap, BorderThickness = new Thickness(0)
    };
    private readonly ScrollViewer _pageScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly Border _pageContent = new() { Padding = new Thickness(24, 20) };
    private readonly TextBlock _status = new() { Name = "ReaderStatus", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _activity = new() { Name = "ReaderActivity", Margin = new Thickness(12, 4) };
    private readonly TextBox _search = new() { Name = "ReaderSearch", Watermark = "Find text (Enter / Shift+Enter)", MaxLength = 1024 };
    private readonly TextBox _matchContext = new()
    {
        Name = "MatchContext", IsReadOnly = true, IsVisible = false,
        FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 13,
        Margin = new Thickness(12, 4), MaxHeight = 55
    };
    private readonly Button _mode = new() { Content = "Preview", Margin = new Thickness(6, 0) };
    private readonly Button _cancelSearch = new() { Content = "Cancel", IsVisible = false, Margin = new Thickness(6, 0) };
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _searchCancellation;
    private long _requestedLine;
    private long _requestedColumn;
    private bool _navigating;
    private TextFilePage? _sourcePage;
    private long? _anchorLine;
    private readonly Stack<TextPosition> _sourceHistory = [];
    private long _visibleFirstLine;
    private long _visibleEndLine;
    private TextMatch? _lastMatch;
    private string? _lastQuery;
    private bool _scrollToBottom;
    private int _pageGeneration;

    public event Action<string>? OpenDocumentRequested;

    public LargeFileWindow(string path, IndexedTextFile file)
    {
        _path = path;
        _file = file;
        _markdown = Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd";
        _showSource = true;
        Title = $"{Path.GetFileName(path)} — lucidVIEW large file reader";
        Width = 1050; Height = 760; MinWidth = 500; MinHeight = 320;

        var toolbar = new DockPanel { Margin = new Thickness(12, 10) };
        var next = new Button { Name = "FindNext", Content = "Next", Margin = new Thickness(6, 0) };
        var previous = new Button { Name = "FindPrevious", Content = "Prev", Margin = new Thickness(6, 0) };
        next.Click += async (_, _) => await FindAsync(false);
        previous.Click += async (_, _) => await FindAsync(true);
        _cancelSearch.Click += (_, _) => _searchCancellation?.Cancel();
        _search.TextChanged += (_, _) =>
        {
            if (!string.Equals(_search.Text, _lastQuery, StringComparison.Ordinal)) _searchCancellation?.Cancel();
        };
        _search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await FindAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            }
        };
        foreach (var button in new[] { next, previous, _cancelSearch })
        {
            DockPanel.SetDock(button, Dock.Right);
            toolbar.Children.Add(button);
        }
        if (_markdown)
        {
            _mode.IsEnabled = false;
            _mode.Click += (_, _) =>
            {
                _showSource = !_showSource;
                _requestedColumn = 0;
                _sourceHistory.Clear();
                _mode.Content = _showSource ? "Preview" : "Source";
                QueuePage(_requestedLine);
            };
            DockPanel.SetDock(_mode, Dock.Right);
            toolbar.Children.Add(_mode);
        }
        var goToLine = new TextBox { Name = "GoToLine", Watermark = "Line[:column]…", Width = 145, Margin = new Thickness(6, 0) };
        goToLine.KeyDown += (_, e) =>
        {
            var parts = goToLine.Text?.Split(':');
            if (e.Key == Key.Enter && parts is { Length: 1 or 2 } && long.TryParse(parts[0], out var line)
                && (parts.Length == 1 || long.TryParse(parts[1], out var ignoredColumn)))
            {
                _sourceHistory.Clear();
                var column = parts.Length == 2 ? Math.Max(0, long.Parse(parts[1]) - 1) : 0;
                if (column > 0) { _showSource = true; _mode.Content = "Preview"; }
                NavigateToPosition(new TextPosition(line - 1, column));
                e.Handled = true;
            }
        };
        DockPanel.SetDock(goToLine, Dock.Right);
        toolbar.Children.Add(goToLine);
        toolbar.Children.Add(_search);

        _pageContent.Child = _source;
        _pageScroll.Content = _pageContent;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        body.Children.Add(_pageScroll);
        _pageScroll.AddHandler(PointerWheelChangedEvent, OnPageWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grid.SetColumn(_position, 1);
        body.Children.Add(_position);
        _position.Maximum = Math.Max(0, file.LineCount - 1);
        _position.ViewportSize = PageLines;
        _position.ValueChanged += (_, e) =>
        {
            if (!_navigating) { _requestedColumn = 0; _sourceHistory.Clear(); }
            QueuePage((long)e.NewValue);
        };

        var layout = new DockPanel();
        foreach (var top in new Control[] { toolbar, _activity, _matchContext })
        {
            DockPanel.SetDock(top, Dock.Top);
            layout.Children.Add(top);
        }
        var statusBar = new Border { Padding = new Thickness(12, 6), Child = _status };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        layout.Children.Add(statusBar);
        layout.Children.Add(body);
        Content = layout;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _loadCancellation?.Cancel();
            _searchCancellation?.Cancel();
            _pageContent.Child = null; // Detaching cancels the Markdown renderer's diagram work.
            _source.Text = null;
        };
        QueuePage(0);
        if (_markdown) _ = BuildMarkdownIndexAsync();
        else _activity.IsVisible = false;
    }

    private async Task BuildMarkdownIndexAsync()
    {
        try
        {
            _activity.Text = "Preparing Markdown preview… source is available while indexing.";
            var progress = new Progress<long>(line =>
            {
                if (!_closed && _markdownIndex is null)
                    _activity.Text = $"Preparing Markdown preview… {line * 100.0 / _file.LineCount:F0}%";
            });
            var index = await Task.Run(() => MarkdownFileIndex.CreateAsync(_file, _lifetime.Token, progress), _lifetime.Token);
            if (_closed) return;
            _markdownIndex = index;
            _mode.IsEnabled = true;
            // A search deliberately shows source so its exact match remains visible.
            if (_lastMatch is null)
            {
                _showSource = false;
                _mode.Content = "Source";
            }
            if (_lastMatch is null)
            {
                _activity.Text = index.ReferencesLimited ? "Some reference links exceed the preview metadata limit; source remains available." : "";
                _activity.IsVisible = index.ReferencesLimited;
            }
            QueuePage(_requestedLine);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed) _activity.Text = $"Markdown preview unavailable: {ex.Message}";
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox { Name: "ReaderSearch" or "GoToLine" }
            && e.Key is not Key.Escape && !(e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))) return;
        if (e.Key == Key.PageDown) MovePage(false);
        else if (e.Key == Key.PageUp) MovePage(true);
        else if (e.Key == Key.Home && e.KeyModifiers.HasFlag(KeyModifiers.Control)) NavigateTo(0);
        else if (e.Key == Key.End && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _sourceHistory.Clear();
            NavigateToPosition(new TextPosition(_file.LineCount - 1, long.MaxValue));
        }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)) _search.Focus();
        else if (e.Key == Key.Escape) _searchCancellation?.Cancel();
        else return;
        e.Handled = true;
    }

    private void OnPageWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        var bottom = Math.Max(0, _pageScroll.Extent.Height - _pageScroll.Viewport.Height);
        if (e.Delta.Y < 0 && _pageScroll.Offset.Y >= bottom - 1) { MovePage(false); e.Handled = true; }
        else if (e.Delta.Y > 0 && _pageScroll.Offset.Y <= 1) { MovePage(true); e.Handled = true; }
    }

    private void MovePage(bool backwards)
    {
        if (_sourcePage is { } source)
        {
            TextPosition target;
            if (backwards)
                target = _sourceHistory.Count > 0 ? _sourceHistory.Pop()
                    : source.Start.Column > 0 ? source.Start with { Column = Math.Max(0, source.Start.Column - IndexedTextFile.MaxDisplayedLineLength) }
                    : new TextPosition(Math.Max(0, source.Start.Line - PageLines), 0);
            else
            {
                if (source.Next is not { } next) return;
                if (_sourceHistory.Count == 512) _sourceHistory.Clear();
                _sourceHistory.Push(source.Start);
                target = next;
            }
            NavigateToPosition(target);
            return;
        }
        _scrollToBottom = backwards;
        var line = backwards
            ? (_markdownIndex is not null && !_showSource
                ? Math.Max(0, _visibleFirstLine - 1) : Math.Max(0, _visibleFirstLine - PageLines))
            : Math.Min(_file.LineCount - 1, _visibleEndLine + 1);
        NavigateTo(line);
    }

    private void NavigateTo(long line)
    {
        _sourceHistory.Clear();
        NavigateToPosition(new TextPosition(line, 0));
    }

    private void NavigateToPosition(TextPosition position)
    {
        var line = Math.Clamp(position.Line, 0, _file.LineCount - 1);
        _requestedColumn = position.Column;
        _navigating = true;
        try
        {
            if ((long)_position.Value == line) QueuePage(line);
            else _position.Value = line;
        }
        finally { _navigating = false; }
    }

    private void QueuePage(long firstLine)
    {
        if (_closed) return;
        _requestedLine = Math.Clamp(firstLine, 0, _file.LineCount - 1);
        _loadCancellation?.Cancel();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var anchorLine = _anchorLine;
        _anchorLine = null;
        _ = LoadPageAsync(new TextPosition(_requestedLine, _requestedColumn), _loadCancellation, ++_pageGeneration, anchorLine);
    }

    private async Task LoadPageAsync(TextPosition requested, CancellationTokenSource operation, int generation, long? anchorLine)
    {
        var cancellationToken = operation.Token;
        try
        {
            await Task.Delay(40, cancellationToken);
            if (requested.Column == long.MaxValue)
            {
                var end = await Task.Run(() => _file.ReadTextPageAsync(requested, cancellationToken: cancellationToken), cancellationToken);
                requested = end.Start with { Column = Math.Max(0, end.Start.Column - IndexedTextFile.MaxDisplayedLineLength) };
                // The preview index already keeps the final normal Markdown block together.
                if (_markdownIndex is not null && !_showSource && !_markdownIndex.FindPage(requested.Line).SourceOnly)
                    requested = requested with { Column = 0 };
            }
            var showPreview = _markdownIndex is not null && !_showSource && requested.Column == 0;
            var page = showPreview ? _markdownIndex!.FindPage(requested.Line) : new MarkdownFilePage(requested.Line, PageLines, false);
            if (showPreview && !page.SourceOnly && anchorLine == requested.Line)
                page = page with { FirstLine = requested.Line, LineCount = checked((int)(page.FirstLine + page.LineCount - requested.Line)) };
            var lines = await Task.Run(async () =>
            {
                var result = new List<FileTextLine>();
                if (!showPreview || page.SourceOnly) return result;
                await foreach (var line in _file.ReadLineRecordsAsync(page.FirstLine, cancellationToken))
                {
                    result.Add(line);
                    if (line.IsTruncated || result.Count == page.LineCount) break;
                }
                return result;
            }, cancellationToken);
            var literal = !showPreview || page.SourceOnly || lines.Any(line => line.IsTruncated);
            TextFilePage? sourcePage = literal
                ? await Task.Run(() => _file.ReadTextPageAsync(requested, cancellationToken: cancellationToken), cancellationToken) : null;
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed || generation != _pageGeneration) return;
            _requestedColumn = requested.Column;
            _sourcePage = sourcePage;
            _visibleFirstLine = sourcePage?.Start.Line ?? lines[0].Number;
            _visibleEndLine = sourcePage?.End.Line ?? lines[^1].Number;
            if (literal)
            {
                _source.Text = sourcePage!.Value.Text;
                _pageContent.Child = _source;
                HighlightSourceMatch(sourcePage.Value);
            }
            else
            {
                // Each page owns its renderer; detached pages cannot publish stale parses or diagrams.
                var view = new LucidMarkdownView
                {
                    SourcePath = Path.GetDirectoryName(_path),
                    Markdown = string.Join('\n', lines.Select(line => line.Text)) + "\n\n" + _markdownIndex!.ReferenceContext
                };
                view.LinkClick += (_, e) => OnLink(e.HRef?.ToString());
                _pageContent.Child = view;
                _source.Text = null;
            }
            _pageScroll.Offset = new Vector(0, 0);
            if (_scrollToBottom)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed && generation == _pageGeneration) _pageScroll.ScrollToEnd();
                }, DispatcherPriority.Background);
            _scrollToBottom = false;
            var detail = showPreview && literal ? "oversized block shown as source" : literal ? "source" : "Markdown preview";
            if (sourcePage is { } fragment && (fragment.Start.Column > 0 || fragment.Next is { Column: > 0 }))
                detail += $" · columns {fragment.Start.Column + 1:N0}–{fragment.End.Column + 1:N0} · PageUp/PageDown to continue";
            _status.Text = $"Lines {_visibleFirstLine + 1:N0}–{_visibleEndLine + 1:N0} of {_file.LineCount:N0} · {_file.EncodingName} · read-only · {detail}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !cancellationToken.IsCancellationRequested) _status.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, operation)) _loadCancellation = null;
            operation.Dispose();
        }
    }

    private void HighlightSourceMatch(TextFilePage page)
    {
        if (_lastMatch is not { } match) return;
        var offset = 0;
        var number = page.Start.Line;
        foreach (var line in page.Text.Split('\n'))
        {
            var column = number == page.Start.Line ? page.Start.Column : 0;
            if (number == match.Line && match.Column >= column && match.Column + match.Length <= column + line.Length)
            {
                _source.SelectionStart = offset + (int)(match.Column - column);
                _source.SelectionEnd = _source.SelectionStart + match.Length;
                return;
            }
            offset += line.Length + 1;
            number++;
        }
    }

    private async Task FindAsync(bool backwards)
    {
        var query = _search.Text;
        if (string.IsNullOrWhiteSpace(query) || _closed) return;
        _searchCancellation?.Cancel();
        var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchCancellation = operation;
        var cancellationToken = operation.Token;
        if (!string.Equals(query, _lastQuery, StringComparison.Ordinal)) _lastMatch = null;
        _lastQuery = query;
        _cancelSearch.IsVisible = true;
        _matchContext.IsVisible = false;
        _status.Text = "Searching…";
        try
        {
            var start = _lastMatch is { } last
                ? new TextPosition(last.Line, last.Column + (backwards ? 0 : 1))
                : new TextPosition(_requestedLine, _requestedColumn);
            var match = await Task.Run(() => _file.FindAsync(query, start, backwards, cancellationToken), cancellationToken);
            if (match is null && (backwards || start.Line != 0 || start.Column != 0))
                match = await Task.Run(() => _file.FindAsync(query,
                    new TextPosition(backwards ? _file.LineCount : 0, 0), backwards, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (match is null) { _status.Text = "No match"; return; }
            var context = await Task.Run(() => _file.ReadMatchContextAsync(match.Value, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _lastMatch = match;
            _showSource = true;
            _mode.Content = "Preview";
            _matchContext.Text = context.Text;
            _matchContext.SelectionStart = checked((int)(match.Value.Column - context.FirstColumn));
            _matchContext.SelectionEnd = _matchContext.SelectionStart + match.Value.Length;
            _matchContext.IsVisible = true;
            _activity.Text = $"Match: line {match.Value.Line + 1:N0}, column {match.Value.Column + 1:N0}";
            _activity.IsVisible = true;
            _sourceHistory.Clear();
            NavigateToPosition(new TextPosition(match.Value.Line, Math.Max(0, match.Value.Column - 80)));
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_searchCancellation, operation)) _status.Text = "Search cancelled";
        }
        catch (Exception ex)
        {
            if (!_closed && !cancellationToken.IsCancellationRequested) _status.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, operation))
            {
                _searchCancellation = null;
                _cancelSearch.IsVisible = false;
            }
            operation.Dispose();
        }
    }

    private void OnLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return;
        if (href.StartsWith('#'))
        {
            var identifier = Uri.UnescapeDataString(href[1..]);
            if (_markdownIndex?.FindHeading(identifier) is { } line)
            {
                // Start at the target heading, even when it was in the middle of a preview page.
                _anchorLine = line;
                NavigateTo(line);
            }
            else _status.Text = _markdownIndex is null ? "Markdown headings are still being indexed." : $"Heading not found: {identifier}";
            return;
        }
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https") OpenDocumentRequested?.Invoke(href);
            return;
        }
        var directory = Path.GetDirectoryName(_path)!;
        var target = Path.GetFullPath(Path.Combine(directory, Uri.UnescapeDataString(href)));
        var boundary = directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;
        if (target.StartsWith(boundary, StringComparison.Ordinal)
            && Path.GetExtension(target).ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt"
            && File.Exists(target)) OpenDocumentRequested?.Invoke(target);
    }
}

using Avalonia.Threading;
using LucidReader.Core.Model;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// The reading pane. Renders whatever ShowArticleAsync is given (called from
/// OnItemSelectedAsync in MainWindow.Items.cs, which also owns the mark-read
/// dwell; nothing here duplicates that).
///
/// Every link the reading pane surfaces, whether clicked inside the rendered
/// markdown or via "open original", came from remote feed content and is
/// therefore attacker-controlled. Both paths route through SafeLinkOpener's
/// http/https allowlist rather than handing a raw string to the platform's
/// "open this" mechanism.
/// </summary>
public partial class MainWindow
{
    private string _articleTitle = string.Empty;
    private string _articleMeta = string.Empty;
    private string _articleMarkdown = string.Empty;
    private bool _showOfflineBadge;
    private string _offlineBadgeText = string.Empty;
    private bool _canFetchFullArticle;
    private string? _heroImagePath;

    // Concurrency of 1 is deliberate: only one article's hero image is ever
    // resolving at a time, unlike the sidebar/list coordinators which fan
    // out across many rows.
    private readonly ImageResolutionCoordinator _heroCoordinator = new(maxConcurrency: 1);

    public string ArticleTitle
    {
        get => _articleTitle;
        private set { if (_articleTitle == value) return; _articleTitle = value; Raise(); }
    }

    public string ArticleMeta
    {
        get => _articleMeta;
        private set { if (_articleMeta == value) return; _articleMeta = value; Raise(); }
    }

    public string ArticleMarkdown
    {
        get => _articleMarkdown;
        private set { if (_articleMarkdown == value) return; _articleMarkdown = value; Raise(); }
    }

    public bool ShowOfflineBadge
    {
        get => _showOfflineBadge;
        private set { if (_showOfflineBadge == value) return; _showOfflineBadge = value; Raise(); }
    }

    public string OfflineBadgeText
    {
        get => _offlineBadgeText;
        private set { if (_offlineBadgeText == value) return; _offlineBadgeText = value; Raise(); }
    }

    public bool CanFetchFullArticle
    {
        get => _canFetchFullArticle;
        private set { if (_canFetchFullArticle == value) return; _canFetchFullArticle = value; Raise(); }
    }

    /// <summary>
    /// Local cached path for the hero image above the article title, resolved
    /// from <c>Item.ImageUrl</c>. Starts null on every article change - the
    /// title and body render immediately - and is assigned later, on the UI
    /// thread, once background resolution completes (MainWindow.Images.cs).
    /// </summary>
    public string? HeroImagePath
    {
        get => _heroImagePath;
        private set { if (_heroImagePath == value) return; _heroImagePath = value; Raise(); Raise(nameof(HasHero)); }
    }

    public bool HasHero => !string.IsNullOrEmpty(_heroImagePath);

    // Assigned in MainWindow's constructor (MainWindow.axaml.cs), which runs
    // after this partial's fields are initialised, so the command can close
    // over FetchFullArticleAsync as a bound instance method.
    public RelayCommand FetchFullArticleCommand { get; private set; } = null!;

    public async Task ShowArticleAsync(ItemRow? row)
    {
        // Every article switch, including to no article at all, invalidates
        // whatever hero resolution was in flight for the previous one - it
        // targets a hero the reading pane no longer shows.
        _heroCoordinator.CancelPending();
        HeroImagePath = null;

        if (row is null)
        {
            ArticleTitle = string.Empty;
            ArticleMeta = string.Empty;
            ArticleMarkdown = string.Empty;
            ShowOfflineBadge = false;
            CanFetchFullArticle = false;
            await RefreshArticleTagsAsync(null);
            return;
        }

        // The tag strip belongs to the article, so it is reloaded on every
        // article switch alongside the title and body. See MainWindow.Tags.cs.
        await RefreshArticleTagsAsync(row.Id);

        // Re-read rather than trusting the row: the download may have completed
        // since the list was populated.
        var item = await _services.Items.GetAsync(row.Id) ?? row.Item;

        ArticleTitle = string.IsNullOrWhiteSpace(item.Title) ? "Untitled" : item.Title!;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Author)) parts.Add(item.Author!);
        parts.Add(row.FeedName);
        if (item.PublishedUtc is { } published)
            parts.Add(published.ToLocalTime().ToString("f"));
        ArticleMeta = string.Join("  ·  ", parts);

        ArticleMarkdown = item.ContentMarkdown
            ?? item.Summary
            ?? "This article has no content yet.";

        (ShowOfflineBadge, OfflineBadgeText, CanFetchFullArticle) = item.OfflineState switch
        {
            OfflineState.Downloaded when item.ContentSource == ContentSource.Extracted =>
                (false, string.Empty, false),
            OfflineState.Downloaded =>
                (true, "Showing the summary the feed provided.", item.Link is not null),
            OfflineState.Failed =>
                (true, "The full article could not be downloaded. " + (item.OfflineError ?? string.Empty), true),
            OfflineState.Pending =>
                (true, "Downloading the full article...", false),
            _ =>
                (true, "Showing the summary the feed provided.", item.Link is not null)
        };

        // Fired without awaiting: the title and body are already rendered,
        // and the hero image fills in above them once it resolves. Reads
        // ImageUrl from the freshly re-read item, not the (possibly stale)
        // row, for the same reason the content above does.
        var imageUrl = item.ImageUrl;
        var token = _heroCoordinator.StartBatch();
        _ = _heroCoordinator.RunAsync(token, async ct =>
        {
            var local = await _services.Images.ResolveAsync(imageUrl, ct);
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() => HeroImagePath = local);
        });
    }

    /// <summary>
    /// Renders the bundled user manual in the reading pane, as an article.
    ///
    /// Three things this must not do, each of which it would do by default.
    ///
    /// It must not mark anything read. Reading state is written by the dwell
    /// timer OnItemSelectedAsync starts, so the manual never goes through
    /// that path: it clears the article selection first, which cancels any
    /// dwell already counting down against the article that was on screen.
    ///
    /// It must not disturb the sidebar. SelectedFeedNode is untouched, so the
    /// item list still holds whatever feed or folder was selected and the
    /// article the reader was on is still in it, one click away.
    ///
    /// And going back to a real article has to work. That is why the
    /// selection is cleared rather than left where it was: SelectedItemRow's
    /// setter returns early on a reference match, so a reader who clicks
    /// straight back onto the row they were reading would otherwise get the
    /// manual still on screen and nothing happening.
    /// </summary>
    public async Task ShowUserManualAsync()
    {
        await ClearArticleSelectionAsync();

        string? markdown;
        try
        {
            markdown = await UserManual.TryLoadAsync(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            // RelayCommand's own catch writes to stderr, which nobody reading
            // the app can see. A manual that is present but unreadable belongs
            // on the status line with the reason, like every other failure.
            StatusMessage = "Could not open the user manual: " + ex.Message;
            return;
        }

        if (markdown is null)
        {
            StatusMessage = UserManual.NotFoundMessage;
            return;
        }

        ArticleTitle = UserManual.Title;
        ArticleMeta = "mylo " + ProductVersion;
        ArticleMarkdown = markdown;
        ShowOfflineBadge = false;
        CanFetchFullArticle = false;
        StatusMessage = UserManual.ShowingMessage;
    }

    /// <summary>
    /// 0 idle, 1 running. A full-article fetch is bounded at
    /// OfflineDownloader.MaxArticleFetchDuration (180 seconds) and holds the
    /// status line for all of it, so without this a user watching nothing
    /// happen and clicking again would start a second fetch of the same
    /// article, and the two would race each other's status writes.
    /// </summary>
    private int _fetchFullArticleRunning;

    public async Task FetchFullArticleAsync()
    {
        if (SelectedItemRow is not { } row) return;
        if (Interlocked.Exchange(ref _fetchFullArticleRunning, 1) != 0) return;

        StatusMessage = "Fetching the full article...";
        try
        {
            await _services.Downloader.DownloadNowAsync(row.Id);
            await ShowArticleAsync(row);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not fetch the article: " + ex.Message;
        }
        finally
        {
            Volatile.Write(ref _fetchFullArticleRunning, 0);
        }
    }

    /// <summary>
    /// Every link in the reading pane came from a remote feed, so it goes
    /// through the allowlist rather than straight to the platform opener.
    ///
    /// Handled is set on EVERY path, including refusal and the
    /// OpenLinksExternally-off path, and that is the whole point rather than
    /// tidiness. MarkdownTextBlock raises this event and then does
    /// `e.Handled = args.Handled`; when it comes back unhandled, Link.Open()
    /// runs `topLevel.Launcher.LaunchUriAsync(HRef)` itself. So leaving it
    /// unhandled meant every link opened twice, and worse, a link this method
    /// deliberately REFUSED (a relative href, file://, credentials in the
    /// URL) was then opened by LiveMarkdown anyway, straight past
    /// SafeLinkOpener. The allowlist only holds if nothing downstream gets a
    /// second go at the URL.
    /// </summary>
    private void OnArticleLinkClicked(object? sender, LiveMarkdown.Avalonia.LinkClickedEventArgs e)
    {
        e.Handled = true;

        if (!_services.Settings.OpenLinksExternally) return;

        if (!SafeLinkOpener.TryOpen(e.HRef?.ToString(), out var reason))
            StatusMessage = reason ?? "That link could not be opened.";
    }

    public void OpenOriginalArticle()
    {
        var link = SelectedItemRow?.Item.Link;
        if (!SafeLinkOpener.TryOpen(link, out var reason))
            StatusMessage = reason ?? "This article has no link to open.";
    }
}

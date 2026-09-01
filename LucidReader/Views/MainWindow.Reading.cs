using Avalonia.Threading;
using LucidReader.Core.Model;
using LucidReader.Models;
using LucidReader.Services;
using MarkdownViewer.Services;

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

    /// <summary>
    /// Incremented on every article switch. A download started for one article
    /// must not redraw the pane after the user has moved to another, so the
    /// fetch captures this and checks it before touching anything.
    /// </summary>
    private int _articleGeneration;

    /// <summary>
    /// The last item an on-select fetch was started for. Without it, an article
    /// whose download fails would be re-fetched by the re-render that reports
    /// the failure, which re-renders, which fetches again.
    /// </summary>
    private long? _autoFetchedItemId;

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

        // A half-typed tag belongs to the article it was being typed on, so an
        // article switch closes the entry rather than carrying the text across
        // to whatever is shown next, where a stray Enter would tag the wrong
        // thing. Deliberately here and not in RefreshArticleTagsAsync, which
        // also runs after every add and must leave the entry open for the next
        // one. See MainWindow.Tags.cs.
        CollapseTagEntry();

        var generation = Interlocked.Increment(ref _articleGeneration);

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

        ArticleMarkdown = BodyMarkdown(item);

        // ContentSource.Extracted and ContentSource.FeedArticle are both the
        // whole article, so neither is badged. Only ContentSource.Feed - a
        // teaser, and all there is - gets the summary badge. Reading that badge
        // off "did this come from the feed?" was the bug: alvinashcraft.com
        // sends complete posts in content:encoded, and every one of them was
        // labelled a summary while the whole article sat on screen underneath.
        (ShowOfflineBadge, OfflineBadgeText, CanFetchFullArticle) = item.OfflineState switch
        {
            OfflineState.Downloaded when item.ContentSource is ContentSource.Extracted
                                                            or ContentSource.FeedArticle =>
                (false, string.Empty, false),
            OfflineState.Downloaded =>
                (true, SummaryOnlyMessage, item.Link is not null),
            OfflineState.Failed =>
                (true, "The full article could not be downloaded. " + (item.OfflineError ?? string.Empty), true),
            OfflineState.Pending =>
                (true, FetchingMessage, false),
            _ =>
                (true, SummaryOnlyMessage, item.Link is not null)
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

        // Opening an article is a request for the article, not for whatever
        // summary the feed happened to carry. Auto-download runs in the
        // background and usually gets there first, but until it does, the pane
        // showed a badge and, in the Pending case, not even a button to press.
        // So fetch it now if we do not already have the extracted body.
        //
        // Not awaited: the title, summary and hero are on screen already and
        // this can take seconds. The generation check stops a slow fetch
        // redrawing the pane after the user has moved on.
        if (NeedsFullArticle(item) && _autoFetchedItemId != row.Id)
        {
            _autoFetchedItemId = row.Id;

            // Said before the work starts, not after it finishes. This can take
            // seconds against a slow site, and a pane sitting on a teaser with
            // no explanation is the thing the badge exists to prevent. Both
            // surfaces say it: the badge because that is where the reader is
            // already looking, the status line because the badge disappears the
            // moment the article lands.
            ShowOfflineBadge = true;
            OfflineBadgeText = FetchingMessage;
            CanFetchFullArticle = false;
            StatusMessage = FetchingMessage;

            _ = FetchOnSelectAsync(row, generation);
        }
    }

    /// <summary>
    /// What the pane says while it is getting the article and turning it into
    /// markdown. One string, used by the Pending badge, by the on-select fetch
    /// and by the manual Fetch command, so the three cannot drift apart.
    /// </summary>
    private const string FetchingMessage =
        "Getting the full article and converting it to markdown...";

    /// <summary>
    /// What the pane says when the stored copy really is only a teaser. Reached
    /// solely through ContentSource.Feed - never through FeedArticle, which is
    /// a complete post that arrived in the feed document.
    /// </summary>
    private const string SummaryOnlyMessage =
        "Showing the summary the feed provided; the full article is not stored.";

    /// <summary>
    /// Whether the stored copy is still the feed's summary rather than the
    /// article. Failed is deliberately excluded: that one has already been
    /// tried, and it keeps its Retry button rather than being retried on every
    /// click.
    /// </summary>
    private static bool NeedsFullArticle(LucidReader.Core.Model.FeedItem item) =>
        item.Link is not null
        && item.OfflineState switch
        {
            // Not fetched yet, or the background sweep has not reached it.
            OfflineState.None or OfflineState.Pending => true,

            // Fetched, and what got stored is only the feed's teaser. The
            // article itself is still somewhere else, so it is worth going for.
            //
            // ContentSource.FeedArticle is deliberately NOT in this set. That
            // is a complete post the feed handed over, already converted and
            // tidied; re-fetching its page would replace an article we have
            // with one request per open, for nothing, against publishers like
            // alvinashcraft.com who are already giving us everything.
            OfflineState.Downloaded => item.ContentSource == ContentSource.Feed,

            // Already tried and failed. Retrying on every open would hammer a
            // site that is never going to yield; the Retry button is the way
            // back.
            _ => false
        };

    private async Task FetchOnSelectAsync(ItemRow row, int generation)
    {
        try
        {
            await _services.Downloader.DownloadNowAsync(row.Id);
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _articleGeneration))
                StatusMessage = "Could not fetch the article: " + ex.Message;
            return;
        }

        // The user moved on while this was running, so the pane is showing a
        // different article and must be left alone - including the status line,
        // which by now belongs to whatever they moved to.
        if (generation != Volatile.Read(ref _articleGeneration)) return;

        // Clear before the redraw, so a failure ShowArticleAsync reports
        // survives rather than being wiped by this line.
        if (StatusMessage == FetchingMessage) StatusMessage = string.Empty;

        await ShowArticleAsync(row);
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

        StatusMessage = FetchingMessage;
        ShowOfflineBadge = true;
        OfflineBadgeText = FetchingMessage;

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
    /// <summary>
    /// The body to render, as markdown.
    ///
    /// Three sources, best first, and only the first was ever used properly:
    ///
    ///   1. ContentMarkdown, written by OfflineDownloader from an article it
    ///      fetched. Already markdown, already absolute, nothing to do.
    ///   2. ContentHtml, the full body a publisher put in content:encoded or
    ///      an Atom content element. This was NOT consulted at all, so a feed
    ///      that ships whole articles showed its short summary until something
    ///      downloaded the page a second time over the same content.
    ///   3. Summary, the teaser.
    ///
    /// Both 2 and 3 are publisher HTML and were previously assigned to
    /// ArticleMarkdown unconverted, which is to say a markdown renderer was
    /// handed a fragment of HTML. Headings did not render as headings, an
    /// anchor did not become a link, and an img rendered as nothing, so a feed
    /// whose content is a picture - APOD is exactly one img inside one a -
    /// showed an article with no image in it.
    ///
    /// The item's own link is passed as the base URI, which is what makes
    /// relative addresses work. AngleSharp resolves href and src against it
    /// during the parse, so "/apod/ap260901.html" comes out absolute. Without
    /// it those links reached SafeLinkOpener as relative strings, and that gate
    /// requires UriKind.Absolute, so clicking one did nothing at all: correct
    /// of the gate, and useless to the reader.
    /// </summary>
    private static string BodyMarkdown(FeedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ContentMarkdown)) return item.ContentMarkdown!;

        var html = !string.IsNullOrWhiteSpace(item.ContentHtml) ? item.ContentHtml : item.Summary;
        if (string.IsNullOrWhiteSpace(html)) return "This article has no content yet.";

        // Absolute only. A feed can carry a relative or malformed link, and a
        // bad base is worth less than no base.
        Uri.TryCreate(item.Link, UriKind.Absolute, out var baseUri);

        try
        {
            // FeedFragmentToMarkdown, NOT the StyloExtract pipeline that
            // OfflineDownloader uses. That pipeline finds an article inside a
            // full page and discards the chrome around it, and a feed fragment
            // has no chrome: it decides there is no article here and returns a
            // single newline. Measured against the real APOD body in six
            // wrappings, including a complete document with a title and an h1.
            // See the summary on FeedFragmentToMarkdown.
            var markdown = FeedFragmentToMarkdown.Convert(html, baseUri);

            // An empty result means the fragment held no text and no image.
            // Falling back to the raw HTML would put markup on screen, which
            // is the bug this method exists to fix, so say plainly that there
            // is nothing rather than showing something that is not an article.
            return string.IsNullOrWhiteSpace(markdown)
                ? "This article has no content yet."
                : markdown;
        }
        catch (Exception)
        {
            // Whatever a publisher wrote can be malformed in ways nothing
            // anticipated. The text alone is a poor article, and still better
            // than an empty pane or a wall of tags.
            return html!;
        }
    }

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

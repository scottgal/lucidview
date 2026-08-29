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

    // Assigned in MainWindow's constructor (MainWindow.axaml.cs), which runs
    // after this partial's fields are initialised, so the command can close
    // over FetchFullArticleAsync as a bound instance method.
    public RelayCommand FetchFullArticleCommand { get; private set; } = null!;

    public async Task ShowArticleAsync(ItemRow? row)
    {
        if (row is null)
        {
            ArticleTitle = string.Empty;
            ArticleMeta = string.Empty;
            ArticleMarkdown = string.Empty;
            ShowOfflineBadge = false;
            CanFetchFullArticle = false;
            return;
        }

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
    }

    public async Task FetchFullArticleAsync()
    {
        if (SelectedItemRow is not { } row) return;

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
    }

    /// <summary>
    /// Every link in the reading pane came from a remote feed, so it goes
    /// through the allowlist rather than straight to the platform opener.
    /// </summary>
    private void OnArticleLinkClicked(object? sender, LiveMarkdown.Avalonia.LinkClickedEventArgs e)
    {
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

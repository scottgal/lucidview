using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// The reading pane. Minimal for Task 6: a compiling no-op that a later task
/// modifies to actually render the selected article. The properties here are
/// stubs the XAML reading pane binds to (ArticleTitle, ArticleMeta,
/// ArticleMarkdown, the offline badge and FetchFullArticleCommand); a later
/// task replaces the backing fields with real values driven by the selected
/// ItemRow.
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
        set { if (_articleTitle == value) return; _articleTitle = value; Raise(); }
    }

    public string ArticleMeta
    {
        get => _articleMeta;
        set { if (_articleMeta == value) return; _articleMeta = value; Raise(); }
    }

    public string ArticleMarkdown
    {
        get => _articleMarkdown;
        set { if (_articleMarkdown == value) return; _articleMarkdown = value; Raise(); }
    }

    public bool ShowOfflineBadge
    {
        get => _showOfflineBadge;
        set { if (_showOfflineBadge == value) return; _showOfflineBadge = value; Raise(); }
    }

    public string OfflineBadgeText
    {
        get => _offlineBadgeText;
        set { if (_offlineBadgeText == value) return; _offlineBadgeText = value; Raise(); }
    }

    public bool CanFetchFullArticle
    {
        get => _canFetchFullArticle;
        set { if (_canFetchFullArticle == value) return; _canFetchFullArticle = value; Raise(); }
    }

    /// <summary>Stub: a later task wires this to OfflineDownloader.DownloadNowAsync.</summary>
    public RelayCommand FetchFullArticleCommand { get; } = new(() => { });

    // CA1822 suppressed on purpose: a later task fills this in with a real
    // body that reads _services and other instance state.
#pragma warning disable CA1822
    public Task ShowArticleAsync(ItemRow? row) => Task.CompletedTask;
#pragma warning restore CA1822
}

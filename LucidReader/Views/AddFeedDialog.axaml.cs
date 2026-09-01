using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// One row in the chooser. Public with public members because the item
/// template binds to <see cref="IsSelected"/> and <see cref="Label"/> by
/// reflection: compiled bindings are off in this project, so a rename here
/// that is not mirrored in AddFeedDialog.axaml fails silently at runtime.
///
/// IsSelected needs no change notification: the CheckBox is the only thing
/// that writes into it (a click anywhere in the row lands on the CheckBox,
/// which is stretched to fill the row) and nothing else reads it until Add is
/// clicked.
/// </summary>
public sealed class DiscoveredFeedChoice
{
    public required DiscoveredFeed Feed { get; init; }

    /// <summary>
    /// Ticked unless this feed is another format of a feed already ticked.
    /// Subscribing to what you asked for is still the common case, but "what
    /// you asked for" is one subscription per publication: a site's RSS and
    /// Atom feeds carry the same articles, and ticking both stored every one
    /// of them twice.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    public string Label => string.IsNullOrWhiteSpace(Feed.Title)
        ? Feed.FeedUrl + AlternateSuffix
        : $"{Feed.Title}  ({Feed.FeedUrl}){AlternateSuffix}";

    /// <summary>
    /// Says why an unticked row is unticked. An unexplained empty checkbox
    /// reads as a feed the app could not handle; this says it is the same
    /// articles in another format and leaves the choice with the user.
    /// </summary>
    public string AlternateSuffix => Feed.IsAlternate
        ? "  - same articles as " + (Feed.AlternateOfUrl ?? "the feed above")
        : string.Empty;
}

/// <summary>
/// One row in the shipped catalogue. Public with public members for the same
/// reason <see cref="DiscoveredFeedChoice"/> is: the item template binds to
/// these names by reflection, and with compiled bindings off a rename here that
/// is not mirrored in AddFeedDialog.axaml fails silently at runtime rather than
/// at build time.
/// </summary>
public sealed class CatalogFeedChoice
{
    public required CatalogFeed Feed { get; init; }

    /// <summary>
    /// Starts false, unlike a discovered feed. See the comment on CatalogPanel
    /// in the XAML: this is a list the user is browsing, not one they asked for.
    /// </summary>
    public bool IsSelected { get; set; }

    public string Label => Feed.Title;

    /// <summary>
    /// The category and the address on a second line, so a row says what kind
    /// of thing it is and where it actually points without the two running
    /// together into one unreadable string.
    /// </summary>
    public string Detail => $"{Feed.Category}  ·  {Feed.FeedUrl}";
}

/// <summary>
/// Turns whatever the user pasted into a set of feeds to subscribe to. The
/// window is a thin shell over named controls and
/// <see cref="AddFeedInput"/>, following SettingsDialog and
/// FeedSettingsDialog rather than a self-bound DataContext: with compiled
/// bindings off, every window-level binding is one silent typo away from a
/// dead control, and there is nothing here that a direct property assignment
/// does not say more plainly.
///
/// There is one constructor, and it takes the autodiscovery the dialog cannot
/// work without. The XAML compiler emits InitializeComponent(bool, bool) and
/// the x:Name backing fields whatever constructors this class declares, so the
/// parameterless one this class used to expose bought nothing; what it cost
/// was a second way to build the dialog, one with no autodiscovery behind it,
/// whose Find button answered "No feeds found at that address." to every
/// input.
///
/// What must never appear here is a hand-rolled InitializeComponent that only
/// calls AvaloniaXamlLoader.Load - the trap MainWindow documents and
/// FeedSettingsDialog once shipped - because it shadows the generated one and
/// leaves every field below null.
/// </summary>
public partial class AddFeedDialog : Window
{
    private readonly FeedAutodiscovery _discovery;

    /// <summary>
    /// How long a lookup is given before it is abandoned. ReaderServices'
    /// HttpClient is deliberately built with an infinite timeout, because
    /// every consumer is expected to bound its own work with a token; this
    /// dialog is one of those consumers. Without it, a host that accepts the
    /// connection and never answers leaves both buttons disabled and the
    /// status line on "Looking for feeds..." for the life of the process.
    /// </summary>
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(30);

    private CancellationTokenSource? _discoveryCts;

    /// <summary>
    /// True while a lookup is running. Find is disabled for the duration, but
    /// Enter in the address box does not go through the button, and key repeat
    /// makes that trivial to trigger: two overlapping runs each clear
    /// Discovered and then each append to it, so the list ends up with
    /// duplicate rows while the status line reports the count one of them saw.
    /// </summary>
    private bool _searching;

    /// <summary>
    /// The scraped page on offer, if the last lookup found no feed but did
    /// find something the detector read as an article list. Held here rather
    /// than pushed into Discovered on purpose: everything in that list is a
    /// feed the site declared and starts ticked, and this is a guess. See the
    /// comment on ScrapePanel in the XAML.
    /// </summary>
    private DiscoveredFeed? _scrapeOffer;

    public AddFeedDialog(FeedAutodiscovery discovery, IReadOnlyList<Folder> folders)
    {
        InitializeComponent();
        DiscoveredList.ItemsSource = Discovered;
        CatalogList.ItemsSource = Catalog;
        CatalogCreditText.Text = FeedCatalog.SourceCredit;

        // Built once, here, rather than on each press of Browse: the list is a
        // compile-time constant, and rebuilding it would throw away whatever
        // the user had already ticked the moment they went back to the address
        // box and returned.
        //
        // FeedCatalog.Allowed(), not FeedCatalog.All: a hard-coded address gets
        // no exemption from FeedUrlPolicy, which is the same rule the starter
        // feeds and everything discovery returns already follow.
        foreach (var feed in FeedCatalog.Allowed())
            Catalog.Add(new CatalogFeedChoice { Feed = feed });

        _discovery = discovery;

        var options = new List<FolderOption> { new(null, "No folder") };
        options.AddRange(folders.Select(f => new FolderOption(f.Id, f.Name)));
        FolderCombo.ItemsSource = options;
        FolderCombo.DisplayMemberBinding = new Binding(nameof(FolderOption.Name));
        FolderCombo.SelectedIndex = 0;
    }

    public ObservableCollection<DiscoveredFeedChoice> Discovered { get; } = [];

    /// <summary>
    /// The shipped catalogue, in the order <see cref="FeedCatalog.Allowed"/>
    /// puts it. Populated once in the constructor and never cleared, so ticks
    /// survive a trip back to the address box.
    /// </summary>
    public ObservableCollection<CatalogFeedChoice> Catalog { get; } = [];

    /// <summary>Empty when the dialog was cancelled or nothing was ticked.</summary>
    public IReadOnlyList<DiscoveredFeed> Selected { get; private set; } = [];

    /// <summary>Null means the top level, matching Feed.FolderId.</summary>
    public long? SelectedFolderId { get; private set; }

    /// <summary>
    /// Resolves the pasted address. Autodiscovery returns an empty list rather
    /// than throwing for an address that simply has no feeds, so an empty
    /// result means exactly what the message says: nothing feed-shaped was
    /// found. It can still throw, though, and now does so on purpose when the
    /// lookup runs past <see cref="DiscoveryTimeout"/>, which is the one thing
    /// standing between this dialog and a permanently stuck one.
    /// </summary>
    public async Task FindAsync()
    {
        if (_searching) return;

        var input = AddFeedInput.Normalise(FeedUrlBox.Text);
        if (input.Length == 0)
        {
            DiscoveryStatusText.Text = AddFeedInput.EmptyAddressMessage;
            return;
        }

        if (AddFeedInput.DescribeAddressProblem(input) is { } problem)
        {
            DiscoveryStatusText.Text = problem;
            return;
        }

        // Show the address that was actually looked up, so a bare domain
        // visibly becomes the https one the request went to.
        FeedUrlBox.Text = input;

        _searching = true;
        Discovered.Clear();
        ResetScrapeOffer();
        AddButton.IsEnabled = false;
        FindButton.IsEnabled = false;
        DiscoveryStatusText.Text = "Looking for feeds...";

        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryCts = new CancellationTokenSource(DiscoveryTimeout);

        try
        {
            var found = await _discovery.DiscoverAsync(input, _discoveryCts.Token);

            // Discovery returns at most one scraped page, and only when it
            // found no real feed at all, so the two cases never mix. Split
            // rather than assumed anyway: a scraped page reaching the chooser
            // would be pre-ticked, which is the one thing this must not do.
            _scrapeOffer = found.FirstOrDefault(f => f.IsScrapedPage) is { IsScrapedPage: true } offer
                ? offer
                : null;

            foreach (var feed in found.Where(f => !f.IsScrapedPage))
                Discovered.Add(new DiscoveredFeedChoice
                {
                    Feed = feed,
                    IsSelected = !feed.IsAlternate
                });

            if (_scrapeOffer is { Scrape: { } scrape })
            {
                ScrapeSummaryText.Text = AddFeedInput.DescribeScrapeOffer(
                    scrape.ArticleCount, scrape.FromFallback);
                ScrapeSampleText.Text = AddFeedInput.DescribeScrapeSample(scrape.SampleTitles);
                ScrapePanel.IsVisible = true;

                // The chooser has nothing in it - a scrape is only ever
                // offered when no feed was found - so it is hidden rather
                // than left as an empty card taking half the dialog above
                // the thing the user is actually being asked about.
                DiscoveredListPanel.IsVisible = false;
            }

            // Said once, in the status line, whichever branch ran. The panel
            // carries the detail; repeating the whole offer in both places
            // read as a bug rather than as emphasis.
            DiscoveryStatusText.Text = AddFeedInput.DescribeDiscovery(
                Discovered.Count, Discovered.Count(f => f.Feed.IsAlternate));
        }
        catch (OperationCanceledException)
        {
            // Either the timer fired or the dialog was closed underneath the
            // read. Both leave nothing to show, and neither is a bug worth an
            // exception message.
            DiscoveryStatusText.Text = AddFeedInput.DiscoveryTimedOutMessage;
        }
        catch (Exception ex)
        {
            DiscoveryStatusText.Text = "Could not look up that address: " + ex.Message;
        }
        finally
        {
            _searching = false;
            FindButton.IsEnabled = true;
            AddButton.IsEnabled = Discovered.Count > 0 || _scrapeOffer is not null;
        }
    }

    /// <summary>
    /// Puts the approval panel back to hidden and unticked. Called at the top
    /// of every lookup: a tick left over from a previous address would carry an
    /// approval the user gave for a different page.
    /// </summary>
    private void ResetScrapeOffer()
    {
        _scrapeOffer = null;
        ScrapePanel.IsVisible = false;

        // Looking up an address is a different question from browsing the
        // starter list, so the catalogue steps aside for the answer. Its ticks
        // are kept but stop counting: OnAdd only reads them while the panel is
        // the one on screen, so what Add does is always what the user can see.
        CatalogPanel.IsVisible = false;
        DiscoveredListPanel.IsVisible = true;
        ScrapeApproveCheck.IsChecked = false;
        ScrapeSummaryText.Text = string.Empty;
        ScrapeSampleText.Text = string.Empty;
    }

    /// <summary>
    /// Cancel and Close leave the dialog gone but the read still pending on a
    /// client with no timeout of its own, so the token has to be cancelled
    /// here or every abandoned lookup outlives the window that started it.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryCts = null;
    }

    private async void OnFind(object? sender, RoutedEventArgs e) => await FindAsync();

    /// <summary>
    /// Enter in the address box runs Find rather than the dialog's default
    /// action: there is nothing to add until a search has run, so Add is not
    /// marked IsDefault at all.
    /// </summary>
    private async void OnUrlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await FindAsync();
    }

    /// <summary>
    /// Shows the shipped catalogue in place of whatever was in row 3.
    ///
    /// Nothing is fetched and nothing is looked up: the list is compiled in,
    /// which is the whole point of it being a catalogue rather than a scrape of
    /// somebody else's page. So this is a plain handler with no work to fail
    /// and no async of its own.
    /// </summary>
    private void OnBrowseCatalog(object? sender, RoutedEventArgs e)
    {
        Discovered.Clear();
        _scrapeOffer = null;
        ScrapePanel.IsVisible = false;
        DiscoveredListPanel.IsVisible = false;
        CatalogPanel.IsVisible = true;

        DiscoveryStatusText.Text = AddFeedInput.DescribeCatalog(Catalog.Count);
        AddButton.IsEnabled = Catalog.Count > 0;
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var chosen = Discovered.Where(d => d.IsSelected).Select(d => d.Feed).ToList();

        // Catalogue rows only count while the catalogue is the panel on screen,
        // so Add always does what the user can see. They become ordinary
        // DiscoveredFeeds and go out through the same Selected list as anything
        // discovery produced, which is what makes subscribing from the
        // catalogue take exactly the same path as any other subscription -
        // duplicate check, FeedRepository.AddAsync, immediate refresh - rather
        // than a second one of its own.
        //
        // No icon travels with them, deliberately. There is none to send: a
        // catalogue entry is a title and two addresses, and guessing a favicon
        // here would duplicate work FeedIconResolver now does properly on the
        // feed's first refresh, from the feed's own image element or the site's
        // declared icon.
        if (CatalogPanel.IsVisible)
        {
            chosen.AddRange(Catalog
                .Where(c => c.IsSelected)
                .Select(c => new DiscoveredFeed(c.Feed.FeedUrl, c.Feed.Title, null)));
        }

        // The approval, and the only route by which a scraped page is ever
        // stored. Nothing pre-ticks this box and nothing else reads it.
        if (_scrapeOffer is { } offer && ScrapeApproveCheck.IsChecked == true)
            chosen.Add(offer);

        if (chosen.Count == 0 && CatalogPanel.IsVisible)
        {
            DiscoveryStatusText.Text = AddFeedInput.CatalogNothingTickedMessage;
            return;
        }

        if (chosen.Count == 0 && _scrapeOffer is not null)
        {
            DiscoveryStatusText.Text = AddFeedInput.ScrapeNotApprovedMessage;
            return;
        }

        if (chosen.Count == 0)
        {
            // Closing with nothing ticked would look like the add silently
            // failed, so the dialog stays open and says what is missing.
            DiscoveryStatusText.Text = "Tick at least one feed to add.";
            return;
        }

        Selected = chosen;
        SelectedFolderId = (FolderCombo.SelectedItem as FolderOption)?.Id;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Selected = [];
        Close();
    }

    private sealed record FolderOption(long? Id, string Name);
}

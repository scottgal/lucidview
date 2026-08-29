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

    public bool IsSelected { get; set; } = true;

    public string Label => string.IsNullOrWhiteSpace(Feed.Title)
        ? Feed.FeedUrl
        : $"{Feed.Title}  ({Feed.FeedUrl})";
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

    public AddFeedDialog(FeedAutodiscovery discovery, IReadOnlyList<Folder> folders)
    {
        InitializeComponent();
        DiscoveredList.ItemsSource = Discovered;

        _discovery = discovery;

        var options = new List<FolderOption> { new(null, "No folder") };
        options.AddRange(folders.Select(f => new FolderOption(f.Id, f.Name)));
        FolderCombo.ItemsSource = options;
        FolderCombo.DisplayMemberBinding = new Binding(nameof(FolderOption.Name));
        FolderCombo.SelectedIndex = 0;
    }

    public ObservableCollection<DiscoveredFeedChoice> Discovered { get; } = [];

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
        AddButton.IsEnabled = false;
        FindButton.IsEnabled = false;
        DiscoveryStatusText.Text = "Looking for feeds...";

        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryCts = new CancellationTokenSource(DiscoveryTimeout);

        try
        {
            var found = await _discovery.DiscoverAsync(input, _discoveryCts.Token);

            foreach (var feed in found)
                Discovered.Add(new DiscoveredFeedChoice { Feed = feed });

            DiscoveryStatusText.Text = AddFeedInput.DescribeDiscovery(found.Count);
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
            AddButton.IsEnabled = Discovered.Count > 0;
        }
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

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var chosen = Discovered.Where(d => d.IsSelected).Select(d => d.Feed).ToList();
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

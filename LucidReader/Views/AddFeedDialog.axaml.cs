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
/// IsSelected needs no change notification: the CheckBox writes into it and
/// nothing else reads it until Add is clicked.
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
/// The public parameterless constructor exists so the generated
/// InitializeComponent (from the XAML compiler, not a hand-written override)
/// populates every x:Name field. A hand-rolled InitializeComponent that only
/// calls AvaloniaXamlLoader.Load - the trap MainWindow documents and
/// FeedSettingsDialog once shipped - would leave every field below null and
/// throw on first use.
/// </summary>
public partial class AddFeedDialog : Window
{
    private readonly FeedAutodiscovery? _discovery;

    public AddFeedDialog()
    {
        InitializeComponent();
        DiscoveredList.ItemsSource = Discovered;
    }

    public AddFeedDialog(FeedAutodiscovery discovery, IReadOnlyList<Folder> folders) : this()
    {
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
    /// Resolves the pasted address. Autodiscovery never throws for a bad
    /// address (it returns an empty list), so an empty result here means
    /// exactly what the message says: nothing feed-shaped was found.
    /// </summary>
    public async Task FindAsync()
    {
        var input = AddFeedInput.Normalise(FeedUrlBox.Text);
        if (input.Length == 0)
        {
            DiscoveryStatusText.Text = AddFeedInput.EmptyAddressMessage;
            return;
        }

        // Show the address that was actually looked up, so a bare domain
        // visibly becomes the https one the request went to.
        FeedUrlBox.Text = input;

        Discovered.Clear();
        AddButton.IsEnabled = false;
        FindButton.IsEnabled = false;
        DiscoveryStatusText.Text = "Looking for feeds...";

        try
        {
            var found = _discovery is null
                ? []
                : await _discovery.DiscoverAsync(input);

            foreach (var feed in found)
                Discovered.Add(new DiscoveredFeedChoice { Feed = feed });

            DiscoveryStatusText.Text = AddFeedInput.DescribeDiscovery(found.Count);
        }
        catch (Exception ex)
        {
            DiscoveryStatusText.Text = "Could not look up that address: " + ex.Message;
        }
        finally
        {
            FindButton.IsEnabled = true;
            AddButton.IsEnabled = Discovered.Count > 0;
        }
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

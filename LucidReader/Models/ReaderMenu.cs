using Avalonia.Input;

namespace LucidReader.Models;

/// <summary>
/// Everything the application menu can ask the window to do. Named actions
/// rather than command references so the whole menu can be described, and
/// tested, without a Window and without any of the commands existing.
/// </summary>
public enum ReaderMenuAction
{
    None,

    // Application
    About,
    OpenSettings,
    Quit,

    // File
    AddFeed,
    ImportOpml,
    ExportOpml,
    ExportArticle,

    // Edit
    Undo,
    Cut,
    Copy,
    Paste,
    SelectAll,
    FindInArticle,

    // View
    LayoutThreePane,
    LayoutListAndReading,
    LayoutReadingOnly,
    CycleLayout,
    FilterAll,
    FilterUnread,
    FilterStarred,
    IncreaseFontSize,
    DecreaseFontSize,
    ResetFontSize,

    // Feed
    RefreshAll,
    RefreshFeed,
    MarkAllRead,
    FeedSettings,
    ResumeFeed,
    Unsubscribe,

    // Help
    OpenHelpSite,
    ShowKeyboardShortcuts
}

/// <summary>
/// What has to be true before an item may be clicked. Kept as a small closed
/// set rather than a predicate per item so the rule for every item can be
/// asserted in a test, and so the window has one method to call when the
/// selection changes rather than one per menu.
/// </summary>
public enum ReaderMenuEnablement
{
    Always,

    /// <summary>A feed, not a folder and not one of the smart rows.</summary>
    RequiresFeed,

    /// <summary>A feed the Core layer auto-paused. Resume means nothing otherwise.</summary>
    RequiresPausedFeed,

    /// <summary>An article selected in the item list.</summary>
    RequiresArticle
}

/// <summary>
/// One row of a menu. A separator carries no action and no header.
/// </summary>
public sealed record ReaderMenuItem
{
    public required string Header { get; init; }
    public ReaderMenuAction Action { get; init; } = ReaderMenuAction.None;
    public ReaderMenuEnablement Enablement { get; init; } = ReaderMenuEnablement.Always;
    public bool IsSeparator { get; init; }

    /// <summary>
    /// The key of this item's accelerator, or null for no accelerator.
    ///
    /// Only ever set for gestures that carry the platform command modifier.
    /// That restriction is the whole point and is not a matter of taste: on
    /// macOS a NativeMenuItem's gesture becomes an AppKit key equivalent,
    /// which is matched before the focused field editor sees the keystroke,
    /// exactly the way Window.KeyBindings used to be matched before the
    /// focused TextBox. The reader already shipped that bug once - typing
    /// "compositor" into the search box launched a browser and wrote read and
    /// starred state - and a bare-letter menu accelerator would bring it
    /// straight back. The bare-letter gestures (J K N P M S R O T /) stay
    /// where they are, in ReaderShortcuts, behind its text-entry guard, and
    /// are deliberately not advertised here.
    /// </summary>
    public Key? GestureKey { get; init; }

    /// <summary>
    /// Extra modifiers on top of the command modifier. Shift only, in
    /// practice, and only where the gesture would otherwise collide.
    /// </summary>
    public KeyModifiers ExtraModifiers { get; init; } = KeyModifiers.None;

    public bool HasGesture => GestureKey is not null;

    public static ReaderMenuItem Separator() => new() { Header = string.Empty, IsSeparator = true };
}

public sealed record ReaderMenuSection(string Header, IReadOnlyList<ReaderMenuItem> Items);

/// <summary>
/// The shape of mylo's application menu, in one place, free of Avalonia's
/// NativeMenu and Menu types so it can be asserted against in a unit test.
/// LucidReader.Views.MainWindow.Menu.cs turns this into whichever of the two
/// the running platform wants.
///
/// The menus exist because the alternative was a toolbar that had grown to
/// seven buttons, two of which (Import OPML, Export OPML) are operations a
/// reader performs perhaps twice in the lifetime of a profile. Those two are
/// now under File and nowhere else. What stays on the toolbar is what a
/// reader reaches for while reading.
/// </summary>
public static class ReaderMenu
{
    /// <summary>
    /// The application menu, whose name is the product's. On macOS the first
    /// menu is drawn by AppKit from the application name rather than from
    /// this header, which is why Program.cs sets Application.Name; the header
    /// is still what the in-window menu on Windows and Linux shows.
    /// </summary>
    public const string AppMenuHeader = "mylo";

    public static IReadOnlyList<ReaderMenuSection> Build()
    {
        var app = new ReaderMenuSection(AppMenuHeader,
        [
            new ReaderMenuItem { Header = "About mylo", Action = ReaderMenuAction.About },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem
            {
                Header = "Settings...",
                Action = ReaderMenuAction.OpenSettings,
                GestureKey = Key.OemComma
            },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem { Header = "Quit mylo", Action = ReaderMenuAction.Quit, GestureKey = Key.Q }
        ]);

        var file = new ReaderMenuSection("File",
        [
            new ReaderMenuItem { Header = "Add Feed...", Action = ReaderMenuAction.AddFeed, GestureKey = Key.N },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem { Header = "Import OPML...", Action = ReaderMenuAction.ImportOpml },
            new ReaderMenuItem { Header = "Export OPML...", Action = ReaderMenuAction.ExportOpml },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem
            {
                Header = "Export Article...",
                Action = ReaderMenuAction.ExportArticle,
                Enablement = ReaderMenuEnablement.RequiresArticle,
                GestureKey = Key.S
            }
        ]);

        // Undo, Cut, Copy, Paste and Select All act on whichever text box has
        // focus (the search box, or a field in an open dialog). They are not
        // gated on anything: a menu that greys out Copy because the window
        // cannot see a selection from here would be wrong more often than it
        // was right, and each one is a no-op when there is nothing to act on.
        var edit = new ReaderMenuSection("Edit",
        [
            new ReaderMenuItem { Header = "Undo", Action = ReaderMenuAction.Undo, GestureKey = Key.Z },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem { Header = "Cut", Action = ReaderMenuAction.Cut, GestureKey = Key.X },
            new ReaderMenuItem { Header = "Copy", Action = ReaderMenuAction.Copy, GestureKey = Key.C },
            new ReaderMenuItem { Header = "Paste", Action = ReaderMenuAction.Paste, GestureKey = Key.V },
            new ReaderMenuItem { Header = "Select All", Action = ReaderMenuAction.SelectAll, GestureKey = Key.A },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem
            {
                Header = "Find in Article",
                Action = ReaderMenuAction.FindInArticle,
                GestureKey = Key.F
            }
        ]);

        var view = new ReaderMenuSection("View",
        [
            new ReaderMenuItem { Header = "Sidebar, List and Article", Action = ReaderMenuAction.LayoutThreePane },
            new ReaderMenuItem { Header = "List and Article", Action = ReaderMenuAction.LayoutListAndReading },
            new ReaderMenuItem { Header = "Article Only", Action = ReaderMenuAction.LayoutReadingOnly },
            new ReaderMenuItem { Header = "Cycle Layout", Action = ReaderMenuAction.CycleLayout },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem { Header = "All Articles", Action = ReaderMenuAction.FilterAll },
            new ReaderMenuItem { Header = "Unread Only", Action = ReaderMenuAction.FilterUnread },
            new ReaderMenuItem { Header = "Starred Only", Action = ReaderMenuAction.FilterStarred },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem
            {
                Header = "Bigger Text",
                Action = ReaderMenuAction.IncreaseFontSize,
                GestureKey = Key.OemPlus
            },
            new ReaderMenuItem
            {
                Header = "Smaller Text",
                Action = ReaderMenuAction.DecreaseFontSize,
                GestureKey = Key.OemMinus
            },
            new ReaderMenuItem
            {
                Header = "Default Text Size",
                Action = ReaderMenuAction.ResetFontSize,
                GestureKey = Key.D0
            }
        ]);

        var feed = new ReaderMenuSection("Feed",
        [
            new ReaderMenuItem { Header = "Refresh All", Action = ReaderMenuAction.RefreshAll },
            new ReaderMenuItem
            {
                Header = "Refresh Feed",
                Action = ReaderMenuAction.RefreshFeed,
                Enablement = ReaderMenuEnablement.RequiresFeed
            },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem
            {
                Header = "Mark All Read",
                Action = ReaderMenuAction.MarkAllRead,
                Enablement = ReaderMenuEnablement.RequiresFeed
            },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem
            {
                Header = "Feed Settings...",
                Action = ReaderMenuAction.FeedSettings,
                Enablement = ReaderMenuEnablement.RequiresFeed
            },
            new ReaderMenuItem
            {
                Header = "Resume Feed",
                Action = ReaderMenuAction.ResumeFeed,
                Enablement = ReaderMenuEnablement.RequiresPausedFeed
            },
            ReaderMenuItem.Separator(),
            new ReaderMenuItem
            {
                Header = "Unsubscribe...",
                Action = ReaderMenuAction.Unsubscribe,
                Enablement = ReaderMenuEnablement.RequiresFeed
            }
        ]);

        var help = new ReaderMenuSection("Help",
        [
            new ReaderMenuItem { Header = "Keyboard Shortcuts", Action = ReaderMenuAction.ShowKeyboardShortcuts },
            new ReaderMenuItem { Header = "mylo on the Web", Action = ReaderMenuAction.OpenHelpSite }
        ]);

        return [app, file, edit, view, feed, help];
    }

    /// <summary>
    /// Whether an item may be clicked, given what the window currently has
    /// selected. One function for every item in every menu, so the answer
    /// cannot drift between menus or between platforms.
    /// </summary>
    public static bool IsEnabled(
        ReaderMenuEnablement enablement,
        bool isFeedSelected,
        bool isPausedFeedSelected,
        bool isArticleSelected) => enablement switch
    {
        ReaderMenuEnablement.RequiresFeed => isFeedSelected,
        ReaderMenuEnablement.RequiresPausedFeed => isPausedFeedSelected,
        ReaderMenuEnablement.RequiresArticle => isArticleSelected,
        _ => true
    };

    /// <summary>
    /// What the Help menu prints. The bare-letter gestures are the ones no
    /// menu accelerator may carry (see ReaderMenuItem.GestureKey), so a list
    /// the user can read is the only place they are written down inside the
    /// app.
    /// </summary>
    public static string KeyboardShortcutSummary =>
        "J next, K previous, N next unread, P previous unread, " +
        "M read, S star, R refresh feed, Shift+R refresh all, " +
        "O open original, T tags, / search.";
}

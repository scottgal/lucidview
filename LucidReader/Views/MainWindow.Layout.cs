using Avalonia.Controls;
using Avalonia.Interactivity;
using LucidReader.Core.Model;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// The three-pane collapse, driven by the one layout button at the left end of
/// the toolbar (where macOS puts the sidebar toggle).
///
/// Each click removes the leftmost pane still showing, so the panes go in the
/// order the eye reads them: sidebar, then article list, then back to all
/// three. The reading pane is never collapsed, because there would then be
/// nowhere to draw the article the user came for.
///
/// The decision itself is in LucidReader.Models.ReaderLayout, which knows
/// nothing about Avalonia and is unit-tested; everything here is the part that
/// can only be done against a live Grid.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// The width the two GridSplitter columns get back when their pane
    /// reappears. It matches the literal in MainWindow.axaml; a collapsed
    /// splitter goes to zero rather than staying at 4, so a hidden pane leaves
    /// no dead 4px strip behind.
    /// </summary>
    private const double SplitterWidth = 4;

    /// <summary>
    /// True where the ordinary system title bar is the right chrome, which is
    /// everywhere except macOS. See ConfigurePlatformChrome for why.
    ///
    /// In a Debug build only, MYLO_FORCE_SYSTEM_CHROME=1 forces it true on
    /// macOS as well, and it exists for the same reason MYLO_FORCE_WINDOW_MENU
    /// does (MainWindow.Menu.cs): the Windows and Linux branch of this file is
    /// otherwise dead code on the machine mylo is developed on, and dead code
    /// is where a silent failure hides. The specific one worth catching is
    /// FindControl returning null, which would leave the 80px macOS gutter in
    /// place on Windows with nothing thrown and nothing logged. With this set
    /// the UI test harness can drive the whole window through that branch and
    /// see the toolbar laid out against a system title bar.
    ///
    /// It does not make macOS look like Windows and is not evidence about
    /// Windows. It only proves this code path runs and lays out.
    ///
    /// Debug-only, like MYLO_DATA_DIR and the harness itself: the Release
    /// build always follows the platform.
    /// </summary>
    private static bool UseSystemTitleBar
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("MYLO_FORCE_SYSTEM_CHROME") == "1") return true;
#endif
            return !OperatingSystem.IsMacOS();
        }
    }

    /// <summary>
    /// Window chrome, which is the one part of this layout that cannot be one
    /// shape on every platform.
    ///
    /// macOS keeps what MainWindow.axaml declares: the client area is extended
    /// under the title bar so the toolbar and the traffic lights share a band,
    /// which is how Mail, Safari and every other current Mac app look, and the
    /// toolbar's 80px left margin is the space those three buttons need.
    ///
    /// Windows and Linux get the ordinary system title bar instead, and the
    /// margin drops to an even 12. Two reasons, and neither is taste:
    ///
    /// 1. The system buttons are on the RIGHT on both platforms. With the
    ///    client area extended and PreferSystemChrome, Windows draws minimise,
    ///    maximise and close over the top right of the client area, which is
    ///    exactly where SettingsButton sits with 12px of margin behind it. The
    ///    button was unreachable, and the 80 on the left was an empty gutter
    ///    at the same time: wrong at both ends of the same row.
    /// 2. Reserving a right-hand gutter instead would only move the problem.
    ///    The caption-button strip is not a fixed width: it changes with the
    ///    Windows version, with display scaling, and again when the window is
    ///    maximised, and on Linux it is whatever the window manager decides,
    ///    if the WM honours client-side decorations at all. There is no number
    ///    to hard-code. The system title bar has none of that: the OS owns the
    ///    band, sizes it, and draws the buttons inside it.
    ///
    /// The in-window Menu at Grid.Row 0 already exists on these platforms
    /// (see MainWindow.Menu.cs), so the window still reads top-down as title
    /// bar, menu, toolbar, which is the native Windows and Linux order.
    ///
    /// Called from the constructor, straight after InitializeComponent, so it
    /// runs before the window is ever shown. Setting
    /// ExtendClientAreaToDecorationsHint after a window is on screen is not
    /// reliably applied on any backend.
    /// </summary>
    private void ConfigurePlatformChrome()
    {
        if (!UseSystemTitleBar) return;

        // ExtendClientAreaTitleBarHeightHint is deliberately not touched. It is
        // already -1 from the XAML and it means nothing once the extension is
        // off, so setting it here would only read as if it did.
        ExtendClientAreaToDecorationsHint = false;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.Default;

        if (this.FindControl<Grid>("ToolbarGrid") is { } toolbar)
            toolbar.Margin = new Avalonia.Thickness(12, 10, 12, 10);
    }

    private ReaderLayoutMode _layoutMode = ReaderLayoutMode.ThreePane;

    /// <summary>
    /// The widths the sidebar and item list had when they were last visible,
    /// so a collapse followed by an expand returns them to where the user
    /// dragged the splitters rather than to the XAML defaults. Seeded with
    /// those defaults for the case where a pane is collapsed at startup and so
    /// has never been measured.
    /// </summary>
    private GridLength _sidebarWidth = new(260);
    private GridLength _itemListWidth = new(340);

    /// <summary>
    /// Bound by the toolbar's PaneLayoutIcon. A plain property with a Raise,
    /// not an AvaloniaProperty: compiled bindings are off in this project, so
    /// this name has to exist and has to match the binding exactly or the icon
    /// silently never updates.
    /// </summary>
    public ReaderLayoutMode LayoutMode
    {
        get => _layoutMode;
        private set
        {
            if (_layoutMode == value) return;
            _layoutMode = value;
            Raise();
            Raise(nameof(LayoutButtonTip));
        }
    }

    /// <summary>
    /// The button's tooltip, naming what the next click does rather than what
    /// the current state is.
    /// </summary>
    public string LayoutButtonTip => ReaderLayout.DescribeNext(_layoutMode);

    private void OnLayoutButtonClicked(object? sender, RoutedEventArgs e) =>
        SetLayoutMode(ReaderLayout.Next(_layoutMode));

    /// <summary>
    /// Puts the window into a named mode. The toolbar button reaches this
    /// with the next mode in the cycle; the View menu reaches it with a mode
    /// the user picked outright. One method rather than two so both routes
    /// apply, describe and persist identically.
    /// </summary>
    internal void SetLayoutMode(ReaderLayoutMode next)
    {
        LayoutMode = next;
        ApplyLayoutMode(next);
        StatusMessage = ReaderLayout.Describe(next);

        // Fire and forget, like the other settings writes from this window:
        // the layout has already changed on screen and a slow disk must not
        // hold the click. The SettingsChanged this raises comes back through
        // ApplySettings, which re-applies the same mode; ApplyLayoutMode is
        // idempotent for exactly that reason.
        _ = PersistLayoutModeAsync(next);
    }

    private async Task PersistLayoutModeAsync(ReaderLayoutMode mode)
    {
        try
        {
            await _services.UpdateSettingsAsync(
                _services.Settings with { LayoutMode = ReaderLayout.ToStoredValue(mode) });
        }
        catch (Exception ex)
        {
            // Losing the saved layout is not worth a crash on a toolbar click.
            StatusMessage = "The layout could not be saved: " + ex.Message;
        }
    }

    /// <summary>
    /// Puts the panes where the mode says they go. Collapsing means three
    /// things at once, and missing any one of them leaves a visible artefact:
    /// the column has to go to zero width (hiding the content alone leaves the
    /// column's 260px hole behind), the content has to be hidden (a zero-width
    /// column still measures and still takes focus), and the GridSplitter next
    /// to it has to go too, or the user is left with a draggable handle
    /// attached to nothing.
    /// </summary>
    private void ApplyLayoutMode(ReaderLayoutMode mode)
    {
        if (PaneGrid is null) return;

        var sidebarColumn = PaneGrid.ColumnDefinitions[0];
        var sidebarSplitterColumn = PaneGrid.ColumnDefinitions[1];
        var itemListColumn = PaneGrid.ColumnDefinitions[2];
        var itemListSplitterColumn = PaneGrid.ColumnDefinitions[3];

        // Read the current widths before anything is zeroed, so a splitter the
        // user dragged is what comes back. Guarded on being a real width: a
        // column already collapsed reads as 0 and must not overwrite the
        // remembered value.
        if (sidebarColumn.Width is { IsAbsolute: true, Value: > 0 })
            _sidebarWidth = sidebarColumn.Width;
        if (itemListColumn.Width is { IsAbsolute: true, Value: > 0 })
            _itemListWidth = itemListColumn.Width;

        var showSidebar = ReaderLayout.ShowsSidebar(mode);
        var showItemList = ReaderLayout.ShowsItemList(mode);

        sidebarColumn.Width = showSidebar ? _sidebarWidth : new GridLength(0);
        sidebarSplitterColumn.Width = new GridLength(showSidebar ? SplitterWidth : 0);
        itemListColumn.Width = showItemList ? _itemListWidth : new GridLength(0);
        itemListSplitterColumn.Width = new GridLength(showItemList ? SplitterWidth : 0);

        SidebarPane.IsVisible = showSidebar;
        SidebarSplitter.IsVisible = showSidebar;
        ItemListPane.IsVisible = showItemList;
        ItemListSplitter.IsVisible = showItemList;

        // The reading column is centred at a width clamped to the pane it is
        // in, and that pane just got wider or narrower. The Bounds watcher in
        // MainWindow.ReadingLayout.cs picks this up too, but only after the
        // next layout pass; re-resolving here means the column is never a
        // frame behind the collapse.
        ApplyReadingColumnWidth();
    }

    /// <summary>
    /// Restores the saved mode. Called from ApplySettings, so it runs at
    /// startup and again whenever settings change from anywhere else.
    /// </summary>
    private void RestoreLayoutMode(ReaderSettings settings)
    {
        var mode = ReaderLayout.Parse(settings.LayoutMode);
        LayoutMode = mode;
        ApplyLayoutMode(mode);
    }

    /// <summary>
    /// Zoom on a double click in the title bar band.
    ///
    /// ExtendClientAreaToDecorationsHint means the toolbar is painted over the
    /// area the OS would otherwise own, so the platform never receives the
    /// double click that zooms a macOS window and the gesture just does
    /// nothing. Restoring it here keeps the window behaving the way every
    /// other Mac app does.
    ///
    /// Taps that landed on a control are ignored: double clicking Refresh all,
    /// or selecting a word in the search box, must not resize the window.
    ///
    /// macOS only. On Windows and Linux ConfigurePlatformChrome leaves the
    /// system title bar in place, so the OS already gets the double click on
    /// the band it owns and applies that platform's own rule for it (maximise,
    /// or whatever the Linux WM is configured to do). Doing it here as well
    /// would maximise the window on a double click in the middle of the
    /// toolbar, which is nothing a user asked for.
    /// </summary>
    private void OnTitleBarDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (UseSystemTitleBar) return;
        if (IsInteractive(e.Source as Avalonia.Visual)) return;

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        e.Handled = true;
    }

    /// <summary>
    /// Drag the window by its toolbar.
    ///
    /// ExtendClientAreaToDecorationsHint means this toolbar is painted over the
    /// band the OS would normally own, and the OS only moves a window when it
    /// owns the pixels under the pointer. So without this the window could not
    /// be moved at all except by its edges, which is not how any Mac app
    /// behaves.
    ///
    /// Left button only, and not on a control: dragging inside the search box
    /// selects text, and pressing a toolbar button must press it rather than
    /// pick the window up. ClickCount is checked so the second press of a
    /// double click still reaches OnTitleBarDoubleTapped to zoom, instead of
    /// being swallowed by a move that never moves anywhere.
    ///
    /// macOS only, for the same reason as the double tap above: on Windows and
    /// Linux the real title bar is still there and is what a user grabs to
    /// move the window. Keeping a second drag surface in the toolbar would
    /// mean a press on any gap between two buttons picks the whole window up,
    /// which no Windows or Linux app does.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (UseSystemTitleBar) return;
        if (e.ClickCount != 1) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsInteractive(e.Source as Avalonia.Visual)) return;

        BeginMoveDrag(e);
    }

    /// <summary>
    /// Walks up from the tapped element to the toolbar, looking for something
    /// the user was actually aiming at. Checking only e.Source is not enough:
    /// a click on a Button lands on the TextBlock inside its template, not on
    /// the Button itself.
    /// </summary>
    private static bool IsInteractive(Avalonia.Visual? source)
    {
        for (var v = source; v is not null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
        {
            if (v is Avalonia.Controls.Primitives.TemplatedControl
                and (Avalonia.Controls.Button
                    or Avalonia.Controls.Primitives.ToggleButton
                    or Avalonia.Controls.TextBox
                    or Avalonia.Controls.Menu
                    or Avalonia.Controls.MenuItem))
                return true;
        }

        return false;
    }
}

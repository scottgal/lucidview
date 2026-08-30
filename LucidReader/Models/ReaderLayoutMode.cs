namespace LucidReader.Models;

/// <summary>
/// Which of the three panes are on screen. The order of the members is the
/// order the toolbar button walks through them, and it is not arbitrary: each
/// step removes the leftmost pane still showing, so the panes disappear left
/// to right and the reading pane, the thing the user is actually looking at,
/// is the last one standing and never disappears at all.
/// </summary>
public enum ReaderLayoutMode
{
    /// <summary>Sidebar, item list and reading pane.</summary>
    ThreePane,

    /// <summary>Sidebar collapsed. Item list and reading pane.</summary>
    ListAndReading,

    /// <summary>Sidebar and item list collapsed. Reading pane only.</summary>
    ReadingOnly
}

/// <summary>
/// The pane-collapse state machine, and the words the toolbar button says
/// about it.
///
/// Plain and Avalonia-free for the same reason <see cref="ReadingColumnMetrics"/>
/// and <see cref="SettingsDraft"/> are: a Window cannot be constructed in a
/// unit test in this repo, so the part that is worth testing (which panes a
/// mode shows, what the next click does, what a stored string round-trips to)
/// has to live outside the view.
///
/// Collapsing is a single button rather than one per column on purpose. Three
/// separate collapse controls would allow eight combinations, six of which are
/// either the same thing twice or nonsense (an item list with no reading pane
/// still has to put the article somewhere). One button cycling through three
/// named modes is what macOS Mail and Finder actually do, and it means the
/// icon can show the whole state rather than a third of it.
/// </summary>
public static class ReaderLayout
{
    /// <summary>The mode the layout button moves to from <paramref name="mode"/>.</summary>
    public static ReaderLayoutMode Next(ReaderLayoutMode mode) => mode switch
    {
        ReaderLayoutMode.ThreePane => ReaderLayoutMode.ListAndReading,
        ReaderLayoutMode.ListAndReading => ReaderLayoutMode.ReadingOnly,
        _ => ReaderLayoutMode.ThreePane
    };

    public static bool ShowsSidebar(ReaderLayoutMode mode) => mode == ReaderLayoutMode.ThreePane;

    public static bool ShowsItemList(ReaderLayoutMode mode) => mode != ReaderLayoutMode.ReadingOnly;

    /// <summary>
    /// Always true. It is stated as a function rather than left implicit
    /// because it is the invariant the whole cycle depends on: there is no
    /// mode in which the article has nowhere to be drawn.
    /// </summary>
    public static bool ShowsReadingPane(ReaderLayoutMode mode) => true;

    /// <summary>
    /// How many panes the mode shows. Used by the icon, which draws one slot
    /// per visible pane.
    /// </summary>
    public static int VisiblePaneCount(ReaderLayoutMode mode) =>
        (ShowsSidebar(mode) ? 1 : 0) + (ShowsItemList(mode) ? 1 : 0) + 1;

    /// <summary>
    /// What the next click will do, for the button's tooltip. Phrased as the
    /// action rather than the destination state, because a tooltip on a
    /// button is read as a promise about the click.
    /// </summary>
    public static string DescribeNext(ReaderLayoutMode mode) => mode switch
    {
        ReaderLayoutMode.ThreePane => "Hide the sidebar",
        ReaderLayoutMode.ListAndReading => "Hide the article list",
        _ => "Show the sidebar and article list"
    };

    /// <summary>The mode's own name, for the status bar.</summary>
    public static string Describe(ReaderLayoutMode mode) => mode switch
    {
        ReaderLayoutMode.ThreePane => "Sidebar, article list and reading pane",
        ReaderLayoutMode.ListAndReading => "Article list and reading pane",
        _ => "Reading pane only"
    };

    /// <summary>
    /// Reads the value stored in ReaderSettings.LayoutMode. Stored as a
    /// string, the way Theme is, so a settings file written by a build that
    /// knows an extra mode does not fail to parse on one that does not:
    /// anything unrecognised, missing or empty falls back to the full layout,
    /// which is the only mode that can never leave the user unable to reach
    /// their feeds.
    /// </summary>
    public static ReaderLayoutMode Parse(string? stored) =>
        Enum.TryParse<ReaderLayoutMode>(stored, ignoreCase: true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : ReaderLayoutMode.ThreePane;

    public static string ToStoredValue(ReaderLayoutMode mode) => mode.ToString();
}

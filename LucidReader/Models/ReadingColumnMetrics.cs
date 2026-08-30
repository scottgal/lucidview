namespace LucidReader.Models;

/// <summary>
/// Works out how wide the reading column should actually be, given the width
/// the user asked for and the width the reading pane currently has.
///
/// Plain and Avalonia-free on purpose, the same way SettingsDraft is: a Window
/// cannot be constructed in a unit test in this repo, so the only way to test
/// the arithmetic that decides the column - the part that is actually easy to
/// get wrong - is to keep it out of the view entirely.
///
/// Two things here are deliberate and load-bearing.
///
/// First, the caller sets the result as an explicit Width on the column, not
/// as a MaxWidth. This is lucidVIEW's lesson (MainWindow.Ruler.cs): MaxWidth is
/// only a cap, so a centred container shrinks to its content's natural width
/// instead, and the symmetric margins collapse the moment the article's own
/// content happens to be narrow.
///
/// Second, <see cref="Resolve"/> is a pure function and nothing here writes
/// back to ReaderSettings. A pane too narrow for the saved width clamps the
/// column for as long as it stays narrow; widen it again and the user's
/// preferred width comes straight back. Persisting the clamp would silently
/// overwrite a setting the user never changed.
/// </summary>
public static class ReadingColumnMetrics
{
    /// <summary>Narrower than this and the column stops being readable at all.</summary>
    public const double MinimumWidth = 320;

    /// <summary>Matches the Maximum on the settings dialog's column-width box.</summary>
    public const double MaximumWidth = 2000;

    /// <summary>
    /// Breathing room kept on each side even when the pane is too narrow to
    /// honour the preferred width, so text never runs into the pane edge.
    /// </summary>
    public const double SideGutter = 24;

    /// <summary>
    /// Kept clear on the right for the reading pane's vertical scrollbar. An
    /// overlay scrollbar drawn on top of the last line of every paragraph is
    /// the sort of detail that looks like a rendering bug.
    /// </summary>
    public const double ScrollBarReserve = 14;

    /// <summary>
    /// The width to give the reading column, in device-independent pixels.
    /// </summary>
    /// <param name="preferredWidth">ReaderSettings.ColumnWidth, untouched by this call.</param>
    /// <param name="paneWidth">
    /// The reading pane's current width. The three-pane split means this
    /// changes when either splitter moves, not only when the window resizes,
    /// which is why it is a parameter rather than something derived from the
    /// window. Zero or NaN (a pane that has not been measured yet) falls back
    /// to the preferred width clamped to its own bounds.
    /// </param>
    public static double Resolve(double preferredWidth, double paneWidth)
    {
        var preferred = double.IsNaN(preferredWidth) || preferredWidth <= 0
            ? MinimumWidth
            : Math.Clamp(preferredWidth, MinimumWidth, MaximumWidth);

        if (double.IsNaN(paneWidth) || paneWidth <= 0) return preferred;

        // Never below MinimumWidth even when the pane cannot fit it: a column
        // narrower than that is unreadable, and letting the ScrollViewer
        // scroll horizontally is the better failure.
        var fits = Math.Max(MinimumWidth, paneWidth - (SideGutter * 2) - ScrollBarReserve);

        return Math.Min(preferred, fits);
    }
}

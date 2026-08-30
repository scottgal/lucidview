namespace LucidReader.Models;

/// <summary>
/// What the View menu's three text-size items do to
/// ReaderSettings.FontSize.
///
/// Plain and Avalonia-free, like ReaderLayout and ReadingColumnMetrics beside
/// it, because the part worth checking is the arithmetic and the bounds, and
/// a Window cannot be constructed in a test in this repo.
///
/// The bounds are not decoration. FontSize is written straight into the
/// reading pane's typography, so an unbounded step run enough times gives a
/// column of text either too small to read or so large that a paragraph does
/// not fit the pane, and both states are reached by holding a key down. The
/// step is a whole point rather than a ratio: the range is narrow enough that
/// a proportional step would be indistinguishable from a fixed one at the
/// bottom and jump three points at the top.
/// </summary>
public static class ReadingFontScale
{
    public const double Minimum = 10;
    public const double Maximum = 28;
    public const double Step = 1;

    /// <summary>The value a new profile gets, and the one Default Text Size returns to.</summary>
    public const double Default = 15;

    /// <summary>
    /// One step up. Returns the clamped result, which is the same value again
    /// once the ceiling is reached; the caller is expected to compare and skip
    /// the write rather than saving a setting that did not change.
    /// </summary>
    public static double Increase(double current) => Clamp(Round(current) + Step);

    public static double Decrease(double current) => Clamp(Round(current) - Step);

    public static double Reset() => Default;

    /// <summary>
    /// Holds a size inside the readable range, and repairs one that is not a
    /// number at all: a hand-edited settings.json can contain 0, a negative
    /// or a NaN, and every one of those reaches the reading pane as a font
    /// size otherwise.
    /// </summary>
    public static double Clamp(double value)
    {
        if (double.IsNaN(value)) return Default;
        return Math.Clamp(value, Minimum, Maximum);
    }

    /// <summary>
    /// Steps land on whole points even when the stored value did not, so
    /// pressing the shortcut twice from 15.4 gives 16 and 17 rather than 16.4
    /// and 17.4.
    /// </summary>
    private static double Round(double value) => double.IsNaN(value) ? Default : Math.Round(value);
}

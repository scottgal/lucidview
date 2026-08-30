using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LucidReader.Models;

namespace LucidReader.Views.Controls;

/// <summary>
/// The toolbar's layout glyph: a small window outline divided into the panes
/// that are actually on screen right now, with the pane the next click will
/// collapse shaded in.
///
/// Drawn here rather than set as a glyph from a font, for three reasons. The
/// obvious candidate, SF Symbols' sidebar.left, exists only on macOS, and this
/// app is published self-contained for Windows and Linux as well, where the
/// codepoint would render as a missing-glyph box. Bundling an icon font to
/// avoid that costs more than these forty lines of geometry. And neither a
/// font glyph nor a static asset can do the thing the button is for: the icon
/// is a picture of the current layout, so its interior has to change with the
/// mode, and a fixed glyph would leave the user guessing what the click did.
///
/// It is a Control with its own Render rather than a Path per state because a
/// 1px stroke is only crisp if it sits on a half-pixel, and doing that means
/// knowing the box after layout has rounded it. Everything below is snapped to
/// whole pixels first and then offset by half of the stroke.
/// </summary>
public sealed class PaneLayoutIcon : Control
{
    /// <summary>
    /// The drawing size, in device-independent pixels. Wider than tall in
    /// roughly a window's proportion, so it reads as a window rather than as
    /// a generic box, and both even numbers so the centring below lands on
    /// whole pixels.
    /// </summary>
    private const double IconWidth = 18;
    private const double IconHeight = 14;

    private const double StrokeThickness = 1;
    private const double CornerRadius = 2.5;

    /// <summary>
    /// Where the pane boundaries sit, measured from the left of the icon.
    ///
    /// These are the real layout's proportions, rounded to whole pixels: the
    /// window is 260 sidebar, 340 list and the rest reading, so at 18px wide
    /// the boundaries fall near 6 and 11. With the sidebar collapsed the list
    /// becomes the leftmost pane and takes 340 of the remaining 1020, so its
    /// boundary moves to 7. The point of using the real proportions is that
    /// the icon is a scale drawing of the window, not a diagram of it.
    /// </summary>
    private const double ThreePaneFirstDivider = 6;
    private const double ThreePaneSecondDivider = 11;
    private const double TwoPaneDivider = 7;

    public static readonly StyledProperty<ReaderLayoutMode> ModeProperty =
        AvaloniaProperty.Register<PaneLayoutIcon, ReaderLayoutMode>(nameof(Mode));

    /// <summary>
    /// The window outline and the pane dividers. Named StrokeBrush rather than
    /// Foreground because Control has no Foreground to inherit and a property
    /// called Foreground here would read as though it did.
    /// </summary>
    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<PaneLayoutIcon, IBrush?>(nameof(StrokeBrush));

    /// <summary>Fill for the pane the next click collapses.</summary>
    public static readonly StyledProperty<IBrush?> PaneBrushProperty =
        AvaloniaProperty.Register<PaneLayoutIcon, IBrush?>(nameof(PaneBrush));

    static PaneLayoutIcon()
    {
        AffectsRender<PaneLayoutIcon>(ModeProperty, StrokeBrushProperty, PaneBrushProperty);
    }

    public ReaderLayoutMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public IBrush? StrokeBrush
    {
        get => GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public IBrush? PaneBrush
    {
        get => GetValue(PaneBrushProperty);
        set => SetValue(PaneBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(IconWidth, IconHeight);

    public override void Render(DrawingContext context)
    {
        if (StrokeBrush is not { } strokeBrush) return;

        // Snapped to whole pixels before the half-pixel stroke offset is
        // added, so the outline is one solid pixel rather than two grey ones.
        var left = Math.Round((Bounds.Width - IconWidth) / 2);
        var top = Math.Round((Bounds.Height - IconHeight) / 2);
        var half = StrokeThickness / 2;

        var outline = new Rect(left + half, top + half,
            IconWidth - StrokeThickness, IconHeight - StrokeThickness);

        var pen = new Pen(strokeBrush, StrokeThickness);

        // Fill first, then dividers, then the outline, so the outline is drawn
        // over both and stays a clean unbroken rectangle.
        var dividers = DividersFor(Mode);

        if (dividers.Length > 0 && PaneBrush is { } paneBrush)
        {
            // The shaded pane is the leftmost one still showing, which is
            // exactly the one the next click removes. Inset by the stroke so
            // the fill sits inside the outline instead of under it.
            var fill = new Rect(
                outline.Left + half,
                outline.Top + half,
                dividers[0] - half - half,
                outline.Height - StrokeThickness);

            if (fill is { Width: > 0, Height: > 0 })
                context.DrawRectangle(paneBrush, null, fill);
        }

        foreach (var divider in dividers)
        {
            var x = Math.Round(left + divider) + half;
            context.DrawLine(pen, new Point(x, outline.Top), new Point(x, outline.Bottom));
        }

        context.DrawRectangle(null, pen, new RoundedRect(outline, CornerRadius));
    }

    /// <summary>
    /// The divider positions for a mode, left to right. An empty result means
    /// one undivided pane, which is what "reading pane only" looks like.
    /// </summary>
    private static double[] DividersFor(ReaderLayoutMode mode) => mode switch
    {
        ReaderLayoutMode.ThreePane => [ThreePaneFirstDivider, ThreePaneSecondDivider],
        ReaderLayoutMode.ListAndReading => [TwoPaneDivider],
        _ => []
    };
}

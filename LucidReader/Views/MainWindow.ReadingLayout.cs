using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Styling;
using LiveMarkdown.Avalonia;
using LucidReader.Core.Model;
using LucidReader.Models;

namespace LucidReader.Views;

/// <summary>
/// How the reading pane is measured and how its text is sized. Both are
/// applied from here rather than from bindings in MainWindow.axaml, and both
/// re-run every time settings change, not only at startup.
///
/// Typography mechanism, and why. LucidMarkdownView exposes only Markdown and
/// SourcePath, so there is no FontSize property to set. Setting FontSize on a
/// container does not work either: LiveMarkdown.Avalonia's Styles.axaml (which
/// App.axaml includes, and must, because it is where the body's TextWrapping
/// comes from) sets FontSize explicitly on md|MarkdownTextBlock, and an
/// explicit Style setter beats an inherited value outright. So the only thing
/// that can win is another Style, applied closer to the control than the
/// application-level one: these go into ReadingPane.Styles, on the control
/// itself, which is the innermost place a style can live.
///
/// lucidVIEW uses a LayoutTransformControl scale instead. That was rejected
/// here for two reasons: it scales the column with the text, so the measured
/// reading width would stop meaning what the setting says, and it cannot give
/// code its own size, which is the point of CodeFontSize.
///
/// Line height is applied as LineSpacing, not LineHeight. LineSpacing is
/// additive on top of the typeface's own metrics, so it cannot clip; an
/// absolute LineHeight computed from the body font size would be smaller than
/// a heading's own line and would overlap it.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// The size LiveMarkdown's own stylesheet uses for body text
    /// (FontSizeM in its Defaults.axaml). Every other size in that stylesheet
    /// is relative to it in practice, so dividing by it turns the user's
    /// FontSize into the scale to apply to headings, keeping the heading
    /// hierarchy intact instead of flattening it to one size.
    /// </summary>
    private const double LiveMarkdownBodyFontSize = 14;

    // LiveMarkdown's heading sizes, from its Defaults.axaml: FontSize3Xl,
    // FontSize2Xl, FontSizeXl, then FontSizeL for h4, h5 and h6 alike.
    private static readonly double[] HeadingFontSizes = [24, 20, 18, 16, 16, 16];

    // Reader.axaml's own sizes for the two TextBlocks above the rendered
    // markdown, scaled alongside it so the whole article moves together.
    private const double ArticleTitleFontSize = 24;
    private const double ArticleMetaFontSize = 12.5;

    private readonly List<IStyle> _readingTypographyStyles = [];

    /// <summary>The width the column is actually given, after clamping.</summary>
    public double ResolvedColumnWidth =>
        ReadingColumn is null ? _services.Settings.ColumnWidth : ReadingColumn.Width;

    /// <summary>
    /// Named ReadingFontSize, not FontSize, because Window already has a
    /// FontSize and a binding naming that would quietly resolve to the wrong
    /// one. Same reason for the two below.
    /// </summary>
    public double ReadingFontSize => _services.Settings.FontSize;

    public double ReadingLineHeight => _services.Settings.LineHeight;

    public double ReadingCodeFontSize => _services.Settings.CodeFontSize;

    /// <summary>
    /// Called once from the constructor. Watching Bounds rather than the
    /// window's own size because the three-pane split means this pane's width
    /// changes when either GridSplitter moves, with the window standing still.
    /// </summary>
    private void WatchReadingPaneSize()
    {
        ReadingScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty) ApplyReadingColumnWidth();
        };
    }

    private void ApplyReadingColumnWidth()
    {
        if (ReadingColumn is null || ReadingScroll is null) return;

        var width = ReadingColumnMetrics.Resolve(
            _services.Settings.ColumnWidth, ReadingScroll.Bounds.Width);

        // Width, not MaxWidth. See the comment on the column in
        // MainWindow.axaml and lucidVIEW's MainWindow.Ruler.cs: a MaxWidth on
        // a centred panel is only a cap, so the panel shrinks to its content
        // and the two margins stop matching.
        if (!ReadingColumn.Width.Equals(width)) ReadingColumn.Width = width;
    }

    private void ApplyReadingTypography(ReaderSettings settings)
    {
        if (ReadingPane is null) return;

        foreach (var style in _readingTypographyStyles)
            ReadingPane.Styles.Remove(style);
        _readingTypographyStyles.Clear();

        var fontSize = settings.FontSize;
        var scale = fontSize / LiveMarkdownBodyFontSize;

        // LineHeight is a multiplier, so the extra space it asks for is the
        // part above 1.0. At exactly 1.0 nothing is added and the typeface's
        // own metrics stand.
        var extraLineSpacing = Math.Max(0, fontSize * (settings.LineHeight - 1.0));

        // Body first, then headings, then code: styles in one collection are
        // applied in order and the last matching setter wins, so the narrower
        // selectors have to come after the one that matches everything.
        Add(x => x.OfType<MarkdownTextBlock>(),
            (TextBlock.FontSizeProperty, fontSize),
            (TextBlock.LineSpacingProperty, extraLineSpacing));

        for (var level = 1; level <= HeadingFontSizes.Length; level++)
        {
            var headingSize = HeadingFontSizes[level - 1] * scale;
            var levelClass = $"Heading{level}";
            Add(x => x.OfType<MarkdownTextBlock>().Class(levelClass),
                (TextBlock.FontSizeProperty, headingSize),
                (TextBlock.LineSpacingProperty, Math.Max(0, headingSize * (settings.LineHeight - 1.0))));
        }

        // The code block's text lives in the CodeBlock template, so it is only
        // reachable through the template selector. Its line spacing goes back
        // to zero: prose line height applied to code just stretches it out.
        Add(x => x.OfType<CodeBlock>().Template().OfType<MarkdownTextBlock>(),
            (TextBlock.FontSizeProperty, settings.CodeFontSize),
            (TextBlock.LineSpacingProperty, 0.0));

        // Inline code, matched the same way LiveMarkdown's own stylesheet
        // matches it.
        Add(x => x.OfType<InlineUIContainer>().Class("Code").Descendant().OfType<MarkdownTextBlock>(),
            (TextBlock.FontSizeProperty, settings.CodeFontSize));

        foreach (var style in _readingTypographyStyles)
            ReadingPane.Styles.Add(style);

        // The title and byline are plain TextBlocks in the reading column with
        // their sizes set by Reader.axaml. A local value on the control beats
        // that style, which is what makes the whole article scale together
        // rather than only the rendered markdown.
        ArticleTitleText.FontSize = ArticleTitleFontSize * scale;
        ArticleMetaText.FontSize = ArticleMetaFontSize * scale;

        void Add(Func<Selector?, Selector> selector, params (AvaloniaProperty Property, object Value)[] setters)
        {
            var style = new Style(selector);
            foreach (var (property, value) in setters)
                style.Setters.Add(new Setter(property, value));
            _readingTypographyStyles.Add(style);
        }
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MermaidSharp.Diagrams.C4;

namespace Mostlylucid.LucidView.Markdown.Controls;

/// <summary>
/// Renders a Naiad <see cref="C4LayoutResult"/> as native Avalonia vector graphics via
/// <see cref="DrawingContext"/> — the C4 analog of <see cref="FlowchartCanvas"/>. Native (rather than
/// a static SVG) so the architecture view can later overlay live activity (a component pulsing while
/// its owning agent works, messages flowing along relationships) and colour elements by owner.
/// </summary>
public class C4Canvas : Control
{
    public static readonly StyledProperty<C4LayoutResult?> LayoutProperty =
        AvaloniaProperty.Register<C4Canvas, C4LayoutResult?>(nameof(Layout));

    public C4LayoutResult? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>
    /// Optional per-element fill override, keyed by element id — e.g. the owning agent's identity
    /// colour, so the architecture doubles as a live ownership map. Falls back to the C4 type palette.
    /// </summary>
    public IReadOnlyDictionary<string, Color>? ElementColors { get; set; }

    static C4Canvas()
    {
        AffectsMeasure<C4Canvas>(LayoutProperty);
        AffectsRender<C4Canvas>(LayoutProperty);
    }

    // ── C4 / Structurizr-ish palette (white text sits on all of these) ─────────────────────────
    static readonly Color PersonColor    = Color.FromRgb(0x08, 0x42, 0x7B);
    static readonly Color SystemColor     = Color.FromRgb(0x11, 0x68, 0xBD);
    static readonly Color ContainerColor  = Color.FromRgb(0x43, 0x8D, 0xD5);
    static readonly Color ComponentColor  = Color.FromRgb(0x85, 0xBB, 0xF0);
    static readonly Color ExternalColor   = Color.FromRgb(0x99, 0x99, 0x99);

    readonly IBrush _textBrush = Brushes.White;
    readonly IBrush _mutedText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB0));
    readonly IPen _boundaryPen = new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x9A)), 1.5)
        { DashStyle = DashStyle.Dash };
    readonly IPen _edgePen = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)), 1.5)
        { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    readonly IBrush _arrowBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90));
    readonly IBrush _edgeLabelBg = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C), 0.72);

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = Layout;
        return layout is null ? default : new Size(layout.Width, layout.Height);
    }

    public override void Render(DrawingContext context)
    {
        var layout = Layout;
        if (layout is null) return;

        // Boundaries behind their members.
        foreach (var b in layout.Boundaries)
        {
            var rect = new RoundedRect(new Rect(b.X, b.Y, b.Width, b.Height), 8);
            context.DrawRectangle(null, _boundaryPen, rect);
            var label = CreateText(b.Boundary.Label, 12, _mutedText, FontWeight.Bold);
            context.DrawText(label, new Point(b.X + 10, b.Y + 6));
        }

        // Relationships under the boxes so arrowheads meet the perimeter cleanly.
        foreach (var e in layout.Edges)
            DrawEdge(context, e);

        foreach (var el in layout.Elements)
            DrawElement(context, el);
    }

    void DrawElement(DrawingContext context, C4PositionedElement el)
    {
        var fill = new SolidColorBrush(ColorFor(el.Element));
        var rect = new Rect(el.X, el.Y, el.Width, el.Height);

        if (el.Element.Type == C4ElementType.Person)
        {
            // Head + rounded body.
            const double head = 16;
            var cx = el.X + el.Width / 2;
            context.DrawEllipse(fill, null, new Point(cx, el.Y + head + 2), head, head);
            var body = new RoundedRect(new Rect(el.X + 8, el.Y + head * 2 + 6, el.Width - 16, el.Height - head * 2 - 10), 8);
            context.DrawRectangle(fill, null, body);
            DrawElementText(context, el, body.Rect);
        }
        else
        {
            context.DrawRectangle(fill, null, new RoundedRect(rect, 8));
            DrawElementText(context, el, rect);
        }
    }

    void DrawElementText(DrawingContext context, C4PositionedElement el, Rect box)
    {
        var lines = new List<FormattedText>
        {
            CreateText(el.Element.Label, 12, _textBrush, FontWeight.Bold),
        };
        if (!string.IsNullOrEmpty(el.Element.Technology))
            lines.Add(CreateText($"[{el.Element.Technology}]", 10, _textBrush));
        if (!string.IsNullOrEmpty(el.Element.Description))
            lines.Add(CreateText(Truncate(el.Element.Description!, 34), 10, _textBrush));

        var totalH = lines.Sum(l => l.Height) + (lines.Count - 1) * 2;
        var y = box.Y + (box.Height - totalH) / 2;
        var cx = box.X + box.Width / 2;
        foreach (var line in lines)
        {
            context.DrawText(line, new Point(cx - line.Width / 2, y));
            y += line.Height + 2;
        }
    }

    void DrawEdge(DrawingContext context, C4PositionedEdge e)
    {
        var from = new Point(e.FromX, e.FromY);
        var to = new Point(e.ToX, e.ToY);
        context.DrawLine(_edgePen, from, to);
        DrawArrowhead(context, from, to);

        if (!string.IsNullOrEmpty(e.Relationship.Label))
        {
            var label = CreateText(e.Relationship.Label!, 10, _mutedText);
            var tl = new Point(e.LabelX - label.Width / 2, e.LabelY - label.Height / 2);
            context.DrawRectangle(_edgeLabelBg, null,
                new RoundedRect(new Rect(tl.X - 3, tl.Y - 1, label.Width + 6, label.Height + 2), 3));
            context.DrawText(label, tl);
        }
    }

    void DrawArrowhead(DrawingContext context, Point from, Point to)
    {
        const double size = 9;
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double spread = Math.PI / 7;
        var p1 = new Point(to.X - size * Math.Cos(angle - spread), to.Y - size * Math.Sin(angle - spread));
        var p2 = new Point(to.X - size * Math.Cos(angle + spread), to.Y - size * Math.Sin(angle + spread));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(to, isFilled: true);
            ctx.LineTo(p1);
            ctx.LineTo(p2);
            ctx.EndFigure(true);
        }
        context.DrawGeometry(_arrowBrush, null, geo);
    }

    Color ColorFor(C4Element element)
    {
        if (ElementColors is not null && ElementColors.TryGetValue(element.Id, out var owner))
            return owner;
        if (element.IsExternal) return ExternalColor;
        return element.Type switch
        {
            C4ElementType.Person => PersonColor,
            C4ElementType.System => SystemColor,
            C4ElementType.Container or C4ElementType.ContainerDb or C4ElementType.ContainerQueue => ContainerColor,
            C4ElementType.Component => ComponentColor,
            _ => SystemColor,
        };
    }

    static FormattedText CreateText(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Arial, sans-serif", FontStyle.Normal, weight), size, brush);

    static string Truncate(string text, int max)
        => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
}

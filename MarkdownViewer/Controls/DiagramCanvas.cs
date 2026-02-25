using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MermaidSharp.Rendering;

namespace MarkdownViewer.Controls;

/// <summary>
/// Renders any Naiad SvgDocument as native Avalonia vector graphics via DrawingContext.
/// Walks the SvgElement tree directly - no XML serialization or SkiaSharp rasterization.
/// Supports CSS class resolution from SvgDocument.CssStyles for elements using class-based styling.
/// </summary>
public class DiagramCanvas : Control
{
    public static readonly StyledProperty<SvgDocument?> DocumentProperty =
        AvaloniaProperty.Register<DiagramCanvas, SvgDocument?>(nameof(Document));

    public static readonly StyledProperty<IBrush?> DefaultTextBrushProperty =
        AvaloniaProperty.Register<DiagramCanvas, IBrush?>(nameof(DefaultTextBrush));

    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> ZoomTargetsProperty =
        AvaloniaProperty.Register<DiagramCanvas, IReadOnlyDictionary<string, string>?>(nameof(ZoomTargets));

    public SvgDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Theme-aware default text brush. Used when no explicit fill is set on text elements.
    /// Set this from the host to match the current theme's text color.
    /// </summary>
    public IBrush? DefaultTextBrush
    {
        get => GetValue(DefaultTextBrushProperty);
        set => SetValue(DefaultTextBrushProperty, value);
    }

    /// <summary>
    /// Maps element IDs to zoom target keys. When set, elements with matching IDs become clickable.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ZoomTargets
    {
        get => GetValue(ZoomTargetsProperty);
        set => SetValue(ZoomTargetsProperty, value);
    }

    /// <summary>
    /// Fired when a zoomable element is clicked. Argument is the zoom target (diagram key or URL).
    /// </summary>
    public event EventHandler<string>? LinkClicked;

    // Caches
    readonly Dictionary<string, Geometry> _geometryCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, IBrush> _brushCache = new(StringComparer.Ordinal);
    readonly Dictionary<(string, double), IPen> _penCache = [];
    readonly Dictionary<string, Matrix> _transformCache = new(StringComparer.Ordinal);

    // Marker defs lookup: marker id → SvgMarker
    readonly Dictionary<string, SvgMarker> _markerLookup = new(StringComparer.Ordinal);

    // Gradient defs lookup: gradient id → IBrush
    readonly Dictionary<string, IBrush> _gradientBrushes = new(StringComparer.Ordinal);

    // CSS class → inline style string (parsed from SvgDocument.CssStyles)
    readonly Dictionary<string, string> _cssClassStyles = new(StringComparer.OrdinalIgnoreCase);

    // Default fill brush from the CSS base rule (#mermaid-svg{fill:...})
    IBrush? _cssDefaultFillBrush;

    double _scale = 1.0;

    // C4 zoom interaction state
    string? _hoveredElementId;

    static DiagramCanvas()
    {
        AffectsRender<DiagramCanvas>(DocumentProperty, DefaultTextBrushProperty);
        AffectsMeasure<DiagramCanvas>(DocumentProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            ClearCaches();
            BuildDefsLookups();
            InvalidateMeasure();
        }
    }

    void ClearCaches()
    {
        _geometryCache.Clear();
        _brushCache.Clear();
        _penCache.Clear();
        _transformCache.Clear();
        _markerLookup.Clear();
        _gradientBrushes.Clear();
        _cssClassStyles.Clear();
        _cssDefaultFillBrush = null;
    }

    void BuildDefsLookups()
    {
        var doc = Document;
        if (doc is null) return;

        foreach (var marker in doc.Defs.Markers)
            _markerLookup[marker.Id] = marker;

        foreach (var gradient in doc.Defs.Gradients)
        {
            var brush = CreateGradientBrush(gradient);
            if (brush is not null)
                _gradientBrushes[gradient.Id] = brush;
        }

        ParseCssStyles(doc.CssStyles);
    }

    // ── CSS class resolution ──

    static readonly Regex CssRuleRegex = new(
        @"([^{]+)\{([^}]*)\}",
        RegexOptions.Compiled);

    void ParseCssStyles(string? css)
    {
        _cssClassStyles.Clear();
        _cssDefaultFillBrush = null;

        if (string.IsNullOrEmpty(css)) return;

        foreach (Match match in CssRuleRegex.Matches(css))
        {
            var selectorsStr = match.Groups[1].Value.Trim();
            var properties = match.Groups[2].Value.Trim();

            // Handle base #mermaid-svg selector (default fill for the whole diagram)
            if (selectorsStr is "#mermaid-svg" or "#mermaid-svg svg")
            {
                var baseFill = GetStyleValue(properties, "fill");
                if (baseFill is not null)
                    _cssDefaultFillBrush = GetOrCreateBrush(baseFill);
                continue;
            }

            // Split comma-separated selectors: "#mermaid-svg .a,#mermaid-svg .b{...}"
            var selectors = selectorsStr.Split(',');
            foreach (var rawSelector in selectors)
            {
                var selector = rawSelector.Trim();

                // Extract class names from selectors like:
                //   "#mermaid-svg .className"
                //   "#mermaid-svg .className text" (descendant - store under className)
                //   "#mermaid-svg g.className" (element.class)

                // Find all .className segments
                var dotIdx = selector.IndexOf('.');
                while (dotIdx >= 0 && dotIdx < selector.Length - 1)
                {
                    var classStart = dotIdx + 1;
                    var classEnd = classStart;
                    while (classEnd < selector.Length &&
                           (char.IsLetterOrDigit(selector[classEnd]) || selector[classEnd] == '-' || selector[classEnd] == '_'))
                        classEnd++;

                    if (classEnd > classStart)
                    {
                        var className = selector[classStart..classEnd];
                        // Store properties - last rule wins (CSS specificity simplified)
                        if (!_cssClassStyles.TryGetValue(className, out var existing))
                            _cssClassStyles[className] = properties;
                        else
                            _cssClassStyles[className] = existing + ";" + properties;
                    }

                    dotIdx = selector.IndexOf('.', classEnd);
                }
            }
        }
    }

    /// <summary>
    /// Get the CSS style string for an element's class attribute.
    /// Handles space-separated multiple classes by merging all matching rules.
    /// </summary>
    string? GetCssStyleForElement(SvgElement element)
    {
        var cssClass = element.Class;
        if (string.IsNullOrEmpty(cssClass)) return null;

        // Single class (most common)
        if (!cssClass.Contains(' '))
            return _cssClassStyles.GetValueOrDefault(cssClass);

        // Multiple classes - merge all matching rules
        string? merged = null;
        foreach (var cls in cssClass.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (_cssClassStyles.TryGetValue(cls, out var style))
                merged = merged is null ? style : merged + ";" + style;
        }
        return merged;
    }

    IBrush GetEffectiveTextBrush() =>
        DefaultTextBrush ?? _cssDefaultFillBrush ?? Brushes.Gray;

    // ── Layout ──

    protected override Size MeasureOverride(Size availableSize)
    {
        var doc = Document;
        if (doc is null) return default;

        var naturalWidth = doc.Width + 20;
        var naturalHeight = doc.Height + 20;

        _scale = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? 1.0
            : Math.Min(1.0, availableSize.Width / naturalWidth);

        return new Size(naturalWidth * _scale, naturalHeight * _scale);
    }

    public override void Render(DrawingContext context)
    {
        var doc = Document;
        if (doc is null) return;

        // Draw background color from the document (ensures text colors contrast correctly)
        if (!string.IsNullOrEmpty(doc.BackgroundColor) && TryParseColor(doc.BackgroundColor, out var bgColor))
        {
            var bgBrush = new ImmutableSolidColorBrush(bgColor);
            context.DrawRectangle(bgBrush, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        using (context.PushTransform(Matrix.CreateScale(_scale, _scale)))
        using (context.PushTransform(Matrix.CreateTranslation(10, 10)))
        {
            foreach (var element in doc.Elements)
                RenderElement(context, element);

            // Draw C4 zoom overlays
            RenderZoomOverlays(context, doc);
        }
    }

    void RenderZoomOverlays(DrawingContext context, SvgDocument doc)
    {
        var targets = ZoomTargets;
        if (targets is null || targets.Count == 0 || doc.HitRegions.Count == 0) return;

        foreach (var (elementId, region) in doc.HitRegions)
        {
            if (!targets.ContainsKey(elementId)) continue;

            var rect = new Rect(region.X, region.Y, region.Width, region.Height);

            // Draw zoom indicator: small magnifying glass icon at bottom-right
            var iconSize = 14.0;
            var iconX = rect.Right - iconSize - 4;
            var iconY = rect.Bottom - iconSize - 4;
            var circleR = iconSize / 3;
            var circleCx = iconX + circleR + 2;
            var circleCy = iconY + circleR + 2;

            var indicatorBrush = new ImmutableSolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            var indicatorPen = new Pen(indicatorBrush, 1.5);

            // Circle part of magnifying glass
            context.DrawEllipse(null, indicatorPen, new Point(circleCx, circleCy), circleR, circleR);

            // Handle part
            var handleStart = new Point(circleCx + circleR * 0.7, circleCy + circleR * 0.7);
            var handleEnd = new Point(iconX + iconSize - 1, iconY + iconSize - 1);
            context.DrawLine(indicatorPen, handleStart, handleEnd);

            // Hover highlight
            if (_hoveredElementId == elementId)
            {
                var highlightBrush = new ImmutableSolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                var highlightPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 2);
                context.DrawRectangle(highlightBrush, highlightPen, rect, 5, 5);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var doc = Document;
        var targets = ZoomTargets;
        if (doc is null || targets is null || targets.Count == 0) return;

        var pos = e.GetPosition(this);
        // Convert to document coordinates (reverse the scale + translation)
        var docX = pos.X / _scale - 10;
        var docY = pos.Y / _scale - 10;

        string? hitId = null;
        foreach (var (elementId, region) in doc.HitRegions)
        {
            if (!targets.ContainsKey(elementId)) continue;

            if (docX >= region.X && docX <= region.X + region.Width &&
                docY >= region.Y && docY <= region.Y + region.Height)
            {
                hitId = elementId;
                break;
            }
        }

        if (hitId != _hoveredElementId)
        {
            _hoveredElementId = hitId;
            Cursor = hitId is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredElementId is not null)
        {
            _hoveredElementId = null;
            Cursor = Cursor.Default;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_hoveredElementId is not null && ZoomTargets is not null &&
            ZoomTargets.TryGetValue(_hoveredElementId, out var target))
        {
            LinkClicked?.Invoke(this, target);
            e.Handled = true;
        }
    }

    void RenderElement(DrawingContext context, SvgElement element)
    {
        switch (element)
        {
            case SvgGroup group:
                RenderGroup(context, group);
                break;
            case SvgRect rect:
                RenderRect(context, rect);
                break;
            case SvgRectNoXY rectNoXy:
                RenderRectNoXY(context, rectNoXy);
                break;
            case SvgCircle circle:
                RenderCircle(context, circle);
                break;
            case SvgEllipse ellipse:
                RenderEllipse(context, ellipse);
                break;
            case SvgLine line:
                RenderLine(context, line);
                break;
            case SvgPath path:
                RenderPath(context, path);
                break;
            case SvgPolygon polygon:
                RenderPolygon(context, polygon);
                break;
            case SvgPolyline polyline:
                RenderPolyline(context, polyline);
                break;
            case SvgMultiLineText multiText:
                RenderMultiLineText(context, multiText);
                break;
            case SvgText text:
                RenderText(context, text);
                break;
            case SvgForeignObject foreign:
                RenderForeignObject(context, foreign);
                break;
        }
    }

    void RenderGroup(DrawingContext context, SvgGroup group)
    {
        if (group.Children.Count == 0) return;

        var transform = ParseTransform(group.Transform);
        var cssStyle = GetCssStyleForElement(group);
        var opacity = ParseOpacityFromStyle(group.Style)
                      * ParseOpacityFromStyle(cssStyle);

        void RenderChildren()
        {
            foreach (var child in group.Children)
                RenderElement(context, child);
        }

        // Nest transform and opacity push states
        if (transform.HasValue)
        {
            using (context.PushTransform(transform.Value))
            {
                if (opacity < 1.0)
                {
                    using (context.PushOpacity(opacity))
                        RenderChildren();
                }
                else
                {
                    RenderChildren();
                }
            }
        }
        else if (opacity < 1.0)
        {
            using (context.PushOpacity(opacity))
                RenderChildren();
        }
        else
        {
            RenderChildren();
        }
    }

    void WithTransform(DrawingContext context, string? transformStr, Action body)
    {
        var transform = ParseTransform(transformStr);
        if (transform.HasValue)
        {
            using (context.PushTransform(transform.Value))
                body();
        }
        else
        {
            body();
        }
    }

    void RenderRect(DrawingContext context, SvgRect rect)
    {
        WithTransform(context, rect.Transform, () =>
        {
            var cssStyle = GetCssStyleForElement(rect);
            var fill = ResolveBrush(rect.Fill, rect.Style, cssStyle, "fill");
            var pen = ResolvePen(rect.Stroke, rect.StrokeWidth, rect.Style, cssStyle);
            var r = new Rect(rect.X, rect.Y, rect.Width, rect.Height);

            if (rect.Rx > 0 || rect.Ry > 0)
                context.DrawRectangle(fill, pen, r, rect.Rx, rect.Ry);
            else
                context.DrawRectangle(fill, pen, r);
        });
    }

    void RenderRectNoXY(DrawingContext context, SvgRectNoXY rect)
    {
        WithTransform(context, rect.Transform, () =>
        {
            var cssStyle = GetCssStyleForElement(rect);
            var fill = ResolveBrushFromStyle(rect.Style, cssStyle, "fill");
            var pen = ResolvePenFromStyle(rect.Style, cssStyle);
            context.DrawRectangle(fill, pen, new Rect(0, 0, rect.Width, rect.Height));
        });
    }

    void RenderCircle(DrawingContext context, SvgCircle circle)
    {
        WithTransform(context, circle.Transform, () =>
        {
            var cssStyle = GetCssStyleForElement(circle);
            var fill = ResolveBrush(circle.Fill, circle.Style, cssStyle, "fill");
            var pen = ResolvePen(circle.Stroke, circle.StrokeWidth, circle.Style, cssStyle);
            context.DrawEllipse(fill, pen, new Point(circle.Cx, circle.Cy), circle.R, circle.R);
        });
    }

    void RenderEllipse(DrawingContext context, SvgEllipse ellipse)
    {
        WithTransform(context, ellipse.Transform, () =>
        {
            var cssStyle = GetCssStyleForElement(ellipse);
            var fill = ResolveBrush(ellipse.Fill, ellipse.Style, cssStyle, "fill");
            var pen = ResolvePen(ellipse.Stroke, null, ellipse.Style, cssStyle);
            context.DrawEllipse(fill, pen, new Point(ellipse.Cx, ellipse.Cy), ellipse.Rx, ellipse.Ry);
        });
    }

    void RenderLine(DrawingContext context, SvgLine line)
    {
        WithTransform(context, line.Transform, () =>
        {
            var cssStyle = GetCssStyleForElement(line);
            var pen = ResolvePen(line.Stroke, line.StrokeWidth, line.Style, cssStyle, line.StrokeDasharray);
            if (pen is not null)
                context.DrawLine(pen, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
        });
    }

    void RenderPath(DrawingContext context, SvgPath path)
    {
        WithTransform(context, path.Transform, () =>
        {
            var geometry = GetOrParseGeometry(path.D);
            if (geometry is null) return;

            var cssStyle = GetCssStyleForElement(path);
            var fill = ResolveBrush(path.Fill, path.Style, cssStyle, "fill");
            var pen = ResolvePen(path.Stroke, path.StrokeWidth, path.Style, cssStyle, path.StrokeDasharray);

            if (path.Opacity.HasValue)
            {
                using (context.PushOpacity(path.Opacity.Value))
                    context.DrawGeometry(fill, pen, geometry);
            }
            else
            {
                context.DrawGeometry(fill, pen, geometry);
            }

            // Render markers at path endpoints
            RenderPathMarkers(context, path);
        });
    }

    void RenderPathMarkers(DrawingContext context, SvgPath path)
    {
        if (path.MarkerEnd is null && path.MarkerStart is null) return;

        if (path.MarkerEnd is not null)
        {
            var markerId = ExtractMarkerId(path.MarkerEnd);
            if (markerId is not null && _markerLookup.TryGetValue(markerId, out var marker))
                RenderMarkerAtEnd(context, marker, path);
        }
    }

    void RenderMarkerAtEnd(DrawingContext context, SvgMarker marker, SvgPath path)
    {
        var markerGeom = GetOrParseGeometry(marker.Path);
        if (markerGeom is null) return;

        var fill = ResolveBrush(marker.Fill, null, null, "fill")
                   ?? ResolveBrush(path.Stroke, path.Style, null, "stroke")
                   ?? GetEffectiveTextBrush();

        var endpoints = GetPathEndpoints(path.D);
        if (endpoints is null) return;

        var (endPoint, angle) = endpoints.Value;

        using (context.PushTransform(
            Matrix.CreateTranslation(-marker.RefX, -marker.RefY) *
            Matrix.CreateRotation(angle) *
            Matrix.CreateTranslation(endPoint.X, endPoint.Y)))
        {
            context.DrawGeometry(fill, null, markerGeom);
        }
    }

    static (Point EndPoint, double Angle)? GetPathEndpoints(string d)
    {
        var parts = d.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var points = new List<Point>();

        double? lastX = null;
        foreach (var raw in parts)
        {
            var part = raw.TrimStart('M', 'L', 'C', 'S', 'Q', 'T', 'A', 'H', 'V',
                                      'm', 'l', 'c', 's', 'q', 't', 'a', 'h', 'v');
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                if (lastX is null)
                    lastX = val;
                else
                {
                    points.Add(new Point(lastX.Value, val));
                    lastX = null;
                }
            }
        }

        if (points.Count < 2) return null;

        var end = points[^1];
        var prev = points[^2];
        var angle = Math.Atan2(end.Y - prev.Y, end.X - prev.X);
        return (end, angle);
    }

    void RenderPolygon(DrawingContext context, SvgPolygon polygon)
    {
        if (polygon.Points.Count < 2) return;

        WithTransform(context, polygon.Transform, () =>
        {
            var geometry = CreatePolygonGeometry(polygon.Points, closed: true);
            var cssStyle = GetCssStyleForElement(polygon);
            var fill = ResolveBrush(polygon.Fill, polygon.Style, cssStyle, "fill");
            var pen = ResolvePen(polygon.Stroke, null, polygon.Style, cssStyle);
            context.DrawGeometry(fill, pen, geometry);
        });
    }

    void RenderPolyline(DrawingContext context, SvgPolyline polyline)
    {
        if (polyline.Points.Count < 2) return;

        WithTransform(context, polyline.Transform, () =>
        {
            var geometry = CreatePolygonGeometry(polyline.Points, closed: false);
            var cssStyle = GetCssStyleForElement(polyline);
            var fill = ResolveBrush(polyline.Fill, polyline.Style, cssStyle, "fill");
            var pen = ResolvePen(polyline.Stroke, polyline.StrokeWidth, polyline.Style, cssStyle, polyline.StrokeDasharray);
            context.DrawGeometry(fill, pen, geometry);
        });
    }

    static StreamGeometry CreatePolygonGeometry(List<MermaidSharp.Models.Position> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(points[0].X, points[0].Y), true);
        for (var i = 1; i < points.Count; i++)
            ctx.LineTo(new Point(points[i].X, points[i].Y));
        ctx.EndFigure(closed);
        return geometry;
    }

    void RenderText(DrawingContext context, SvgText text)
    {
        if (string.IsNullOrEmpty(text.Content)) return;

        WithTransform(context, text.Transform, () =>
        {
            var cssStyle = GetCssStyleForElement(text);
            var fontSize = ParseFontSize(text.FontSize, text.Style)
                           ?? ParseFontSize(null, cssStyle)
                           ?? 14.0;
            var fontFamily = text.FontFamily
                             ?? GetStyleValue(cssStyle, "font-family")
                             ?? "Segoe UI, Arial, sans-serif";
            var fontWeight = ParseFontWeight(text.FontWeight, text.Style, cssStyle);
            var fill = ResolveBrush(text.Fill, text.Style, cssStyle, "fill")
                       ?? GetEffectiveTextBrush();

            var typeface = new Typeface(new FontFamily(fontFamily), FontStyle.Normal, fontWeight);
            var formatted = new FormattedText(
                text.Content,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                fill);

            var x = text.OmitXY ? 0.0 : text.X;
            var y = text.OmitXY ? 0.0 : text.Y;

            // Apply text-anchor alignment
            var anchor = text.TextAnchor
                         ?? GetStyleValue(text.Style, "text-anchor")
                         ?? GetStyleValue(cssStyle, "text-anchor");
            if (anchor == "middle")
                x -= formatted.Width / 2;
            else if (anchor == "end")
                x -= formatted.Width;

            // Apply dominant-baseline
            var baseline = text.DominantBaseline ?? GetStyleValue(text.Style, "dominant-baseline");
            if (baseline is "central" or "middle")
                y -= formatted.Height / 2;
            else if (baseline is "hanging" or "text-before-edge")
            {
                // y is at top - no adjustment
            }
            else if (baseline is "text-after-edge" or "bottom" or "ideographic")
            {
                // y is at the bottom of the text
                y -= formatted.Height;
            }
            else
            {
                // Default: alphabetic baseline — approximate
                y -= formatted.Height * 0.8;
            }

            context.DrawText(formatted, new Point(x, y));
        });
    }

    void RenderMultiLineText(DrawingContext context, SvgMultiLineText multiText)
    {
        if (multiText.Lines.Length == 0) return;

        var cssStyle = GetCssStyleForElement(multiText);
        var fontSize = ParseFontSize(multiText.FontSize, null)
                       ?? ParseFontSize(null, cssStyle)
                       ?? 14.0;
        var fontFamily = multiText.FontFamily
                         ?? GetStyleValue(cssStyle, "font-family")
                         ?? "Segoe UI, Arial, sans-serif";
        var fontWeight = ParseFontWeight(multiText.FontWeight, null, cssStyle);
        var fill = ResolveBrush(multiText.Fill, null, cssStyle, "fill")
                   ?? GetEffectiveTextBrush();

        var typeface = new Typeface(new FontFamily(fontFamily), FontStyle.Normal, fontWeight);

        var anchor = multiText.TextAnchor ?? GetStyleValue(cssStyle, "text-anchor");
        var baseline = multiText.DominantBaseline ?? GetStyleValue(cssStyle, "dominant-baseline");

        var y = multiText.StartY;
        for (var i = 0; i < multiText.Lines.Length; i++)
        {
            var line = multiText.Lines[i];
            if (string.IsNullOrEmpty(line))
            {
                y += multiText.LineHeight;
                continue;
            }

            var formatted = new FormattedText(
                line,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                fill);

            var x = multiText.X;
            if (anchor == "middle")
                x -= formatted.Width / 2;
            else if (anchor == "end")
                x -= formatted.Width;

            var drawY = y;
            if (baseline is "central" or "middle")
                drawY -= formatted.Height / 2;
            else if (baseline is "hanging" or "text-before-edge")
            {
                // y is at top - no adjustment
            }
            else
            {
                // Default: alphabetic baseline
                drawY -= formatted.Height * 0.8;
            }

            context.DrawText(formatted, new Point(x, drawY));
            y += multiText.LineHeight;
        }
    }

    void RenderForeignObject(DrawingContext context, SvgForeignObject foreign)
    {
        if (string.IsNullOrEmpty(foreign.HtmlContent)) return;

        // Extract plain text from HTML content
        var plainText = StripHtml(foreign.HtmlContent);
        if (string.IsNullOrWhiteSpace(plainText)) return;

        WithTransform(context, foreign.Transform, () =>
        {
            var cssStyle = GetCssStyleForElement(foreign);
            var fontSize = ParseFontSizeFromStyle(foreign.Style)
                           ?? ParseFontSizeFromStyle(cssStyle)
                           ?? 14.0;
            var fill = ResolveBrushFromStyle(foreign.Style, cssStyle, "color")
                       ?? ResolveBrushFromStyle(foreign.Style, cssStyle, "fill")
                       ?? GetEffectiveTextBrush();

            var typeface = new Typeface("Segoe UI, Arial, sans-serif");
            var formatted = new FormattedText(
                plainText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                fill)
            {
                MaxTextWidth = foreign.Width > 0 ? foreign.Width : double.PositiveInfinity,
                TextAlignment = TextAlignment.Center
            };

            // Center text within the foreign object bounds
            var x = foreign.X + Math.Max(0, (foreign.Width - formatted.Width) / 2);
            var y = foreign.Y + Math.Max(0, (foreign.Height - formatted.Height) / 2);
            context.DrawText(formatted, new Point(x, y));
        });
    }

    // ── Brush / Pen resolution ──
    // Resolution order: direct attribute → inline style → CSS class style → null

    IBrush? ResolveBrush(string? directValue, string? inlineStyle, string? cssStyle, string property)
    {
        // 1. Direct attribute
        var value = directValue;

        // 2. Inline style
        value ??= GetStyleValue(inlineStyle, property);

        // 3. CSS class style
        value ??= GetStyleValue(cssStyle, property);

        if (value is null or "none") return null;

        // Check gradient reference: url(#gradientId)
        if (value.StartsWith("url(#", StringComparison.Ordinal))
        {
            var id = value[5..^1];
            return _gradientBrushes.GetValueOrDefault(id);
        }

        return GetOrCreateBrush(value);
    }

    IBrush? ResolveBrushFromStyle(string? inlineStyle, string? cssStyle, string property)
    {
        var value = GetStyleValue(inlineStyle, property)
                    ?? GetStyleValue(cssStyle, property);
        if (value is null or "none") return null;
        return GetOrCreateBrush(value);
    }

    IBrush? GetOrCreateBrush(string colorStr)
    {
        if (_brushCache.TryGetValue(colorStr, out var cached))
            return cached;

        if (TryParseColor(colorStr, out var color))
        {
            var brush = new ImmutableSolidColorBrush(color);
            _brushCache[colorStr] = brush;
            return brush;
        }

        return null;
    }

    IPen? ResolvePen(string? stroke, double? strokeWidth, string? inlineStyle, string? cssStyle,
        string? dashArray = null)
    {
        var strokeColor = stroke
                          ?? GetStyleValue(inlineStyle, "stroke")
                          ?? GetStyleValue(cssStyle, "stroke");
        if (strokeColor is null or "none") return null;

        var width = strokeWidth
                    ?? ParseDouble(GetStyleValue(inlineStyle, "stroke-width"))
                    ?? ParseDouble(GetStyleValue(cssStyle, "stroke-width"))
                    ?? 1.0;
        var dash = dashArray
                   ?? GetStyleValue(inlineStyle, "stroke-dasharray")
                   ?? GetStyleValue(cssStyle, "stroke-dasharray");

        var key = (strokeColor + (dash ?? ""), width);
        if (_penCache.TryGetValue(key, out var cached))
            return cached;

        var brush = GetOrCreateBrush(strokeColor);
        if (brush is null) return null;

        var pen = new Pen(brush, width);
        if (!string.IsNullOrEmpty(dash) && dash != "none")
        {
            var dashes = ParseDashArray(dash);
            if (dashes is not null)
                pen.DashStyle = new DashStyle(dashes, 0);
        }

        _penCache[key] = pen;
        return pen;
    }

    IPen? ResolvePenFromStyle(string? inlineStyle, string? cssStyle)
    {
        return ResolvePen(null, null, inlineStyle, cssStyle);
    }

    // ── Geometry parsing ──

    Geometry? GetOrParseGeometry(string d)
    {
        if (string.IsNullOrEmpty(d)) return null;

        if (_geometryCache.TryGetValue(d, out var cached))
            return cached;

        try
        {
            var geometry = Geometry.Parse(d);
            _geometryCache[d] = geometry;
            return geometry;
        }
        catch
        {
            return null;
        }
    }

    // ── Transform parsing ──

    Matrix? ParseTransform(string? transform)
    {
        if (string.IsNullOrEmpty(transform)) return null;

        if (_transformCache.TryGetValue(transform, out var cached))
            return cached;

        var matrix = ParseTransformString(transform);
        if (matrix.HasValue)
            _transformCache[transform] = matrix.Value;

        return matrix;
    }

    static Matrix? ParseTransformString(string transform)
    {
        var result = Matrix.Identity;
        var applied = false;

        var idx = 0;
        while (idx < transform.Length)
        {
            var remaining = transform.AsSpan(idx);

            if (remaining.StartsWith("translate(", StringComparison.Ordinal))
            {
                var start = idx + 10;
                var end = transform.IndexOf(')', start);
                if (end < 0) break;

                var args = ParseArgs(transform[start..end]);
                if (args.Length >= 1)
                {
                    var tx = args[0];
                    var ty = args.Length >= 2 ? args[1] : 0;
                    result *= Matrix.CreateTranslation(tx, ty);
                    applied = true;
                }
                idx = end + 1;
            }
            else if (remaining.StartsWith("rotate(", StringComparison.Ordinal))
            {
                var start = idx + 7;
                var end = transform.IndexOf(')', start);
                if (end < 0) break;

                var args = ParseArgs(transform[start..end]);
                if (args.Length >= 1)
                {
                    var angle = args[0] * Math.PI / 180.0;
                    if (args.Length >= 3)
                    {
                        result *= Matrix.CreateTranslation(-args[1], -args[2]);
                        result *= Matrix.CreateRotation(angle);
                        result *= Matrix.CreateTranslation(args[1], args[2]);
                    }
                    else
                    {
                        result *= Matrix.CreateRotation(angle);
                    }
                    applied = true;
                }
                idx = end + 1;
            }
            else if (remaining.StartsWith("scale(", StringComparison.Ordinal))
            {
                var start = idx + 6;
                var end = transform.IndexOf(')', start);
                if (end < 0) break;

                var args = ParseArgs(transform[start..end]);
                if (args.Length >= 1)
                {
                    var sx = args[0];
                    var sy = args.Length >= 2 ? args[1] : sx;
                    result *= Matrix.CreateScale(sx, sy);
                    applied = true;
                }
                idx = end + 1;
            }
            else
            {
                idx++;
            }
        }

        return applied ? result : null;
    }

    static double[] ParseArgs(string argsStr)
    {
        var parts = argsStr.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<double>(parts.Length);
        foreach (var part in parts)
        {
            if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                result.Add(val);
        }
        return result.ToArray();
    }

    // ── Style parsing helpers ──

    static string? GetStyleValue(string? style, string property)
    {
        if (string.IsNullOrEmpty(style)) return null;

        var searchFor = property + ":";
        var idx = style.IndexOf(searchFor, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        // Make sure we're at a property boundary (start of string, or preceded by ; or whitespace)
        if (idx > 0)
        {
            var prev = style[idx - 1];
            if (prev != ';' && prev != ' ' && prev != '\t')
                return null;
        }

        var valueStart = idx + searchFor.Length;
        var valueEnd = style.IndexOf(';', valueStart);
        if (valueEnd < 0) valueEnd = style.Length;

        var value = style[valueStart..valueEnd].Trim();
        // Strip !important
        if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            value = value[..^"!important".Length].Trim();

        return value;
    }

    static double? ParseFontSize(string? fontSize, string? style)
    {
        var value = fontSize ?? GetStyleValue(style, "font-size");
        if (value is null) return null;

        value = value.Replace("px", "").Trim();
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    static double? ParseFontSizeFromStyle(string? style) => ParseFontSize(null, style);

    static FontWeight ParseFontWeight(string? weight, string? style, string? cssStyle = null)
    {
        var value = weight ?? GetStyleValue(style, "font-weight") ?? GetStyleValue(cssStyle, "font-weight");
        if (value is null) return FontWeight.Normal;

        return value.ToLowerInvariant() switch
        {
            "bold" or "bolder" => FontWeight.Bold,
            "lighter" => FontWeight.Light,
            "100" => FontWeight.Thin,
            "200" => FontWeight.ExtraLight,
            "300" => FontWeight.Light,
            "400" => FontWeight.Normal,
            "500" => FontWeight.Medium,
            "600" => FontWeight.SemiBold,
            "700" => FontWeight.Bold,
            "800" => FontWeight.ExtraBold,
            "900" => FontWeight.Black,
            _ => FontWeight.Normal
        };
    }

    static double ParseOpacityFromStyle(string? style)
    {
        var value = GetStyleValue(style, "opacity");
        if (value is not null &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
            return Math.Clamp(opacity, 0, 1);
        return 1.0;
    }

    static double? ParseDouble(string? value)
    {
        if (value is null) return null;
        value = value.Replace("px", "").Trim();
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    static double[]? ParseDashArray(string dashStr)
    {
        var parts = dashStr.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var values = new List<double>(parts.Length);
        foreach (var part in parts)
        {
            if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                values.Add(val);
            else
                return null;
        }
        return values.Count > 0 ? values.ToArray() : null;
    }

    // ── Color parsing ──

    static bool TryParseColor(string colorStr, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(colorStr)) return false;

        colorStr = colorStr.Trim();

        try
        {
            color = Color.Parse(colorStr);
            return true;
        }
        catch
        {
            // Fall through to manual parsing
        }

        if (colorStr.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var start = colorStr.IndexOf('(');
            var end = colorStr.IndexOf(')');
            if (start >= 0 && end > start)
            {
                var args = ParseArgs(colorStr[(start + 1)..end]);
                if (args.Length >= 3)
                {
                    var r = (byte)Math.Clamp(args[0], 0, 255);
                    var g = (byte)Math.Clamp(args[1], 0, 255);
                    var b = (byte)Math.Clamp(args[2], 0, 255);
                    var a = args.Length >= 4 ? (byte)Math.Clamp(args[3] * 255, 0, 255) : (byte)255;
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
        }

        // Handle hsl() colors
        if (colorStr.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
        {
            var start = colorStr.IndexOf('(');
            var end = colorStr.IndexOf(')');
            if (start >= 0 && end > start)
            {
                var inner = colorStr[(start + 1)..end].Replace("%", "");
                var args = ParseArgs(inner);
                if (args.Length >= 3)
                {
                    var h = args[0] / 360.0;
                    var s = args[1] / 100.0;
                    var l = args[2] / 100.0;
                    HslToRgb(h, s, l, out var r, out var g, out var b);
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
            }
        }

        return false;
    }

    static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double r1, g1, b1;
        if (s == 0)
        {
            r1 = g1 = b1 = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r1 = HueToRgb(p, q, h + 1.0 / 3);
            g1 = HueToRgb(p, q, h);
            b1 = HueToRgb(p, q, h - 1.0 / 3);
        }
        r = (byte)Math.Clamp(r1 * 255 + 0.5, 0, 255);
        g = (byte)Math.Clamp(g1 * 255 + 0.5, 0, 255);
        b = (byte)Math.Clamp(b1 * 255 + 0.5, 0, 255);
    }

    static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    // ── Gradient support ──

    static IBrush? CreateGradientBrush(SvgGradient gradient)
    {
        if (gradient.Stops.Count == 0) return null;

        var stops = new GradientStops();
        foreach (var stop in gradient.Stops)
        {
            if (TryParseColor(stop.Color, out var color))
                stops.Add(new GradientStop(color, stop.Offset / 100.0));
        }

        if (stops.Count == 0) return null;

        if (gradient.IsRadial)
        {
            return new RadialGradientBrush
            {
                GradientStops = stops,
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative)
            };
        }

        return new LinearGradientBrush
        {
            GradientStops = stops,
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };
    }

    // ── Marker ID extraction ──

    static string? ExtractMarkerId(string markerRef)
    {
        if (markerRef.StartsWith("url(#", StringComparison.Ordinal) && markerRef.EndsWith(')'))
            return markerRef[5..^1];
        return null;
    }

    // ── HTML stripping for ForeignObject ──

    static string StripHtml(string html)
    {
        var result = Regex.Replace(html, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(result).Trim();
    }
}

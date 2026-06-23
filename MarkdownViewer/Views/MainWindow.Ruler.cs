using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MarkdownViewer.Views;

// Bar Width is ElementName-bound to the document Border so layout keeps
// the handles aligned through every transform. Code-behind only handles
// drag (mutating Border.Width) and click-to-set.
public partial class MainWindow
{
    private const double MinContentWidth = 300;
    private const double MaxContentWidth = 2000;
    // Keeps the right ruler handle inside the window so it stays draggable.
    private const double WindowChromeReserve = 60;

    private double EffectiveMaxContentWidth =>
        Math.Max(MinContentWidth, Math.Min(MaxContentWidth, Width - WindowChromeReserve));

    private void ApplyContentMaxWidth()
    {
        var w = _settings.ContentMaxWidth;
        // Recover from a previously-saved width that's wider than the
        // current window — clamp to the effective max so the column fits
        // and the right handle stays grabbable. Also persist the recovery
        // so the next launch starts in a sensible state.
        var maxAllowed = EffectiveMaxContentWidth;
        if (w < MinContentWidth || w > maxAllowed)
        {
            w = Math.Min(900, maxAllowed);
            _settings.ContentMaxWidth = w;
            _settings.Save();
        }
        // Use Width (not MaxWidth). MaxWidth is just a cap and lets
        // HorizontalAlignment=Center shrink the Border to its content's
        // natural width — which makes the ruler-bound width WRONG.
        MarkdownContentBorder.Width = w;
        UpdateWidthReadout();
    }

    private void ApplyRulerVisibility()
    {
        InlineRulerBar.IsVisible = _settings.ShowRuler;
        if (_settings.ShowRuler) UpdateWidthReadout();
    }

    public void ToggleRuler()
    {
        _settings.ShowRuler = !_settings.ShowRuler;
        ApplyRulerVisibility();
        _settings.Save();
    }

    private void OnToggleRuler(object? sender, RoutedEventArgs e)
    {
        ToggleRuler();
        if (sender is ToggleButton tb) tb.IsChecked = _settings.ShowRuler;
    }

    private void RefreshRulerForScaleChange() => UpdateWidthReadout();

    private void UpdateWidthReadout()
    {
        if (RulerWidthLabel is null) return;
        var logical = MarkdownContentBorder.Width;
        if (double.IsNaN(logical) || double.IsInfinity(logical))
            logical = _settings.ContentMaxWidth;
        RulerWidthLabel.Text = $"{(int)logical} px";
    }

    private void OnRulerHandleDragDelta(object? sender, VectorEventArgs e)
    {
        // Both edges move symmetrically so a deltaX on either handle changes
        // the column by 2×deltaX. The Thumb is inside the LayoutTransformControl
        // so the delta is in the same coordinate space as the Border's Width
        // — no scale conversion needed.
        var direction = sender == RulerLeftThumb ? -1.0 : 1.0;
        var current = MarkdownContentBorder.Width;
        if (double.IsNaN(current) || double.IsInfinity(current)) current = _settings.ContentMaxWidth;

        var next = Math.Clamp(current + direction * e.Vector.X * 2.0,
            MinContentWidth, EffectiveMaxContentWidth);

        MarkdownContentBorder.Width = next;
        _settings.ContentMaxWidth = next;
        UpdateWidthReadout();
        // Persist on every delta — settings.json is tiny so the write cost is
        // negligible. Survives unexpected app exit, not just clean close.
        _settings.Save();
    }

    // Fires only on empty bar background — Thumb captures its own pointer
    // events. Snap-sets column width to twice the distance from bar centre.
    private void OnRulerCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control bar) return;
        if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(bar);
        var halfWidth = Math.Abs(pos.X - bar.Bounds.Width / 2.0);
        var next = Math.Clamp(halfWidth * 2.0, MinContentWidth, EffectiveMaxContentWidth);

        MarkdownContentBorder.Width = next;
        _settings.ContentMaxWidth = next;
        UpdateWidthReadout();
        _settings.Save();
        e.Handled = true;
    }
}

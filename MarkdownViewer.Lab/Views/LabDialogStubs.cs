// Stub dialog views for #if LAB blocks in lean source.
// Real dialogs delivered in Task 7 (UI layer) of the lucidLAB plan.
using Avalonia.Controls;

namespace MarkdownViewer.Views;

/// <summary>
/// Placeholder first-run bootstrap dialog. Shows nothing in Task 2 skeleton;
/// replaced with real onboarding UI in Task 7.
/// </summary>
public sealed class FirstRunBootstrapDialog : Window
{
    public FirstRunBootstrapDialog()
    {
        Title = "lucidLAB — First Run";
        Width = 480;
        Height = 300;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TextBlock
        {
            Text = "First-run setup coming in a future task. Press Escape to close.",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(24)
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) Close();
        };
    }
}

/// <summary>
/// Placeholder extraction-details panel. Shows nothing in Task 2 skeleton;
/// replaced with real telemetry UI in Task 7.
/// </summary>
public sealed class ExtractionDetailsPanel : Window
{
    public ExtractionDetailsPanel()
    {
        Title = "lucidLAB — Extraction Details";
        Width = 640;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TextBlock
        {
            Text = "Extraction details coming in a future task. Press Escape to close.",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(24)
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) Close();
        };
    }
}

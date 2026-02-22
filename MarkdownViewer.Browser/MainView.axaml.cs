using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MarkdownViewer.Controls;
using MermaidSharp;

namespace MarkdownViewer.Browser;

public partial class MainView : UserControl
{
    private readonly TextBox _inputBox;
    private readonly TextBox _svgBox;
    private readonly TextBlock _statusText;
    private readonly DiagramCanvas _diagramCanvas;

    public MainView()
    {
        AvaloniaXamlLoader.Load(this);
        _inputBox = this.FindControl<TextBox>("InputBox")
            ?? throw new InvalidOperationException("InputBox not found");
        _svgBox = this.FindControl<TextBox>("SvgBox")
            ?? throw new InvalidOperationException("SvgBox not found");
        _statusText = this.FindControl<TextBlock>("StatusText")
            ?? throw new InvalidOperationException("StatusText not found");
        _diagramCanvas = this.FindControl<DiagramCanvas>("DiagramCanvas")
            ?? throw new InvalidOperationException("DiagramCanvas not found");

        _inputBox.Text = """
            flowchart LR
                A[Browser] --> B{Naiad}
                B --> C[Render SVG]
            """;
    }

    private void OnRenderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var input = _inputBox.Text ?? string.Empty;
            var doc = Mermaid.RenderToDocument(input);
            var svg = Mermaid.Render(input);
            _svgBox.Text = svg;
            _diagramCanvas.Document = doc;
            _diagramCanvas.DefaultTextBrush = Brushes.Black;
            _statusText.Text = $"Rendered {svg.Length} SVG chars";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Error: {ex.Message}";
        }
    }
}

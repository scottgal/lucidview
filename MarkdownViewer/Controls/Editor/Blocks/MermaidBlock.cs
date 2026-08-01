using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class MermaidBlock : UserControl, IEditorBlock
{
    private readonly string _mermaidCode;
    private readonly Border _diagramHost;
    private readonly TextBlock _placeholder;
    private readonly Button _toggleBtn;
    private TextBox? _editor;
    private bool _isEditing;
    private bool _showSource;

    public string BlockTypeLabel => "Mermaid";
    public EditorBlockType BlockType => EditorBlockType.Mermaid;
    public bool IsEditing => _isEditing;

    public event EventHandler? ContentChanged;
    public event EventHandler? DeleteRequested;
    public event EventHandler<BlockSplitEventArgs>? SplitRequested;
    public event EventHandler? FocusPreviousRequested;
    public event EventHandler? FocusNextRequested;

    Control IEditorBlock.View => this;

    public MermaidBlock(MarkdownBlock descriptor)
    {
        _mermaidCode = descriptor.Content ?? string.Empty;

        _placeholder = new TextBlock
        {
            Text = "Mermaid Diagram\nClick to edit source",
            FontSize = 13,
            FontStyle = FontStyle.Italic,
            TextAlignment = TextAlignment.Center
        };

        _diagramHost = new Border
        {
            MinHeight = 60,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(4),
            Child = _placeholder
        };

        _toggleBtn = new Button
        {
            Content = "Edit Source",
            FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _toggleBtn.Click += (_, _) => ToggleSource();

        ApplyTheme();

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(_diagramHost);
        panel.Children.Add(_toggleBtn);
        Content = panel;
    }

    public void RefreshTheme() => ApplyTheme();

    private void ApplyTheme()
    {
        _diagramHost.Background = EditorTheme.CardHover;
        _placeholder.Foreground = EditorTheme.TextSecondary;
        _toggleBtn.Foreground = EditorTheme.TextSecondary;
    }

    public void ActivateEditing() => _isEditing = true;
    public void CommitEditing()
    {
        _isEditing = false;
        _showSource = false;
    }

    public string ToMarkdown() => $"```mermaid\n{_mermaidCode}\n```";

    private void ToggleSource()
    {
        _showSource = !_showSource;

        if (_showSource)
        {
            _editor = new TextBox
            {
                Text = _mermaidCode,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace"),
                FontSize = 12,
                MinHeight = 80,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = EditorTheme.TextSecondary
            };
            _diagramHost.Child = _editor;
            _editor.Focus();
        }
        else
        {
            _diagramHost.Child = _placeholder;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

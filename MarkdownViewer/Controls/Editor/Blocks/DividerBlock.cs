using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class DividerBlock : UserControl, IEditorBlock
{
    private bool _isEditing;
    private readonly Border _line;

    public string BlockTypeLabel => "Divider";
    public EditorBlockType BlockType => EditorBlockType.HorizontalRule;
    public bool IsEditing => _isEditing;

    public event EventHandler? ContentChanged;
    public event EventHandler? DeleteRequested { add { } remove { } }
    public event EventHandler<BlockSplitEventArgs>? SplitRequested;
    public event EventHandler? FocusPreviousRequested;
    public event EventHandler? FocusNextRequested;

    Control IEditorBlock.View => this;

    public DividerBlock(MarkdownBlock descriptor)
    {
        _line = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8)
        };
        ApplyTheme();
        Content = _line;
    }

    public void RefreshTheme() => ApplyTheme();

    private void ApplyTheme()
    {
        _line.Background = EditorTheme.BorderSubtle;
    }

    public void ActivateEditing() => _isEditing = true;
    public void CommitEditing() => _isEditing = false;
    public string ToMarkdown() => "---";
}

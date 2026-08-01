using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor;

public abstract class TextBlockBase : UserControl, IEditorBlock
{
    protected readonly TextBlock _previewLabel = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14
    };

    private TextBox? _editor;
    private bool _isEditing;

    public abstract string BlockTypeLabel { get; }
    public abstract EditorBlockType BlockType { get; }
    public bool IsEditing => _isEditing;

    public event EventHandler? ContentChanged;
    public event EventHandler? DeleteRequested;
    public event EventHandler<BlockSplitEventArgs>? SplitRequested;
    public event EventHandler? FocusPreviousRequested;
    public event EventHandler? FocusNextRequested;

    Control IEditorBlock.View => this;

    protected string RawText { get; set; } = string.Empty;

    protected TextBlockBase(string initialText)
    {
        RawText = initialText;
        ApplyThemeToPreview();
        Content = _previewLabel;
        UpdatePreview();
    }

    public abstract string ToMarkdown();
    public TextBox? GetActiveTextBox() => _editor;

    public void ActivateEditing()
    {
        if (_isEditing) return;
        _editor = CreateEditor();
        _editor.Text = RawText;
        WireEditorEvents(_editor);
        Content = _editor;
        _isEditing = true;
        _editor.Focus();
        _editor.CaretIndex = _editor.Text?.Length ?? 0;
    }

    public void CommitEditing()
    {
        if (!_isEditing || _editor is null) return;
        RawText = _editor.Text ?? string.Empty;
        UpdatePreview();
        Content = _previewLabel;
        _isEditing = false;
        _editor = null;
    }

    protected virtual TextBox CreateEditor()
    {
        return new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 24,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace"),
            FontSize = 14,
            Foreground = EditorTheme.Text,
            CaretBrush = EditorTheme.Text,
            Watermark = "Type / for commands..."
        };
    }

    protected virtual void UpdatePreview()
    {
        _previewLabel.Text = string.IsNullOrWhiteSpace(RawText) ? " " : RawText;
    }

    /// <summary>Re-apply theme brushes to the preview label after a theme change.</summary>
    public void RefreshTheme()
    {
        ApplyThemeToPreview();
        UpdatePreview();
    }

    protected virtual void ApplyThemeToPreview()
    {
        _previewLabel.Foreground = EditorTheme.Text;
    }

    private void WireEditorEvents(TextBox editor)
    {
        editor.TextChanging += (_, _) =>
        {
            RawText = editor.Text ?? string.Empty;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        };

        editor.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    e.Handled = true;
                    SplitRequested?.Invoke(this, new BlockSplitEventArgs(editor.CaretIndex));
                    break;

                case Key.Back when editor.CaretIndex == 0:
                    if (string.IsNullOrWhiteSpace(editor.Text))
                    {
                        e.Handled = true;
                        DeleteRequested?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        e.Handled = true;
                        FocusPreviousRequested?.Invoke(this, EventArgs.Empty);
                    }
                    break;

                case Key.Up when editor.CaretIndex == 0:
                    e.Handled = true;
                    FocusPreviousRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case Key.Down when editor.CaretIndex >= (editor.Text?.Length ?? 0):
                    e.Handled = true;
                    FocusNextRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        };
    }
}

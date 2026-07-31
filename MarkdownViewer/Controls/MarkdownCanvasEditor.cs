using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LiveMarkdown.Avalonia;

namespace MarkdownViewer.Controls;

/// <summary>
/// A native, block-based Markdown editing surface. A block is rendered exactly
/// as it will appear in the document until it is selected, at which point that
/// one block becomes an in-place editor. This is intentionally not a WebView
/// or HTML contenteditable control: Markdown remains the document format.
/// </summary>
public sealed class MarkdownCanvasEditor : UserControl
{
    private readonly StackPanel _blocksPanel = new() { Spacing = 8 };
    private readonly List<string> _blocks = [];
    private readonly List<Border> _cards = [];
    private int? _editingIndex;
    private bool _suppressChanges;

    public MarkdownCanvasEditor()
    {
        Content = _blocksPanel;
    }

    public event EventHandler<string>? MarkdownChanged;
    public event EventHandler<TextBox?>? ActiveEditorChanged;

    public string ImageBasePath { get; set; } = Path.GetTempPath();

    public string Markdown
    {
        get => string.Join("\n\n", _blocks);
        set
        {
            if (value == Markdown) return;
            _suppressChanges = true;
            _editingIndex = null;
            _blocks.Clear();
            _blocks.AddRange(SplitIntoBlocks(value));
            if (_blocks.Count == 0) _blocks.Add(string.Empty);
            RenderBlocks();
            _suppressChanges = false;
            ActiveEditorChanged?.Invoke(this, null);
        }
    }

    public void FocusEditor()
    {
        if (_editingIndex is int index && _cards[index].Child is TextBox textBox)
        {
            textBox.Focus();
            return;
        }

        ActivateBlock(0);
    }

    public void WrapSelection(string prefix, string suffix, string placeholder)
    {
        if (!TryGetActiveTextBox(out var editor))
        {
            FocusEditor();
            if (!TryGetActiveTextBox(out editor)) return;
        }

        var text = editor.Text ?? string.Empty;
        var start = editor.SelectionStart;
        var end = editor.SelectionEnd;
        var selected = end > start ? text[start..end] : placeholder;
        editor.Text = text[..start] + prefix + selected + suffix + text[end..];
        editor.SelectionStart = start + prefix.Length;
        editor.SelectionEnd = start + prefix.Length + selected.Length;
        editor.Focus();
    }

    public void PrefixCurrentLine(string prefix)
    {
        if (!TryGetActiveTextBox(out var editor))
        {
            FocusEditor();
            if (!TryGetActiveTextBox(out editor)) return;
        }

        var text = editor.Text ?? string.Empty;
        var caret = editor.CaretIndex;
        var start = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        editor.Text = text.Insert(start, prefix);
        editor.CaretIndex = caret + prefix.Length;
        editor.Focus();
    }

    private bool TryGetActiveTextBox(out TextBox editor)
    {
        if (_editingIndex is int index && _cards[index].Child is TextBox active)
        {
            editor = active;
            return true;
        }

        editor = null!;
        return false;
    }

    private void RenderBlocks()
    {
        _blocksPanel.Children.Clear();
        _cards.Clear();
        for (var index = 0; index < _blocks.Count; index++)
        {
            var card = new Border
            {
                Padding = new Thickness(8, 5),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Ibeam),
                Tag = index
            };
            card.PointerPressed += OnBlockPointerPressed;
            _cards.Add(card);
            _blocksPanel.Children.Add(card);
            ShowRenderedBlock(index);
        }
    }

    private void ShowRenderedBlock(int index)
    {
        if (index < 0 || index >= _cards.Count) return;
        var builder = new ObservableStringBuilder();
        builder.Append(_blocks[index]);
        _cards[index].Child = new MarkdownRenderer
        {
            MarkdownBuilder = builder,
            ImageBasePath = ImageBasePath
        };
        _cards[index].Background = Brushes.Transparent;
    }

    private void OnBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: int index })
            ActivateBlock(index);
    }

    private void ActivateBlock(int index)
    {
        if (index < 0 || index >= _blocks.Count) return;
        if (_editingIndex == index && _cards[index].Child is TextBox existing)
        {
            existing.Focus();
            return;
        }

        CommitActiveBlock();
        _editingIndex = index;
        var editor = new TextBox
        {
            Name = "CanvasBlockEditor",
            Text = _blocks[index],
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 30,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace"),
            FontSize = 14,
            Foreground = Brushes.White,
            Watermark = "Write Markdown..."
        };
        editor.TextChanging += (_, _) =>
        {
            if (_editingIndex != index) return;
            _blocks[index] = editor.Text ?? string.Empty;
            RaiseMarkdownChanged();
        };
        editor.LostFocus += (_, _) =>
        {
            if (_editingIndex == index)
                CommitActiveBlock();
        };
        _cards[index].Child = editor;
        _cards[index].Background = new SolidColorBrush(Color.FromArgb(50, 120, 120, 140));
        ActiveEditorChanged?.Invoke(this, editor);
        editor.Focus();
        editor.CaretIndex = editor.Text?.Length ?? 0;
    }

    private void CommitActiveBlock()
    {
        if (_editingIndex is not int index) return;
        if (_cards[index].Child is TextBox editor)
            _blocks[index] = editor.Text ?? string.Empty;
        _editingIndex = null;
        ShowRenderedBlock(index);
        ActiveEditorChanged?.Invoke(this, null);
        RaiseMarkdownChanged();
    }

    private void RaiseMarkdownChanged()
    {
        if (!_suppressChanges)
            MarkdownChanged?.Invoke(this, Markdown);
    }

    private static IEnumerable<string> SplitIntoBlocks(string markdown)
    {
        // Paragraph-level separation means an untouched block round-trips
        // verbatim, including tables, images, HTML and Mermaid fences.
        return System.Text.RegularExpressions.Regex.Split(markdown, "(?:\\r?\\n){2,}")
            .Where(block => block.Length > 0);
    }
}

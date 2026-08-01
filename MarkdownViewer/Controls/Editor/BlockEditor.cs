using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor;

public sealed class BlockEditor : UserControl
{
    private readonly StackPanel _blocksPanel = new() { Spacing = 4 };
    private readonly List<MarkdownBlock> _blockDescriptors = [];
    private readonly List<IEditorBlock> _editorBlocks = [];
    private readonly List<Border> _blockCards = [];

    private IEditorBlock? _activeBlock;
    private bool _suppressEvents;

    private Popup? _slashMenu;
    private ListBox? _slashListBox;
    private bool _slashMenuOpen;

    public BlockEditor()
    {
        var scrollViewer = new ScrollViewer
        {
            Content = _blocksPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Content = scrollViewer;
    }

    public event EventHandler<string>? MarkdownChanged;
    public event EventHandler<TextBox?>? ActiveEditorChanged;

    public string ImageBasePath { get; set; } = Path.GetTempPath();

    public string Markdown
    {
        get => MarkdownBlockParser.ToMarkdown(_blockDescriptors);
        set
        {
            _suppressEvents = true;
            _activeBlock?.CommitEditing();
            _activeBlock = null;
            LoadBlocks(value);
            _suppressEvents = false;
            ActiveEditorChanged?.Invoke(this, null);
        }
    }

    public void FocusEditor()
    {
        var firstEditable = _editorBlocks.FirstOrDefault(b => b.BlockType != EditorBlockType.HorizontalRule);
        firstEditable?.ActivateEditing();
    }

    public void WrapSelection(string prefix, string suffix, string placeholder)
    {
        if (_activeBlock is not TextBlockBase textBlock) return;
        var editor = textBlock.GetActiveTextBox();
        if (editor is null) return;
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
        if (_activeBlock is not TextBlockBase textBlock) return;
        var editor = textBlock.GetActiveTextBox();
        if (editor is null) return;
        var text = editor.Text ?? string.Empty;
        var caret = editor.CaretIndex;
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        editor.Text = text.Insert(lineStart, prefix);
        editor.CaretIndex = caret + prefix.Length;
        editor.Focus();
    }

    /// <summary>Refresh theme on all blocks and cards after a theme change.</summary>
    public void RefreshTheme()
    {
        foreach (var block in _editorBlocks)
        {
            if (block is TextBlockBase tb) tb.RefreshTheme();
            if (block is Blocks.DividerBlock db) db.RefreshTheme();
            if (block is Blocks.MermaidBlock mb) mb.RefreshTheme();
            if (block is Blocks.ImageBlock ib) ib.RefreshTheme();
        }
        foreach (var card in _blockCards)
            ApplyCardTheme(card);
        if (_slashListBox is not null)
            ApplySlashMenuTheme(_slashListBox);
    }

    // ─── Block lifecycle ───

    private void LoadBlocks(string markdown)
    {
        foreach (var block in _editorBlocks)
        {
            block.ContentChanged -= OnBlockContentChanged;
            block.DeleteRequested -= OnBlockDeleteRequested;
            block.SplitRequested -= OnBlockSplitRequested;
            block.FocusPreviousRequested -= OnFocusPrevious;
            block.FocusNextRequested -= OnFocusNext;
        }

        _blocksPanel.Children.Clear();
        _blockCards.Clear();
        _blockDescriptors.Clear();
        _editorBlocks.Clear();

        var descriptors = MarkdownBlockParser.Parse(markdown);
        foreach (var descriptor in descriptors)
        {
            _blockDescriptors.Add(descriptor);
            var editorBlock = CreateBlockFor(descriptor);
            _editorBlocks.Add(editorBlock);
            WireBlockEvents(editorBlock);
            var card = CreateBlockCard(editorBlock, _editorBlocks.Count - 1);
            _blockCards.Add(card);
            _blocksPanel.Children.Add(card);
        }

        if (_blockDescriptors.Count == 0)
        {
            var empty = new MarkdownBlock(EditorBlockType.Paragraph, string.Empty);
            _blockDescriptors.Add(empty);
            var block = CreateBlockFor(empty);
            _editorBlocks.Add(block);
            WireBlockEvents(block);
            var card = CreateBlockCard(block, 0);
            _blockCards.Add(card);
            _blocksPanel.Children.Add(card);
        }
    }

    private void WireBlockEvents(IEditorBlock block)
    {
        block.ContentChanged += OnBlockContentChanged;
        block.DeleteRequested += OnBlockDeleteRequested;
        block.SplitRequested += OnBlockSplitRequested;
        block.FocusPreviousRequested += OnFocusPrevious;
        block.FocusNextRequested += OnFocusNext;
    }

    private IEditorBlock CreateBlockFor(MarkdownBlock descriptor)
    {
        return descriptor.Type switch
        {
            EditorBlockType.Heading => new Blocks.HeadingBlock(descriptor),
            EditorBlockType.Code => new Blocks.CodeBlock(descriptor),
            EditorBlockType.Image => new Blocks.ImageBlock(descriptor, ImageBasePath),
            EditorBlockType.Blockquote => new Blocks.QuoteBlock(descriptor),
            EditorBlockType.UnorderedList => new Blocks.ListBlock(descriptor),
            EditorBlockType.OrderedList => new Blocks.ListBlock(descriptor),
            EditorBlockType.HorizontalRule => new Blocks.DividerBlock(descriptor),
            EditorBlockType.Mermaid => new Blocks.MermaidBlock(descriptor),
            _ => new Blocks.ParagraphBlock(descriptor)
        };
    }

    // ─── Card chrome ───

    private Border CreateBlockCard(IEditorBlock block, int index)
    {
        // Action bar (hover-visible)
        var typeBadge = new Border
        {
            Padding = new Thickness(6, 1),
            CornerRadius = new CornerRadius(3),
            Child = new TextBlock { Text = block.BlockTypeLabel, FontSize = 10 }
        };
        ApplyBadgeTheme(typeBadge);

        var deleteBtn = new Button
        {
            Content = "×",
            FontSize = 14,
            Width = 22, Height = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = index
        };
        deleteBtn.Foreground = EditorTheme.TextSecondary;
        deleteBtn.Click += (_, _) => DeleteBlock(index);

        var actionBar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 2),
            IsVisible = false
        };
        actionBar.Children.Add(typeBadge);
        actionBar.Children.Add(deleteBtn);

        var cardContent = new StackPanel { Spacing = 0 };
        cardContent.Children.Add(actionBar);
        cardContent.Children.Add(block.View);

        var card = new Border
        {
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Ibeam),
            Tag = index,
            Child = cardContent
        };

        card.PointerPressed += (_, e) =>
        {
            if (_activeBlock == block && block.IsEditing) return;
            ActivateBlock(index);
            e.Handled = true;
        };

        card.PointerEntered += (_, _) =>
        {
            if (_activeBlock != block)
                card.Background = EditorTheme.CardHover;
            actionBar.IsVisible = true;
        };

        card.PointerExited += (_, _) =>
        {
            if (_activeBlock != block)
                card.Background = Brushes.Transparent;
            actionBar.IsVisible = false;
        };

        return card;
    }

    private static void ApplyBadgeTheme(Border badge)
    {
        badge.Background = EditorTheme.CardHover;
        if (badge.Child is TextBlock tb)
            tb.Foreground = EditorTheme.TextSecondary;
    }

    private static void ApplyCardTheme(Border card)
    {
        if (card.Background != Brushes.Transparent)
            card.Background = EditorTheme.CardHover;
    }

    private void ActivateBlock(int index)
    {
        if (index < 0 || index >= _editorBlocks.Count) return;
        if (_activeBlock is not null && _activeBlock != _editorBlocks[index])
        {
            _activeBlock.CommitEditing();
            UpdateActiveCardVisual(false);
        }
        _activeBlock = _editorBlocks[index];
        _activeBlock.ActivateEditing();
        UpdateActiveCardVisual(true);
        ActiveEditorChanged?.Invoke(this, (_activeBlock as TextBlockBase)?.GetActiveTextBox());
    }

    private void UpdateActiveCardVisual(bool isActive)
    {
        if (_activeBlock is null) return;
        var idx = _editorBlocks.IndexOf(_activeBlock);
        if (idx < 0 || idx >= _blockCards.Count) return;
        _blockCards[idx].Background = isActive ? EditorTheme.CardActive : Brushes.Transparent;
    }

    // ─── Block operations ───

    private void InsertBlock(int afterIndex, EditorBlockType type)
    {
        var descriptor = type switch
        {
            EditorBlockType.Heading => new MarkdownBlock(EditorBlockType.Heading, "## New heading", "New heading", 2),
            EditorBlockType.Code => new MarkdownBlock(EditorBlockType.Code, "```\n\n```", "", language: ""),
            EditorBlockType.Image => new MarkdownBlock(EditorBlockType.Image, "![Alt text](https://)", "Alt text", url: "https://"),
            EditorBlockType.Blockquote => new MarkdownBlock(EditorBlockType.Blockquote, "> Quote"),
            EditorBlockType.UnorderedList => new MarkdownBlock(EditorBlockType.UnorderedList, "- List item"),
            EditorBlockType.OrderedList => new MarkdownBlock(EditorBlockType.OrderedList, "1. List item"),
            EditorBlockType.HorizontalRule => new MarkdownBlock(EditorBlockType.HorizontalRule, "---"),
            EditorBlockType.Mermaid => new MarkdownBlock(EditorBlockType.Mermaid, "```mermaid\nflowchart TD\n    A[Start]\n```", "flowchart TD\n    A[Start]", language: "mermaid"),
            _ => new MarkdownBlock(EditorBlockType.Paragraph, string.Empty)
        };
        InsertBlockAt(afterIndex, descriptor);
    }

    private void DeleteBlock(int index)
    {
        if (_editorBlocks.Count <= 1) return;
        _activeBlock?.CommitEditing();
        _activeBlock = null;
        _blockDescriptors.RemoveAt(index);
        var oldBlock = _editorBlocks[index];
        _editorBlocks.RemoveAt(index);
        oldBlock.ContentChanged -= OnBlockContentChanged;
        oldBlock.DeleteRequested -= OnBlockDeleteRequested;
        oldBlock.SplitRequested -= OnBlockSplitRequested;
        oldBlock.FocusPreviousRequested -= OnFocusPrevious;
        oldBlock.FocusNextRequested -= OnFocusNext;
        RebuildPanelFrom(Math.Min(index, _editorBlocks.Count - 1));
        ActivateBlock(Math.Min(index, _editorBlocks.Count - 1));
        RaiseMarkdownChanged();
    }

    private void SplitBlock(int blockIndex, int caretPosition)
    {
        if (blockIndex < 0 || blockIndex >= _editorBlocks.Count) return;
        var block = _editorBlocks[blockIndex];
        if (block is not TextBlockBase textBlock) return;
        var editor = textBlock.GetActiveTextBox();
        if (editor is null) return;
        var fullText = editor.Text ?? string.Empty;
        var before = fullText[..caretPosition];
        var after = fullText[caretPosition..];
        _blockDescriptors[blockIndex].RawMarkdown = before;
        _blockDescriptors[blockIndex].Content = before;
        editor.Text = before;
        var newType = _blockDescriptors[blockIndex].Type;
        if (newType == EditorBlockType.Heading && string.IsNullOrWhiteSpace(before))
            newType = EditorBlockType.Paragraph;
        var newDescriptor = new MarkdownBlock(newType, after, after);
        _blockDescriptors.Insert(blockIndex + 1, newDescriptor);
        var newBlock = CreateBlockFor(newDescriptor);
        _editorBlocks.Insert(blockIndex + 1, newBlock);
        WireBlockEvents(newBlock);
        RebuildPanelFrom(blockIndex);
        RaiseMarkdownChanged();
        _activeBlock = newBlock;
        newBlock.ActivateEditing();
        ActiveEditorChanged?.Invoke(this, (newBlock as TextBlockBase)?.GetActiveTextBox());
    }

    private void RebuildPanelFrom(int startIndex)
    {
        while (_blockCards.Count > startIndex)
            _blockCards.RemoveAt(_blockCards.Count - 1);
        while (_blocksPanel.Children.Count > startIndex)
            _blocksPanel.Children.RemoveAt(_blocksPanel.Children.Count - 1);
        for (var i = startIndex; i < _editorBlocks.Count; i++)
        {
            var card = CreateBlockCard(_editorBlocks[i], i);
            _blockCards.Add(card);
            _blocksPanel.Children.Add(card);
        }
    }

    // ─── Event handlers ───

    private void OnBlockContentChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents || sender is not IEditorBlock block) return;
        var idx = _editorBlocks.IndexOf(block);
        if (idx >= 0 && idx < _blockDescriptors.Count)
            _blockDescriptors[idx].RawMarkdown = block.ToMarkdown();
        RaiseMarkdownChanged();
    }

    private void OnBlockDeleteRequested(object? sender, EventArgs e)
    {
        if (sender is IEditorBlock block) { var idx = _editorBlocks.IndexOf(block); if (idx >= 0) DeleteBlock(idx); }
    }

    private void OnBlockSplitRequested(object? sender, BlockSplitEventArgs e)
    {
        if (sender is IEditorBlock block) { var idx = _editorBlocks.IndexOf(block); if (idx >= 0) SplitBlock(idx, e.CaretPosition); }
    }

    private void OnFocusPrevious(object? sender, EventArgs e)
    {
        if (sender is IEditorBlock block) { var idx = _editorBlocks.IndexOf(block); if (idx > 0) ActivateBlock(idx - 1); }
    }

    private void OnFocusNext(object? sender, EventArgs e)
    {
        if (sender is IEditorBlock block) { var idx = _editorBlocks.IndexOf(block); if (idx < _editorBlocks.Count - 1) ActivateBlock(idx + 1); }
    }

    private void RaiseMarkdownChanged() { if (!_suppressEvents) MarkdownChanged?.Invoke(this, Markdown); }

    // ─── Slash menu ───

    public void ShowSlashMenu(TextBox editor)
    {
        if (_slashMenuOpen) return;
        _slashMenu ??= CreateSlashMenu();
        _slashMenu.PlacementTarget = editor;
        _slashMenu.Open();
        _slashMenuOpen = true;
    }

    public void HideSlashMenu() { _slashMenu?.Close(); _slashMenuOpen = false; }
    public bool IsSlashMenuOpen => _slashMenuOpen;

    private Popup CreateSlashMenu()
    {
        var items = new (string Label, string Description, EditorBlockType Type)[]
        {
            ("Heading 1", "Large section heading", EditorBlockType.Heading),
            ("Heading 2", "Medium section heading", EditorBlockType.Heading),
            ("Heading 3", "Small section heading", EditorBlockType.Heading),
            ("Paragraph", "Plain text block", EditorBlockType.Paragraph),
            ("Code Block", "Syntax-highlighted code", EditorBlockType.Code),
            ("Image", "Embed an image", EditorBlockType.Image),
            ("Quote", "Blockquote for citations", EditorBlockType.Blockquote),
            ("Bullet List", "Unordered list", EditorBlockType.UnorderedList),
            ("Numbered List", "Ordered list", EditorBlockType.OrderedList),
            ("Divider", "Horizontal rule", EditorBlockType.HorizontalRule),
            ("Mermaid Diagram", "Flowchart or diagram", EditorBlockType.Mermaid),
        };

        var listBox = new ListBox
        {
            MaxHeight = 300,
            BorderThickness = new Thickness(1),
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<ValueTuple<string, string, EditorBlockType>>((item, _) =>
            {
                var panel = new StackPanel { Spacing = 2, Margin = new Thickness(8, 4) };
                var label = new TextBlock { Text = item.Item1, FontSize = 13, FontWeight = FontWeight.Medium };
                label.Foreground = EditorTheme.Text;
                var desc = new TextBlock { Text = item.Item2, FontSize = 11 };
                desc.Foreground = EditorTheme.TextSecondary;
                panel.Children.Add(label);
                panel.Children.Add(desc);
                return panel;
            })
        };
        ApplySlashMenuTheme(listBox);

        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is not ValueTuple<string, string, EditorBlockType> selected) return;
            HideSlashMenu();
            var idx = _activeBlock is not null ? _editorBlocks.IndexOf(_activeBlock) : _editorBlocks.Count - 1;
            if (selected.Item3 == EditorBlockType.Heading)
            {
                int level = selected.Item1 switch { "Heading 1" => 1, "Heading 3" => 3, _ => 2 };
                var prefix = new string('#', level) + " ";
                InsertBlockAt(idx, new MarkdownBlock(EditorBlockType.Heading, prefix + "New heading", "New heading", level));
            }
            else
            {
                InsertBlock(idx, selected.Item3);
            }
        };

        _slashListBox = listBox;
        return new Popup { Child = listBox, Placement = PlacementMode.Bottom };
    }

    private static void ApplySlashMenuTheme(ListBox lb)
    {
        lb.Background = EditorTheme.BackgroundSecondary;
        lb.BorderBrush = EditorTheme.BorderSubtle;
    }

    private void InsertBlockAt(int afterIndex, MarkdownBlock descriptor)
    {
        var insertIdx = afterIndex + 1;
        _blockDescriptors.Insert(insertIdx, descriptor);
        var editorBlock = CreateBlockFor(descriptor);
        _editorBlocks.Insert(insertIdx, editorBlock);
        WireBlockEvents(editorBlock);
        RebuildPanelFrom(insertIdx);
        ActivateBlock(insertIdx);
        RaiseMarkdownChanged();
    }
}

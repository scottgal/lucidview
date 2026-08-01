using Avalonia.Controls;

namespace MarkdownViewer.Controls.Editor;

/// <summary>
/// A single block in the block editor. Each block type maps to one markdown
/// structural element (paragraph, heading, code fence, image, etc.).
/// </summary>
public interface IEditorBlock
{
    /// <summary>Human-readable type label shown in the block chrome.</summary>
    string BlockTypeLabel { get; }

    /// <summary>The parsed block type.</summary>
    EditorBlockType BlockType { get; }

    /// <summary>Serialize this block back to its canonical markdown form.</summary>
    string ToMarkdown();

    /// <summary>Fired whenever the block's content changes.</summary>
    event EventHandler? ContentChanged;

    /// <summary>Fired when user requests deletion of this block (Backspace on empty).</summary>
    event EventHandler? DeleteRequested;

    /// <summary>Fired when user presses Enter at start or end, requesting a split/insert.</summary>
    event EventHandler<BlockSplitEventArgs>? SplitRequested;

    /// <summary>Fired when user presses ArrowUp at the very top — focus previous block.</summary>
    event EventHandler? FocusPreviousRequested;

    /// <summary>Fired when user presses ArrowDown at the very bottom — focus next block.</summary>
    event EventHandler? FocusNextRequested;

    /// <summary>Activate inline editing and place the caret.</summary>
    void ActivateEditing();

    /// <summary>Commit any in-progress edit back to the rendered view.</summary>
    void CommitEditing();

    /// <summary>True while the block is in editing mode.</summary>
    bool IsEditing { get; }

    /// <summary>The Avalonia control that hosts this block.</summary>
    Control View { get; }
}

/// <summary>
/// Events args for block split: user pressed Enter, the block should split at
/// the given caret position. The text before the caret stays in this block;
    /// the text after becomes a new block of the same type inserted below.
/// </summary>
public class BlockSplitEventArgs : EventArgs
{
    public int CaretPosition { get; }
    public BlockSplitEventArgs(int caretPosition) => CaretPosition = caretPosition;
}

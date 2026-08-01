using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class ListBlock : TextBlockBase
{
    private readonly bool _ordered;

    public override string BlockTypeLabel => _ordered ? "Numbered List" : "Bullet List";
    public override EditorBlockType BlockType => _ordered ? EditorBlockType.OrderedList : EditorBlockType.UnorderedList;

    public ListBlock(MarkdownBlock descriptor) : base(descriptor.RawMarkdown)
    {
        _ordered = descriptor.Type == EditorBlockType.OrderedList;
    }

    protected override void ApplyThemeToPreview()
    {
        base.ApplyThemeToPreview();
        _previewLabel.FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace");
        _previewLabel.FontSize = 13;
    }

    public override string ToMarkdown() => RawText;
}

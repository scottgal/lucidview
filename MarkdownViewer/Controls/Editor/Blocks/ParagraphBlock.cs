using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class ParagraphBlock : TextBlockBase
{
    public override string BlockTypeLabel => "Paragraph";
    public override EditorBlockType BlockType => EditorBlockType.Paragraph;

    public ParagraphBlock(MarkdownBlock descriptor) : base(descriptor.RawMarkdown) { }

    public override string ToMarkdown() => RawText;
}

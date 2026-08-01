using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class HeadingBlock : TextBlockBase
{
    private readonly int _level;

    public override string BlockTypeLabel => $"H{_level}";
    public override EditorBlockType BlockType => EditorBlockType.Heading;

    public HeadingBlock(MarkdownBlock descriptor) : base(descriptor.Content ?? descriptor.RawMarkdown)
    {
        _level = descriptor.HeadingLevel;
    }

    protected override void UpdatePreview()
    {
        base.UpdatePreview();
        var scale = _level switch
        {
            1 => 2.0,
            2 => 1.5,
            3 => 1.25,
            _ => 1.0
        };
        _previewLabel.FontSize = 14 * scale;
        _previewLabel.FontWeight = _level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;
    }

    public override string ToMarkdown()
    {
        var prefix = new string('#', _level) + " ";
        var text = RawText.TrimStart();
        if (text.StartsWith(prefix.TrimEnd()) && text.Length > _level)
            text = text[_level..].TrimStart();
        return prefix + text;
    }
}

using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class QuoteBlock : TextBlockBase
{
    public override string BlockTypeLabel => "Quote";
    public override EditorBlockType BlockType => EditorBlockType.Blockquote;

    public QuoteBlock(MarkdownBlock descriptor)
        : base(StripQuoteMarkers(descriptor.RawMarkdown)) { }

    protected override void ApplyThemeToPreview()
    {
        base.ApplyThemeToPreview();
        _previewLabel.FontStyle = FontStyle.Italic;
        _previewLabel.Foreground = EditorTheme.TextSecondary;
    }

    public override string ToMarkdown()
    {
        var lines = RawText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith('>'))
                lines[i] = "> " + trimmed;
        }
        return string.Join("\n", lines);
    }

    private static string StripQuoteMarkers(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("> "))
                lines[i] = trimmed[2..];
            else if (trimmed.StartsWith('>'))
                lines[i] = trimmed[1..];
        }
        return string.Join("\n", lines).Trim();
    }
}

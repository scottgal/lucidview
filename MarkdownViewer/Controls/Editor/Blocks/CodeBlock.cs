using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class CodeBlock : TextBlockBase
{
    private readonly string _language;

    public override string BlockTypeLabel => string.IsNullOrEmpty(_language) ? "Code" : _language;
    public override EditorBlockType BlockType => EditorBlockType.Code;

    public CodeBlock(MarkdownBlock descriptor)
        : base(descriptor.Content ?? string.Empty)
    {
        _language = descriptor.Language ?? string.Empty;
    }

    protected override void ApplyThemeToPreview()
    {
        base.ApplyThemeToPreview();
        _previewLabel.FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace");
        _previewLabel.FontSize = 13;
        _previewLabel.Foreground = EditorTheme.TextSecondary;
    }

    protected override void UpdatePreview()
    {
        _previewLabel.Text = string.IsNullOrWhiteSpace(RawText) ? " " : RawText;
    }

    protected override TextBox CreateEditor()
    {
        var editor = base.CreateEditor();
        editor.FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace");
        editor.FontSize = 13;
        editor.Foreground = EditorTheme.TextSecondary;
        return editor;
    }

    public override string ToMarkdown()
    {
        var fence = string.IsNullOrEmpty(_language) ? "```" : $"```{_language}";
        return $"{fence}\n{RawText}\n```";
    }
}

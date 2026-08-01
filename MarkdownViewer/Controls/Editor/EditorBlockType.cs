namespace MarkdownViewer.Controls.Editor;

/// <summary>
/// Structural markdown block types that the editor understands.
/// </summary>
public enum EditorBlockType
{
    Paragraph,
    Heading,
    Code,
    Image,
    Blockquote,
    UnorderedList,
    OrderedList,
    HorizontalRule,
    Mermaid,
    Table
}

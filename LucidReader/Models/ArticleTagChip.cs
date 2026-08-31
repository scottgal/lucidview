namespace LucidReader.Models;

/// <summary>
/// One tag on the article the reading pane is showing, as the chip strip
/// binds it.
///
/// A class with a single property rather than binding the strip straight to
/// a list of strings: the chip's remove button recovers what to remove from
/// its own DataContext (the same way the item list's hover actions recover
/// their ItemRow), and a bare string DataContext gives a Click handler
/// nothing to work with beyond the button's own content, which is a glyph.
///
/// Immutable, because a chip is never edited in place: adding or removing a
/// tag rebuilds the strip from the database, so what is on screen is always
/// something that was actually stored.
/// </summary>
public sealed class ArticleTagChip
{
    public required string Name { get; init; }
}

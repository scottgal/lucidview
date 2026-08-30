using LucidReader.Core.Storage;
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The display half of the search snippet: turning the delimited passage FTS5
/// returned into words the item list can lay out and highlight. The
/// delimiters are control characters, so anything that fails to strip them
/// shows nothing rather than junk, but a row that shows nothing where a word
/// should be is still wrong.
/// </summary>
public class SearchSnippetTests
{
    private const char Start = SearchHit.MatchStart;
    private const char End = SearchHit.MatchEnd;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_in_gives_nothing_out(string? snippet) =>
        Assert.Empty(SearchSnippet.ToWords(snippet));

    [Fact]
    public void Words_are_split_on_whitespace_and_the_matched_one_is_flagged()
    {
        var words = SearchSnippet.ToWords($"a body about {Start}kingfishers{End} and herons");

        Assert.Equal("a body about kingfishers and herons", Texts(words));
        Assert.Equal("kingfishers", Matches(words));
    }

    [Fact]
    public void Several_matches_are_all_flagged()
    {
        var words = SearchSnippet.ToWords($"{Start}writer{End} and {Start}lock{End}");

        Assert.Equal("writer and lock", Texts(words));
        Assert.Equal("writer lock", Matches(words));
    }

    [Fact]
    public void Punctuation_attached_to_a_matched_word_stays_with_it()
    {
        // FTS5 marks tokens, and a token stops at the comma, so the comma
        // falls outside the delimiters while belonging to the same word.
        var words = SearchSnippet.ToWords($"about {Start}kingfishers{End}, mostly");

        Assert.Equal("about kingfishers, mostly", Texts(words));
        Assert.Equal("kingfishers,", Matches(words));
    }

    [Fact]
    public void A_snippet_with_no_markers_is_all_plain_words()
    {
        var words = SearchSnippet.ToWords("nothing was marked here");

        Assert.Equal(4, words.Count);
        Assert.All(words, w => Assert.False(w.IsMatch));
    }

    [Fact]
    public void An_unclosed_marker_does_not_lose_the_rest_of_the_snippet()
    {
        var words = SearchSnippet.ToWords($"about {Start}kingfishers and herons");

        Assert.Equal("about kingfishers and herons", Texts(words));
        Assert.Equal("kingfishers and herons", Matches(words));
    }

    [Fact]
    public void Repeated_whitespace_does_not_produce_empty_words()
    {
        var words = SearchSnippet.ToWords("  a   b  ");

        Assert.Equal("a b", Texts(words));
    }

    /// <summary>
    /// The row shows one preview line or the other, never both and never
    /// neither, which is what the two IsVisible bindings in MainWindow.axaml
    /// depend on.
    /// </summary>
    [Fact]
    public void An_ordinary_row_shows_the_plain_preview()
    {
        var row = NewRow(matched: string.Empty, isSearchResult: false);

        Assert.True(row.IsPlainSnippetVisible);
        Assert.False(row.IsSearchSnippetVisible);
    }

    [Fact]
    public void A_search_result_shows_the_matched_passage_instead()
    {
        var row = NewRow(matched: $"about {Start}kingfishers{End}", isSearchResult: true);

        Assert.False(row.IsPlainSnippetVisible);
        Assert.True(row.IsSearchSnippetVisible);
        Assert.Equal(2, row.SnippetWords.Count);
    }

    /// <summary>
    /// A hit with no usable passage (an empty column, say) falls back to the
    /// ordinary preview rather than showing a blank line where the reason for
    /// the result should be.
    /// </summary>
    [Fact]
    public void A_search_result_with_no_passage_falls_back_to_the_plain_preview()
    {
        var row = NewRow(matched: string.Empty, isSearchResult: true);

        Assert.True(row.IsPlainSnippetVisible);
        Assert.False(row.IsSearchSnippetVisible);
    }

    /// <summary>The words back as one string, so a mismatch reads as prose rather than as an index.</summary>
    private static string Texts(IEnumerable<SnippetWord> words) =>
        string.Join(" ", words.Select(w => w.Text));

    /// <summary>Just the words flagged as part of the match.</summary>
    private static string Matches(IEnumerable<SnippetWord> words) =>
        string.Join(" ", words.Where(w => w.IsMatch).Select(w => w.Text));

    private static ItemRow NewRow(string matched, bool isSearchResult) => new()
    {
        Item = new LucidReader.Core.Model.FeedItem
        {
            FeedId = 1,
            Guid = "g",
            FirstSeenUtc = DateTimeOffset.UnixEpoch
        },
        FeedName = "Feed",
        Snippet = "the stored preview",
        MatchedSnippet = matched,
        IsSearchResult = isSearchResult
    };
}

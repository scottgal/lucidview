using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// The string half of search safety. Every one of these inputs reaches the
/// builder in practice, because search runs on every keystroke and a query
/// is half-typed far more often than it is finished. What must never happen
/// is an FTS5 metacharacter surviving into the MATCH expression, where it
/// stops being text the user is looking for and becomes syntax - either a
/// query that throws, or worse, one that quietly means something else.
///
/// The companion tests in SearchRepositoryTests run the same shapes against
/// a real database, since "produces a plausible string" and "SQLite accepts
/// it" are two different claims.
/// </summary>
public class FtsQueryBuilderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void No_input_produces_no_query(string? input) =>
        Assert.Null(FtsQueryBuilder.Build(input));

    [Theory]
    [InlineData("!!!")]
    [InlineData("()")]
    [InlineData("\"\"\"")]
    [InlineData("^^^")]
    [InlineData("* * *")]
    [InlineData("- . , ; :")]
    public void A_query_of_nothing_but_punctuation_produces_no_query(string input) =>
        Assert.Null(FtsQueryBuilder.Build(input));

    [Fact]
    public void A_single_word_becomes_a_quoted_prefix_phrase()
    {
        // Quoted so a bareword keyword is a search term, starred so the word
        // still matching while it is being typed is the default.
        Assert.Equal("\"compos\"*", FtsQueryBuilder.Build("compos"));
    }

    [Fact]
    public void Only_the_last_term_gets_the_prefix_operator()
    {
        // The earlier terms are finished words: the user has moved past them,
        // and prefixing them all would widen the query with every keystroke
        // rather than narrowing it.
        Assert.Equal("\"writer\" \"loc\"*", FtsQueryBuilder.Build("writer loc"));
    }

    [Fact]
    public void A_trailing_separator_means_the_word_is_finished()
    {
        // "compositor " is a completed word, so it is matched exactly. This is
        // what stops a finished query silently matching more than it says.
        Assert.Equal("\"compositor\"", FtsQueryBuilder.Build("compositor "));
        Assert.Equal("\"compositor\"", FtsQueryBuilder.Build("compositor."));
    }

    [Fact]
    public void Prefixing_can_be_turned_off_for_a_query_that_is_not_being_typed()
    {
        Assert.Equal("\"compos\"", FtsQueryBuilder.Build("compos", prefixLastTerm: false));
    }

    [Theory]
    // Quotes, balanced or not, cannot survive into the expression.
    [InlineData("\"unbalanced quote AND (", "\"unbalanced\" \"quote\" \"AND\"")]
    [InlineData("\"quoted phrase\"", "\"quoted\" \"phrase\"")]
    // Bare boolean keywords are terms, not operators.
    [InlineData("cats OR dogs", "\"cats\" \"OR\" \"dogs\"")]
    [InlineData("cats NOT dogs", "\"cats\" \"NOT\" \"dogs\"")]
    [InlineData("cats NEAR dogs", "\"cats\" \"NEAR\" \"dogs\"")]
    // Column filters and anchors are punctuation, so they split terms.
    [InlineData("title:compositor", "\"title\" \"compositor\"")]
    [InlineData("^first", "\"first\"")]
    // A user-typed star does not become a second prefix operator.
    [InlineData("com*positor", "\"com\" \"positor\"")]
    public void Fts5_syntax_is_reduced_to_plain_terms(string input, string expectedPrefix)
    {
        var built = FtsQueryBuilder.Build(input);

        Assert.NotNull(built);
        Assert.StartsWith(expectedPrefix, built, StringComparison.Ordinal);

        // Whatever else it did, the result is a sequence of quoted phrases
        // with at most one trailing prefix operator, so nothing in it can be
        // read by FTS5 as an operator.
        Assert.Equal(0, built!.Count(c => c == '(' || c == ')' || c == ':' || c == '^'));
        Assert.True(built.Count(c => c == '*') <= 1);
        Assert.Equal(0, built.Count(c => c == '"') % 2);
    }

    [Fact]
    public void An_apostrophe_splits_a_word_the_same_way_the_tokenizer_does()
    {
        // unicode61 indexes "don't" as the two tokens don and t, so searching
        // for the two of them is what finds the stored word.
        Assert.Equal("\"don\" \"t\"*", FtsQueryBuilder.Build("don't"));
    }

    [Fact]
    public void Non_ascii_letters_are_letters_rather_than_separators()
    {
        Assert.Equal("\"naïve\"*", FtsQueryBuilder.Build("naïve"));
        Assert.Equal("\"日本語\"*", FtsQueryBuilder.Build("日本語"));
    }

    [Fact]
    public void Digits_and_underscores_stay_inside_a_term()
    {
        Assert.Equal("\"net10\" \"os_x\"*", FtsQueryBuilder.Build("net10 os_x"));
    }

    [Fact]
    public void Repeated_whitespace_does_not_produce_empty_terms()
    {
        Assert.Equal("\"a\" \"b\"*", FtsQueryBuilder.Build("   a     b"));
    }
}
